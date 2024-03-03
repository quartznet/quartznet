using System.Collections.Specialized;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Initialization arguments holder for <see cref="IDriverDelegate" /> implementations.
/// </summary>
public sealed class DelegateInitializationArgs
{
    /// <summary>
    /// Whether simple <see cref="NameValueCollection"/> should be used (for serialization safety).
    /// </summary>
    public bool UseProperties { get; set; }

    /// <summary>
    /// The prefix of all table names.
    /// </summary>
    public string TablePrefix { get; set; } = null!;

    /// <summary>
    /// The instance's name.
    /// </summary>
    public string InstanceName { get; set; } = null!;

    /// <summary>
    /// The instance id.
    /// </summary>
    public string InstanceId { get; set; } = null!;

    /// <summary>
    /// The db provider.
    /// </summary>
    public IDbProvider DbProvider { get; set; } = null!;

    /// <summary>
    /// The type loading strategy.
    /// </summary>
    public ITypeLoadHelper TypeLoadHelper { get; set; } = null!;

    /// <summary>
    /// Object serializer and deserializer strategy to use.
    /// </summary>
    public IObjectSerializer? ObjectSerializer { get; set; }

    /// <summary>
    /// Custom driver delegate initialization.
    /// </summary>
    /// <remarks>
    /// initStrings are of the format:
    /// settingName=settingValue|otherSettingName=otherSettingValue|...
    /// </remarks>
    public string? InitString { get; set; }

    /// <summary>
    /// Time provider to use, defaults to <see cref="System.TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}