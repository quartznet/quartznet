using System.Collections.Specialized;

namespace Quartz
{
    public abstract class PropertiesHolder : IPropertyConfigurer
    {
        private readonly string prefix;
        private readonly NameValueCollection properties;

        protected PropertiesHolder(NameValueCollection properties, string prefix = "")
        {
            this.properties = properties;
            this.prefix = prefix.TrimEnd('.');
        }

        protected PropertiesHolder(PropertiesHolder parent, string prefix = "")
        {
            properties = parent.properties;
            this.prefix = prefix.TrimEnd('.');
        }

        protected PropertiesHolder(IPropertyConfigurer parent, string prefix = "")
        {
            properties = parent.Properties;
            this.prefix = prefix.TrimEnd('.');
        }

        public void SetProperty(string name, string value)
        {
            if (name.IndexOf('.') < 0 && !string.IsNullOrWhiteSpace(prefix))
            {
                name = prefix + '.' + name;
            }
            properties[name] = value;
        }

        public NameValueCollection Properties => properties;
    }
}