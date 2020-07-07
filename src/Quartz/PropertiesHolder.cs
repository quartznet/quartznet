using System.Collections.Specialized;

namespace Quartz
{
    public abstract class PropertiesHolder : IPropertyConfigurer
    {
        private readonly NameValueCollection properties;

        protected PropertiesHolder(NameValueCollection properties)
        {
            this.properties = properties;
        }

        protected PropertiesHolder(PropertiesHolder parent)
        {
            properties = parent.properties;
        }

        protected PropertiesHolder(IPropertyConfigurer parent)
        {
            properties = parent.Properties;
        }

        public void SetProperty(string name, string value)
        {
            properties[name] = value;
        }

        public NameValueCollection Properties => properties;
    }
}