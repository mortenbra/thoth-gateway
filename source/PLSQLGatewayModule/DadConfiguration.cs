using System.Configuration;
using System;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Handles configuration of Database Access Descriptors (DADs) in the web.config file
    /// </summary>
    public class DadConfiguration
    {

        public const string DEFAULT_EXCLUSION_LIST = "sys. dbms_ utl_ owa_ htp. htf. wpg_docload. ctxsys. mdsys.";

        public const string INVOCATION_PROTOCOL_CGI = "CGI";
        public const string INVOCATION_PROTOCOL_SOAP = "SOAP";

        private static DadSection _dadSection = (DadSection)System.Configuration.ConfigurationManager.GetSection("thoth");
        private static DadElement _dadElement = null;

        private string _nlsLanguage = "";
        private string _nlsTerritory = "";
        private string _nlsCharset = "";

        public DadConfiguration(string dadName)
        {
            _dadElement = _dadSection.Dads[dadName];

            string langString = NLSLanguageString;

            // The NLS_LANG parameter has three components: language, territory, and character set.
            // Specify it in the following format, including the punctuation:
            // NLS_LANG = language_territory.charset

            string langLangTerritory = langString.Substring(0, langString.IndexOf("."));
            _nlsLanguage = langLangTerritory.Substring(0, langLangTerritory.IndexOf("_"));
            _nlsTerritory = langLangTerritory.Substring(langLangTerritory.IndexOf("_") + 1);
            _nlsCharset = langString.Substring(langString.IndexOf(".") + 1);

        }

        private string getVal(string name, string defaultValue)
        {
            return (_dadElement.Params[name] != null ? _dadElement.Params[name].Value : defaultValue);
        }

        private int getIntVal(string name, int defaultValue)
        {
            String val = getVal(name, null);
            if (val != null)
            {
                return Convert.ToInt32(val);
            }
            else
            {
                return defaultValue;
            }
        }

        private bool getBoolVal(string name, bool defaultValue)
        {
            String val = getVal(name, null);
            if (val != null)
            {
                return Convert.ToBoolean(val);
            }
            else
            {
                return defaultValue;
            }
        }

        public static string DefaultDad
        {
            get { return ConfigurationSettings.AppSettings["DefaultDad"]; }
        }
        
        public static bool DefaultDadEnabled
        {
            get { return bool.Parse(ConfigurationSettings.AppSettings["DefaultDadEnabled"]); }
        }

        public static bool ServeStaticContent
        {
            get { return ConfigurationSettings.AppSettings["ServeStaticContent"] != null ? bool.Parse(ConfigurationSettings.AppSettings["ServeStaticContent"]) : false; }
        }

        public static bool CompressDynamicContent
        {
            get { return ConfigurationSettings.AppSettings["CompressDynamicContent"] != null ? bool.Parse(ConfigurationSettings.AppSettings["CompressDynamicContent"]) : false; }
        }

        public static bool HideServerBanner
        {
            get { return ConfigurationSettings.AppSettings["HideServerBanner"] != null ? bool.Parse(ConfigurationSettings.AppSettings["HideServerBanner"]) : false; }
        }

        public static string CGIServerSoftware
        {
            get { return ConfigurationSettings.AppSettings["CGIServerSoftware"] != null ? ConfigurationSettings.AppSettings["CGIServerSoftware"] : ""; }
        }

        public static string CGIApexListenerVersion
        {
            get { return ConfigurationSettings.AppSettings["CGIApexListenerVersion"] != null ? ConfigurationSettings.AppSettings["CGIApexListenerVersion"] : ""; }
        }

        public static string CGIPLSQLGateway
        {
            // name of the gateway (our claim to fame! :-)
            get { return ConfigurationSettings.AppSettings["CGIPLSQLGateway"] != null ? ConfigurationSettings.AppSettings["CGIPLSQLGateway"] : "THOTH"; }
        }

        public static string CGIGatewayIVersion
        {
            // note: the Thoth Gateway reports itself as version "3" by default (like the Apex Listener)
            get { return ConfigurationSettings.AppSettings["CGIGatewayIVersion"] != null ? ConfigurationSettings.AppSettings["CGIGatewayIVersion"] : "3"; }
        }

        public static bool IsValidDad(string dadName)
        {
            // check if dad entry exists in config file
            return _dadSection.Dads.IndexOf(dadName) > -1;
        }

        public string AuthenticationMode
        {
            get { return getVal("AuthenticationMode", ""); }
        }

        public string ErrorStyle
        {
            get { return getVal("ErrorStyle", "None"); }
        }
        
        public string DatabaseConnectString
        {
            get { return getVal("DatabaseConnectString", ""); }
        }

        public string DatabaseConnectStringAttributes
        {
            get { return getVal("DatabaseConnectStringAttributes", "Enlist=false"); }
        }
        
        public string DatabaseUserName
        {
            get { return getVal("DatabaseUserName", ""); }
        }

        public string DatabasePassword
        {
            get { return getVal("DatabasePassword", ""); }
        }

        private string NLSLanguageString
        {
            get { return getVal("NLSLanguage", "AMERICAN_AMERICA.AL32UTF8"); }
        }

        public string NLSLanguage
        {
            get { return _nlsLanguage; }
        }

        public string NLSTerritory
        {
            get { return _nlsTerritory; }
        }

        public string NLSCharset
        {
            get { return _nlsCharset; }
        }

        public string IANACharset
        {
            // This is the IANA (Internet Assigned Number Authority) equivalent of the REQUEST_CHARSET CGI environment variable.
            get { return getVal("IANACharset", "UTF-8"); }
        }
        
        public string ExclusionList
        {
            get { return getVal("ExclusionList", ""); }
        }

        public string InclusionList
        {
            get { return getVal("InclusionList", ""); }
        }
        
        public string RequestValidationFunction
        {
            get { return getVal("RequestValidationFunction", ""); }
        }

        public string DefaultPage
        {
            get { return getVal("DefaultPage", ""); }
        }

        public string DocumentPath
        {
            get { return getVal("DocumentPath", "docs"); }
        }

        public string DocumentProcedure
        {
            get { return getVal("DocumentProcedure", "process_download"); }
        }
        
        public string DocumentTableName
        {
            get { return getVal("DocumentTableName", ""); }
        }
 
        public int DocumentMaxUploadSize
        {
            get { return getIntVal("DocumentMaxUploadSize", 0); }
        }

        public int DocumentMaxNameLength
        {
            get { return getIntVal("DocumentMaxNameLength", 90); }
        }
        
        public string DocumentFilePath
        {
            get { return getVal("DocumentFilePath", ""); }
        }

        public string DocumentXdbPath
        {
            get { return getVal("DocumentXdbPath", ""); }
        }
        
        public string PathAlias
        {
            get { return getVal("PathAlias", ""); }
        }

        public string PathAliasProcedure
        {
            get { return getVal("PathAliasProcedure", ""); }
        }

        public bool PathAliasIncludeParameters
        {
            get { return getBoolVal("PathAliasIncludeParameters", false); }
        }

        public string BeforeProcedure
        {
            get { return getVal("BeforeProcedure", ""); }
        }

        public string AfterProcedure
        {
            get { return getVal("AfterProcedure", ""); }
        }

        public int[] BindBucketLengths
        {
            get {

                string[] input = getVal("BindBucketLengths", "4,20,100,400").Split(',');

                return Array.ConvertAll<string, int>(input, delegate(string s) { return int.Parse(s); } );
            }
        }

        public int[] BindBucketWidths
        {
            get
            {
                string[] input = getVal("BindBucketWidths", "32,128,1024,2048,4000,8000,16000,32767").Split(',');

                return Array.ConvertAll<string, int>(input, delegate(string s) { return int.Parse(s); });
            }
        }

        public int FetchBufferSize
        {
            get { return getIntVal("FetchBufferSize", 200); }
        }

        public string InvocationProtocol
        {
            get { return getVal("InvocationProtocol", INVOCATION_PROTOCOL_CGI); }
        }

        public string SoapTargetNamespace
        {
            get { return getVal("SoapTargetNamespace", "http://tempuri.org/myservice"); }
        }

        public string SoapFaultStyle
        {
            get { return getVal("SoapFaultStyle", "Generic"); }
        }

        public string SoapFaultStringTag
        {
            get { return getVal("SoapFaultStringTag", "usrerr"); }
        }

        public string SoapFaultDetailTag
        {
            get { return getVal("SoapFaultDetailTag", "errinfo"); }
        }

        public string SoapDateFormat
        {
            get { return getVal("SoapDateFormat", "YYYY-MM-DD\"T\"HH24:MI:SS\".000\""); }
        }
        
        public string XdbAlias
        {
            get { return getVal("XdbAlias", ""); }
        }

        public string XdbAliasRoot
        {
            get { return getVal("XdbAliasRoot", ""); }
        }
    
    }
}