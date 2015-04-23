using System;
using System.Collections;
using System.Collections.Generic;
using System.Web;
using System.Data;
using Oracle.ManagedDataAccess;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Specialized;
using Oracle.ManagedDataAccess.Types;
using System.Text;
using log4net;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Handles the database connection to Oracle and executes SQL and PL/SQL
    /// </summary>
    public class OracleInterface
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(PLSQLHttpModule));

        public const int PLSQL_MAX_STR_SIZE = 32767;
        public const int ORA_APPLICATION_ERROR_BEGIN = 20000;
        public const int ORA_APPLICATION_ERROR_END = 20999;
        
        private string _dadName = "";
        private DadConfiguration _dadConfig = null;
        private OracleParameterCache _opc = null;

        private string _connStr = "";
        private OracleConnection _conn;
        private OracleTransaction _txn;
        private string _lastError = "";
        private int _lastErrorCode = 0;

        private bool _moreToFetch = true;
        private bool _connected = false;

        private string _soapReturnValue = "";

        private string _wsdlBody = "";
        private string _soapBody = "";

      public OracleInterface(GatewayRequest req, OracleParameterCache opc)
      {

          _dadName = req.DadName;
          _dadConfig = req.DadConfig;
          _opc = opc;

          string dbUsername = _dadConfig.DatabaseUserName;
          string dbPassword = _dadConfig.DatabasePassword;

          // Integrated Windows Authentication (use in combination with Oracle proxy authentication)
          if (dbUsername == "LOGON_USER")
          {
              dbUsername = req.WindowsUsername;
              // if username contains backslash (domain\user), add double quotes to username
              if (dbUsername.IndexOf("\\") > -1) {
                  dbUsername = "\"" + dbUsername + "\"";
              }
          }

          if (dbUsername == "LOGON_USER_NO_DOMAIN")
          {
              dbUsername = req.WindowsUsernameNoDomain;
          }
                    
          // for connection string attributes, see http://download.oracle.com/docs/html/E15167_01/featConnecting.htm#i1006259
          _connStr = "User Id=" + dbUsername + ";Password=" + dbPassword + ";Data Source=" + _dadConfig.DatabaseConnectString + ";" + _dadConfig.DatabaseConnectStringAttributes;
          
          // careful with this one, it will expose the passwords in the log
          // use it just for additional debugging during development
          // logger.Debug("Connection string: " + _connStr);

          // Connect to Oracle
          if (logger.IsDebugEnabled)
          {
              logger.Debug("Connecting with user " + dbUsername + " to " + _dadConfig.DatabaseConnectString + "...");
          }
          
          _conn = new OracleConnection(_connStr);

          try
          {
              _conn.Open();
              _connected = true;
              if (logger.IsDebugEnabled)
              {
                  logger.Debug("Connected to Oracle " + _conn.ServerVersion);
              }
          }
          catch (OracleException e)
          {
              _lastError = e.Message;
              logger.Error("Failed to connect to database: " + e.Message);
          }

          if (_connected)
          {
              _txn = _conn.BeginTransaction();

              // setup National Language Support (NLS)

              string sql = "alter session set nls_language='" + _dadConfig.NLSLanguage + "' nls_territory='" + _dadConfig.NLSTerritory + "'";
              ExecuteSQL(sql, new ArrayList());

              //OracleGlobalization glb = OracleGlobalization.GetThreadInfo();
              //logger.Debug ("ODP.NET Client Character Set: " + glb.ClientCharacterSet);

              // ensure a stateless environment by resetting package state
              sql = "begin dbms_session.modify_package_state(dbms_session.reinitialize); end;";
              ExecuteSQL(sql, new ArrayList());

              if (_dadConfig.InvocationProtocol == DadConfiguration.INVOCATION_PROTOCOL_SOAP)
              {
                  // use SOAP date encoding
                  sql = "alter session set nls_date_format = '" + _dadConfig.SoapDateFormat + "'";
                  ExecuteSQL(sql, new ArrayList());
              }

          }

      }

      public void DoCommit()
      {
          if (logger.IsDebugEnabled)
          {
              logger.Debug("Committing database transaction...");
          }
          
          _txn.Commit();

          if (logger.IsDebugEnabled)
          {
              logger.Debug("Commit completed.");
          }
      }

      public void DoRollback()
      {
          if (_connected)
          {
              logger.Debug("Rolling back database transaction...");
              _txn.Rollback();
              logger.Debug("Rollback completed.");
          }
      }

      public void CloseConnection()
      {
          if (logger.IsDebugEnabled)
          {
              logger.Debug("Closing database connection...");
          }
          
          _conn.Close();
          _conn.Dispose();

          if (logger.IsDebugEnabled)
          {
              logger.Debug("Database connection closed.");
          }
      }

      public string GetLastErrorText()
      {
          return _lastError;
      }

      public int GetLastErrorCode()
      {
          return _lastErrorCode;
      }
        
      public bool ExecuteSQL(string sql, ArrayList paramValues)
      {

          OracleCommand cmd = new OracleCommand(sql, _conn);

          int paramCount = 0;

          foreach (string s in paramValues)
          {
              paramCount = paramCount + 1;
              OracleParameter p = cmd.Parameters.Add("b" + paramCount.ToString(), OracleDbType.Varchar2, s, ParameterDirection.Input);
          }

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              cmd.ExecuteNonQuery();
              _lastError = "";
          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return false;
          }

          return true;

      }

      /// <summary>
      /// given an array of valid int values, return the first value equal to or above the given value
      /// </summary>
      /// <param name="size"></param>
      /// <param name="validSizes"></param>
      /// <returns></returns>
      private int GetNextValidSize(int size, int[] validSizes)
      {
          int returnValue = size;

          for (int i = 0; i < validSizes.Length; i++)
          {
              if (validSizes[i] >= size)
              {
                  returnValue = validSizes[i];
                  break;
              }
          }

          return returnValue;
      }

      public bool ExecuteMainProc(OwaProcedure owaProc, List<NameValuePair> paramList, bool describeProc, string procName)
      {

          int[] bindBucketLengths = _dadConfig.BindBucketLengths;
          int[] bindBucketWidths = _dadConfig.BindBucketWidths;

          NameValueCollection procParams = new NameValueCollection();
          string dataType = "";
          bool paramExists = false;

          string sql = "";

          int paramCount = 0;

          if (describeProc)
          {
              procParams = DescribeSingleProc(_dadName, procName);
              sql = owaProc.BuildSQLStatement(procParams, paramList);
          }
          else
          {
              sql = owaProc.BuildSQLStatement();
          }

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter pReturnValue = null;
          OracleParameter pIsDownload = null;
          
          if (owaProc.RequestValidationFunction.Length > 0)
          {
              OracleParameter pName = cmd.Parameters.Add("p_proc_name", OracleDbType.Varchar2, PLSQL_MAX_STR_SIZE, procName, ParameterDirection.Input);
          }

          if (owaProc.IsSoapRequest)
          {
              pReturnValue = cmd.Parameters.Add("p_return_value", OracleDbType.Clob, ParameterDirection.Output);
          }

          foreach (NameValuePair nvp in paramList)
          {

              if (describeProc)
              {
                  paramExists = (procParams.GetValues(nvp.Name) != null);
                  if (paramExists)
                  {
                      dataType = procParams.GetValues(nvp.Name)[0];
                  }
              }
              else
              {
                  paramExists = true;
                  dataType = "";
              }

              if (paramExists)
              {
                  paramCount = paramCount + 1;

                  if (nvp.ValueType == ValueType.ArrayValue || dataType == "PL/SQL TABLE")
                  {
                      OracleParameter p = cmd.Parameters.Add("b" + paramCount.ToString(), OracleDbType.Varchar2, ParameterDirection.Input);
                      p.CollectionType = OracleCollectionType.PLSQLAssociativeArray;
                      p.Value = nvp.ValuesAsArray;
                      //p.Size = nvp.Values.Count();
                      p.Size = GetNextValidSize(nvp.Values.Count, bindBucketLengths);
                  }
                  else
                  {
                      if (nvp.Value.Length > PLSQL_MAX_STR_SIZE || dataType == "CLOB")
                      {
                          OracleParameter p = cmd.Parameters.Add("b" + paramCount.ToString(), OracleDbType.Clob, nvp.Value.Length, nvp.Value, ParameterDirection.Input);
                      }
                      else
                      {
                          OracleParameter p = cmd.Parameters.Add("b" + paramCount.ToString(), OracleDbType.Varchar2, GetNextValidSize(nvp.Value.Length, bindBucketWidths), nvp.Value, ParameterDirection.Input);
                      }
                  }

              }
              else
              {
                  logger.Warn(string.Format("Mismatch between metadata ({0} parameters) and actual invocation ({1} parameters): Parameter '{2}' was not found in metadata, and was skipped to avoid errors.", procParams.Count, paramList.Count, nvp.Name));
              }

          }

          if (owaProc.CheckForDownload)
          {
              pIsDownload = cmd.Parameters.Add("p_is_download", OracleDbType.Int32, ParameterDirection.Output);
          }

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              cmd.ExecuteNonQuery();
              _lastError = "";
              _lastErrorCode = 0;
          }
          catch (OracleException e)
          {
              if (logger.IsDebugEnabled)
              {
                  logger.Debug("Command failed: " + e.Message);
              }
              _lastError = e.Message;
              _lastErrorCode = e.Number;
              return false;
          }

          if (owaProc.CheckForDownload)
          {
            int isDownload = (int)(OracleDecimal)pIsDownload.Value;
            IsDownload = (isDownload != 0);
            if (logger.IsDebugEnabled && IsDownload)
            {
                logger.Debug("  IsDownload = True");
            }
          }
          
          if (owaProc.IsSoapRequest)
          {

              if (pReturnValue.Status == OracleParameterStatus.NullFetched)
              {
                  _soapReturnValue = "";
              }
              else
              {
                  OracleClob tempClob = (OracleClob)pReturnValue.Value;
                  _soapReturnValue = tempClob.Value;
                  
              }
          }

          return true;

      }

        
      public bool SetupOwaCGI(List<NameValuePair> serverVariables, string hostName, string hostAddress, string basicAuthUsername, string basicAuthPassword)
      {

          // note: as of Thoth Gateway 1.3.7, the following elements have been removed as no longer relevant for "modern" usage of the gateway
          // * setting up the owa.ip_address record based on the client IP address (does not work with IPv6 addresses anyway)
          // * setting up the hostname, user id and password for basic authentication (the Apex Listener does not do this either)
          // * calling owa.initialize() before owa.init_cgi_env() (the Apex Listener does not do this either)
          
          // htbuf_len: reduce this limit based on your worst-case character size.
          // For most character sets, this will be 2 bytes per character, so the limit would be 127.
          // For UTF8 Unicode, it's 3 bytes per character, meaning the limit should be 85.
          // For the newer AL32UTF8 Unicode, it's 4 bytes per character, and the limit should be 63.

          string sql = "begin owa.init_cgi_env(:ecount, :namarr, :valarr); htp.init; htp.htbuf_len := 63; end;";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          // note: even though the parameters are named, ODP.NET maps the parameters to bind variables by position unless cmd.BindByName is true
          // see http://oradim.blogspot.com/2009/03/odpnet-tip-bind-variables-bindbyname.html

          OracleParameter Param1 = cmd.Parameters.Add("ecount", OracleDbType.Int32, ParameterDirection.Input);
          OracleParameter Param2 = cmd.Parameters.Add("namarr", OracleDbType.Varchar2, ParameterDirection.Input);
          OracleParameter Param3 = cmd.Parameters.Add("valarr", OracleDbType.Varchar2, ParameterDirection.Input);

          string[] paramNameArray = new string[serverVariables.Count];
          string[] paramValueArray = new string[serverVariables.Count];

          int count = 0;
          foreach (NameValuePair nvp in serverVariables)
          {
              paramNameArray[count] = nvp.Name;
              paramValueArray[count] = nvp.Value;
              count = count + 1;

              //logger.Debug("CGI param name = " + nvp.Name + ", value = " + nvp.Value);
          }

          Param1.Value = count;
          
          Param2.CollectionType = OracleCollectionType.PLSQLAssociativeArray;
          Param2.Value = paramNameArray;
          
          Param3.CollectionType = OracleCollectionType.PLSQLAssociativeArray;
          Param3.Value = paramValueArray;

          if (logger.IsDebugEnabled)
          {
              logger.Debug("Executing SQL: " + cmd.CommandText);
          }

          try
          {
              cmd.ExecuteNonQuery();
              _lastError = "";
          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return false;
          }

          return true;

      }

      public bool MoreToFetch()
      {
          return _moreToFetch;
      }

      public bool Connected()
      {
          return _connected;
      }

      public string GetOwaPageFragment()
      {
          int linesToFetch = _dadConfig.FetchBufferSize;
          int linesFetched = 0;

          StringBuilder pageFragment = new StringBuilder();

          string sql = "begin owa.get_page(:linearr, :nlines); end;";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter Param1 = cmd.Parameters.Add("linearr", OracleDbType.Varchar2, ParameterDirection.Output);
          OracleParameter Param2 = cmd.Parameters.Add("nlines", OracleDbType.Int32, ParameterDirection.InputOutput);

          int[] bindSizes = new int[linesToFetch];

          for (int i = 0; i < bindSizes.Length; i++)
          {
              bindSizes[i] = 256;
          }

          Param1.CollectionType = OracleCollectionType.PLSQLAssociativeArray;
          Param1.Value = null;
          Param1.Size = linesToFetch;
          Param1.ArrayBindSize = bindSizes;

          Param2.Value = linesToFetch;

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              cmd.ExecuteNonQuery();
              _lastError = "";

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
          }

          linesFetched = (int)((OracleDecimal)Param2.Value);

          if (linesFetched < linesToFetch)
          {
              _moreToFetch = false;
          }
          else
          {
              _moreToFetch = true;
          }

          //for (int i = 0; i < Param1.Size; i++)
          for (int i = 0; i < linesFetched; i++)
          {
              pageFragment.Append((Param1.Value as Array).GetValue(i));
          }
          
          return pageFragment.ToString();

      }


      public bool UploadFiles(List<UploadedFile> files)
      {

          logger.Debug(string.Format("Uploading {0} file(s)...", files.Count));

          string documentFilePath = _dadConfig.DocumentFilePath;
          string documentXdbPath = _dadConfig.DocumentXdbPath;

          for (int i = 0; i < files.Count; i++)
			{

              HttpPostedFile f = files[i].PostedFile;

              if (_dadConfig.DocumentMaxUploadSize > 0 && f.InputStream.Length > _dadConfig.DocumentMaxUploadSize)
              {
                  logger.Warn("File size of file " + f.FileName + " (" + f.InputStream.Length.ToString() + " bytes) exceeds allowed maximum (" + _dadConfig.DocumentMaxUploadSize.ToString() + " bytes), skipping upload");
              }
              else
              {

                  if (documentFilePath.Length > 0)
                  {

                      string fileLocation = documentFilePath + "\\" + files[i].UniqueFileName;
                      logger.Debug("Uploading to file system: " + fileLocation);

                      try
                      {
                          files[i].PostedFile.SaveAs(fileLocation);
                      }
                      catch (Exception e)
                      {
                          logger.Error("The SaveAs operation failed: " + e.Message);
                          _lastError = e.Message;
                          return false;
                      }
                  }
                  else if (documentXdbPath.Length > 0)
                  {
                      string resourceLocation = documentXdbPath + "/" + files[i].UniqueFileName;
                      logger.Debug("Uploading to XDB Repository: " + resourceLocation);

                      // read the file into a byte array
                      byte[] fileData = new byte[f.InputStream.Length];
                      f.InputStream.Read(fileData, 0, System.Convert.ToInt32(f.InputStream.Length));

                      // see http://stanford.edu/dept/itss/docs/oracle/10g/appdev.101/b10790/xdb19rpl.htm#i1028077
                      // see http://www.adp-gmbh.ch/ora/misc/globalization.html#char_sets

                      // TODO: why do we have to specify a character set for binary files (blobs) ?

                      string sql = "declare l_result boolean; begin l_result := dbms_xdb.createresource(:p_path, :p_data, nls_charset_id(:p_charset)); if l_result then :p_result := 1; else :p_result := 0; end if; end;";

                      OracleCommand cmd = new OracleCommand(sql, _conn);

                      OracleParameter p1 = cmd.Parameters.Add("p_path", OracleDbType.Varchar2, resourceLocation, ParameterDirection.Input);
                      OracleParameter p2 = cmd.Parameters.Add("p_data", OracleDbType.Blob, fileData, ParameterDirection.Input);
                      OracleParameter p3 = cmd.Parameters.Add("p_charset", OracleDbType.Varchar2, _dadConfig.NLSCharset, ParameterDirection.Input);
                      OracleParameter p4 = cmd.Parameters.Add("p_result", OracleDbType.Int32, ParameterDirection.Output);

                      logger.Debug("Executing SQL: " + cmd.CommandText);

                      try
                      {
                          cmd.ExecuteNonQuery();
                          _lastError = "";

                          int xdbResult = (int)(OracleDecimal)p4.Value;
                          return (xdbResult != 0);

                      }
                      catch (OracleException e)
                      {
                          logger.Error("Command failed: " + e.Message);
                          _lastError = e.Message;
                          return false;
                      }

                  }
                  else
                  {
                      logger.Debug("Uploading to database table with unique file name: " + files[i].UniqueFileName);

                      // read the file into a byte array
                      byte[] fileData = new byte[f.InputStream.Length];
                      f.InputStream.Read(fileData, 0, System.Convert.ToInt32(f.InputStream.Length));

                      string sql = "insert into " + _dadConfig.DocumentTableName + " (name, mime_type, doc_size, dad_charset, last_updated, content_type, blob_content) values (:p_name, :p_mime_type, :p_doc_size, :p_dad_charset, sysdate, 'BLOB', :p_blob_content )";

                      OracleCommand cmd = new OracleCommand(sql, _conn);

                      OracleParameter p1 = cmd.Parameters.Add("p_name", OracleDbType.Varchar2, files[i].UniqueFileName, ParameterDirection.Input);
                      OracleParameter p2 = cmd.Parameters.Add("p_mime_type", OracleDbType.Varchar2, f.ContentType, ParameterDirection.Input);
                      OracleParameter p3 = cmd.Parameters.Add("p_doc_size", OracleDbType.Int32, f.InputStream.Length, ParameterDirection.Input);
                      OracleParameter p4 = cmd.Parameters.Add("p_dad_charset", OracleDbType.Varchar2, _dadConfig.NLSCharset, ParameterDirection.Input);
                      OracleParameter p5 = cmd.Parameters.Add("p_blob_content", OracleDbType.Blob, fileData, ParameterDirection.Input);

                      logger.Debug("Executing SQL: " + cmd.CommandText);

                      try
                      {
                          cmd.ExecuteNonQuery();
                          _lastError = "";
                      }
                      catch (OracleException e)
                      {
                          logger.Error("Command failed: " + e.Message);
                          _lastError = e.Message;
                          return false;
                      }
                  }

              }

          }

          return true;

      }

      public bool IsDownload
      {
          get;
          set;
      }

      public string GetDownloadInfo()
      {

          string sql = "begin wpg_docload.get_download_file (:p_download_info); end;";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter p = cmd.Parameters.Add("p_download_info", OracleDbType.Varchar2, ParameterDirection.Output);

          p.Size = 4000;

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              cmd.ExecuteNonQuery();

              string downloadInfo = (string)(OracleString)p.Value;

              logger.Debug("  download info = " + downloadInfo);

              _lastError = "";

              return downloadInfo;

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return "";
          }

      }


      public byte[] GetDownloadFile(string fileType)
      {

          string sql = "";
          OracleDbType fileParameterType = OracleDbType.Blob;

          if (fileType == "B")
          {
              sql = "begin wpg_docload.get_download_blob (:b1); end;";
              fileParameterType = OracleDbType.Blob;
          }
          else if (fileType == "F")
          {
              sql = "begin wpg_docload.get_download_bfile (:b1); end;";
              fileParameterType = OracleDbType.BFile;
          }

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter p = cmd.Parameters.Add("b1", fileParameterType, ParameterDirection.Output);

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              cmd.ExecuteNonQuery();

              byte[] byteData = new byte[0];

              // fetch the value of Oracle parameter into the byte array
              byteData = (byte[])((OracleBlob)(p.Value)).Value;

              _lastError = "";

              return byteData;

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return new byte[0];
          }

      }

      public byte[] GetDownloadFileFromDocTable(string fileName)
      {

          string sql = "select blob_content from " + _dadConfig.DocumentTableName + " where name = :b1";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter p = cmd.Parameters.Add("b1", OracleDbType.Varchar2, 256, fileName, ParameterDirection.Input);

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {

              OracleDataReader dr = cmd.ExecuteReader();

              byte[] byteData = new byte[0];

              while (dr.Read())
              {
                  OracleBlob blob = dr.GetOracleBlob(0);
                  byteData = (byte[])blob.Value;
              }

              _lastError = "";

              return byteData;

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return new byte[0];
          }

      }

      private OracleObjectInfo ResolveName(string procName)
      {
          OracleObjectInfo ooi = new OracleObjectInfo();

          logger.Debug("Attempting to resolve name " + procName);

          string sql = "begin dbms_utility.name_resolve (:p_name, 1, :p_schema, :p_part1, :p_part2, :p_dblink, :p_part1_type, :p_object_number); end;";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter p1 = cmd.Parameters.Add("p_name", OracleDbType.Varchar2, ParameterDirection.Input);
          p1.Value = procName;

          OracleParameter p2 = cmd.Parameters.Add("p_schema", OracleDbType.Varchar2, ParameterDirection.Output);
          p2.Size = 30;
          OracleParameter p3 = cmd.Parameters.Add("p_part1", OracleDbType.Varchar2, ParameterDirection.Output);
          p3.Size = 30;
          OracleParameter p4 = cmd.Parameters.Add("p_part2", OracleDbType.Varchar2, ParameterDirection.Output);
          p4.Size = 30;
          OracleParameter p5 = cmd.Parameters.Add("p_dblink", OracleDbType.Varchar2, ParameterDirection.Output);
          p5.Size = 30;
          OracleParameter p6 = cmd.Parameters.Add("p_part1_type", OracleDbType.Varchar2, ParameterDirection.Output);
          p6.Size = 30;
          OracleParameter p7 = cmd.Parameters.Add("p_object_number", OracleDbType.Int32, ParameterDirection.Output);

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              cmd.ExecuteNonQuery();
              _lastError = "";


              if (p2.Status == OracleParameterStatus.NullFetched)
              {
                  ooi.SchemaName = "";
              }
              else
              {
                  ooi.SchemaName = (string)(OracleString)p2.Value;
              }
              
              if (p3.Status == OracleParameterStatus.NullFetched)
              {
                  ooi.PackageName = "";
              }
              else
              {
                  ooi.PackageName = (string)(OracleString)p3.Value;
              }

              if (p4.Status == OracleParameterStatus.NullFetched)
              {
                  ooi.ObjectName = "";
              }
              else
              {
                  ooi.ObjectName = (string)(OracleString)p4.Value;
              }
              
              ooi.ObjectType = (string)(OracleString)p6.Value;
              ooi.ObjectId = (int)(OracleDecimal)p7.Value;

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
          }

          return ooi;

      }

      private List<string> GetPackageFunctions(string schemaName, string packageName)
      {
          List<string> functions = new List<string>();

          string sql = "select distinct object_name from all_arguments where owner = :p_owner and package_name = :p_package_name and position = 0 order by 1";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          cmd.CommandText = sql;
          cmd.Parameters.Clear();
          cmd.Parameters.Add("p_owner", OracleDbType.Varchar2, schemaName, ParameterDirection.Input);
          cmd.Parameters.Add("p_package_name", OracleDbType.Varchar2, packageName, ParameterDirection.Input);

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              OracleDataReader dr = cmd.ExecuteReader();

              while (dr.Read())
              {
                  functions.Add(dr[0].ToString());
              }

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return functions;
          }

          return functions;
      }

      private NameValueCollection GetProcParams(string schemaName, string packageName, string objectName)
      {
          NameValueCollection procParams = new NameValueCollection();

          string sql = "";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          if (packageName == "")
          {
              sql = "select argument_name, data_type from all_arguments where owner = :p_owner and package_name is null and object_name = :p_object_name and argument_name is not null order by overload, sequence";

              cmd.CommandText = sql;
              cmd.Parameters.Clear();
              cmd.Parameters.Add("p_owner", OracleDbType.Varchar2, schemaName, ParameterDirection.Input);
              cmd.Parameters.Add("p_object_name", OracleDbType.Varchar2, objectName, ParameterDirection.Input);

          }
          else
          {
              sql = "select argument_name, data_type from all_arguments where owner = :p_owner and package_name = :p_package_name and object_name = :p_object_name and argument_name is not null order by overload, sequence";

              cmd.CommandText = sql;
              cmd.Parameters.Clear();
              cmd.Parameters.Add("p_owner", OracleDbType.Varchar2, schemaName, ParameterDirection.Input);
              cmd.Parameters.Add("p_package_name", OracleDbType.Varchar2, packageName, ParameterDirection.Input);
              cmd.Parameters.Add("p_object_name", OracleDbType.Varchar2, objectName, ParameterDirection.Input);

          }

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {
              OracleDataReader dr = cmd.ExecuteReader();

              while (dr.Read())
              {
                  procParams.Add(dr[0].ToString(), dr[1].ToString());
              }


          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return procParams;
          }

          return procParams;

      }
        
      private NameValueCollection DescribeSingleProc(string dadName, string procName)
      {

          NameValueCollection procParams = _opc.GetParamsFromCache(dadName, procName);

          if (procParams.Count == 0)
          {
              logger.Debug("Procedure metadata not in cache, looking it up from database...");

              OracleObjectInfo ooi = ResolveName(procName);

              if (_lastError != "")
              {
                  return procParams; 
              }

              procParams = GetProcParams(ooi.SchemaName, ooi.PackageName, ooi.ObjectName);

              if (_lastError != "")
              {
                  return procParams;
              }

              _opc.SetParamsInCache(dadName, procName, procParams);

          }
          else
          {
              logger.Debug("Found procedure metadata in cache.");
          }

          return procParams;

      }

      private string GetSoapDataType(string oracleDatatype)
      {

          // see http://www.w3.org/TR/xmlschema-2/#built-in-primitive-datatypes
          
          switch (oracleDatatype)
          {
              case "CHAR":
              case "VARCHAR":
              case "VARCHAR2":
              case "CLOB":
                  return "string";
              case "NUMBER":
                  return "double";
              case "INTEGER":
                  return "int";
              case "DATE":
                  return "dateTime";
              default:
                  return "string";
          }
      }

      public bool GenerateWsdl(string serverName, string moduleName, string dadName, string procName)
      {
          OracleObjectInfo ooi = ResolveName(procName);

          List<string> functionList = new List<string>();

          string serviceName = "";

          if (ooi.PackageName.Length > 0 && ooi.ObjectName.Length == 0)
          {
              // get all the functions in the package, and get their parameters
              functionList = GetPackageFunctions(ooi.SchemaName, ooi.PackageName);
              serviceName = StringUtil.PrettyStr(ooi.PackageName);
          }
          else
          {
              functionList.Add(ooi.ObjectName);
              serviceName = StringUtil.PrettyStr(procName) + "Service";
          }
          
          NameValueCollection procParams = null;

          StringBuilder sb = new StringBuilder();

          sb.Append("<?xml version='1.0' encoding='utf-8'?>");
          sb.AppendFormat("<wsdl:definitions xmlns:soap='http://schemas.xmlsoap.org/wsdl/soap/' xmlns:soapenc='http://schemas.xmlsoap.org/soap/encoding/' xmlns:mime='http://schemas.xmlsoap.org/wsdl/mime/' xmlns:tns='{0}' xmlns:s='http://www.w3.org/2001/XMLSchema' xmlns:soap12='http://schemas.xmlsoap.org/wsdl/soap12/' xmlns:http='http://schemas.xmlsoap.org/wsdl/http/' targetNamespace='{0}' xmlns:wsdl='http://schemas.xmlsoap.org/wsdl/' >", _dadConfig.SoapTargetNamespace);

          sb.Append("<wsdl:types>");

          sb.AppendFormat("<s:schema elementFormDefault='qualified' targetNamespace='{0}'>", _dadConfig.SoapTargetNamespace);

          foreach (string functionName in functionList)
          {
              string prettyProcName = StringUtil.PrettyStr(functionName);

              procParams = _opc.GetParamsFromCache(dadName, ooi.PackageName + "." + functionName);
              if (procParams.Count == 0)
              {
                  procParams = GetProcParams(ooi.SchemaName, ooi.PackageName, functionName);
                  _opc.SetParamsInCache(dadName, ooi.PackageName + "." + functionName, procParams);
              }
              
              sb.AppendFormat("<s:element name='{0}'>", prettyProcName);
              sb.Append("<s:complexType>");
              sb.Append("<s:sequence>");
              foreach (string s in procParams)
              {
                  sb.AppendFormat("<s:element minOccurs='1' maxOccurs='1' name='{0}' type='s:{1}' />", s.ToLower(), GetSoapDataType(procParams.GetValues(s)[0]));
              }
              sb.Append("</s:sequence>");
              sb.Append("</s:complexType>");
              sb.Append("</s:element>");

              sb.AppendFormat("<s:element name='{0}Response'>", prettyProcName);
              sb.Append("<s:complexType>");
              sb.Append("<s:sequence>");
              // for now, we only support returning one (string) value
              sb.AppendFormat("<s:element minOccurs='1' maxOccurs='1' name='{0}Result' nillable='true' type='s:string' />", prettyProcName);
              sb.Append("</s:sequence>");
              sb.Append("</s:complexType>");
              sb.Append("</s:element>");
              
          }

          sb.Append("</s:schema>");
          sb.Append("</wsdl:types>");

          foreach (string functionName in functionList)
          {
              string prettyProcName = StringUtil.PrettyStr(functionName);

              sb.AppendFormat("<wsdl:message name='{0}SoapIn'>", prettyProcName);
              sb.AppendFormat("<wsdl:part name='parameters' element='tns:{0}' />", prettyProcName);
              sb.Append("</wsdl:message>");

              sb.AppendFormat("<wsdl:message name='{0}SoapOut'>", prettyProcName);
              sb.AppendFormat("<wsdl:part name='parameters' element='tns:{0}Response' />", prettyProcName);
              sb.Append("</wsdl:message>");

          }

          sb.AppendFormat("<wsdl:portType name='{0}Soap'>", serviceName);
         
          foreach (string functionName in functionList)
          {
              string prettyProcName = StringUtil.PrettyStr(functionName);

              sb.AppendFormat("<wsdl:operation name='{0}'>", prettyProcName);
              sb.AppendFormat("<wsdl:input message='tns:{0}SoapIn' />", prettyProcName);
              sb.AppendFormat("<wsdl:output message='tns:{0}SoapOut' />", prettyProcName);
              sb.Append("</wsdl:operation>");

          }

          sb.Append("</wsdl:portType>");

          sb.AppendFormat("<wsdl:binding name='{0}Soap' type='tns:{0}Soap'>", serviceName);

          foreach (string functionName in functionList)
          {
              string prettyProcName = StringUtil.PrettyStr(functionName);

              sb.Append("<soap:binding transport='http://schemas.xmlsoap.org/soap/http' />");
              sb.AppendFormat("<wsdl:operation name='{0}'>", prettyProcName);
              sb.AppendFormat("<soap:operation soapAction='{0}/{1}' style='document' />", _dadConfig.SoapTargetNamespace, prettyProcName);
              sb.Append("<wsdl:input>");
              sb.Append("<soap:body use='literal' />");
              sb.Append("</wsdl:input>");
              sb.Append("<wsdl:output>");
              sb.Append("<soap:body use='literal' />");
              sb.Append("</wsdl:output>");
              sb.Append("</wsdl:operation>");

          }

          sb.Append("</wsdl:binding>");

          sb.AppendFormat("<wsdl:service name='{0}'>", serviceName);
          sb.AppendFormat("<wsdl:port name='{0}Soap' binding='tns:{0}Soap'>", serviceName);

          if (ooi.PackageName.Length > 0)
          {
              sb.AppendFormat("<soap:address location='http://{0}/{1}/{2}/{3}' />", serverName, moduleName, dadName, ooi.PackageName.ToLower());
          }
          else
          {
              sb.AppendFormat("<soap:address location='http://{0}/{1}/{2}/{3}' />", serverName, moduleName, dadName, procName);
          }
          sb.Append("</wsdl:port>");
          sb.Append("</wsdl:service>");

          sb.Append("</wsdl:definitions>");

          _wsdlBody = sb.ToString();
          return true;
      }

      public void GenerateSoapFault(int errorCode, string errorText)
      {
          string soapFaultStyle = _dadConfig.SoapFaultStyle;
          string soapFaultStringTag = _dadConfig.SoapFaultStringTag;
          string soapFaultDetailTag = _dadConfig.SoapFaultDetailTag;

          string faultString = "";
          string faultDetail = "";

          StringBuilder sb = new StringBuilder();

          sb.Append("<?xml version='1.0' encoding='utf-8'?>");
          sb.Append("<soap:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema' xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>");
          sb.Append("<soap:Body>");
          sb.Append("<soap:Fault>");
          if (IsApplicationError(errorCode))
          {
              logger.Debug("User-defined application error, code " + errorCode + ", returning SOAP Fault as Client type.");

              if (soapFaultStyle == "Raw")
              {
                  faultString = errorCode.ToString();
                  faultDetail = errorText;
              }
              else if (soapFaultStyle == "UserFriendly")
              {
                  faultString = StringUtil.GetTagValue(errorText, soapFaultStringTag, "The request was rejected with server error code " + errorCode.ToString() + ".");
                  faultDetail = StringUtil.GetTagValue(errorText, soapFaultDetailTag, "See server logs for details.");
              }
              else
              {
                  faultString = "Invalid request was rejected by server.";
                  faultDetail = "See server logs for details.";
              }

              sb.Append("<faultcode>soap:Client</faultcode>");
              sb.AppendFormat("<faultstring>{0}</faultstring>", faultString);
              sb.AppendFormat("<detail>{0}</detail>", HttpUtility.HtmlEncode(faultDetail));
          }
          else
          {
              logger.Error("Unhandled Oracle error, code " + errorCode + ", returning SOAP Fault as Server type.");

              if (soapFaultStyle == "Raw")
              {
                  faultString = errorCode.ToString();
                  faultDetail = errorText;
              }
              else if (soapFaultStyle == "UserFriendly")
              {
                  faultString = "The server encountered a problem (server error code " + errorCode.ToString() + ") during processing of the request.";
                  faultDetail = "See server logs for details.";
              }
              else
              {
                  faultString = "Unhandled server error.";
                  faultDetail = "See server logs for details.";
              }

              sb.Append("<faultcode>soap:Server</faultcode>");
              sb.AppendFormat("<faultstring>{0}</faultstring>", faultString);
              sb.AppendFormat("<detail>{0}</detail>", HttpUtility.HtmlEncode(faultDetail));
          }
          sb.Append("</soap:Fault>");
          sb.Append("</soap:Body>");
          sb.Append("</soap:Envelope>");

          _soapBody = sb.ToString();
      }

      public void GenerateSoapResponse(string procName)
      {
          BuildSoapResponse(procName, _soapReturnValue);
      }

      private void BuildSoapResponse(string procName, string resultValue)
      {
          string prettyProcName = "";

          int startPos = procName.IndexOf(".");

          if (startPos > -1)
          {
              // just get the last part (the actual function name)
              prettyProcName = StringUtil.PrettyStr(procName.Substring(startPos + 1));
          }
          else
          {
              prettyProcName = StringUtil.PrettyStr(procName);
          }

          StringBuilder sb = new StringBuilder();

          sb.Append("<?xml version='1.0' encoding='utf-8'?>");
          sb.Append("<soap:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema' xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>");
          sb.Append("<soap:Body>");
          sb.AppendFormat("<{0}Response xmlns='{1}'>", prettyProcName, _dadConfig.SoapTargetNamespace);
          if (resultValue.Length > 0)
          {
              sb.AppendFormat("<{0}Result>{1}</{0}Result>", prettyProcName, HttpUtility.HtmlEncode(resultValue));
          }
          else
          {
              sb.AppendFormat("<{0}Result xsi:nil='true' />", prettyProcName); // since zero-length strings are the same as NULLs in Oracle... 
          }
          sb.AppendFormat("</{0}Response>", prettyProcName);
          sb.Append("</soap:Body>");
          sb.Append("</soap:Envelope>");

          _soapBody = sb.ToString();
      
      }

      public string WsdlBody()
      {
          return _wsdlBody;
      }

      public string SoapBody()
      {
          return _soapBody;
      }

      public string XdbContentType
      {
          get;
          set;
      }

      public string XdbResourceName
      {
          get;
          set;
      }
        
      public bool GetXdbResource(string resourceName)
      {

          XdbResourceName = _dadConfig.XdbAliasRoot + "/" + resourceName;

          logger.Debug("Getting XDB resource metadata for " + XdbResourceName);

          string sql = "select xdburitype(:b1).getContentType() from dual";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter p = cmd.Parameters.Add("b1", OracleDbType.Varchar2, 2000, XdbResourceName, ParameterDirection.Input);

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {

              OracleDataReader dr = cmd.ExecuteReader();

              while (dr.Read())
              {
                  OracleString str = dr.GetOracleString(0);
                  XdbContentType = (string)str.Value;
                  logger.Debug("XDB ContentType is " + XdbContentType);
              }

              _lastError = "";

              return true;

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return false;
          }


      }

      public byte[] GetXdbResourceFile()
      {

          logger.Debug("Getting XDB resource " + XdbResourceName);

          string sql = "select xdburitype(:b1).getBlob() from dual";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter p = cmd.Parameters.Add("b1", OracleDbType.Varchar2, 2000, XdbResourceName, ParameterDirection.Input);

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {

              OracleDataReader dr = cmd.ExecuteReader();

              byte[] byteData = new byte[0];

              while (dr.Read())
              {
                  OracleBlob blob = dr.GetOracleBlob(0);
                  byteData = (byte[])blob.Value;
              }

              _lastError = "";

              return byteData;

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return new byte[0];
          }

      }


      public string GetXdbResourceText()
      {

          logger.Debug("Getting XDB resource " + XdbResourceName);

          string sql = "select xdburitype(:b1).getClob() from dual";

          OracleCommand cmd = new OracleCommand(sql, _conn);

          OracleParameter p = cmd.Parameters.Add("b1", OracleDbType.Varchar2, 2000, XdbResourceName, ParameterDirection.Input);

          logger.Debug("Executing SQL: " + cmd.CommandText);

          try
          {

              OracleDataReader dr = cmd.ExecuteReader();

              string s = "";

              while (dr.Read())
              {
                  OracleClob clob = dr.GetOracleClob(0);
                  s = (string)clob.Value;
              }

              _lastError = "";

              return s;

          }
          catch (OracleException e)
          {
              logger.Error("Command failed: " + e.Message);
              _lastError = e.Message;
              return "";
          }

      }

      public static Boolean IsApplicationError(int errorCode)
      {
          if (errorCode >= ORA_APPLICATION_ERROR_BEGIN && errorCode <= ORA_APPLICATION_ERROR_END)
          {
              return true;
          }
          else
          {
              return false;
          }
      }

    }

}
