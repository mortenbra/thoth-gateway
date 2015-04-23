using System;
using System.Collections.Generic;
using System.Web;
using System.Text;
using System.Globalization;

namespace PLSQLGatewayModule
{
    public static class StringUtil
    {

        public static string AppendStr(string str, string textToAppend, string delimiter)
        {
            if (str.Length > 0)
            {
                return str + delimiter + textToAppend;
            }
            else
            {
                return textToAppend;
            }
        }


        public static string RemoveSpecialCharacters(string str) {

           if (str != null)
           {

               StringBuilder sb = new StringBuilder(str.Length);
               
               foreach (char c in str)
               {
                   // only include characters that would be valid for specifying an Oracle schema/package/procedure/parameter
                   if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_' || c == '#' || c == '$') 
                   {
                       sb.Append(c);
                   }
               }

               return sb.ToString();

           }
           else
           {
               return "";
           }

        }

        public static string base64Decode(string data)
        {
            try
            {
                System.Text.UTF8Encoding encoder = new System.Text.UTF8Encoding();
                System.Text.Decoder utf8Decode = encoder.GetDecoder();

                byte[] todecode_byte = Convert.FromBase64String(data);
                int charCount = utf8Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
                char[] decoded_char = new char[charCount];
                utf8Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);
                string result = new String(decoded_char);
                return result;
            }
            catch (Exception e)
            {
                throw new Exception("Error in base64Decode" + e.Message);
            }
        }


        public static string base64Encode(string data)
        {
            try
            {
                byte[] encData_byte = new byte[data.Length];
                encData_byte = System.Text.Encoding.UTF8.GetBytes(data);
                string encodedData = Convert.ToBase64String(encData_byte);
                return encodedData;
            }
            catch (Exception e)
            {
                throw new Exception("Error in base64Encode" + e.Message);
            }
        }

        public static string InitCap(string str)
        {
            // CultureInfo.InvariantCulture()
            return new CultureInfo("en").TextInfo.ToTitleCase(str);
        }

        public static string FileNameStr(string str)
        {
            // convert string to name suitable for files

            // http://stackoverflow.com/questions/620605/how-to-make-a-valid-windows-filename-from-an-arbitrary-string

            string returnValue = str;

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                returnValue = returnValue.Replace(c, '_');
            }

            return returnValue;
            
        }
        
        public static string PrettyStr(string str)
        {
            // convert from my_ugly_string to MyUglyString
            return InitCap(str.ToLower()).Replace("_", "").Replace(".", "");
        }

        public static string ReversePrettyStr(string prettyStr)
        {
            // convert from MyPrettyString to my_pretty_string

            if (prettyStr != null)
            {

                StringBuilder sb = new StringBuilder();
                int counter = 0;

                foreach (char c in prettyStr)
                {
                    counter++;
                    if (c >= 'A' && c <= 'Z')
                    {
                        if (counter == 1)
                        {
                            sb.Append(c.ToString().ToLower());
                        }
                        else
                        {
                            sb.Append("_" + c.ToString().ToLower());
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                return sb.ToString();

            }
            else
            {
                return "";
            }

        
        }

        public static string GetTagValue(string str, string tagName, string defaultValue)
        {
            string returnValue = "";
            int beginPos = str.IndexOf("<" + tagName + ">");

            if (beginPos > 0)
            {
	          beginPos = beginPos + tagName.Length + 2;
              int endPos = str.IndexOf("</" + tagName + ">");

              if (endPos == 0)
              {
                endPos = str.Length;
              }
              else
	          {
                endPos = endPos - 1;
	          }

              returnValue = str.Substring(beginPos, endPos - beginPos + 1);
                 
            }
            else
            {
                returnValue = "";
            }

            if (returnValue == "")
            {
                returnValue = defaultValue;
            }

            return returnValue;


        }

    }
}
