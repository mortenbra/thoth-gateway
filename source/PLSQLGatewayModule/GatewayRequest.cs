using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using log4net;
using System.Collections;
using System.Xml;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Handles parsing of the request and setting up the PL/SQL procedure call
    /// </summary>
    public class GatewayRequest
    {

        private static readonly ILog logger = LogManager.GetLogger(typeof(PLSQLHttpModule));

        private List<NameValuePair> _requestParams = new List<NameValuePair>();
        private List<NameValuePair> _cgiParams = new List<NameValuePair>();
        private List<UploadedFile> _uploadedFiles = new List<UploadedFile>();

        private string _requestBody = "";

        public GatewayRequest(string serverName, string requestMethod, string requestPath, string rawUrl, string soapAction)
        {

            ServerName = serverName;
            OriginalRequest = requestMethod + " " + requestPath;

            if (logger.IsInfoEnabled)
            {
                logger.Info("Request: " + OriginalRequest);
            }

            // accept the URL request and parse out the dad and database procedure call from it
            ParseRequest(requestMethod, requestPath, rawUrl, soapAction);

        }

        public void AddRequestParameters(NameValueCollection requestQueryString, NameValueCollection requestForm, HttpFileCollection requestFiles)
        {

            // combine the querystring, form, and uploaded files into one parameter collection
            // combine querystring and form parameters
            // note: we must sanitize the parameter names since they will be copied verbatim into the SQL to execute

            // NOTE: special characters (such as Norwegian øæå) don't work in querystring? (seems to be an ASP.NET problem, ie not a database problem...)
            // see http://forums.asp.net/t/1422064.aspx
            // see http://www.velocityreviews.com/forums/t69568-requestquerystring-does-not-return-extended-characters.html
            
            //foreach (string s in requestQueryString)
            //{
            //    logger.Debug("Querystring: " + s + " = " + HttpUtility.UrlDecode(requestQueryString.Get(s)));
            //}

            foreach (string s in requestQueryString)
            {
                // ignore parameter if no parameter name specified (for example "?foo" instead of "?foo=bar")
                if (s != null)
                {
                    NameValuePair nvp = new NameValuePair(StringUtil.RemoveSpecialCharacters(s), requestQueryString.GetValues(s));
                    _requestParams.Add(nvp);
                }
                else
                {
                    if (logger.IsDebugEnabled)
                    {
                        logger.Warn("A querystring parameter has no name, and will be ignored. Value = " + requestQueryString.GetValues(s)[0]);
                    }
                }
            }

            foreach (string s in requestForm)
            {
                NameValuePair nvp = new NameValuePair(StringUtil.RemoveSpecialCharacters(s), requestForm.GetValues(s));
                _requestParams.Add(nvp);
            }

            string[] fileParamNames = requestFiles.AllKeys;

            for (int i = 0; i < requestFiles.Count; i++)
            {

                if (requestFiles[i].ContentLength > 0)
                {
                    string paramName = StringUtil.RemoveSpecialCharacters(fileParamNames[i]);

                    // if the file is stored in either filesystem or XDB, it should be named differently than if stored in the document table
                    bool useDocTableNamingConvention = (DadConfig.DocumentFilePath.Length == 0 && DadConfig.DocumentXdbPath.Length == 0);

                    UploadedFile uf = new UploadedFile(paramName, requestFiles[i].FileName, requestFiles[i], DadConfig.DocumentMaxNameLength, useDocTableNamingConvention);
                    _uploadedFiles.Add(uf);
                
                }
                else
                {
                    logger.Debug(string.Format("File number {0} (parameter name = {1}) was empty (zero bytes).", i, fileParamNames[i]));
                }
                

            }

            int uploadedFileCount = _uploadedFiles.Count;

            if (uploadedFileCount == 1)
            {

                // avoid extra parsing work if there is only one file uploaded
                NameValuePair nvp = new NameValuePair(_uploadedFiles[0].ParamName, _uploadedFiles[0].UniqueFileName);
                _requestParams.Add(nvp);

            }
            else if (uploadedFileCount > 1)
            {
                // if two or more file parameter names are equal, then add the corresponding filenames as an array instead of string
                // we do this by adding the entries to a Dictionary to group by parameter name, then adding them back to a NameValuePair

                Dictionary<string, List<string>> files = new Dictionary<string,List<string>>();

                foreach (UploadedFile uf in _uploadedFiles)
                {
                    
                    if (!files.ContainsKey(uf.ParamName))
	                {
                		 files[uf.ParamName] = new List<string>();
	                }

                    files[uf.ParamName].Add(uf.UniqueFileName);

                }

                foreach (KeyValuePair<string, List<string>> f in files)
	            {
                    NameValuePair nvp = new NameValuePair(f.Key, (List<string>)f.Value);
                    _requestParams.Add(nvp);
	 
	            }

            }

            // the parameters have been set up, now build the database call
            BuildOwaProc();

        }

        public void AddRequestParametersForSOAP(string procName, string requestBody)
        {
            _requestBody = requestBody;

            string tagName = "";
            int startPos = procName.IndexOf(".");

            if (startPos > -1)
            {
                // just get the last part (the actual function name)
                tagName = StringUtil.PrettyStr(procName.Substring(startPos+1));
            }
            else
            {
                tagName = StringUtil.PrettyStr(procName);
            }

            if (logger.IsDebugEnabled)
            {
                logger.Debug("Adding request parameters from SOAP request. Proc name = " + procName + ", tag name = " + tagName);
            }

            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(_requestBody);

                // TODO: get the namespace (abbreviation) used in the request (if any -- soapUI does)
                XmlNodeList nl = xmlDoc.GetElementsByTagName(tagName);

                // outer loop should only occur once
                foreach (XmlNode x in nl)
                {
                    foreach (XmlNode c in x.ChildNodes)
                    {
                        // remove special characters from the parameter names, since these are passed verbatim to the PL/SQL block to be executed
                        NameValuePair nvp = new NameValuePair(StringUtil.RemoveSpecialCharacters(c.Name.ToLower()), c.InnerText);
                        _requestParams.Add(nvp);
                    }

                }
            }
            catch (Exception e)
              {
                  logger.Error("Failed to parse XML for SOAP request: " + e.Message);
                  logger.Debug(_requestBody);
              }

          // the parameters have been set up, now build the database call
          BuildOwaProc();

        }
        
        public void AddCGIEnvironment(NameValueCollection serverVariables)
        {

            //logger.Debug("CGI variables: " + serverVariables.ToString());

            // see http://msdn.microsoft.com/en-us/library/ms524602(v=vs.90).aspx for default IIS server variables
            // note that some values are modified, and some are added, in the code below

            foreach (string key in serverVariables.AllKeys)
            {
                if (!key.StartsWith("ALL_"))
                {
                    if (key == "SERVER_SOFTWARE")
                    {
                        if (DadConfiguration.CGIServerSoftware != "")
                        {
                            NameValuePair nvp = new NameValuePair(key, DadConfiguration.CGIServerSoftware);
                            _cgiParams.Add(nvp);
                        }
                        else
                        {
                            // use the default
                            // according to the docs: "The name and version of the server software that answers the request and runs the gateway. The format is name/version."
                            // ie., this should return something like "Microsoft-IIS/7.5"
                            NameValuePair nvp = new NameValuePair(key, serverVariables.GetValues(key));
                            _cgiParams.Add(nvp);
                        }
                    
                    }
                    else if (key == "SCRIPT_NAME")
                    {
                        NameValuePair nvp = new NameValuePair(key, "/" + ModuleName + "/" + DadName);
                        _cgiParams.Add(nvp);
                    }
                    else if (key == "PATH_INFO")
                    {
                        NameValuePair nvp = new NameValuePair(key, "/" + ProcName);
                        _cgiParams.Add(nvp);
                    }
                    else if (key == "HTTP_AUTHORIZATION")
                    {
                        // parse username and password and set properties
                        string encodedAuth = serverVariables.GetValues(key)[0];

                        logger.Debug("HTTP Authorization: " + encodedAuth);

                        if (encodedAuth.StartsWith("Basic "))
                        {
                            string decodedAuth = encodedAuth.Substring(6);
                            //decodedAuth = StringUtil.base64Decode(decodedAuth);
                            decodedAuth = System.Text.Encoding.Default.GetString(Convert.FromBase64String(decodedAuth));

                            // commented out to avoid logging usernames/passwords in the log file
                            //logger.Debug("Decoded value: " + decodedAuth);
                            string[] auth = decodedAuth.Split(':');
                            BasicAuthUsername = auth[0];
                            BasicAuthPassword = auth[1];
                        }

                        NameValuePair nvp = new NameValuePair(key, serverVariables.GetValues(key));
                        _cgiParams.Add(nvp);
                    }
                    else
                    {
                        NameValuePair nvp = new NameValuePair(key, serverVariables.GetValues(key));

                        if (nvp.Name == "HTTP_COOKIE")
                        {
                            logger.Debug("Cookies: " + nvp.ValuesAsString);
                        }

                        _cgiParams.Add(nvp);
                    }
                }

            }

            // add custom CGI variables

            NameValuePair nvp1 = new NameValuePair("PLSQL_GATEWAY", DadConfiguration.CGIPLSQLGateway); 
            _cgiParams.Add(nvp1);
            NameValuePair nvp2 = new NameValuePair("GATEWAY_IVERSION", DadConfiguration.CGIGatewayIVersion); 
            _cgiParams.Add(nvp2);
            NameValuePair nvp3 = new NameValuePair("DAD_NAME", DadName);
            _cgiParams.Add(nvp3);
            NameValuePair nvp4 = new NameValuePair("REQUEST_CHARSET", DadConfig.NLSCharset);
            _cgiParams.Add(nvp4);
            NameValuePair nvp5 = new NameValuePair("REQUEST_IANA_CHARSET", DadConfig.IANACharset); 
            _cgiParams.Add(nvp5);
            NameValuePair nvp6 = new NameValuePair("DOC_ACCESS_PATH", DadConfig.DocumentPath);
            _cgiParams.Add(nvp6);
            NameValuePair nvp7 = new NameValuePair("DOCUMENT_TABLE", DadConfig.DocumentTableName);
            _cgiParams.Add(nvp7);
            NameValuePair nvp8 = new NameValuePair("PATH_ALIAS", DadConfig.PathAlias);
            _cgiParams.Add(nvp8);

            // REQUEST_PROTOCOL: not supplied by IIS, but required for Apex Listener compatibility
            // see https://code.google.com/p/thoth-gateway/issues/detail?id=8

            string requestProtocol = "http";

            if (serverVariables["HTTPS"].ToLower() == "on")
            {
                requestProtocol = "https";
            }

            NameValuePair nvp9 = new NameValuePair("REQUEST_PROTOCOL", requestProtocol);
            _cgiParams.Add(nvp9);
            
            // impersonate Apex Listener, if necessary/desired
            if (DadConfiguration.CGIApexListenerVersion != "")
            {
                NameValuePair nvp10 = new NameValuePair("APEX_LISTENER_VERSION", DadConfiguration.CGIApexListenerVersion);
                _cgiParams.Add(nvp10);
            }

            // get the current Windows username, useful for Integrated Windows Authentication
            WindowsUsername = serverVariables["LOGON_USER"];
            logger.Debug("Current Windows user name (LOGON_USER) = " + WindowsUsername);

        }


        private void BuildOwaProc()
        {

            // build the structure (class instance) that represents the main procedure call

            OwaProcedure owaProc = new OwaProcedure();

            owaProc.CheckForDownload = true;

            if (IsFlexibleParams)
            {
                // flexible parameter mode (name/value collections)
                owaProc.MainProc = ProcName;
                owaProc.MainProcParams = ":b1, :b2";
            }
            else if (IsPathAlias)
            {
                // path aliasing (forward URL to PathAlias procedure)
                owaProc.MainProc = DadConfig.PathAliasProcedure;

                if (DadConfig.PathAliasIncludeParameters)
                {
                    // forward querystring and form parameters as well as URL
                    owaProc.MainProcParams = ":b1, :b2, :b3";

                }
                else
                {
                    // standard mod_plsql behaviour (just forward the URL)
                    owaProc.MainProcParams = ":b1";
                }

            }
            else if (IsDocumentPath)
            {
                // download procedure via DocumentPath folder (no parameters, procedure is expected to identify file using get_cgi_env)
                // see http://download.oracle.com/docs/cd/B15897_01/web.1012/b14010/concept.htm#i1010535
                owaProc.MainProc = DadConfig.DocumentProcedure;

            }
            else if (IsXdbAlias)
            {
                // XDB resource will be fetched by separate call
                owaProc.MainProc = "null";
                owaProc.CheckForDownload = false;
            }
            else
            {
                owaProc.MainProc = ProcName;

                string sqlParams = "";
                int paramCount = 0;

                foreach (NameValuePair nvp in _requestParams)
                {
                    paramCount = paramCount + 1;
                    sqlParams = StringUtil.AppendStr(sqlParams, nvp.Name + " => :b" + paramCount.ToString(), ", ");

                    if (logger.IsDebugEnabled)
                    {
                        logger.Debug("Parameter " + paramCount.ToString() + ": " + nvp.Name + " = " + nvp.DebugValue);
                    }

                }

                if (sqlParams.Length > 0)
                {
                    owaProc.MainProcParams = sqlParams;
                }
                else
                {
                    owaProc.MainProcParams = "";
                }

            }

            owaProc.BeforeProc = DadConfig.BeforeProcedure;
            owaProc.AfterProc = DadConfig.AfterProcedure;
            owaProc.RequestValidationFunction = DadConfig.RequestValidationFunction;
            owaProc.IsSoapRequest = IsSoapRequest;

            OwaProc = owaProc;

        }

        
        private string SanitizeProcName(string procName, string dadExclusionList)
        {
          string newProcName = procName.ToLower().Trim();
          string finalProcName = "";

          newProcName = StringUtil.RemoveSpecialCharacters(newProcName);

          // check against exclusion/inclusion lists

          // prefer the inclusion list over the exclusion list

          string inclusionList = DadConfig.InclusionList;

          if (inclusionList.Length > 0)
          {
              string[] inclusions = inclusionList.Split(' ');

              finalProcName = "";
              
              foreach (string s in inclusions)
              {
                  if (newProcName.StartsWith(s))
                  {
                      logger.Debug("Procedure name matches inclusion list (" + inclusionList + ")");
                      finalProcName = newProcName;
                  }  
              }
          }
          else
          {

              string defaultExclusionList = DadConfiguration.DEFAULT_EXCLUSION_LIST;

              string exclusionList = "";

              if (dadExclusionList.Length > 0)
              {
                  exclusionList = defaultExclusionList + " " + dadExclusionList;
              }
              else
              {
                  exclusionList = defaultExclusionList;
              }
              
              string[] exclusions = exclusionList.Split(' ');

              finalProcName = newProcName;

              foreach (string s in exclusions)
              {
                  if (newProcName.StartsWith(s))
                  {
                      logger.Warn("Procedure name matches exclusion list (" + exclusionList + ")");
                      finalProcName = "";
                  }
              }
          }

          return finalProcName;

      }

        private string GetAliasValue(string procName, string thePath)
        {
            string aliasValue = "";
            
            int startIndex = thePath.IndexOf(procName) + procName.Length + 1;
            if (thePath.Length >= startIndex)
            {
                aliasValue = thePath.Substring(startIndex);
            }
            else
            {
                aliasValue = "";
            }

            return aliasValue;

        }


        private void ParseRequest(string requestMethod, string thePath, string rawUrl, string soapAction)
        {

            string requestPath = thePath.Substring(1);

            DadSpecifiedInRequest = true;
            IsFlexibleParams = false;
            IsPathAlias = false;

            string[] pathElements = requestPath.Split('/');

            if (pathElements.Length >= 2)
            {
                ModuleName = pathElements[0];
                DadName = pathElements[1];

                if (DadName.Length == 0 || !DadConfiguration.IsValidDad(DadName))
                {
                    DadSpecifiedInRequest = false;
                    DadName = DadConfiguration.DefaultDad;
                }

                // get dad-specific configuration settings
                DadConfig = new DadConfiguration(DadName);

                if (pathElements.Length >= 3)
                {
                    ProcName = pathElements[2];
                    if (ProcName.Length > 0)
                    {
                        if (ProcName.Substring(0, 1) == "!")
                        {
                            IsFlexibleParams = true;
                        }
                        else if (ProcName == DadConfig.PathAlias)
                        {
                            IsPathAlias = true;
                            PathAliasValue = GetAliasValue(ProcName, thePath);
                        }
                        else if (ProcName == DadConfig.XdbAlias)
                        {
                            IsXdbAlias = true;
                            XdbAliasValue = GetAliasValue(ProcName, thePath);
                        }
                        else if (ProcName == DadConfig.DocumentPath)
                        {
                            IsDocumentPath = true;
                        }
                    }
                }
                else
                {
                    // missing trailing slash in request
                    ProcName = "";
                }

                if (ProcName.Length == 0)
                {
                    ProcName = DadConfig.DefaultPage;
                }

                if (DadConfig.InvocationProtocol == DadConfiguration.INVOCATION_PROTOCOL_SOAP)
                {
                    IsSoapRequest = true;
                    IsWsdlRequest = requestMethod.ToUpper() == "GET" && rawUrl.ToLower().EndsWith("?wsdl");
                }

                if (IsSoapRequest && !IsWsdlRequest)
                {
                    // get the procedure name from the SOAPAction header
                    string methodName = soapAction.Replace("\"", "");
                    methodName = methodName.Substring(methodName.LastIndexOf("/")+1);
                    ProcName = ProcName + "." + StringUtil.ReversePrettyStr(methodName);
                }

                ProcName = SanitizeProcName(ProcName, DadConfig.ExclusionList);

            }
            else
            {
                ModuleName = "";
                DadName = "";
                ProcName = "";
            }
            
            if (DadName.Length > 0 && ProcName.Length > 0)
            {

                string[] urlProcElements = ProcName.Split('.');

                if (urlProcElements.Length == 3)
                {
                    // schema, package and procedure specified
                    OraSchema = urlProcElements[0];
                    OraPackage = urlProcElements[1];
                    OraProc = urlProcElements[2];
                }
                else if (urlProcElements.Length == 2)
                {
                    // assume package and procedure specified (although it could also be schema and procedure, but there is no way to be certain)
                    OraSchema = "";
                    OraPackage = urlProcElements[0];
                    OraProc = urlProcElements[1];
                }
                else
                {
                    // just the procedure is specified
                    OraSchema = "";
                    OraPackage = "";
                    OraProc = ProcName;
                }

                logger.Debug("Parsed module = " + ModuleName + ", dad = " + DadName + ", proc = " + ProcName);
            
            }


        }

        public List<NameValuePair> GetFlexibleParams()
        {

            List<NameValuePair> flexParams = new List<NameValuePair>();

            List<string> names = new List<string>();
            List<string> values = new List<string>();

            foreach (NameValuePair nvp in RequestParameters)
            {

                if (nvp.ValueType == ValueType.ArrayValue)
                {
                    // create multiple parameters with the same name if there are multiple parameter values
                    foreach (string s in nvp.Values)
                    {
                        names.Add(nvp.Name);
                        values.Add(s);
                    }
                }
                else
                {
                    names.Add(nvp.Name);
                    values.Add(nvp.Value);
                }
                
            }

            // force ValueType to be set to Array even if there is only one element in the array (because the PL/SQL API specifies that flexible params are passed using arrays)

            NameValuePair nvpNames = new NameValuePair("name_array", names);
            nvpNames.ValueType = ValueType.ArrayValue;
            flexParams.Add(nvpNames);

            NameValuePair nvpValues = new NameValuePair("value_array", values);
            nvpValues.ValueType = ValueType.ArrayValue;
            flexParams.Add(nvpValues);

            return flexParams;

        }

        public List<NameValuePair> GetPathAliasParams()
        {

            List<NameValuePair> pathAliasParams = new List<NameValuePair>();

            pathAliasParams.Add(new NameValuePair("b1", PathAliasValue));

            if (DadConfig.PathAliasIncludeParameters)
            {
                // optionally include request parameters as name/value collections
                List<NameValuePair> flexParams = GetFlexibleParams();

                foreach (NameValuePair nvp in flexParams)
                {
                    pathAliasParams.Add(nvp);
                }

            }

            return pathAliasParams;

        }

        public DadConfiguration DadConfig
        {
            get;
            set;
        }


        public string OriginalRequest
        {
            get;
            set;
        }

        public string ServerName
        {
            get;
            set;
        }
        
        public string ModuleName
        {
            get;
            set;
        }
        
        public string DadName
        {
            get;
            set; 
        }

        public bool DadSpecifiedInRequest
        {
            get;
            set;
        }

        public bool ValidRequest
        {
            get
            {
                return (DadName.Length > 0 && ProcName.Length > 0);
            }
        }

        public string ProcName
        {
            get;
            set;
        }

        public bool IsFlexibleParams
        {
            get;
            set;
        }

        public bool IsPathAlias
        {
            get;
            set;
        }

        public bool IsXdbAlias
        {
            get;
            set;
        }
        
        public bool IsDocumentPath
        {
            get;
            set;
        }

        public bool IsSoapRequest
        {
            get;
            set;
        }

        public bool IsWsdlRequest
        {
            get;
            set;
        }
        
        public string PathAliasValue
        {
            get;
            set;
        }

        public string XdbAliasValue
        {
            get;
            set;
        }
        
        public string OraSchema
        {
            get;
            set;
        }

        public string OraPackage
        {
            get;
            set;
        }

        public string OraProc
        {
            get;
            set;
        }

        public string OraSQLCall
        {
            get;
            set;
        }

        public OwaProcedure OwaProc
        {
            get;
            set;
        }

        public string WindowsUsername
        {
            get;
            set;
        }

        public string WindowsUsernameNoDomain
        {
            get
            {
                int slashPos = WindowsUsername.IndexOf("\\");
                if (slashPos > -1)
	            {
                  return WindowsUsername.Substring(slashPos +1);
	            }
                else
                {
                  return WindowsUsername;
                }

            }
        }

        public string BasicAuthUsername
        {
            get;
            set;
        }

        public string BasicAuthPassword
        {
            get;
            set;
        }

        public List<NameValuePair> RequestParameters
        {
            get
            {
                return _requestParams;
            }
        }
        
        public List<NameValuePair> CGIParameters
        {
            get
            {
                return _cgiParams;
            }
        }

        public List<UploadedFile> UploadedFiles
        {
            get
            {
                return _uploadedFiles;
            }
        }


    }
}
