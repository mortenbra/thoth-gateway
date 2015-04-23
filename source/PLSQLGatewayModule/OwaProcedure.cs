using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Collections.Specialized;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Represents a PL/SQL procedure that will be executed by the gateway.
    /// </summary>
    public class OwaProcedure
    {

        public OwaProcedure()
        {
        }
        
        public string MainProc
        {
            get;
            set;
        }

        public string MainProcParams
        {
            get;
            set;
        }
        
        public string BeforeProc
        {
            get;
            set;
        }

        public string AfterProc
        {
            get;
            set;
        }

        public string RequestValidationFunction
        {
            get;
            set;
        }

        public bool IsSoapRequest
        {
            get;
            set;
        }

        public bool CheckForDownload
        {
            get;
            set;
        }

        public string SQLStatement
        {
            get;
            set;
        }

        public string BuildSQLStatement()
        {
            //return BuildSQLStatement(new NameValueCollection(), new List<NameValuePair>());
            return BuildSQLStatement(null, null);
        }

        public string BuildSQLStatement(NameValueCollection actualParams, List<NameValuePair> requestParams)
        {
            string sql = "";
            string sqlParams = "";
            bool paramExists = false;

            if (requestParams != null && actualParams != null)
            {
                // check request parameters against actual parameters (from metadata), and include only those that actually exist
                // the purpose is to avoid errors due to extra parameters added (dynamically, and typically on the client side)
                // a similar check is performed when binding actual values into the bind variable placeholders (see OracleInterface.ExecuteMainProc)

                int paramCount = 0;

                foreach (NameValuePair nvp in requestParams)
                {

                    paramExists = (actualParams.GetValues(nvp.Name) != null);

                    if (paramExists)
                    {
                        paramCount = paramCount + 1;
                        sqlParams = StringUtil.AppendStr(sqlParams, nvp.Name + " => :b" + paramCount.ToString(), ", ");
                    }
                    
                }

                MainProcParams = sqlParams;
                
            }

            sql = MainProc;

            if (MainProcParams.Length > 0)
            {
                sql = sql + " (" + MainProcParams + ")";
            }

            if (IsSoapRequest)
            {
                // assume SOAP call is a function
                sql = ":p_return_value := to_clob(" + sql + ")";
            }

            sql = sql + ";";
            
            if (BeforeProc.Length > 0)
            {
                sql = BeforeProc + "; " + sql;
            }

            if (AfterProc.Length > 0)
            {
                sql = sql + " " + AfterProc + ";";
            }

            if (CheckForDownload)
            {
                sql = sql + " if wpg_docload.is_file_download then :p_is_download := 1; else :p_is_download := 0; end if;";
            }

            if (RequestValidationFunction.Length > 0)
            {
                sql = "begin if " + RequestValidationFunction + " (:p_proc_name) then " + sql + " else raise_application_error (-20000, 'Procedure call forbidden by request validation function (" + RequestValidationFunction + ")'); end if; end;";
            }
            else
            {
                sql = "begin " + sql + " end;";
            }

            // store the generated statement (used on error page)
            SQLStatement = sql;

            return sql;
        }

    
    }
}
