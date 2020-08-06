using System.Collections.Specialized;

namespace Quartz
{
    /// <summary>
    /// Configuration interface that allows hooking strongly typed helpers for configuration.
    /// </summary>
    public interface IPropertyConfigurer
    {
        void SetProperty(string name, string value);
        NameValueCollection Properties { get; }
    }
}