using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

namespace Quartz;

/// <summary>
/// Configuration interface that allows hooking strongly typed helpers for configuration.
/// </summary>
public interface IPropertyConfigurer : IPropertySetter
{
    NameValueCollection Properties { get; }
}

/// <summary>
/// Configuration interface that allows hooking strongly typed helpers for configuration.
/// </summary>
public interface IPropertySetter
{
    void SetProperty(string name, string value);
}

/// <summary>
/// Marker interface to to target outside configuration extensions better.
/// </summary>
public interface IPropertyConfigurationRoot : IPropertySetter
{
}

internal interface IContainerConfigurationSupport : IPropertyConfigurer, IPropertyConfigurationRoot
{
    void RegisterSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TImplementation>()
        where TService : class
        where TImplementation : class, TService;
}