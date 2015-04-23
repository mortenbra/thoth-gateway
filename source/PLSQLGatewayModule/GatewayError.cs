using System;
using System.Collections.Generic;
using System.Web;
using System.Text;
using System.Reflection;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Generates error pages in the gateway
    /// </summary>
    public static class GatewayError
    {

        private const string PRODUCT_URL = "http://code.google.com/p/thoth-gateway/";

        /// <summary>
        /// returns the version string for the assembly
        /// </summary>
        /// <returns></returns>
        private static string GetGatewayAssemblyVersion()
        {
            Assembly assembly = Assembly.GetAssembly(typeof(PLSQLHttpModule));
            return assembly.GetName().Version.ToString();
        }

        /// <summary>
        /// returns a signature for the page footer
        /// </summary>
        /// <returns></returns>
        private static string GetSignature()
        {
            return string.Format("<hr><p><a href='{0}'>Thoth PL/SQL Gateway Module</a> version {1}</p>", PRODUCT_URL, GetGatewayAssemblyVersion());
        }

        /// <summary>
        /// Given a request, returns a HTML-formatted error page with the details of the request and the error
        /// </summary>
        public static string GetErrorDebugPage(GatewayRequest req, string errorMessage)
        {
            StringBuilder page = new StringBuilder();

            string htmlErrorMessage = errorMessage.Replace("\n", "<br>");

            page.Append("<style> body { font-family: verdana, sans-serif; } p { font-size: 9pt; } div.errormsg { border: 1px double black; background-color: gold; font-family: lucida console, courier; font-size: 14pt; padding: 10px; width: 85%; } div.code { border: 1px dotted black; font-family: lucida console, courier; font-size: 14pt; padding: 10px; } </style>");
            page.AppendFormat("<h1>Thoth Gateway Error</h1><p>{2}: Module = {0}, Database Access Descriptor (DAD) = {1}</p>", req.ModuleName, req.DadName, DateTime.Now.ToString());

            page.AppendFormat("<p>The gateway encountered the following error while executing the request:</p><div class='errormsg'>{0}</div>", htmlErrorMessage);

            page.AppendFormat("<h3>Request</h3><div class='code'>{0}</div>", req.OriginalRequest);
            
            page.AppendFormat("<h3>SQL</h3><div class='code'>{0}</div>", req.OwaProc.SQLStatement);

            page.AppendFormat("<h3>Parameters ({0})</h3><ul>", req.RequestParameters.Count);
            foreach (NameValuePair nvp in req.RequestParameters)
            {
                page.AppendFormat("<li>{0} = {1}</li>", nvp.Name, nvp.DebugValue);
            }
            page.AppendFormat("</ul>");

            page.AppendFormat("<h3>Environment ({0})</h3><ul>", req.CGIParameters.Count);
            foreach (NameValuePair nvp in req.CGIParameters)
            {
                page.AppendFormat("<li>{0} = {1}</li>", nvp.Name, nvp.DebugValue);
            }
            page.AppendFormat("</ul>");

            page.AppendFormat("<h3>DAD Configuration ({0})</h3><ul>", req.DadName);
            page.AppendFormat("<li>DatabaseConnectString = {0}</li>", req.DadConfig.DatabaseConnectString);
            page.AppendFormat("<li>DatabaseUserName = {0}</li>", req.DadConfig.DatabaseUserName);
            page.AppendFormat("<li>NLSLanguage = {0}_{1}.{2}</li>", req.DadConfig.NLSLanguage, req.DadConfig.NLSTerritory, req.DadConfig.NLSCharset);
            page.AppendFormat("<li>InvocationProtocol = {0}</li>", req.DadConfig.InvocationProtocol);
            page.AppendFormat("<li>DocumentTableName = {0}</li>", req.DadConfig.DocumentTableName);
            page.AppendFormat("<li>DocumentFilePath = {0}</li>", req.DadConfig.DocumentFilePath);
            page.AppendFormat("<li>DocumentXdbPath = {0}</li>", req.DadConfig.DocumentXdbPath);
            page.AppendFormat("<li>Document Path = {0}</li>", req.DadConfig.DocumentPath);
            page.AppendFormat("<li>Path Alias = {0}</li>", req.DadConfig.PathAlias);
            page.AppendFormat("<li>Xdb Alias = {0}</li>", req.DadConfig.XdbAlias);
            page.AppendFormat("</ul>");

            page.Append(GetSignature());
            
            return page.ToString();

        }

        /// <summary>
        /// For an invalid request, returns a HTML-formatted error page with the details of the request and the error
        /// </summary>
        public static string GetInvalidRequestDebugPage(string requestPath, DadConfiguration dadConfig)
        {
            StringBuilder page = new StringBuilder();

            page.Append("<style> body { font-family: verdana, sans-serif; } p { font-size: 9pt; } div.errormsg { border: 1px double black; background-color: gold; font-family: lucida console, courier; font-size: 14pt; padding: 10px; width: 85%; } div.code { border: 1px dotted black; font-family: lucida console, courier; font-size: 14pt; padding: 10px; } </style>");
            page.AppendFormat("<h1>Thoth Gateway Invalid Request</h1>");

            page.AppendFormat("<p>The request is invalid. Please check the URL syntax, and examine the inclusion and exclusion lists.</p>");

            page.AppendFormat("<h3>Request</h3><div class='code'>{0}</div>", requestPath);

            page.AppendFormat("<h3>Inclusion List</h3><div class='code'>{0}</div>", dadConfig.InclusionList);

            page.AppendFormat("<h3>Exclusion List</h3><div class='code'>{0}</div>", DadConfiguration.DEFAULT_EXCLUSION_LIST + " " + dadConfig.ExclusionList);

            page.Append(GetSignature());

            return page.ToString();

        }

    }
}
