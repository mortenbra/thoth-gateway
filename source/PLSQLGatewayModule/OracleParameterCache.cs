using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;
using System.Text;
using System.Collections;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Uses the ASP.NET application cache to store and retrieve PL/SQL parameter metadata
    /// </summary>
    public class OracleParameterCache
    {

        private HttpContext _context = null;

        public OracleParameterCache (HttpContext ctx)
        {
            _context = ctx;
        }

        public void SetParamsInCache (string dadName, string keyName, NameValueCollection procParams)
        {
            _context.Cache.Insert(dadName + "|" + keyName, procParams);
        }

        public NameValueCollection GetParamsFromCache(string dadName, string keyName)
        {

            object cachedParams = _context.Cache.Get(dadName + "|" + keyName);

            if (cachedParams is NameValueCollection)
            {
                return (cachedParams as NameValueCollection);
            }
            else
            {
                return new NameValueCollection();
            }
            
        }

        public string GetCacheDebugHTML()
        {
            StringBuilder sb = new StringBuilder();

            IDictionaryEnumerator d = _context.Cache.GetEnumerator();

            sb.AppendFormat("<h3>Cache Entries ({0})</h3><ul>", _context.Cache.Count);
            
            while(d.MoveNext())
            {
                sb.AppendFormat("<li>{0} = {1}</li>", d.Key.ToString(), d.Value.ToString());
            }

            sb.AppendFormat("</ul>");

            return sb.ToString();
        }

    }
}
