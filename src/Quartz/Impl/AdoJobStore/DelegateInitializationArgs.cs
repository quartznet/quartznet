using System.Collections.Specialized;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Initialization arguments holder for <see cref="IDriverDelegate" /> implementations.
/// </summary>
public sealed record DelegateInitializationArgs
{
    /// <summary>
    /// Whether simple <see cref="NameValueCollection"/> should be used (for serialization safety).
    /// </summary>
    public bool UseProperties { get; init; }

    /// <summary>
    /// The prefix of all table names.
    /// </summary>
    public required string TablePrefix { get; init; }

    /// <summary>
    /// The instance's name.
    /// </summary>
    public required string InstanceName { get; init; }

    /// <summary>
    /// The instance id.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// The db provider.
    /// </summary>
    public required IDbProvider DbProvider { get; init; }

    /// <summary>
    /// The type loading strategy.
    /// </summary>
    public required ITypeLoader TypeLoader { get; init; }

    /// <summary>
    /// Object serializer and deserializer strategy to use.
    /// </summary>
    public IObjectSerializer? ObjectSerializer { get; init; }

    /// <summary>
    /// Custom trigger persistence delegates the delegate serves beyond the built-in five, registered
    /// through <c>UseTriggerPersistenceDelegate&lt;T&gt;()</c>.
    /// </summary>
    public IReadOnlyCollection<ITriggerPersistenceDelegate> TriggerPersistenceDelegates { get; init; } = [];

    /// <summary>
    /// Time provider to use, defaults to <see cref="System.TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
