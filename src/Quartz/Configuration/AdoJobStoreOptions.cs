namespace Quartz;

/// <summary>
/// Strongly typed configuration for the ADO.NET (persistent) job store.
/// </summary>
/// <remarks>
/// Binds from the <c>JobStore</c> section of the Quartz configuration, and is the typed
/// replacement for the <c>quartz.jobStore.*</c> property keys. Scheduler identity is not repeated
/// here — components that need it inject <see cref="QuartzSchedulerOptions"/> as well, and neither
/// are the clustering settings, which live once on <see cref="ClusteringOptions"/>.
/// </remarks>
public sealed class AdoJobStoreOptions
{
    /// <summary>
    /// The default value for <see cref="TablePrefix"/>.
    /// </summary>
    public const string DefaultTablePrefix = "QRTZ_";

    /// <summary>
    /// The name of the data source this job store reads and writes through.
    /// </summary>
    /// <remarks>Resolves the matching named <see cref="DataSourceOptions"/>.</remarks>
    public string DataSource { get; set; } = "";

    /// <summary>
    /// The prefix applied to every Quartz table name.
    /// </summary>
    public string TablePrefix { get; set; } = DefaultTablePrefix;

    /// <summary>
    /// When <see langword="true"/>, job data maps are persisted as name/value string pairs rather than
    /// serialized objects, which keeps stored data readable and version tolerant.
    /// </summary>
    public bool UseProperties { get; set; }

    /// <summary>
    /// How far past its scheduled fire time a trigger may be before it is considered misfired.
    /// </summary>
    public TimeSpan MisfireThreshold { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often the misfire handler runs. Defaults to <see cref="MisfireThreshold"/> when unset.
    /// </summary>
    public TimeSpan? MisfireHandlerFrequency { get; set; }

    /// <summary>
    /// The maximum number of misfired triggers handled in a single pass.
    /// </summary>
    public int MaxMisfiresToHandleAtATime { get; set; } = 20;

    /// <summary>
    /// How long to wait before retrying after a database failure.
    /// </summary>
    public TimeSpan DbRetryInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The number of times a transient database failure is retried.
    /// </summary>
    public int MaxTransientRetries { get; set; } = 3;

    /// <summary>
    /// How long to wait between transient database failure retries.
    /// </summary>
    public TimeSpan TransientRetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many consecutive failures of a retryable action occur before they are logged as errors.
    /// </summary>
    public int RetryableActionErrorLogThreshold { get; set; } = 4;

    /// <summary>
    /// Whether database row locks are used for synchronization. Required for clustering.
    /// </summary>
    public bool UseDbLocks { get; set; }

    /// <summary>
    /// Whether a lock is taken when inserting new job or trigger rows.
    /// </summary>
    public bool LockOnInsert { get; set; } = true;

    /// <summary>
    /// Whether trigger acquisition happens inside the database lock.
    /// </summary>
    public bool AcquireTriggersWithinLock { get; set; }

    /// <summary>
    /// Whether transactions use the serializable isolation level.
    /// </summary>
    public bool TxIsolationLevelSerializable { get; set; }

    /// <summary>
    /// Whether the job store may take part in a transaction the application owns, rather than
    /// always managing an ADO.NET transaction of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, the job store uses the connection the application enlisted with
    /// <see cref="SchedulerEnlistmentExtensions.EnlistTransaction" /> or
    /// <see cref="SchedulerEnlistmentExtensions.EnlistConnection" /> for operations on that
    /// asynchronous flow, so scheduling either commits with the rest of the application's work or
    /// not at all.
    /// </para>
    /// <para>
    /// Taking part always means handing over a connection. Operations with nothing enlisted keep
    /// using a connection of the job store's own, and for <see cref="Quartz.Impl.AdoJobStore.LocalTransactionJobStore" />
    /// that connection is deliberately kept out of any ambient
    /// <see cref="System.Transactions.Transaction" />. <see cref="Quartz.Impl.AdoJobStore.ExternalTransactionJobStore" />
    /// is the exception, since running inside a container-managed transaction is that store's contract.
    /// </para>
    /// </remarks>
    public bool AcceptEnlistedTransactions { get; set; }

    /// <summary>
    /// Whether the misfire handler double-checks the lock before doing work.
    /// </summary>
    public bool DoubleCheckLockMisfireHandler { get; set; } = true;

    /// <summary>
    /// Whether the job store's background threads run as background threads.
    /// </summary>
    public bool MakeThreadsDaemons { get; set; }

    /// <summary>
    /// Whether the expected schema objects are verified to exist at startup.
    /// </summary>
    public bool PerformSchemaValidation { get; set; } = true;

    /// <summary>
    /// Overrides the SQL statement used to acquire the row lock.
    /// </summary>
    public string? SelectWithLockSql { get; set; }
}
