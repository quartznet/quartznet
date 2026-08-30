namespace Quartz;

/// <summary>
/// The settings <see cref="QuartzAspireHostApplicationBuilderExtensions.AddQuartzPersistentStore"/> reads,
/// bound from <c>Aspire:Quartz</c> and then from <c>Aspire:Quartz:&lt;connection name&gt;</c> over it.
/// </summary>
/// <remarks>
/// <para>
/// The two-section shape is the one every Aspire client integration has: the outer section carries what
/// is true of every connection in the application, and the inner one — named after the connection —
/// overrides it for that connection alone. An application with a single database never writes the inner
/// one.
/// </para>
/// <para>
/// Everything here is deliberately small. This type is not a second spelling of Quartz's own
/// configuration: <c>Quartz:JobStore</c>, <c>Quartz:Scheduler</c> and the rest still say what they always
/// said, and <c>AddQuartz</c> still reads them. What these settings decide is the handful of things that
/// follow from an Aspire <em>connection</em> — which database it is, and whether the signals that come
/// with it are wired — because that is what a connection name is evidence of.
/// </para>
/// </remarks>
public sealed class QuartzAspireSettings
{
    /// <summary>
    /// The connection string reaching the database, when it is not the one Aspire injected.
    /// </summary>
    /// <remarks>
    /// Left unset, <c>ConnectionStrings:&lt;connection name&gt;</c> supplies it — which is the
    /// environment variable the AppHost's <c>WithReference</c> sets, so an application under Aspire
    /// never writes this. It matters for the value that decides the provider: the string is read at
    /// configuration time whether or not the store ends up using it, because its shape is what says
    /// which database this is.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Which ADO.NET driver reaches the database, as one of the names on
    /// <see cref="DataSourceOptions.Providers"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Left unset, it is inferred from the connection string's shape. The inference recognises the five
    /// shapes Aspire's own database resources produce and throws for anything it cannot place — a
    /// guess would choose the driver delegate that writes the SQL, and SQL the database cannot run is a
    /// worse outcome than a startup failure that names this property.
    /// </para>
    /// <para>
    /// Three of the shipped provider names are never inferred, because nothing in a connection string
    /// distinguishes them: <c>MySql</c> from <c>MySqlConnector</c>, <c>SQLite</c> from
    /// <c>SQLite-Microsoft</c>, and <c>Firebird</c> from a connection string that looks like several
    /// others. Naming one here is how an application chooses.
    /// </para>
    /// <para>
    /// A name Quartz ships no description for is still usable: it selects the generic SQL dialect, and
    /// whatever <c>DbMetadataFactory</c> the application registered describes the driver.
    /// </para>
    /// </remarks>
    public string? Provider { get; set; }

    /// <summary>
    /// Which scheduler this store belongs to, for an application that registers more than one.
    /// </summary>
    /// <remarks>
    /// Left unset, every scheduler in the container gets this store — which is right for the single
    /// scheduler an application normally has, and wrong the moment two of them talk to two databases.
    /// The name is the one passed to <c>AddQuartz(name, …)</c>, because a string means a scheduler
    /// everywhere in Quartz.
    /// </remarks>
    public string? SchedulerName { get; set; }

    /// <summary>
    /// The prefix on the Quartz table names, when the schema was created with something other than
    /// <c>QRTZ_</c>.
    /// </summary>
    /// <remarks>
    /// Left unset, <c>AdoJobStoreOptions.TablePrefix</c> keeps whatever it already had — its own
    /// default, or a value <c>Quartz:JobStore:TablePrefix</c> set.
    /// </remarks>
    public string? TablePrefix { get; set; }

    /// <summary>
    /// Whether this scheduler takes part in a cluster with every other scheduler sharing the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aspire makes replicas cheap — <c>WithReplicas(2)</c> is one call — and two schedulers on one
    /// database that are not clustered will both fire every trigger. Turning this on turns database
    /// locking on with it, as <c>UseClustering()</c> always has.
    /// </para>
    /// <para>
    /// It also makes the scheduler derive its <c>InstanceId</c>, because a cluster needs distinct ids and
    /// a replica set has none of its own to borrow: a node recognises its own check-in row and its own
    /// fired triggers by that id, and every replica starts life carrying
    /// <see cref="QuartzSchedulerOptions.DefaultInstanceId"/> — <c>NON_CLUSTERED</c>. An application that
    /// already set <c>GenerateInstanceId</c>, or that named its nodes by setting <c>InstanceId</c>, keeps
    /// what it said; this only fills the gap.
    /// </para>
    /// </remarks>
    public bool Clustered { get; set; }

    /// <summary>
    /// Whether the scheduler's health check is left unregistered.
    /// </summary>
    /// <remarks>
    /// The check is <c>AddQuartzHealthChecks()</c> from the core package, registered on the same
    /// <c>IHealthChecksBuilder</c> an Aspire ServiceDefaults project put its own <c>self</c> check on,
    /// so <c>MapDefaultEndpoints()</c> serves both.
    /// </remarks>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Whether Quartz's <c>ActivitySource</c> is left unsubscribed.
    /// </summary>
    /// <remarks>
    /// Subscribing is <c>AddSource(QuartzInstrumentation.ActivitySourceName)</c> on the application's
    /// existing tracer provider. No exporter is added — a ServiceDefaults project already owns that, and
    /// <c>UseOtlpExporter()</c> may be called only once.
    /// </remarks>
    public bool DisableTracing { get; set; }

    /// <summary>
    /// Whether Quartz's <c>Meter</c> is left unsubscribed.
    /// </summary>
    /// <inheritdoc cref="DisableTracing" path="/remarks"/>
    public bool DisableMetrics { get; set; }
}
