using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Net;
using System.Text;
using System.Web;
using log4net;
using log4net.Config;
using Oracle.ManagedDataAccess.Client;
using System.IO;
using System.IO.Compression;

namespace PLSQLGatewayModule

{
 /// <summary>
 /// Thoth PL/SQL Gateway Module for Microsoft Internet Information Server (IIS)
 /// 
 ///
 ///The Thoth Gateway (https://github.com/mortenbra/thoth-gateway) is released as open source under the BSD license:
 ///
 /// http://www.opensource.org/licenses/bsd-license.php
 ///
 /// Copyright (c) 2009-2015, MORTEN BRATEN (http://ora-00001.blogspot.com)
 /// All rights reserved.
 /// 
 /// Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
 /// 
 /// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
 /// 
 /// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
 /// 
 /// The names of the contributors may not be used to endorse or promote products derived from this software without specific prior written permission.
 /// 
 /// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 /// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 /// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDERS OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
 /// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
 /// GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 /// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 /// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 /// 
 /// </summary>

 public class PLSQLHttpModule : IHttpModule
 {

   private static readonly ILog logger = LogManager.GetLogger(typeof(PLSQLHttpModule));
     
   public PLSQLHttpModule()
   {
      XmlConfigurator.Configure();
   }

  public void Init(HttpApplication app)
  {
      // Register our event handler with Application object.

      // problem: LOGON_USER not available when running gateway on IIS7
      // cause: "In Integrated mode, both IIS and ASP.NET authentication stages have been unified. 
      //        Because of this, the results of IIS authentication are not available until the PostAuthenticateRequest stage,
      //        when both ASP.NET and IIS authentication methods have completed. "
      //        http://learn.iis.net/page.aspx/381/aspnet-20-breaking-changes-on-iis/
      // solution: hook the gateway up to the PostAuthenticateRequest event rather than the BeginRequest event

      //app.BeginRequest += new EventHandler(HandleRequest);
      app.PostAuthenticateRequest += new EventHandler(HandleRequest);

  }

  public void Dispose()
  {
      // Left blank because we don't have to do anything.
  }

  /// <summary>
  /// The main routine that handles each request, calls the database, and outputs the response from the OWA toolkit back to the client through IIS.
  /// </summary>
  /// <param name="o"></param>
  /// <param name="a"></param>
   
