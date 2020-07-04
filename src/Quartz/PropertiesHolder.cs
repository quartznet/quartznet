using System.Collections.Specialized;

namespace Quartz
{
    public abstract class PropertiesHolder
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

        public void SetProperty(string name, string value)
        {
            properties[name] = value;
        }

        internal NameValueCollection Properties => properties;
    }
}