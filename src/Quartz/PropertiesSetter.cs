namespace Quartz
{
    public abstract class PropertiesSetter : IPropertySetter
    {
        private readonly string prefix;
        private readonly IPropertySetter parent;

        protected PropertiesSetter(IPropertySetter parent, string prefix = "")
        {
            this.parent = parent;
            this.prefix = prefix.TrimEnd('.');
        }

        public void SetProperty(string name, string value)
        {
            if (name.IndexOf('.') < 0 && !string.IsNullOrWhiteSpace(prefix))
            {
                name = prefix + '.' + name;
            }
            parent.SetProperty(name, value);
        }
    }
}