  private void HandleRequest(object o, EventArgs a)
  {

      // grab the request context from ASP.NET
      HttpApplication app = (HttpApplication)o;
      HttpContext ctx = (HttpContext)app.Context;

      if (DadConfiguration.HideServerBanner)
      {

          // http://blogs.technet.com/b/stefan_gossner/archive/2008/03/12/iis-7-how-to-send-a-custom-server-http-header.aspx
          
          try
          {
              app.Response.Headers.Remove("Server");
              app.Response.Headers.Remove("X-AspNet-Version");
              app.Response.Headers.Remove("X-Powered-By");
          }
          catch (PlatformNotSupportedException e)
          {
              logger.Warn("Attempted to hide server banners (HideServerBanner = True), but failed with error: " + e.Message);
          }
      }
      
      // check if gateway should be bypassed (return normal execution to IIS)
      if (DadConfiguration.ServeStaticContent)
      {
          string physicalPath = app.Request.PhysicalPath;

          if (File.Exists(physicalPath))
          {
              logger.Debug("Requested file " + physicalPath + " exists on disk. Gateway will not handle this request.");
              return;
          }
          else
          {
              logger.Debug("Requested file " + physicalPath + " was NOT found on disk, continuing with normal gateway request.");
          }
          
      }
      
      // parse the request (URL); we are expecting calls in a format similar to the following:
      // http://servername/PLSQLGatewayModule/dadname/[schema.][package.]procedure?parameter1=xxx&parameter2=yyy

      string serverName = app.Request.ServerVariables["HTTP_HOST"];
      string requestContentType = app.Request.ContentType;
      string requestBody = "";
      string requestPath = app.Request.FilePath.Substring(1);
      string soapAction = app.Request.Headers["SOAPAction"];

      if (requestContentType.ToLower() != "application/x-www-form-urlencoded")
      {
          requestBody = new StreamReader(app.Request.InputStream, System.Text.Encoding.Default).ReadToEnd();
          requestBody = HttpUtility.UrlDecode(requestBody);
      }

      GatewayRequest gReq = new GatewayRequest(serverName, app.Request.HttpMethod, app.Request.FilePath, app.Request.RawUrl, soapAction);

      if (!gReq.DadSpecifiedInRequest)
      {

          if (DadConfiguration.DefaultDadEnabled)
          {
              string defaultURL = "/" + gReq.ModuleName + "/" + gReq.DadName + "/" + gReq.ProcName;

              logger.Debug("DAD not specified in URL, or specified DAD not defined in configuration file. Redirecting to default DAD and default procedure: " + defaultURL);
              ctx.Response.Redirect(defaultURL);
              //ctx.Response.StatusCode = 302; // moved temporarily
              app.CompleteRequest();
              return;
          }
          else
          {
              logger.Warn("DAD not specified in request, or DAD not defined in configuration file, and default DAD disabled. Request terminated with 404.");
              throw new HttpException(404, "Not Found");
          }
      }

      if (gReq.ValidRequest)
      {
          // the requested procedure name is valid
          // now process querystring and form variables, set up the OWA packages with the CGI environment information,
          // and check if there are files to be uploaded

          if (gReq.IsWsdlRequest)
          {
             logger.Debug("Request is for WSDL document");
          }
          else if (gReq.IsSoapRequest)
          {
              logger.Debug("Invocation protocol is SOAP");
              string requestBodyForSOAP = new StreamReader(app.Request.InputStream, System.Text.Encoding.Default).ReadToEnd();
              requestBodyForSOAP = HttpUtility.UrlDecode(requestBodyForSOAP);
              gReq.AddRequestParametersForSOAP(gReq.ProcName, requestBodyForSOAP);
          }
          else
          {
              gReq.AddRequestParameters(app.Request.QueryString, app.Request.Form, app.Request.Files, requestBody);
          }
          
          gReq.AddCGIEnvironment(ctx.Request.ServerVariables);

          OracleParameterCache opc = new OracleParameterCache(ctx);

          // connect to the database
          OracleInterface ora = new OracleInterface(gReq, opc);

          if (ora.Connected())
          {
              ora.SetupOwaCGI(gReq.CGIParameters, ctx.Request.UserHostName, ctx.Request.UserHostAddress, gReq.BasicAuthUsername, gReq.BasicAuthPassword);

              if (ctx.Request.Files.Count > 0)
              {
                  bool uploadSuccess = ora.UploadFiles(gReq.UploadedFiles);
              }
              
          }

          bool success = false;

          // the GatewayResponse object will hold the result of the database call (headers, cookies, status codes, response body, etc.)
          GatewayResponse gResp = new GatewayResponse();

          if (!ora.Connected())
          {
              logger.Debug("Failed to connect to database, skipping procedure execution.");
              success = false;
          }
          else if (gReq.IsSoapRequest)
          {
              if (gReq.IsWsdlRequest)
              {
                  success = ora.GenerateWsdl(gReq.ServerName, gReq.ModuleName, gReq.DadName, gReq.ProcName);
              }
              else
              {
                  success = ora.ExecuteMainProc(gReq.OwaProc, gReq.RequestParameters, false, gReq.ProcName);
                  if (success)
                  {
                      ora.GenerateSoapResponse(gReq.ProcName);
                  }
                  else
                  {
                      int errorCode = ora.GetLastErrorCode();
                      string errorText = ora.GetLastErrorText();
                      logger.Debug("SOAP request failed with error code " + errorCode + ", generating SOAP Fault response.");
                      ora.GenerateSoapFault(errorCode, errorText);
                  }
              }
          }
          else if (gReq.IsFlexibleParams)
          {
              logger.Debug("Using flexible parameter mode (converting parameters to name/value arrays)");
              success = ora.ExecuteMainProc(gReq.OwaProc, gReq.GetFlexibleParams(), false, gReq.ProcName);
          }
          else if (gReq.IsPathAlias)
          {
              logger.Debug("Forwarding request to PathAliasProcedure");
              success = ora.ExecuteMainProc(gReq.OwaProc, gReq.GetPathAliasParams(), false, gReq.ProcName);
          }
          else if (gReq.IsXdbAlias)
          {
              logger.Debug("Forwarding request to XDB Repository");
              success = ora.GetXdbResource(gReq.XdbAliasValue);
          }
          else if (gReq.IsDocumentPath)
          {
              logger.Debug("Forwarding request to DocumentProcedure");
              success = ora.ExecuteMainProc(gReq.OwaProc, new List<NameValuePair>(), false, gReq.ProcName);
          }
          else
          {
              success = ora.ExecuteMainProc(gReq.OwaProc, gReq.RequestParameters, false, gReq.ProcName);

              if (!success && ora.GetLastErrorText().IndexOf("PLS-00306:") > -1)
              {
                  logger.Debug("Wrong number or types of arguments in call. Will retry call after looking up parameter metadata in data dictionary.");
                  success = ora.ExecuteMainProc(gReq.OwaProc, gReq.RequestParameters, true, gReq.ProcName);
              }
              if (!success && ora.GetLastErrorText().IndexOf("ORA-01460:") > -1)
              {
                  logger.Debug("Unimplemented or unreasonable conversion requested. Will retry call after looking up parameter metadata in data dictionary.");
                  success = ora.ExecuteMainProc(gReq.OwaProc, gReq.RequestParameters, true, gReq.ProcName);
              }
              else if (!success)
              {
                  logger.Error("Call failed: " + ora.GetLastErrorText());
              }
          }

          if (success)
          {
              logger.Info("Gateway procedure executed successfully.");
              ora.DoCommit();

              if (gReq.IsWsdlRequest)
              {
                  logger.Info("Responding with WSDL document");
                  gResp.FetchWsdlResponse(ora);
              }
              else if (gReq.IsSoapRequest)
              {
                  logger.Info("Responding with SOAP response");
                  gResp.FetchSoapResponse(ora);
              }
              else if (gReq.IsXdbAlias)
              {
                  logger.Info("Responding with XDB Resource");
                  gResp.FetchXdbResponse(ora);
              }
              else
              {
                  // fetch the response buffers from OWA
                  logger.Debug("Fetch buffer size = " + gReq.DadConfig.FetchBufferSize);
                  gResp.FetchOwaResponse(ora);
              }

              ora.CloseConnection();
              
              // process response
              ProcessResponse(gReq, gResp, ctx, app);

          }
          else
          {

              ora.DoRollback();

              if (gReq.IsSoapRequest)
              {
                  logger.Debug("SOAP request failed, returning SOAP fault as part of normal response.");
                  gResp.FetchSoapResponse(ora);
                  ProcessResponse(gReq, gResp, ctx, app);
              }
              else if (gReq.DadConfig.ErrorStyle == "DebugStyle")
              {
                  logger.Error("Request failed, showing debug error page");
                  ctx.Response.Write(GatewayError.GetErrorDebugPage(gReq, ora.GetLastErrorText()));
              }
              else
              {
                  logger.Error("Request failed, user gets status code 404");
                  throw new HttpException(404, "Not Found");
                  //ctx.Response.Clear();
                  //ctx.Response.StatusCode = 404;
              }
              
              // TODO: does this get called if HttpException is thrown above... don't think so!
              ora.CloseConnection();

          }

      }
      else
      {

          logger.Warn("Request (" + requestPath + ") not valid, returning 404...");

          if (gReq.DadConfig.ErrorStyle == "DebugStyle")
          {
              ctx.Response.Write(GatewayError.GetInvalidRequestDebugPage(requestPath, gReq.DadConfig));
          }
          else
          {
              throw new HttpException(404, "Not Found");
              //ctx.Response.Clear();
              //ctx.Response.StatusCode = 404;
          }

      }

       app.CompleteRequest();
       logger.Info("Request completed.");

    }

