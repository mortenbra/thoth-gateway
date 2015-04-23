using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Collections.Specialized;

namespace PLSQLGatewayModule
{
    public class OracleObjectInfo
    {

        public OracleObjectInfo()
        {
        }
        
        public string SchemaName
        {
            get;
            set;
        }

        public string PackageName
        {
            get;
            set;
        }

        public string ObjectName
        {
            get;
            set;
        }

        public string ObjectType
        {
            get;
            set;
        }

        public int ObjectId
        {
            get;
            set;
        }

        public NameValueCollection Parameters
        {
            get;
            set;
        }
    
    }
}
