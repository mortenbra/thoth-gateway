using System;
using System.Collections.Generic;
using System.Web;

namespace PLSQLGatewayModule
{
    public enum ValueType
    {
        NullValue,
        ScalarValue,
        ArrayValue
    };

    public class NameValuePair
    {
        private ValueType _valueType;
        private string _name = "";
        private string _value = "";
        private List<string> _values = new List<string>();

        public NameValuePair(string name, string value)
        {
            _name = name;
            _value = value;
            _values.Add(value);
            _valueType = ValueType.ScalarValue;

        }

        public NameValuePair(string name, List<string> values)
        {
            _name = name;

            if (values.Count == 0)
            {
                _value = "";
                _values.Add("");
                _valueType = ValueType.NullValue;

            }
            else if (values.Count == 1)
            {
                _value = values[0];
                _values.Add(_value);
                _valueType = ValueType.ScalarValue;
            }
            else
            {
                _value = values[0];
                _values = values;
                _valueType = ValueType.ArrayValue;

            }

        }

        public NameValuePair(string name, string[] values)
        {
            _name = name;

            if (values == null)
            {
                _value = "";
                _values.Add("");
                _valueType = ValueType.NullValue;
            }
            else if (values != null && values.Length == 1)
            {
                _value = values[0];
                _values.Add(_value);
                _valueType = ValueType.ScalarValue;
            }
            else if (values != null && values.Length > 1)
            {
                _value = values[0];
                
                foreach (string s in values)
                {
                    _values.Add(s);
                }

                _valueType = ValueType.ArrayValue;
            }

        }
        
        public string Name
        {
            get { return _name; }
        }

        public string Value
        {
            get { return _value; }
        }

        public List<string> Values
        {
            get { return _values; }
        }

        public string[] ValuesAsArray
        {
            get { return _values.ToArray(); }
        }

        public ValueType ValueType
        {
            get { return _valueType; }
            set { _valueType = value; }
        }

        public string ValuesAsString
        {
            get
            {
                return string.Join(", ", _values.ToArray());
            }
        }

        public string DebugValue
        {
            get
            {
                if (_valueType == ValueType.ScalarValue)
                {
                    return Value;
                }
                else
                {
                    return ValuesAsString;
                }
            }
        }
    
    }
}
