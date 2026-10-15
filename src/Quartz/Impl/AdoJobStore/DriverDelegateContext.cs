using System.Collections.Specialized;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>
    /// The factory the delegate creates its logger from, defaulting to
    /// <see cref="NullLoggerFactory.Instance" />.
    /// </summary>
    /// <remarks>
    /// The job store passes the factory its container gave it, which is what makes a delegate's
    /// statements and failures visible in an application that never set
    /// <see cref="Quartz.Diagnostics.LogProvider" />. A delegate initialized by hand — in a test, in a
    /// benchmark — is handed nothing and logs nowhere, which is what a delegate outside a scheduler
    /// should do.
    /// </remarks>
    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    /// <summary>
    /// The execution groups this node serves and nothing else, as
    /// <see cref="QuartzSchedulerOptions.ServedExecutionGroups" /> configured them; <see langword="null" />
    /// when the node serves every group not refused.
    /// </summary>
    /// <remarks>
    /// Acquisition takes its filter from the <see cref="TriggerAcquisitionCriteria" /> of each request.
    /// This is for the statements that have no request to carry one — the misfire sweep's count and read —
    /// which <see cref="StdAdoDelegate" /> builds once from here, so that a node's sweep handles only the
    /// triggers its acquisition would take. At most one of the two is set.
    /// </remarks>
    public IReadOnlyCollection<ExecutionGroupScope>? ServedExecutionGroups { get; init; }

    /// <summary>
    /// The execution groups this node refuses, as <see cref="QuartzSchedulerOptions.RefusedExecutionGroups" />
    /// configured them; <see langword="null" /> when it refuses none.
    /// </summary>
    public IReadOnlyCollection<ExecutionGroupScope>? RefusedExecutionGroups { get; init; }
}
