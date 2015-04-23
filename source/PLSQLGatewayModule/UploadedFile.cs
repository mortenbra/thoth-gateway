using System;
using System.Collections.Generic;
using System.Web;

namespace PLSQLGatewayModule
{
    /// <summary>
    /// Represents a file that is uploaded from the client
    /// </summary>
    public class UploadedFile
    {
        private System.Random _rnd = new System.Random(Guid.NewGuid().GetHashCode());

        private string _fileName = "";
        private int _maxNameLength = 0;
        private bool _useDocTableNamingConvention = true;

        public UploadedFile(string paramName, string fileName, HttpPostedFile postedFile, int maxNameLength, bool useDocTableNamingConvention)
        {
            _maxNameLength = maxNameLength;
            _useDocTableNamingConvention = useDocTableNamingConvention;
            ParamName = paramName;
            FileName = fileName;
            PostedFile = postedFile;
        }

        public string ParamName
        {
            get;
            set;
        }

        public string FileName
        {
            get
            {
                return _fileName;
            }
            
            set
            {
                // extract filename only from full path, and add random number to make file name unique

                _fileName = value;
                int id = _rnd.Next();
                if (_useDocTableNamingConvention)
                {
                  UniqueFileName = "F" + id.ToString() + "/" + System.IO.Path.GetFileName(FileName);
                }
                else
                {
                    //UniqueFileName = "F" + id.ToString() + "_" + StringUtil.FileNameStr(System.IO.Path.GetFileName(FileName));
                    UniqueFileName = "F" + id.ToString() + "_" + System.IO.Path.GetFileName(FileName);
                }

                // note: Apex (as of version 3.2) has a 90-character limit on file name in the wwv_flow_file_object$ table
                // the max length can therefore be set in the DAD configuration

                if (UniqueFileName.Length > _maxNameLength)
                {
                    UniqueFileName = UniqueFileName.Substring(0, _maxNameLength);
                }

            }
        }

        public string UniqueFileName
        {
            get;
            set;
        }

        public HttpPostedFile PostedFile
        {
            get;
            set;
        }

    }
}
