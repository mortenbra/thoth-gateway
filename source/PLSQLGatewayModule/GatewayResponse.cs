using System;
using System.Collections.Generic;
using System.Web;
using System.Text;
using log4net;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Handles getting the response back from the OWA toolkit and parsing the response
    /// </summary>
    public class GatewayResponse
    {

        private static readonly ILog logger = LogManager.GetLogger(typeof(PLSQLHttpModule));

        private StringBuilder _responseBody = new StringBuilder();
        private HttpCookieCollection _cookies = new HttpCookieCollection();
        private List<NameValuePair> _headers = new List<NameValuePair>();

        public GatewayResponse()
        {
            RedirectLocation = "";
        }

        public void FetchOwaResponse(OracleInterface ora)
        {
            int pageFragmentCount = 0;

            while (ora.MoreToFetch())
            {
                pageFragmentCount = pageFragmentCount + 1;

                if (pageFragmentCount == 1)
                {
                    string firstFragment = ora.GetOwaPageFragment();

                    ParseResponseHeaders(firstFragment);

                    // check if we are downloading a file, if so ignore any OWA/HTP generated output (except the headers)
                    //IsDownload = ora.IsDownload();
                    IsDownload = ora.IsDownload;

                    if (IsDownload)
                    {
                        // get the file info
                        string downloadInfo = ora.GetDownloadInfo();

                        if (downloadInfo.Equals("B") || downloadInfo.Equals("F"))
                        {
                            // get the file
                            FileData = ora.GetDownloadFile(downloadInfo);
                        }
                        else
                        {
                            // decode the file info (filename, last_updated, mime_type, content_type, dad_charset, doc_size)
                            if (downloadInfo.StartsWith("12XNOT_MODIFIED"))
                            {
                                StatusCode = 302;
                                //StatusDescription = "Not Modified";
                            }
                            else
                            {
                                int fileNameStartPos = downloadInfo.IndexOf("X") + 1;
                                int fileNameLength = int.Parse(downloadInfo.Substring(0, fileNameStartPos - 1));
                                string fileName = downloadInfo.Substring(fileNameStartPos, fileNameLength);

                                string leftToParse = downloadInfo.Substring(fileNameStartPos + fileNameLength + 1);

                                int lastModifiedStartPos = leftToParse.IndexOf("X") + 1;
                                int lastModifiedLength = int.Parse(leftToParse.Substring(0, lastModifiedStartPos - 1));
                                string lastModified = leftToParse.Substring(lastModifiedStartPos, lastModifiedLength);

                                leftToParse = leftToParse.Substring(lastModifiedStartPos + lastModifiedLength + 1);

                                int contentTypeStartPos = leftToParse.IndexOf("X") + 1;
                                int contentTypeLength = int.Parse(leftToParse.Substring(0, contentTypeStartPos - 1));
                                string contentType = leftToParse.Substring(contentTypeStartPos, contentTypeLength);

                                leftToParse = leftToParse.Substring(contentTypeStartPos + contentTypeLength + 1);

                                // skip other encoded fields, chop off last X, content-length is last
                                leftToParse = leftToParse.Substring(0, leftToParse.Length - 1);

                                int contentLengthStartPos = leftToParse.LastIndexOf("X");
                                int contentLength = int.Parse(leftToParse.Substring(contentLengthStartPos + 1));

                                FileData = ora.GetDownloadFileFromDocTable(fileName);
                                ContentType = contentType;
                                ContentLength = contentLength;
                                _headers.Add(new NameValuePair("Last-Modified", lastModified));


                            }

                        }

                        break; // exit loop since we do not need to fetch more from the HTP buffer
                    }

                }
                else
                {
                    // second or later fragment, just get the text
                    _responseBody.Append(ora.GetOwaPageFragment());

                }

            }

        }
        
        private void ParseResponseHeaders(string firstFragment)
        {

            string firstFragmentMinusHeaders = "";
            string headerFragment = "";
            int headerEndPosition = 0;
            bool hasHeader = false;

            // split headers from rest of content
            
            // MBR 18.04.2015: Apex 5 no longer has space between header name and header value, adjusted code accordingly
            //                 also affects parsing of header values, see below
            hasHeader = (firstFragment.IndexOf("Content-type:", StringComparison.OrdinalIgnoreCase) > -1
                         || firstFragment.IndexOf("Location:", StringComparison.OrdinalIgnoreCase) > -1
                         || firstFragment.IndexOf("Status:", StringComparison.OrdinalIgnoreCase) > -1
                         || firstFragment.IndexOf("X-DB-Content-length:", StringComparison.OrdinalIgnoreCase) > -1
                         || firstFragment.IndexOf("WWW-Authenticate:", StringComparison.OrdinalIgnoreCase) > -1);

            if (hasHeader)
            {
                headerEndPosition = firstFragment.IndexOf("" + '\n' + '\n');

                logger.Debug("Response has headers, headerEndPosition = " + headerEndPosition.ToString());

                // MBR 14.07.2011: handle response with headers, but no body...
                if (headerEndPosition == -1)
                {
                    headerFragment = firstFragment;
                    firstFragmentMinusHeaders = "";
                    logger.Debug("Response has only headers, no body content");
                    logger.Debug(headerFragment);
                }
                else
                {
                    headerEndPosition = headerEndPosition + 2;
                    // MBR 13.10.2021: added some defensive code
                    if (headerEndPosition <= firstFragment.Length) {
                      firstFragmentMinusHeaders = firstFragment.Substring(headerEndPosition);
                      headerFragment = firstFragment.Substring(0, headerEndPosition);
                    }
                    else
                    {
                        logger.Warn("Calculated length of header is larger than actual header size, treating all response as content");
                        headerFragment = "";
                        firstFragmentMinusHeaders = firstFragment;
                        ContentType = "text/html";
                    }
                }

                
                //logger.Debug("headerFragment = " + headerFragment);

            }
            else
            {
                firstFragmentMinusHeaders = firstFragment;
                headerFragment = "";
                ContentType = "text/html";

                logger.Debug("Response has no headers, treating all response as content");
            
            }

            string[] headers = headerFragment.Split('\n');

            foreach (string s in headers)
            {

                logger.Debug("Processing header " + s);

                if (s.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
                {
                    string newLocation = s.Substring(9).TrimStart(null);
                    RedirectLocation = newLocation;
                    return;
                }
                else if (s.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
                {
                    string statusLine = s.Substring(7).TrimStart(null);
                    //logger.Debug("StatusLine = " + statusLine);
                    StatusCode = int.Parse(statusLine.Split(' ')[0]);
                    //logger.Debug("StatusCode = " + StatusCode.ToString());
                }
                else if (s.StartsWith("WWW-Authenticate:", StringComparison.OrdinalIgnoreCase))
                {
                    // note: for basic authentication to work, *all* forms of authenticated access (including "Basic" !!!) must be switched off under Directory Security in IIS
                    //       the only checkbox that should be enabled is the one for "Anonymous" access
                    _headers.Add(new NameValuePair(s.Substring(0, s.IndexOf(":")), s.Substring(s.IndexOf(":") + 1).TrimStart(null)));
                    StatusCode = 401;
                    StatusDescription = "";
                    //Status = statusLine;
                }
                else if (s.StartsWith("Content-type:", StringComparison.OrdinalIgnoreCase))
                {
                    string contentType = s.Substring(13).TrimStart(null);
                    ContentType = contentType;
                }
                else if (s.StartsWith("X-DB-Content-length:", StringComparison.OrdinalIgnoreCase))
                {
                    int contentLength = int.Parse(s.Substring(20).TrimStart(null));
                    ContentLength = contentLength;
                }
                else if (s.StartsWith("X-ORACLE-IGNORE:", StringComparison.OrdinalIgnoreCase))
                {
                    // ignore it (duh!)
                }
                else if (s.StartsWith("Set-Cookie:", StringComparison.OrdinalIgnoreCase))
                {
                    // create and set cookies

                    string[] cookieElements = s.Substring(11).TrimStart(null).Split(';');

                    string c1 = cookieElements[0];

                    string cookieName = c1.Substring(0, c1.IndexOf("="));
                    string cookieValue = c1.Substring(c1.IndexOf("=") + 1);

                    HttpCookie c = new HttpCookie(cookieName, cookieValue);

                    foreach (string ce in cookieElements)
                    {
                        if (ce.StartsWith(" expires=", StringComparison.OrdinalIgnoreCase))
                        {
                            c.Expires = DateTime.Parse(ce.Substring(9));
                        }

                        if (ce.StartsWith(" path=", StringComparison.OrdinalIgnoreCase))
                        {
                            c.Path = ce.Substring(6);
                        }

                        if (ce.StartsWith(" domain=", StringComparison.OrdinalIgnoreCase))
                        {
                            c.Domain = ce.Substring(8);
                        }

                        if (ce.StartsWith(" secure", StringComparison.OrdinalIgnoreCase))
                        {
                            c.Secure = true;
                        }

                        if (ce.StartsWith(" httponly", StringComparison.OrdinalIgnoreCase))
                        {
                            c.HttpOnly = true;
                        }

                    }

                    _cookies.Add(c);

                }
                else if (s.Length > 0)
                {
                    // get other headers as-is
                    if (s.Contains(":"))
                    {
                      _headers.Add(new NameValuePair(s.Substring(0, s.IndexOf(":")), s.Substring(s.IndexOf(":") + 1).TrimStart(null)));
                    }
                    else
                    {
                        logger.Warn("Name/value separator not found in header string: " + s);
                    }

                }

            }

            _responseBody.Append(firstFragmentMinusHeaders);

        }

        public void FetchWsdlResponse(OracleInterface ora)
        {
            _headers.Clear();
            _headers.Add(new NameValuePair("Content-Type", "text/xml; charset=utf-8;"));
            _headers.Add(new NameValuePair("Content-Length", ora.WsdlBody().Length.ToString()));
            _responseBody.Append(ora.WsdlBody());
        }

        public void FetchSoapResponse(OracleInterface ora)
        {
            _headers.Clear();
            _headers.Add(new NameValuePair("Content-Type", "application/soap+xml; charset=utf-8;"));
            _headers.Add(new NameValuePair("Content-Length", ora.SoapBody().Length.ToString()));
            _responseBody.Append(ora.SoapBody());
        }

        public void FetchXdbResponse(OracleInterface ora)
        {
            ContentType = ora.XdbContentType;
            //ContentType = "application/octet-stream";

             if (ContentType.StartsWith("text")) //  || contentType.StartsWith("text/html") || contentType.StartsWith("text/xml")
            {
                _responseBody.Append(ora.GetXdbResourceText());
            }
            else
            {
                FileData = ora.GetXdbResourceFile();
                IsDownload = true;
            }

        }
        
        public string ContentType
        {
            get;
            set;
        }

        public int ContentLength
        {
            get;
            set;
        }

        public string RedirectLocation
        {
            get;
            set;
        }

        public int StatusCode
        {
            get;
            set;
        }

        public string StatusDescription
        {
            get;
            set;
        }
        
        public StringBuilder ResponseBody
        {
            get
            {
                return _responseBody;
            }
        }

        public HttpCookieCollection Cookies
        {
            get
            {
                return _cookies;
            }
        }

        public List<NameValuePair> Headers
        {
            get
            {
                return _headers;
            }
        }

        public bool IsDownload
        {
            get;
            set;
        }

        public byte[] FileData
        {
            get;
            set;
        }
    }
}
