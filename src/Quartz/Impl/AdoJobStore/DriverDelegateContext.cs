using System.Collections.Specialized;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The settings a <see cref="IDriverDelegate" /> works from, handed to
/// <see cref="IDriverDelegate.Initialize" /> once by the job store before the delegate is used.
/// </summary>
/// <remarks>
/// This arrives after construction rather than through the delegate's constructor because
/// <see cref="InstanceId" /> can be generated once the scheduler starts — see the remarks on
/// <c>AdoJobStoreBase.InstanceId</c> — so it is not known when the container builds the delegate.
/// </remarks>
public sealed record DriverDelegateContext
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
    /// Name of the scheduler whose rows the delegate reads and writes, stored in <c>SCHED_NAME</c>.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The identifier of this scheduler node within a cluster, stored in <c>INSTANCE_NAME</c>.
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

    /// <summary>
    /// How long a statement may run before the provider cancels it. <see langword="null" /> leaves the
    /// provider's own default in place.
    /// </summary>
    public TimeSpan? CommandTimeout { get; init; }
}
