using System.Data;

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
    /// The isolation level the job store begins its own transactions at. Left unset, they are
    /// <see cref="IsolationLevel.ReadCommitted"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaced a <c>TxIsolationLevelSerializable</c> flag, which could say only "serializable" or
    /// "the default" and so could not express <see cref="IsolationLevel.Snapshot"/>,
    /// <see cref="IsolationLevel.RepeatableRead"/> or a deliberate
    /// <see cref="IsolationLevel.ReadUncommitted"/>. <see langword="null"/> means Quartz's default rather
    /// than the provider's, because those differ — MySQL's is repeatable read — and inheriting the
    /// provider's would silently change how the store behaves depending on which database it is talking
    /// to.
    /// </para>
    /// <para>
    /// SQLite is the exception: the store forces serializable there whatever this says, because
    /// concurrent SQLite transactions at a lower level fail with "database is locked".
    /// </para>
    /// <para>
    /// This applies only to connections the job store opens itself. An operation running on a connection
    /// the application enlisted uses whatever isolation level that transaction was begun at.
    /// </para>
    /// </remarks>
    public IsolationLevel? TransactionIsolationLevel { get; set; }

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
    /// Whether the misfire handler and cluster manager run on background threads, which do not keep
    /// the process alive on their own.
    /// </summary>
    /// <remarks>
    /// These two are the only real threads Quartz creates — the scheduling loop is a
    /// <see cref="System.Threading.Tasks.Task"/> — so this is the whole of the "do Quartz's threads
    /// hold my console application open" question. <c>UseBackgroundThreads</c> rather than
    /// <c>MakeThreadsDaemons</c>: "daemon" is the Java word for what .NET calls
    /// <see cref="System.Threading.Thread.IsBackground"/>.
    /// </remarks>
    public bool UseBackgroundThreads { get; set; }

    /// <summary>
    /// Whether the expected schema objects are verified to exist at startup.
    /// </summary>
    public bool PerformSchemaValidation { get; set; } = true;

    /// <summary>
    /// Overrides the SQL statement used to acquire the row lock. Defaulted for SQL Server to its
    /// <c>WITH (UPDLOCK,ROWLOCK)</c> form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read only when the job store builds a database-locking handler for itself, which is what it does
    /// when <see cref="UseDbLocks"/> is on — through clustering, enlisted transactions, or the setting
    /// itself — and no lock handler was supplied. A handler chosen with <c>UseLockHandler</c> takes its
    /// statement through its own constructor, and the store logs a warning at startup if this is set as
    /// well, since the value would silently do nothing.
    /// </para>
    /// <para>
    /// The statement must select the row in <c>{0}LOCKS</c> matching the <c>@schedulerName</c> and
    /// <c>@lockName</c> parameters, taking whatever lock the database needs to serialize the callers;
    /// <c>{0}</c> is the table prefix.
    /// </para>
    /// </remarks>
    public string? SelectWithLockSql { get; set; }

    /// <summary>
    /// Whether <see cref="Impl.AdoJobStore.ExternalTransactionJobStore" /> opens the connections it
    /// creates before handing them to an operation. Defaults to <see langword="false" />, leaving the
    /// opening to the externally managed transaction. Read only by that store.
    /// </summary>
    public bool OpenConnection { get; set; }
}