  private void ProcessResponse(GatewayRequest req, GatewayResponse resp, HttpContext ctx, HttpApplication app)
  {

      string acceptEncoding = app.Request.Headers["Accept-Encoding"];

      acceptEncoding = (acceptEncoding != null ? acceptEncoding.ToLower() : String.Empty);

      // check if dynamic content should be compressed
      bool doCompress = (DadConfiguration.CompressDynamicContent && !resp.IsDownload && acceptEncoding.Length >0);

      if (doCompress)
      {

          if (logger.IsDebugEnabled)
          {
              logger.Debug("Compression of dynamic content is enabled, client accepts " + acceptEncoding);
          }

          if (acceptEncoding.Contains("gzip") || acceptEncoding == "*")
          {
              ctx.Response.Filter = new GZipStream(ctx.Response.Filter, CompressionMode.Compress);
              ctx.Response.AppendHeader("Content-encoding", "gzip");
              ctx.Response.Cache.VaryByHeaders["Accept-encoding"] = true;
          }
          else if (acceptEncoding.Contains("deflate"))
          {
              ctx.Response.Filter = new DeflateStream(ctx.Response.Filter, CompressionMode.Compress);
              app.Response.AppendHeader("Content-Encoding", "deflate");
              ctx.Response.Cache.VaryByHeaders["Accept-encoding"] = true;
          }

          // note: since compression changes the content-length, the content-length header (if set in database) cannot be used

      }
      
      
      // see http://stackoverflow.com/questions/472906/net-string-to-byte-array-c
      int responseBodyLength = System.Text.Encoding.UTF8.GetBytes(resp.ResponseBody.ToString()).Length;
      
      ctx.Response.ContentType = resp.ContentType;

      if ((resp.ContentLength != 0) && (!doCompress)) // don't write content-length if response is compressed
      {
          if ((!resp.IsDownload) && (resp.ContentLength != responseBodyLength))
          {
              if (logger.IsDebugEnabled)
              {
                  logger.Warn(string.Format("Actual response content length ({0}) is different from Content-Length header ({1}). Content-Length was set via X-DB-Content-Length header (which means the client/gateway character set is different from the database character set).", responseBodyLength, resp.ContentLength));
                  logger.Debug("To ensure proper page rendering in browser, Content-Length header will be set equal to actual content length");
              }

              ctx.Response.AddHeader("Content-Length", responseBodyLength.ToString());
          
          }
          else
          {
              ctx.Response.AddHeader("Content-Length", resp.ContentLength.ToString());
          }
      }

      for (int i = 0; i < resp.Cookies.Count; i++)
      {
          HttpCookie c = resp.Cookies.Get(i);
          ctx.Response.Cookies.Add(c);
      }

      if (resp.RedirectLocation.Length > 0)
      {
          string newLocation = resp.RedirectLocation;

          if (!newLocation.StartsWith("/") && !newLocation.StartsWith("http://") && !newLocation.StartsWith("https://"))
          {
              logger.Debug("Converting relative path (" + resp.RedirectLocation + ") to absolute path");
              newLocation = "/" + req.ModuleName + "/" + req.DadName + "/" + newLocation;
          }

          logger.Debug("Redirecting to " + newLocation);
          //ctx.Response.StatusCode = 302; // moved temporarily
          ctx.Response.Redirect(newLocation);
          app.CompleteRequest();
          return;
      }

      if (resp.StatusCode != 0)
      {
          logger.Debug("Setting HTTP status code to " + resp.StatusCode.ToString());
          ctx.Response.StatusCode = resp.StatusCode;
          //ctx.Response.StatusDescription = gResp.StatusDescription;

          // see http://www.evanclosson.com/devlog/bettercustomerrorsinaspnet for more about status codes

      }

      foreach (NameValuePair nvp in resp.Headers)
      {
          if (nvp.Name == "Content-length")
          {

              if ((!resp.IsDownload) && (int.Parse(nvp.Value) != responseBodyLength))
              {
                  logger.Warn(string.Format("Actual response content length ({0}) is different from Content-Length header ({1}).", responseBodyLength, nvp.Value));
              }

              if (!doCompress) // don't write content-length if response is compressed)
              {
                  ctx.Response.AddHeader(nvp.Name, nvp.Value);
              }
          
          }
          else
          {
              ctx.Response.AddHeader(nvp.Name, nvp.Value);
          }

      
      }

      if (resp.IsDownload)
      {
          logger.Debug(string.Format("Writing binary response ({0} bytes) to client...", resp.FileData.Length));

          if (resp.FileData.Length > 0)
          {
              ctx.Response.BinaryWrite(resp.FileData);
          }
          else
          {
              logger.Warn("Binary response is empty");
          }

      }
      else
      {
          if (logger.IsDebugEnabled)
          {
              logger.Debug(string.Format("Writing response ({0} bytes) to client...", responseBodyLength));
              //logger.Debug(string.Format("Last 100 characters of response body: {0}", resp.ResponseBody.ToString().Substring(resp.ResponseBody.Length - 100)));

          }

          ctx.Response.Write(resp.ResponseBody.ToString());

          if (resp.ResponseBody.Length == 0)
          {
              logger.Info("Response body is empty");
          }

      }

  }


  }


}
