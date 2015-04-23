using System;
using System.Collections.Generic;
using System.Web;
using System.Configuration;

namespace PLSQLGatewayModule
{


    public class DadSection : ConfigurationSection
    {
        [ConfigurationProperty("", IsDefaultCollection = true)]
        public DadCollection Dads
        {
            get { return (DadCollection)base[""]; }
        }
    }

    public class DadCollection : ConfigurationElementCollection
    {
        public new DadElement this[string name]
        {
            get
            {
                if (IndexOf(name) < 0) return null;

                return (DadElement)BaseGet(name);
            }
        }

        public DadElement this[int index]
        {
            get { return (DadElement)BaseGet(index); }
        }

        public int IndexOf(string name)
        {
            name = name.ToLower();

            for (int idx = 0; idx < base.Count; idx++)
            {
                if (this[idx].Name.ToLower() == name)
                    return idx;
            }

            return -1;
        }

        public override ConfigurationElementCollectionType CollectionType
        {
            get { return ConfigurationElementCollectionType.BasicMap; }
        }

        protected override ConfigurationElement CreateNewElement()
        {
            return new DadElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((DadElement)element).Name;
        }

        protected override string ElementName
        {
            get { return "dad"; }
        }
    }

    public class DadElement : ConfigurationElement
    {
        [ConfigurationProperty("name", DefaultValue = "mydad", IsRequired = true, IsKey = true)]
        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }


        [ConfigurationProperty("params", IsDefaultCollection = false)]
        public ParamCollection Params
        {
            get { return (ParamCollection)base["params"]; }
        }
    }

    public class ParamCollection : ConfigurationElementCollection
    {
        public new ParamElement this[string name]
        {
            get
            {
                if (IndexOf(name) < 0) return null;

                return (ParamElement)BaseGet(name);
            }
        }

        public ParamElement this[int index]
        {
            get { return (ParamElement)BaseGet(index); }
        }

        public int IndexOf(string name)
        {
            name = name.ToLower();

            for (int idx = 0; idx < base.Count; idx++)
            {
                if (this[idx].Name.ToLower() == name)
                    return idx;
            }
            return -1;
        }

        public override ConfigurationElementCollectionType CollectionType
        {
            get { return ConfigurationElementCollectionType.BasicMap; }
        }

        protected override ConfigurationElement CreateNewElement()
        {
            return new ParamElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ParamElement)element).Name;
        }

        protected override string ElementName
        {
            get { return "param"; }
        }
    }

    public class ParamElement : ConfigurationElement
    {
        [ConfigurationProperty("name", DefaultValue = "", IsRequired = true, IsKey = true)]
        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }

        [ConfigurationProperty("value", DefaultValue = "", IsRequired = true, IsKey = false)]
        public string Value
        {
            get { return (string)this["value"]; }
            set { this["value"] = value; }
        }
    }




}
