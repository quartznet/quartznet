#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Extensibility;
using Quartz.Util;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Contains base functionality for ADO.NET-based JobStore implementations.
/// </summary>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public abstract class AdoJobStoreBase : IJobStore
{
    private readonly bool useProperties;
    private readonly Dictionary<string, ICalendar?> calendarCache = [];
    private readonly IDriverDelegate driverDelegate;
    private TimeSpan misfireThreshold = TimeSpan.FromMinutes(1); // one minute
    private readonly TimeSpan? misfirehandlerFrequence;

    private ClusterManager? clusterManager;
    private MisfireHandler? misfireHandler;
    private readonly ITypeLoader typeLoader;
    private readonly ISchedulerSignaler schedSignaler;
    internal readonly TimeProvider timeProvider;

    private volatile bool schedulerRunning;
    private volatile bool shutdown;
    private readonly JobStoreActivityTracer activityTracer = new();
    private readonly ITriggerPersistenceDelegate[] triggerPersistenceDelegates;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdoJobStoreBase"/> class.
    /// </summary>
    protected AdoJobStoreBase(
        ISchedulerSignaler schedulerSignaler,
        ITypeLoader typeLoader,
        TimeProvider timeProvider,
        IOptions<QuartzSchedulerOptions> schedulerOptions,
        IOptions<AdoJobStoreOptions> storeOptions,
        IOptions<ClusteringOptions> clusteringOptions,
        IObjectSerializer objectSerializer,
        IDbConnectionManager connectionManager,
        IDbProvider dbProvider,
        IDriverDelegate driverDelegate,
        ISemaphore? lockHandler = null,
        IEnumerable<ITriggerPersistenceDelegate>? triggerPersistenceDelegates = null)
    {
        schedSignaler = schedulerSignaler;
        ObjectSerializer = objectSerializer;
        this.typeLoader = typeLoader;
        this.timeProvider = timeProvider;
        InstanceName = schedulerOptions.Value.InstanceName;
        InstanceId = schedulerOptions.Value.InstanceId;

        // Created from the runtime type, so LocalTransactionJobStore and ExternalTransactionJobStore log
        // under their own names rather than everything arriving as AdoJobStoreBase.
        Logger = LogProvider.CreateLogger(GetType().FullName!);
        ConnectionManager = connectionManager;

        var options = storeOptions.Value;
        DataSource = options.DataSource;
        TablePrefix = options.TablePrefix ?? "";
        useProperties = options.UseProperties;
        MisfireThreshold = options.MisfireThreshold;
        misfirehandlerFrequence = options.MisfireHandlerFrequency;
        MaxMisfiresToHandleAtATime = options.MaxMisfiresToHandleAtATime;

        var clustering = clusteringOptions.Value;
        Clustered = clustering.Enabled;
        ClusterCheckinInterval = clustering.CheckinInterval;
        ClusterCheckinMisfireThreshold = clustering.CheckinMisfireThreshold;

        DbRetryInterval = options.DbRetryInterval;
        MaxTransientRetries = options.MaxTransientRetries;
        TransientRetryInterval = options.TransientRetryInterval;
        RetryableActionErrorLogThreshold = options.RetryableActionErrorLogThreshold;
        UseDbLocks = options.UseDbLocks;
        LockOnInsert = options.LockOnInsert;
        AcquireTriggersWithinLock = options.AcquireTriggersWithinLock;
        TxIsolationLevelSerializable = options.TxIsolationLevelSerializable;
        AcceptEnlistedTransactions = options.AcceptEnlistedTransactions;
        DoubleCheckLockMisfireHandler = options.DoubleCheckLockMisfireHandler;
        UseBackgroundThreads = options.UseBackgroundThreads;
        PerformSchemaValidation = options.PerformSchemaValidation;
        SelectWithLockSql = options.SelectWithLockSql;

        // Registered through UseTriggerPersistenceDelegate<T>() (or translated from the legacy
        // quartz.jobStore.driverDelegateInitString key by the property bridge) and handed to the driver
        // delegate when it is initialized.
        this.triggerPersistenceDelegates = triggerPersistenceDelegates?.ToArray() ?? [];

        // The store uses the provider it was given. It is also published to the connection manager under
        // the data source name, because code outside the container still resolves providers by name --
        // but the store never reads it back, so two schedulers whose data sources happen to share a name
        // cannot end up talking to each other's database.
        DbProvider = dbProvider;
        ConnectionManager.AddDbProvider(DataSource, dbProvider);

        // The delegate and lock handler are chosen by configuration and built by the container, rather
        // than loaded from a type name here.
        this.driverDelegate = driverDelegate;

        // A lock handler is only injected when one was chosen explicitly. Left null, Initialize picks
        // between database row locks and an in-process monitor once the delegate and clustering settings
        // are known — a decision that cannot be made at registration time, because it depends on which
        // database this store turns out to be talking to.
        LockHandler = lockHandler!;
    }

    /// <summary>
    /// The name of the data source this store reads and writes through.
    /// </summary>
    protected internal string DataSource { get; } = "";

    /// <summary>
    /// The database connection manager this store publishes its provider to.
    /// </summary>
    protected internal IDbConnectionManager ConnectionManager { get; }

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    internal ILogger Logger { get; }

    /// <summary>
    /// The prefix pre-pended to all table names.
    /// </summary>
    protected internal string TablePrefix { get; }

    /// <summary>
    /// The instance id of the scheduler (unique within a cluster).
    /// </summary>
    /// <remarks>
    /// Written once more after construction when the id is generated rather than configured, because
    /// the store is built before the generator has run and its rows are keyed by the value.
    /// </remarks>
    internal string InstanceId { get; set; } = "";

    /// <summary>
    /// The name of the scheduler, shared by every node of a cluster.
    /// </summary>
    /// <inheritdoc cref="InstanceId" path="/remarks" />
    internal string InstanceName { get; set; } = "";

    /// <summary>
    /// The number of retries before an error is logged for recovery operations.
    /// </summary>
    internal int RetryableActionErrorLogThreshold { get; }

    /// <summary>
    /// The serializer that turns job data and calendars into what the database stores.
    /// </summary>
    protected internal IObjectSerializer? ObjectSerializer { get; }

    public TimeSpan EstimatedTimeToReleaseAndAcquireTrigger { get; } = TimeSpan.FromMilliseconds(70);

    /// <summary>
    /// Whether this instance is part of a cluster.
    /// </summary>
    /// <remarks>
    /// Derived state rather than a setting of the store: it reports what
    /// <see cref="ClusteringOptions.Enabled" /> says.
    /// </remarks>
    public bool Clustered { get; }

    /// <summary>
    /// The frequency at which this instance "checks-in"
    /// with the other instances of the cluster. -- Affects the rate of
    /// detecting failed instances.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="ClusteringOptions.CheckinInterval" />.
    /// </remarks>
    internal TimeSpan ClusterCheckinInterval { get; }

    /// <summary>
    /// The time span by which a check-in must have missed its
    /// next-fire-time, in order for it to be considered "misfired" and thus
    /// other scheduler instances in a cluster can consider a "misfired" scheduler
    /// instance as failed or dead.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="ClusteringOptions.CheckinMisfireThreshold" />.
    /// </remarks>
    protected internal TimeSpan ClusterCheckinMisfireThreshold { get; }

    /// <summary>
    /// The maximum number of misfired triggers that the misfire handling
    /// thread will try to recover at one time (within one transaction).  The
    /// default is 20.
    /// </summary>
    protected internal int MaxMisfiresToHandleAtATime { get; }

    /// <summary>
    /// The database retry interval.
    /// </summary>
    /// <value>The db retry interval.</value>
    internal TimeSpan DbRetryInterval { get; }

    /// <summary>
    /// The maximum number of retries for transient database exceptions
    /// (such as deadlocks) before giving up and propagating the exception.
    /// </summary>
    /// <remarks>
    /// Defaults to 3. A value of 0 disables transient retries. Each retry is
    /// delayed by <see cref="TransientRetryInterval"/>.
    /// </remarks>
    protected internal int MaxTransientRetries { get; }

    /// <summary>
    /// The delay between automatic retries for transient database
    /// exceptions (such as deadlocks).
    /// </summary>
    /// <remarks>
    /// Defaults to 1 second. This is intentionally shorter than <see cref="DbRetryInterval"/>
    /// because transient errors like deadlocks resolve quickly and the retry should be
    /// near-immediate. <see cref="TimeSpan.Zero"/> means no delay between retries.
    /// </remarks>
    protected internal TimeSpan TransientRetryInterval { get; }

    /// <summary>
    /// Whether this instance uses database-based thread synchronization.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="AdoJobStoreOptions.UseDbLocks" />, and turned on by
    /// <see cref="Initialize" /> for a configuration that cannot work without it - clustering,
    /// enlisted transactions, and container-managed transactions.
    /// </remarks>
    protected internal bool UseDbLocks { get; internal set; }

    /// <summary>
    /// Whether this instance may take part in a transaction the application owns, rather than always
    /// managing an ADO.NET transaction of its own. Defaults to <see langword="false" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, the job store uses the connection the application enlisted with
    /// <see cref="SchedulerEnlistmentExtensions.EnlistTransaction" /> or
    /// <see cref="SchedulerEnlistmentExtensions.EnlistConnection" /> for operations on that
    /// asynchronous flow. The application owns the commit, so the job store neither commits nor rolls
    /// back, and scheduling either happens together with the rest of the work or not at all.
    /// </para>
    /// <para>
    /// Taking part always means handing over a connection. Operations with nothing enlisted keep using
    /// a connection of the job store own, and for <see cref="LocalTransactionJobStore" /> that
    /// connection is deliberately kept out of any ambient
    /// <see cref="System.Transactions.Transaction" />: joining a scope whose outcome the job store does
    /// not control would also put a second connection in that transaction, which needs a distributed
    /// transaction and is not available on every provider.
    /// <see cref="ExternalTransactionJobStore" /> is the exception - running inside a transaction its
    /// container manages is that store contract, so its connections enlist as they always have.
    /// </para>
    /// <para>
    /// Because locks taken during an application-owned transaction are only released when that
    /// transaction completes, enabling this switches locking to database locks
    /// (<see cref="UseDbLocks" />) unless an explicit lock handler was configured.
    /// </para>
    /// </remarks>
    internal bool AcceptEnlistedTransactions { get; }

    /// <summary>
    /// Whether or not to obtain locks when inserting new jobs/triggers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see langword="true" />, which is safest - some db's (such as
    /// MS SQLServer) seem to require this to avoid deadlocks under high load,
    /// while others seem to do fine without.  Settings this to false means
    /// isolation guarantees between job scheduling and trigger acquisition are
    /// entirely enforced by the database.  Depending on the database and it's
    /// configuration this may cause unusual scheduling behaviors.
    /// </para>
    /// <para>
    /// Setting this property to <see langword="false" /> will provide a
    /// significant performance increase during the addition of new jobs
    /// and triggers.
    /// </para>
    /// </remarks>
    protected internal bool LockOnInsert { get; } = true;

    /// <summary>
    /// The time span by which a trigger must have missed its
    /// next-fire-time, in order for it to be considered "misfired" and thus
    /// have its misfire instruction applied.
    /// </summary>
    /// <remarks>
    /// The one configuration value that stays settable on both stores: it is read on every misfire
    /// pass rather than only at startup, and a test or an operator tool changes it on a live store.
    /// </remarks>
    public TimeSpan MisfireThreshold
    {
        get => misfireThreshold;
        set
        {
            if (value.TotalMilliseconds < 1)
            {
                Throw.ArgumentException("MisfireThreshold must be larger than 0");
            }
            misfireThreshold = value;
        }
    }

    /// <summary>
    /// How often the misfire handler checks for misfires. Defaults to
    /// <see cref="MisfireThreshold"/>.
    /// </summary>
    internal TimeSpan MisfireHandlerFrequency => misfirehandlerFrequence.GetValueOrDefault(MisfireThreshold);

    /// <summary>
    /// Whether the transaction isolation level of the connections this store opens is serializable.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="AdoJobStoreOptions.TxIsolationLevelSerializable" />, and turned on
    /// by <see cref="Initialize" /> for SQLite, which needs it.
    /// </remarks>
    protected internal bool TxIsolationLevelSerializable { get; internal set; }

    /// <summary>
    /// Whether or not the query and update to acquire a Trigger for firing
    /// should be performed after obtaining an explicit DB lock (to avoid
    /// possible race conditions on the trigger's db row).  This is
    /// is considered unnecessary for most databases (due to the nature of
    ///  the SQL update that is performed), and therefore a superfluous performance hit.
    /// </summary>
    /// <remarks>
    /// However, if batch acquisition is used, it is important for this behavior
    /// to be used for all dbs.
    /// </remarks>
    protected internal bool AcquireTriggersWithinLock { get; internal set; }

    /// <summary>
    /// When true, all operations (including reads) acquire a lock before
    /// accessing the database. Required for SQLite to prevent concurrent
    /// serializable transactions from causing "database is locked" errors.
    /// </summary>
    internal bool LockAllOperations { get; set; }

    /// <summary>
    /// The SQL statement used to select and lock a row in the "locks" table.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="AdoJobStoreOptions.SelectWithLockSql" />, and defaulted by
    /// <see cref="Initialize" /> to the SQL Server specific statement when that is the database in use.
    /// </remarks>
    /// <seealso cref="SelectForUpdateSemaphore" />
    protected internal string? SelectWithLockSql { get; internal set; }

    protected ITypeLoader TypeLoader => typeLoader;

    /// <summary>
    /// Whether the threads spawned by this JobStore are
    /// marked as daemon.  Possible threads include the <see cref="MisfireHandler" />
    /// and the <see cref="ClusterManager"/>.
    /// </summary>
    internal bool UseBackgroundThreads { get; }

    /// <summary>
    /// Whether to check to see if there are Triggers that have misfired
    /// before actually acquiring the lock to recover them.  This should be
    /// set to false if the majority of the time, there are misfired
    /// Triggers.
    /// </summary>
    protected internal bool DoubleCheckLockMisfireHandler { get; }

    /// <summary>
    /// Whether to perform a schema check on scheduler startup and try to determine if correct tables are in place.
    /// Defaults to true.
    /// </summary>
    protected internal bool PerformSchemaValidation { get; } = true;

    public TimeSpan GetAcquireRetryDelay(int failureCount) => DbRetryInterval;

    protected DbMetadata DbMetadata => DbProvider.Metadata;

    protected abstract ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the connection the application enlisted for this scheduler on the current
    /// asynchronous flow, or <see langword="null" /> when enlisted transactions are not accepted or
    /// nothing is enlisted. The returned holder does not own the connection or the transaction.
    /// </summary>
    /// <remarks>
    /// A store that overrides <see cref="GetLocalTransactionConnection" /> has to start with this, or
    /// it silently opens a connection of its own while the caller believes the scheduling is part of
    /// their transaction. Everything an enlistment needs in order to be safe to use happens here: the
    /// transaction is checked to be alive and still current, the provider is checked to match, the
    /// connection is opened if it is not, and it is booked out for the duration of the operation so
    /// two concurrent scheduler calls cannot share it. Cleaning the returned holder up through
    /// <see cref="CleanupConnection" /> hands the booking back.
    /// </remarks>
    protected async ValueTask<ConnectionAndTransactionHolder?> GetEnlistedConnection(CancellationToken cancellationToken = default)
    {
        var enlisted = AmbientConnection.Get(InstanceName);
        if (enlisted is null)
        {
            return null;
        }

        // Refused rather than ignored. Ignoring it would commit the scheduling in a transaction of the
        // store own while the caller believes it is part of theirs, and that only shows up later as a
        // job firing for an entity the caller rolled back. This is also what covers schedulers the
        // enlistment call site could not inspect, such as a decorator around the real one.
        if (!AcceptEnlistedTransactions)
        {
            Throw.JobPersistenceException(
                $"A connection is enlisted for scheduler '{InstanceName}', but it is not configured to take part in "
                + "transactions the application owns, so this operation would commit on its own. Call "
                + "AcceptEnlistedTransactions() when configuring the persistent store, or set "
                + "'quartz.jobStore.acceptEnlistedTransactions' to true.");
        }

        // The enlisted transaction may have finished while its scope was still open - the application
        // committed or rolled back, or its TransactionScope ended. Carrying on would run this
        // operation in autocommit, where a half-finished write can no longer be rolled back, so refuse
        // rather than quietly drop the transactional guarantee the caller asked for.
        if (enlisted.Transaction is not null && enlisted.Transaction.Connection is null)
        {
            Throw.JobPersistenceException(
                $"The transaction enlisted for scheduler '{InstanceName}' has already been committed or rolled back, "
                + "so this operation would run with no transaction at all. Dispose the enlistment scope once the "
                + "transaction completes, and enlist a new one for any further scheduling.");
        }

        // Compared with == rather than by reference: a dependent clone is a different object standing
        // for the same transaction, and refusing those would break legitimate fan-out.
        if (enlisted.Ambient is not null && enlisted.Ambient != System.Transactions.Transaction.Current)
        {
            Throw.JobPersistenceException(
                $"The transaction the connection enlisted for scheduler '{InstanceName}' belongs to is no longer the "
                + "current one, so this operation would run with no transaction at all. Keep the enlistment scope inside "
                + "the transaction scope it was created in, and dispose it before that scope ends.");
        }

        var expected = DbProvider.Metadata.ConnectionType;
        if (expected is not null && !expected.IsInstanceOfType(enlisted.Connection))
        {
            Throw.JobPersistenceException(
                $"The connection enlisted for scheduler '{InstanceName}' is {enlisted.Connection.GetType().FullName}, but "
                + $"this job store is configured for {expected.FullName}. A connection from a different provider cannot "
                + "carry its commands - configure both against the same one.");
        }

        if (!enlisted.TryClaim())
        {
            Throw.JobPersistenceException(
                $"The connection enlisted for scheduler '{InstanceName}' is already serving another job store operation. "
                + "An enlisted connection carries a single transaction and cannot be used concurrently, so scheduler calls "
                + "made inside an enlistment scope must be awaited one at a time.");
        }

        try
        {
            // Anything that is not open needs opening - Broken and Connecting are their own states, and
            // handing a broken connection to the delegate produces a provider error instead of any of
            // the diagnostics above.
            if (enlisted.Connection.State != ConnectionState.Open)
            {
                if (enlisted.Connection.State != ConnectionState.Closed)
                {
                    await enlisted.Connection.CloseAsync().ConfigureAwait(false);
                }

                await enlisted.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a persistence failure, and callers match on it.
            enlisted.Release();
            throw;
        }
        catch (Exception e)
        {
            enlisted.Release();
            Throw.JobPersistenceException($"Failed to open the connection enlisted for scheduler '{InstanceName}': {e}", e);
        }

        return new ConnectionAndTransactionHolder(enlisted.Connection, enlisted.Transaction, ownsResources: false, borrowedFrom: enlisted);
    }

    /// <summary>
    /// Whether the current operation runs inside a transaction the application owns. That is the case
    /// only when the application enlisted a connection: a connection the job store opens for itself
    /// deliberately stays outside whatever the caller has in flight.
    /// </summary>
    private bool InApplicationOwnedTransaction =>
        AcceptEnlistedTransactions && AmbientConnection.Get(InstanceName) is not null;

    /// <summary>
    /// Opens a connection that belongs to the job store.
    /// </summary>
    /// <remarks>
    /// While <see cref="AcceptEnlistedTransactions" /> is on, such a connection is kept out of any
    /// ambient <see cref="System.Transactions.Transaction" />. The application takes part by enlisting
    /// a connection, not by the job store quietly joining a scope whose outcome it does not control;
    /// letting it enlist would also put a second connection in that transaction, which needs a
    /// distributed transaction and is not available on every provider.
    /// </remarks>
    private async ValueTask<DbConnection> OpenOwnConnection(CancellationToken cancellationToken)
    {
        using var ambientSuppression = AcceptEnlistedTransactions && System.Transactions.Transaction.Current is not null
            ? new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeOption.Suppress,
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled)
            : null;

        var conn = DbProvider.CreateConnection();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    /// <summary>
    /// Gets the connection and starts a new transaction.
    /// </summary>
    /// <returns></returns>
    protected virtual async ValueTask<ConnectionAndTransactionHolder> GetConnection(CancellationToken cancellationToken = default)
    {
        var enlisted = await GetEnlistedConnection(cancellationToken).ConfigureAwait(false);
        if (enlisted is not null)
        {
            return enlisted;
        }

        DbConnection conn;
        DbTransaction tx;
        try
        {
            conn = await OpenOwnConnection(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Failed to obtain DB connection from data source '{DataSource}': {e}", e);
            return default;
        }

        try
        {
            if (TxIsolationLevelSerializable)
            {
                tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            Throw.JobPersistenceException("Failure setting up connection.", e);
            return default;
        }

        return new ConnectionAndTransactionHolder(conn, tx);
    }

    protected DateTimeOffset MisfireTime
    {
        get
        {
            DateTimeOffset misfireTime = timeProvider.GetUtcNow();
            if (MisfireThreshold > TimeSpan.Zero)
            {
                misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
            }

            return misfireTime;
        }
    }

    /// <summary>
    /// Gets the threshold for considering a fired trigger record in ACQUIRED state as stale.
    /// Uses 2x <see cref="MisfireThreshold"/> with a floor of 2 minutes, which is
    /// generous enough to never interfere with normal acquisition (which takes at
    /// most idleWaitTime ~30s plus processing time).
    /// </summary>
    protected TimeSpan StaleAcquiredTriggerThreshold
    {
        get
        {
            TimeSpan threshold = MisfireThreshold + MisfireThreshold;
            return threshold < TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : threshold;
        }
    }

    protected virtual string GetFiredTriggerRecordId()
    {
        Interlocked.Increment(ref ftrCtr);
        return InstanceId + ftrCtr;
    }

    /// <summary>
    /// Hands the container-supplied delegate the settings it needs, which are only complete once the
    /// store has been configured.
    /// </summary>
    private void InitializeDelegate()
    {
        driverDelegate.Initialize(new DelegateInitializationArgs
        {
            UseProperties = CanUseProperties,
            TablePrefix = TablePrefix,
            InstanceName = InstanceName,
            InstanceId = InstanceId,
            DbProvider = DbProvider,
            TypeLoader = typeLoader,
            ObjectSerializer = ObjectSerializer,
            TriggerPersistenceDelegates = triggerPersistenceDelegates,
            TimeProvider = timeProvider,
        });
    }

    /// <summary>
    /// The driver delegate this store speaks to its database through.
    /// </summary>
#pragma warning disable CA1716
    protected IDriverDelegate Delegate => driverDelegate;
#pragma warning restore CA1716

    /// <summary>
    /// The database provider this store was built with.
    /// </summary>
    protected internal IDbProvider DbProvider { get; }

    protected internal ISemaphore LockHandler { get; set; } = null!;

    /// <summary>
    /// Get whether String-only properties will be handled in JobDataMaps.
    /// </summary>
    protected internal bool CanUseProperties => useProperties;

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
    /// used, in order to give it a chance to Initialize.
    /// </summary>
    public virtual async ValueTask Initialize(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DataSource))
        {
            Throw.SchedulerConfigException("DataSource name not set.");
        }

        LastCheckin = timeProvider.GetUtcNow();
        InitializeDelegate();

        if (Delegate is SQLiteDelegate && LockHandler is not SQLiteSemaphore)
        {
            Logger.LogInformation("Detected SQLite usage, changing to use SQLiteSemaphore for in-memory locking");
            LockHandler = new SQLiteSemaphore();
        }

        if (Delegate is SQLiteDelegate)
        {
            if (Clustered)
            {
                Throw.InvalidConfigurationException("SQLite cannot be used as clustered mode due to locking problems");
            }
            if (!AcquireTriggersWithinLock)
            {
                Logger.LogInformation("With SQLite we need to set AcquireTriggersWithinLock to true, changing");
                AcquireTriggersWithinLock = true;
            }
            if (!TxIsolationLevelSerializable)
            {
                Logger.LogInformation("Detected usage of SQLiteDelegate - defaulting 'txIsolationLevelSerializable' to 'true'");
                TxIsolationLevelSerializable = true;
            }
            if (!LockAllOperations)
            {
                Logger.LogInformation("With SQLite all operations must be serialized, setting LockAllOperations to true");
                LockAllOperations = true;
            }
        }

        // The job store own connections still honour this; a connection the application enlisted was
        // begun at whatever level the application chose, and cannot be changed after the fact.
        if (AcceptEnlistedTransactions && TxIsolationLevelSerializable && Delegate is not SQLiteDelegate)
        {
            Logger.LogWarning("'quartz.jobStore.txIsolationLevelSerializable' applies only to connections the job store opens itself: an operation running on a connection enlisted by the application uses that transaction isolation level instead.");
        }

        // If the user hasn't specified an explicit lock handler, then
        // choose one based on CMT/Clustered/UseDbLocks.
        if (LockHandler is null)
        {
            // If the user hasn't specified an explicit lock handler,
            // then we *must* use DB locks with clustering
            if (Clustered)
            {
                UseDbLocks = true;
            }

            // The same applies when the application owns the transaction: SimpleSemaphore releases its
            // in-process lock as soon as our work is done, which is before the application has
            // committed, so another caller could act on scheduling data that is not visible yet.
            if (AcceptEnlistedTransactions)
            {
                UseDbLocks = true;
            }

            if (UseDbLocks)
            {
                if (Delegate is SqlServerDelegate)
                {
                    if (SelectWithLockSql is null)
                    {
                        const string DefaultLockSql = "SELECT * FROM {0}LOCKS WITH (UPDLOCK,ROWLOCK) WHERE " + AdoConstants.ColumnSchedulerName + " = @schedulerName AND LOCK_NAME = @lockName";
                        Logger.LogInformation("Detected usage of SqlServerDelegate - defaulting 'selectWithLockSQL' to '{DefaultLockSql}'.", DefaultLockSql);
                        SelectWithLockSql = DefaultLockSql;
                    }
                }

                if (Delegate is PostgreSQLDelegate)
                {
                    LockHandler = new PostgreSqlSelectForUpdateSemaphore(TablePrefix, InstanceName, SelectWithLockSql, DbProvider);
                }
                else
                {
                    LockHandler = new SelectForUpdateSemaphore(TablePrefix, InstanceName, SelectWithLockSql, DbProvider);
                }

                Logger.LogInformation("Using db table-based data access locking (synchronization) via {LockHandlerType}.", LockHandler.GetType().Name);
            }
            else
            {
                LockHandler = new SimpleSemaphore();
                Logger.LogInformation("Using thread monitor-based data access locking (synchronization) via {LockHandlerType}.", LockHandler.GetType().Name);
            }
        }
        else
        {
            // be ready to give a friendly warning if locks would be released before the application commits
            if (AcceptEnlistedTransactions && !LockHandler.RequiresConnection)
            {
                if (LockHandler is SQLiteSemaphore)
                {
                    // SQLite gets this handler unconditionally, before the upgrade to database locks
                    // can be applied, and it cannot be swapped for one - so this is a property of the
                    // combination rather than something to reconfigure away.
                    Logger.LogWarning("Accepting enlisted transactions with SQLite keeps in-process locking: SQLiteSemaphore releases the lock when the scheduling call returns, while the application transaction still holds the SQLite writer lock, so a concurrent scheduler operation can fail with 'database is locked' until that transaction completes. Keep enlisted transactions short, or use a database that supports row locking.");
                }
                else
                {
                    Logger.LogWarning("Accepting enlisted transactions with lock handler {LockHandlerType}, which does not lock in the database. Its locks are released before the application commits its transaction, so concurrent callers can act on scheduling data that is not visible to them yet.", LockHandler.GetType().Name);
                }
            }

            // be ready to give a friendly warning if SQL Server is used and sub-optimal locking
            if (LockHandler is UpdateRowSemaphore and not SqlServerMemoryOptimizedUpdateRowSemaphore && Delegate is SqlServerDelegate)
            {
                Logger.LogWarning("Detected usage of SqlServerDelegate and UpdateRowSemaphore, removing 'quartz.jobStore.lockHandler.type' would allow more efficient SQL Server specific (UPDLOCK,ROWLOCK) row access");
            }
            // be ready to give a friendly warning if SQL Server provider and wrong delegate
            if (DbProvider.Metadata.ConnectionType?.Namespace is not null
                && DbProvider.Metadata.ConnectionType.Namespace.Contains("SqlClient")
                && DbProvider.Metadata.ConnectionType.Name == "SqlConnection"
                && !(Delegate is SqlServerDelegate))
            {
                Logger.LogWarning("Detected usage of SQL Server provider without SqlServerDelegate, SqlServerDelegate would provide better performance");
            }
        }

        // The lock handler learns which scheduler it locks for from the store, on both construction
        // paths: a handler the store built itself is told the same identity its constructor arguments
        // carried, and a handler the container or configuration supplied would otherwise query
        // QRTZ_LOCKS with a null scheduler name, whatever the store is actually configured with.
        LockHandler.Initialize(new SemaphoreContext
        {
            SchedulerName = InstanceName,
            InstanceId = InstanceId,
            TablePrefix = TablePrefix,
        });

        activityTracer.SetSchedulerContext(InstanceName, InstanceId);

        if (PerformSchemaValidation)
        {
            try
            {
                var objectCount = await ExecuteWithoutLock<int>(conn => Delegate.ValidateSchema(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
                Logger.LogInformation("Successfully validated presence of {SchemaObjectCount} schema objects", objectCount);
            }
            catch (Exception ex)
            {
                const string error = "Database schema validation failed."
                                     + " Make sure you have created the database tables that Quartz requires using the database schema scripts."
                                     + " You can disable this check by setting quartz.jobStore.performSchemaValidation to false";

                throw new SchedulerException(error, ex);
            }
        }

    }

    /// <seealso cref="IJobStore.SchedulerStarted(CancellationToken)" />
    public async ValueTask SchedulerStarted(
        CancellationToken cancellationToken = default)
    {
        // Recovery below competes for the same TRIGGER_ACCESS lock that a scheduling call made earlier
        // in this scope is still holding on the caller uncommitted transaction, and the caller cannot
        // commit while awaiting Start(). That deadlock has no diagnostic of its own, so refuse the call
        // instead of hanging. Nothing has been created yet, so the scheduler stays startable.
        if (AcceptEnlistedTransactions && AmbientConnection.Get(InstanceName) is not null)
        {
            throw new Core.SchedulerStartRefusedException(
                $"The scheduler '{InstanceName}' cannot be started from inside an enlistment scope: startup work waits for "
                + "locks the enlisted transaction holds until the application commits, which it cannot do while starting. "
                + "Start the scheduler outside the scope.");
        }

        // Everything below belongs to the scheduler, not to whoever called Start(). Suppressing here
        // keeps job recovery and the first cluster check-in off an enlisted connection, and - because
        // the misfire handler and cluster manager loops capture the execution context when they are
        // started - keeps those loops from ever seeing an enlistment either.
        using var suppression = AmbientConnection.Suppress();

        if (Clustered)
        {
            clusterManager = new ClusterManager(this);
            await clusterManager.Initialize().ConfigureAwait(false);
        }
        else
        {
            try
            {
                await RecoverJobs(cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                Logger.LogError(se, "Failure occurred during job recovery: {ExceptionMessage}", se.Message);
                Throw.SchedulerConfigException("Failure occurred during job recovery.", se);
            }
        }

        misfireHandler = new MisfireHandler(this);
        misfireHandler.Initialize();
        schedulerRunning = true;
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the JobStore that
    /// the scheduler has been paused.
    /// </summary>
    public ValueTask SchedulerPaused(CancellationToken cancellationToken = default)
    {
        schedulerRunning = false;
        return default;
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the JobStore that
    /// the scheduler has resumed after being paused.
    /// </summary>
    public ValueTask SchedulerResumed(CancellationToken cancellationToken = default)
    {
        schedulerRunning = true;
        return default;
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
    /// it should free up all of it's resources because the scheduler is
    /// shutting down.
    /// </summary>
    public virtual async ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        shutdown = true;

        if (misfireHandler is not null)
        {
            await misfireHandler.Shutdown().ConfigureAwait(false);
        }

        if (clusterManager is not null)
        {
            await clusterManager.Shutdown().ConfigureAwait(false);
        }

        try
        {
            DbProvider.Shutdown();
        }
        catch (Exception sqle)
        {
            Logger.LogWarning(sqle, "Database connection Shutdown unsuccessful.");
        }
    }

    /// <summary>
    /// Indicates whether this job store supports persistence.
    /// </summary>
    /// <value></value>
    /// <returns></returns>
    public bool SupportsPersistence => true;

    protected async ValueTask ReleaseLock(
        Guid requestorId,
        SchedulerLock? lockKind,
        bool shouldRelease,
        CancellationToken cancellationToken = default)
    {
        if (shouldRelease && lockKind is not null)
        {
            try
            {
                await LockHandler.ReleaseLock(requestorId, lockKind.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (LockException le)
            {
                Logger.LogError(le, "Error returning lock: {ExceptionMessage}", le.Message);
            }
        }
    }

    /// <summary>
    /// Will recover any failed or misfired jobs and clean up the data store as
    /// appropriate.
    /// </summary>
    protected ValueTask RecoverJobs(CancellationToken cancellationToken = default)
    {
        return ExecuteInLocalTransactionLock(
            SchedulerLock.TriggerAccess,
            conn => RecoverJobs(conn, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Will recover any failed or misfired jobs and clean up the data store as
    /// appropriate.
    /// </summary>
    protected async ValueTask RecoverJobs(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // update inconsistent job states
            int rows = await Delegate.UpdateTriggerStatesFromOtherStates(conn, StoredTriggerState.Waiting, [StoredTriggerState.Acquired, StoredTriggerState.Blocked], cancellationToken).ConfigureAwait(false);

            rows += await Delegate.UpdateTriggerStatesFromOtherStates(conn, StoredTriggerState.Paused, [StoredTriggerState.PausedBlocked], cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Freed {Count} triggers from 'acquired' / 'blocked' state.", rows);

            // clean up misfired jobs
            await RecoverMisfiredJobs(conn, true, cancellationToken).ConfigureAwait(false);

            // recover jobs marked for recovery that were not fully executed
            var recoveringJobTriggers = await Delegate.SelectTriggersForRecoveringJobs(conn, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("Recovering {Count} jobs that were in-progress at the time of the last shut-down.", recoveringJobTriggers.Count);

            foreach (IOperableTrigger trigger in recoveringJobTriggers)
            {
                if (await JobExists(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false))
                {
                    trigger.ComputeFirstFireTimeUtc(null);
                    await AddTrigger(conn, trigger, null, false, StoredTriggerState.Waiting, false, true, cancellationToken).ConfigureAwait(false);
                }
            }
            Logger.LogInformation("Recovery complete.");

            // remove lingering 'complete' triggers...
            var triggersInState = await Delegate.SelectTriggersInState(conn, StoredTriggerState.Complete, cancellationToken).ConfigureAwait(false);
            foreach (var trigger in triggersInState)
            {
                await DeleteTrigger(conn, trigger, cancellationToken).ConfigureAwait(false);
            }
            Logger.LogInformation("Removed  {Count} 'complete' triggers.", triggersInState.Count);

            // clean up any fired trigger entries
            int n = await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery(), cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("Removed {Count} stale fired job entries.", n);
        }
        catch (JobPersistenceException)
        {
            throw;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't recover jobs: " + e.Message, e);
        }
    }

    //private int lastRecoverCount = 0;

    protected internal async ValueTask<RecoverMisfiredJobsResult> RecoverMisfiredJobs(
        ConnectionAndTransactionHolder conn,
        bool recovering,
        CancellationToken cancellationToken = default)
    {
        // If recovering, we want to handle all of the misfired
        // triggers right away.
        int maxMisfiresToHandleAtATime = recovering ? -1 : MaxMisfiresToHandleAtATime;

        DateTimeOffset earliestNewTime = DateTimeOffset.MaxValue;

        // Read the whole batch as fully populated triggers in one round-trip, rather than reading keys
        // and then reading each trigger back individually.
        MisfiredTriggerBatch batch =
            await Delegate.SelectMisfiredTriggersToRecover(conn, StoredTriggerState.Waiting, MisfireTime, maxMisfiresToHandleAtATime, cancellationToken).ConfigureAwait(false);

        List<IOperableTrigger> misfiredTriggers = batch.Triggers;
        bool hasMoreMisfiredTriggers = batch.HasMore;

        if (hasMoreMisfiredTriggers)
        {
            Logger.LogInformation(
                "Handling the first {Count} triggers that missed their scheduled fire-time. More misfired triggers remain to be processed.",
                misfiredTriggers.Count);
        }
        else if (misfiredTriggers.Count > 0)
        {
            Logger.LogInformation(
                "Handling {Count} trigger(s) that missed their scheduled fire-time.", misfiredTriggers.Count);
        }
        else
        {
            // A healthy scheduler takes this branch on every misfire scan, forever, so it is Debug -
            // "nothing happened" is not news. The branches above, where something did misfire, stay
            // at Information.
            Logger.LogDebug(
                "Found 0 triggers that missed their scheduled fire-time.");
            return RecoverMisfiredJobsResult.NoOp;
        }

        // Cache calendars across the batch to avoid redundant DB round-trips
        // when multiple triggers reference the same calendar.
        Dictionary<string, ICalendar?> batchCalendarCache = new();

        List<MisfiredTriggerUpdate> updates = new(misfiredTriggers.Count);
        List<IOperableTrigger>? finalized = null;

        foreach (IOperableTrigger trig in misfiredTriggers)
        {
            try
            {
                updates.Add(await PrepareMisfiredTriggerUpdate(conn, trig, StoredTriggerState.Waiting, batchCalendarCache, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error preparing misfire update for trigger: '{TriggerKey}'", trig.Key);
                continue;
            }

            DateTimeOffset? nextTime = trig.NextFireTimeUtc;
            if (nextTime.HasValue)
            {
                if (nextTime.Value < earliestNewTime)
                {
                    earliestNewTime = nextTime.Value;
                }
            }
            else
            {
                (finalized ??= []).Add(trig);
            }
        }

        try
        {
            await Delegate.UpdateMisfiredTriggers(conn, updates, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error updating {Count} misfired trigger(s)", updates.Count);
            return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
        }

        if (finalized is not null)
        {
            foreach (IOperableTrigger trig in finalized)
            {
                await schedSignaler.NotifySchedulerListenersFinalized(trig, cancellationToken).ConfigureAwait(false);
            }
        }

        return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
    }

    /// <summary>
    /// Runs the in-memory half of misfire handling for one trigger — notify the listeners, apply the
    /// trigger's misfire policy, and work out the state and misfire-original-fire-time to persist — and
    /// returns the resulting update for the caller to apply as part of a batch.
    /// </summary>
    /// <remarks>
    /// Shares its logic with <see cref="DoUpdateOfMisfiredTriggerOptimized" />, which is the same thing
    /// for a single trigger that is written immediately.
    /// </remarks>
    private async ValueTask<MisfiredTriggerUpdate> PrepareMisfiredTriggerUpdate(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trig,
        StoredTriggerState newStateIfNotComplete,
        Dictionary<string, ICalendar?>? calendarCache,
        CancellationToken cancellationToken)
    {
        // Calendar lookup with batch-local cache (when available).
        ICalendar? calendar = null;
        if (trig.CalendarName is not null)
        {
            if (calendarCache is null || !calendarCache.TryGetValue(trig.CalendarName, out calendar))
            {
                calendar = await GetCalendar(conn, trig.CalendarName, cancellationToken).ConfigureAwait(false);
                if (calendarCache is not null)
                {
                    calendarCache[trig.CalendarName] = calendar;
                }
            }
        }

        await schedSignaler.NotifyTriggerListenersMisfired(trig, cancellationToken).ConfigureAwait(false);

        DateTimeOffset? originalFireTime = trig.NextFireTimeUtc;
        DateTimeOffset now = timeProvider.GetUtcNow();

        trig.UpdateAfterMisfire(calendar);

        // Determine new state.
        DateTimeOffset? newFireTime = trig.NextFireTimeUtc;
        StoredTriggerState newState = newFireTime.HasValue ? newStateIfNotComplete : StoredTriggerState.Complete;

        // Compute misfire-original-fire-time for "fire now" policies (folded into the single UPDATE).
        DateTimeOffset? misfireOrigFireTime = null;
        if (originalFireTime.HasValue && newFireTime.HasValue
            && originalFireTime.Value != newFireTime.Value
            && Math.Abs((newFireTime.Value - now).TotalMilliseconds) < TriggerBase.FireNowMisfireDetectionThresholdMs)
        {
            misfireOrigFireTime = originalFireTime;
        }

        return new MisfiredTriggerUpdate(trig, newState, misfireOrigFireTime);
    }

    /// <summary>
    /// Recover triggers that have been stuck in the ACQUIRED state for longer than
    /// expected. This can happen when <see cref="Extensibility.IJobStore.ReleaseAcquiredTrigger"/>
    /// fails after <see cref="Extensibility.IJobStore.TriggersFired"/> fails, leaving the trigger
    /// in ACQUIRED state with no one to fire or release it.
    /// </summary>
    protected async ValueTask<int> RecoverStaleAcquiredTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        TimeSpan staleThreshold = StaleAcquiredTriggerThreshold;
        DateTimeOffset staleCutoff = timeProvider.GetUtcNow() - staleThreshold;

        IReadOnlyCollection<FiredTriggerRecord> firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken).ConfigureAwait(false);

        int recoveredCount = 0;
        foreach (FiredTriggerRecord rec in firedTriggers)
        {
            // Use the later of scheduled fire time and acquisition time to avoid
            // premature recovery when IdleWaitTime is large (triggers are legitimately
            // ACQUIRED until their scheduled fire time arrives).
            DateTimeOffset effectiveTimestamp = rec.ScheduleTimestamp > rec.FireTimestamp
                ? rec.ScheduleTimestamp
                : rec.FireTimestamp;

            if (rec.FireInstanceState == StoredTriggerState.Acquired && effectiveTimestamp < staleCutoff)
            {
                try
                {
                    // Mirror ReleaseAcquiredTrigger: update from both ACQUIRED and BLOCKED,
                    // because TriggersFired may have moved the trigger to BLOCKED state (for
                    // DisallowConcurrentExecution jobs) while the fired record is still ACQUIRED.
                    await Delegate.UpdateTriggerStateFromOtherState(conn, rec.TriggerKey, StoredTriggerState.Waiting, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                    await Delegate.UpdateTriggerStateFromOtherState(conn, rec.TriggerKey, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                    await Delegate.DeleteFiredTrigger(conn, rec.FireInstanceId, cancellationToken).ConfigureAwait(false);
                    recoveredCount++;
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Error recovering stale acquired trigger '{TriggerKey}'", rec.TriggerKey);
                }
            }
        }

        if (recoveredCount > 0)
        {
            Logger.LogInformation("Recovered {RecoveredCount} trigger(s) stuck in ACQUIRED state (stale threshold: {StaleThreshold})", recoveredCount, staleThreshold);
        }

        return recoveredCount;
    }

    /// <summary>
    /// Pre-check (no lock required) to see if there are any fired trigger records in
    /// ACQUIRED state that have exceeded the stale threshold. Queries the same data as
    /// <see cref="RecoverStaleAcquiredTriggers"/> but only to decide whether to acquire
    /// the lock; the actual recovery re-queries under lock for correctness.
    /// </summary>
    private async Task<bool> HasStaleAcquiredTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset staleCutoff = timeProvider.GetUtcNow() - StaleAcquiredTriggerThreshold;

        IReadOnlyCollection<FiredTriggerRecord> firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken).ConfigureAwait(false);

        foreach (FiredTriggerRecord rec in firedTriggers)
        {
            DateTimeOffset effectiveTimestamp = rec.ScheduleTimestamp > rec.FireTimestamp
                ? rec.ScheduleTimestamp
                : rec.FireTimestamp;

            if (rec.FireInstanceState == StoredTriggerState.Acquired && effectiveTimestamp < staleCutoff)
            {
                return true;
            }
        }

        return false;
    }

    protected async ValueTask<bool> UpdateMisfiredTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newStateIfNotComplete,
        bool forceState,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var trig = (await GetTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))!;

            DateTimeOffset misfireTime = timeProvider.GetUtcNow();
            if (MisfireThreshold > TimeSpan.Zero)
            {
                misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
            }

            if (trig.NextFireTimeUtc.GetValueOrDefault() > misfireTime)
            {
                return false;
            }

            await DoUpdateOfMisfiredTriggerOptimized(conn, trig, newStateIfNotComplete, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't update misfired trigger '{triggerKey}': {e.Message}", e);
            return false;
        }
    }

    private async ValueTask DoUpdateOfMisfiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trig,
        bool forceState, StoredTriggerState newStateIfNotComplete, bool recovering)
    {
        ICalendar? calendar = null;
        if (trig.CalendarName is not null)
        {
            calendar = await GetCalendar(conn, trig.CalendarName).ConfigureAwait(false);
        }

        await schedSignaler.NotifyTriggerListenersMisfired(trig).ConfigureAwait(false);

        var originalFireTime = trig.NextFireTimeUtc;
        var now = timeProvider.GetUtcNow();

        trig.UpdateAfterMisfire(calendar);

        if (!trig.NextFireTimeUtc.HasValue)
        {
            await AddTrigger(conn, trig, null, true, StoredTriggerState.Complete, forceState, recovering).ConfigureAwait(false);
            await schedSignaler.NotifySchedulerListenersFinalized(trig).ConfigureAwait(false);
        }
        else
        {
            await AddTrigger(conn, trig, null, true, newStateIfNotComplete, forceState, recovering).ConfigureAwait(false);
        }

        // Persist original fire time for "fire now" misfire policies.
        // "Fire now" policies set nextFireTimeUtc to ~now; "reschedule next" policies
        // set it to a future schedule time where the existing code is already correct.
        var newFireTime = trig.NextFireTimeUtc;
        if (originalFireTime.HasValue && newFireTime.HasValue
            && originalFireTime.Value != newFireTime.Value
            && Math.Abs((newFireTime.Value - now).TotalMilliseconds) < TriggerBase.FireNowMisfireDetectionThresholdMs)
        {
            await Delegate.UpdateMisfireOriginalFireTime(conn, trig.Key, originalFireTime, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Optimized misfire update path that bypasses the AddTrigger method's unnecessary
    /// queries (existence check, pause-group checks, job retrieval, blocked-state check,
    /// trigger-type lookup). Safe when the caller already holds <c>SchedulerLock.TriggerAccess</c>
    /// and has determined the trigger's persisted state and corresponding
    /// <paramref name="newStateIfNotComplete"/> to use across the misfire update.
    /// This covers triggers found in WAITING state during batch recovery as well as
    /// single-trigger misfire handling in the acquisition and resume paths.
    /// </summary>
    private async ValueTask DoUpdateOfMisfiredTriggerOptimized(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trig,
        StoredTriggerState newStateIfNotComplete,
        CancellationToken cancellationToken)
    {
        MisfiredTriggerUpdate update = await PrepareMisfiredTriggerUpdate(conn, trig, newStateIfNotComplete, calendarCache: null, cancellationToken).ConfigureAwait(false);

        // Single targeted UPDATE (1-2 DB round-trips) instead of AddTrigger's 7-12.
        await Delegate.UpdateMisfiredTrigger(conn, trig, update.NewState, update.MisfireOriginalFireTime, cancellationToken).ConfigureAwait(false);

        if (!trig.NextFireTimeUtc.HasValue)
        {
            await schedSignaler.NotifySchedulerListenersFinalized(trig, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Store the given <see cref="IJobDetail" /> and <see cref="IOperableTrigger" />.
    /// </summary>
    /// <param name="job">Job to be stored.</param>
    /// <param name="trigger">Trigger to be stored.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask ScheduleJob(
        IJobDetail job,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.ScheduleJob,
            () => ExecuteInLock<object?>(LockOnInsert ? SchedulerLock.TriggerAccess : null, async conn =>
            {
                await AddJob(conn, job, false, cancellationToken).ConfigureAwait(false);
                await AddTrigger(conn, trigger, job, false, StoredTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);
                return null;
            }, cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.JobGroup, job.Key.Group);
                activity.SetTag(ActivityTags.JobName, job.Key.Name);
                activity.SetTag(ActivityTags.TriggerGroup, trigger.Key.Group);
                activity.SetTag(ActivityTags.TriggerName, trigger.Key.Name);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores the given <see cref="IJobDetail" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="replace">
    ///     If <see langword="true" />, any <see cref="IJob" /> existing in the
    ///     <see cref="IJobStore" /> with the same name &amp; group should be over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask AddJob(IJobDetail job, bool replace, CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.AddJob,
            () => ExecuteInLock(
                LockOnInsert || replace ? SchedulerLock.TriggerAccess : null,
                conn => AddJob(conn, job, replace, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.JobGroup, job.Key.Group);
                activity.SetTag(ActivityTags.JobName, job.Key.Name);
            }).ConfigureAwait(false);
    }

    /// <summary> <para>
    /// Insert or update a job.
    /// </para>
    /// </summary>
    protected async ValueTask AddJob(
        ConnectionAndTransactionHolder conn,
        IJobDetail newJob,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        bool existingJob = await JobExists(conn, newJob.Key, cancellationToken).ConfigureAwait(false);
        try
        {
            if (existingJob)
            {
                if (!replace)
                {
                    Throw.ObjectAlreadyExistsException(newJob);
                }
                if (await Delegate.UpdateJobDetail(conn, newJob, cancellationToken).ConfigureAwait(false) > 0)
                {
                    return;
                }
            }
            if (await Delegate.InsertJobDetail(conn, newJob, cancellationToken).ConfigureAwait(false) < 1)
            {
                throw new JobPersistenceException("Couldn't store job. Insert failed.");
            }
        }
        catch (IOException e)
        {
            Throw.JobPersistenceException("Couldn't store job: " + e.Message, e);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't store job: " + e.Message, e);
        }
    }

    /// <summary>
    /// Check existence of a given job.
    /// </summary>
    protected async ValueTask<bool> JobExists(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.JobExists(conn, jobKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't determine job existence (" + jobKey + "): " + e.Message, e);
            return false;
        }
    }

    /// <summary>
    /// Store the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> to be stored.</param>
    /// <param name="replace">
    ///     If <see langword="true" />, any <see cref="ITrigger" /> existing in
    ///     the <see cref="IJobStore" /> with the same name &amp; group should
    ///     be over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    /// if a <see cref="ITrigger" /> with the same name/group already
    /// exists, and replace is set to false.
    /// </exception>
    public async ValueTask AddTrigger(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.AddTrigger,
            () => ExecuteInLock(
                LockOnInsert || replace ? SchedulerLock.TriggerAccess : null,
                conn => AddTrigger(conn, trigger, null, replace, StoredTriggerState.Waiting, false, false, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, trigger.Key.Group);
                activity.SetTag(ActivityTags.TriggerName, trigger.Key.Name);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Insert or update a trigger.
    /// </summary>
    protected async ValueTask AddTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger newTrigger,
        IJobDetail? job,
        bool replace,
        StoredTriggerState state,
        bool forceState,
        bool recovering,
        CancellationToken cancellationToken = default)
    {
        bool existingTrigger = await TriggerExists(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false);

        if (existingTrigger && !replace)
        {
            Throw.ObjectAlreadyExistsException(newTrigger);
        }

        try
        {
            if (!forceState)
            {
                bool shouldBepaused = await Delegate.IsTriggerGroupPaused(conn, newTrigger.Key.Group, cancellationToken).ConfigureAwait(false);

                if (!shouldBepaused)
                {
                    shouldBepaused = await Delegate.IsTriggerGroupPaused(conn, AdoConstants.AllGroupsPaused, cancellationToken).ConfigureAwait(false);

                    if (shouldBepaused)
                    {
                        await Delegate.InsertPausedTriggerGroup(conn, newTrigger.Key.Group, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (shouldBepaused && state is StoredTriggerState.Waiting or StoredTriggerState.Acquired)
                {
                    state = StoredTriggerState.Paused;
                }
            }

            if (job is null)
            {
                job = await GetJob(conn, newTrigger.JobKey, cancellationToken).ConfigureAwait(false);
            }
            if (job is null)
            {
                Throw.JobPersistenceException($"The job ({newTrigger.JobKey}) referenced by the trigger does not exist.");
            }
            if (job.ConcurrentExecutionDisallowed && !recovering)
            {
                state = await CheckBlockedState(conn, job.Key, state, cancellationToken).ConfigureAwait(false);
            }
            if (existingTrigger)
            {
                // Preserve PreviousFireTimeUtc from the existing trigger when replacing,
                // so that context.PreviousFireTimeUtc is not lost on application restart (#1834)
                if (newTrigger.PreviousFireTimeUtc is null)
                {
                    IOperableTrigger? existingTrig = await Delegate.SelectTrigger(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false);
                    var prevFireTime = existingTrig?.PreviousFireTimeUtc;
                    if (prevFireTime is not null)
                    {
                        newTrigger.PreviousFireTimeUtc = prevFireTime;
                    }
                }

                await Delegate.UpdateTrigger(conn, newTrigger, state, job, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Delegate.InsertTrigger(conn, newTrigger, state, job, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            string message = $"Couldn't store trigger '{newTrigger.Key}' for '{newTrigger.JobKey}' job: {e.Message}";
            Throw.JobPersistenceException(message, e);
        }
    }

    /// <summary>
    /// Check existence of a given trigger.
    /// </summary>
    protected async ValueTask<bool> TriggerExists(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.TriggerExists(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't determine trigger existence (" + triggerKey + "): " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="IJob" /> with the given
    /// name, and any <see cref="ITrigger" /> s that reference
    /// it.
    /// </summary>
    ///
    /// <remarks>
    /// If removal of the <see cref="IJob" /> results in an empty group, the
    /// group should be removed from the <see cref="IJobStore" />'s list of
    /// known group names.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> if a <see cref="IJob" /> with the given name &amp;
    /// group was found and removed from the store.
    /// </returns>
    public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.DeleteJob,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, conn => DeleteJob(conn, jobKey, true, cancellationToken), cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.JobGroup, jobKey.Group);
                activity.SetTag(ActivityTags.JobName, jobKey.Name);
            });
    }

    protected async ValueTask<bool> DeleteJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        bool activeDeleteSafe,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var jobTriggers = await Delegate.SelectTriggerKeysForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);

            foreach (TriggerKey jobTrigger in jobTriggers)
            {
                await DeleteTriggerAndChildren(conn, jobTrigger, cancellationToken).ConfigureAwait(false);
            }

            return await DeleteJobAndChildren(conn, jobKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't remove job: " + e.Message, e);
            return default;
        }
    }

    public ValueTask<bool> DeleteJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.DeleteJobs,
            () => ExecuteInLock(
                SchedulerLock.TriggerAccess, async conn =>
                {
                    bool allFound = true;

                    // TODO: make this more efficient with a true bulk operation...
                    foreach (JobKey jobKey in jobKeys)
                    {
                        allFound = await DeleteJob(conn, jobKey, true, cancellationToken).ConfigureAwait(false) && allFound;
                    }

                    return allFound;
                }, cancellationToken));
    }

    public ValueTask<bool> DeleteTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.DeleteTriggers,
            () => ExecuteInLock(
                SchedulerLock.TriggerAccess,
                async conn =>
                {
                    bool allFound = true;

                    // TODO: make this more efficient with a true bulk operation...
                    foreach (TriggerKey triggerKey in triggerKeys)
                    {
                        allFound = await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false) && allFound;
                    }

                    return allFound;
                }, cancellationToken));
    }

    public async ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.ScheduleJobs,
            () => ExecuteInLock(
                LockOnInsert || replace ? SchedulerLock.TriggerAccess : null, async conn =>
                {
                    // TODO: make this more efficient with a true bulk operation...
                    foreach (var pair in triggersAndJobs)
                    {
                        var job = pair.Key;
                        var triggers = pair.Value;
                        await AddJob(conn, job, replace, cancellationToken).ConfigureAwait(false);
                        foreach (var trigger in triggers)
                        {
                            await AddTrigger(conn, trigger, job, replace, StoredTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete a job and its listeners.
    /// </summary>
    /// <seealso cref="AdoJobStoreBase.DeleteJob(ConnectionAndTransactionHolder, JobKey, bool, CancellationToken)" />
    /// <seealso cref="DeleteTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
    private async ValueTask<bool> DeleteJobAndChildren(
        ConnectionAndTransactionHolder conn,
        JobKey key,
        CancellationToken cancellationToken)
    {
        // Clean up any fired trigger records referencing this job to prevent
        // orphaned EXECUTING rows that block re-creation of the same job (#1696)
        await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery { Job = key }, cancellationToken).ConfigureAwait(false);

        return await Delegate.DeleteJobDetail(conn, key, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Delete a trigger, its listeners, and its Simple/Cron/BLOB sub-table entry.
    /// </summary>
    /// <seealso cref="DeleteJob(ConnectionAndTransactionHolder, JobKey, bool, CancellationToken)" />
    /// <seealso cref="DeleteTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
    /// <seealso cref="ReplaceTrigger(ConnectionAndTransactionHolder, TriggerKey, IOperableTrigger, CancellationToken)" />
    private async ValueTask<bool> DeleteTriggerAndChildren(
        ConnectionAndTransactionHolder conn,
        TriggerKey key,
        CancellationToken cancellationToken)
    {
        bool deleted = await Delegate.DeleteTrigger(conn, key, cancellationToken).ConfigureAwait(false) > 0;
        
        // Also clean up any fired trigger records to prevent recovery triggers from being created
        if (deleted)
        {
            await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery { Trigger = key }, cancellationToken).ConfigureAwait(false);
        }
        
        return deleted;
    }

    /// <summary>
    /// Retrieve the <see cref="IJobDetail" /> for the given
    /// <see cref="IJob" />.
    /// </summary>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="IJob" />, or null if there is no match.</returns>
    public ValueTask<IJobDetail?> GetJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetJob(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected async ValueTask<IJobDetail?> GetJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var job = await Delegate.SelectJobDetail(conn, jobKey, TypeLoader, cancellationToken).ConfigureAwait(false);
            return job;
        }
        catch (TypeLoadException e)
        {
            Throw.JobPersistenceException("Couldn't retrieve job because a required type was not found: " + e.Message, e);
            return default;
        }
        catch (IOException e)
        {
            Throw.JobPersistenceException("Couldn't retrieve job because the BLOB couldn't be deserialized: " + e.Message, e);
            return default;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't retrieve job: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given name.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// If removal of the <see cref="ITrigger" /> results in an empty group, the
    /// group should be removed from the <see cref="IJobStore" />'s list of
    /// known group names.
    /// </para>
    ///
    /// <para>
    /// If removal of the <see cref="ITrigger" /> results in an 'orphaned' <see cref="IJob" />
    /// that is not 'durable', then the <see cref="IJob" /> should be deleted
    /// also.
    /// </para>
    /// </remarks>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> if a <see cref="ITrigger" /> with the given
    /// name &amp; group was found and removed from the store.
    ///</returns>
    public ValueTask<bool> DeleteTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.DeleteTrigger,
            () => ExecuteInLock(
                SchedulerLock.TriggerAccess,
                conn => DeleteTrigger(conn, triggerKey, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, triggerKey.Group);
                activity.SetTag(ActivityTags.TriggerName, triggerKey.Name);
            });
    }

    protected ValueTask<bool> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return DeleteTrigger(conn, triggerKey, null, cancellationToken);
    }

    protected async ValueTask<bool> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IJobDetail? job,
        CancellationToken cancellationToken = default)
    {
        bool removedTrigger;
        try
        {
            // this must be called before we delete the trigger, obviously
            // we use fault tolerant type loading as we only want to delete things
            if (job is null)
            {
                job = await Delegate.SelectJobForTrigger(conn, triggerKey, new NullJobTypeLoader(), loadJobType: false, cancellationToken).ConfigureAwait(false);
            }

            removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (null != job && !job.Durable)
            {
                int numTriggers = await Delegate.CountTriggersForJob(conn, job.Key, cancellationToken).ConfigureAwait(false);
                if (numTriggers == 0)
                {
                    // Don't call DeleteJob() because we don't want to check for
                    // triggers again.
                    if (await DeleteJobAndChildren(conn, job.Key, cancellationToken).ConfigureAwait(false))
                    {
                        await schedSignaler.NotifySchedulerListenersJobDeleted(job.Key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
            return default;
        }

        return removedTrigger;
    }

    private sealed class NullJobTypeLoader : ITypeLoader
    {
        public Type? LoadType(string name)
        {
            return null;
        }
    }

    /// <see cref="IJobStore.ReplaceTrigger(TriggerKey, IOperableTrigger, CancellationToken)" />
    public ValueTask<bool> ReplaceTrigger(
        TriggerKey triggerKey,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.ReplaceTrigger,
            () => ExecuteInLock(SchedulerLock.TriggerAccess,
                conn => ReplaceTrigger(conn, triggerKey, trigger, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, triggerKey.Group);
                activity.SetTag(ActivityTags.TriggerName, triggerKey.Name);
            });
    }

    protected async ValueTask<bool> ReplaceTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IOperableTrigger newTrigger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // this must be called before we delete the trigger, obviously
            var job = await Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoader, loadJobType: true, cancellationToken).ConfigureAwait(false);

            if (job is null)
            {
                return false;
            }

            if (!newTrigger.JobKey.Equals(job.Key))
            {
                Throw.JobPersistenceException("New trigger is not related to the same job as the old trigger.");
            }

            bool removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            await AddTrigger(conn, newTrigger, job, false, StoredTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);

            return removedTrigger;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> UpdateTriggerDetails(
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.UpdateTriggerDetails,
            () => ExecuteInLock(
                SchedulerLock.TriggerAccess,
                conn => UpdateTriggerDetails(conn, triggerKey, update, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, triggerKey.Group);
                activity.SetTag(ActivityTags.TriggerName, triggerKey.Name);
            });
    }

    protected async ValueTask<bool> UpdateTriggerDetails(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IOperableTrigger? existing = await Delegate.SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return false;
            }

            if (!update.HasDescription && !update.HasPriority && !update.HasJobDataMap
                && !update.HasCalendarName && !update.HasMisfireInstruction && !update.HasPreferredNode
                && !update.HasExecutionGroup)
            {
                return true;
            }

            update.EnsureMisfireInstructionMatchesFamily(existing, triggerKey);

            if (update.HasCalendarName && update.CalendarName is not null)
            {
                bool calExists = await CalendarExists(conn, update.CalendarName, cancellationToken).ConfigureAwait(false);
                if (!calExists)
                {
                    Throw.JobPersistenceException($"Calendar '{update.CalendarName}' does not exist.");
                }
            }

            if (update.HasDescription)
            {
                existing.Description = update.Description;
            }

            if (update.HasPriority)
            {
                existing.Priority = update.Priority;
            }

            if (update.HasJobDataMap)
            {
                JobDataMap newMap = update.JobDataMap is { Count: > 0 }
                    ? new JobDataMap((IDictionary<string, object?>) update.JobDataMap)
                    : new JobDataMap();

                // Force dirty flag so Delegate.UpdateTrigger writes the BLOB
                newMap[SchedulerConstants.ForceJobDataMapDirty] = true;
                newMap.Remove(SchedulerConstants.ForceJobDataMapDirty);

                existing.JobDataMap = newMap;
            }

            if (update.HasCalendarName)
            {
                existing.CalendarName = update.CalendarName;
            }

            if (update.HasMisfireInstruction)
            {
                existing.MisfireInstructionCode = update.MisfireInstructionCode;
            }

            if (update.HasPreferredNode)
            {
                // Setting the property marks the pin dirty, so the subsequent store writes the
                // preferred node columns.
                existing.PreferredNode = update.PreferredNode;
            }

            if (update.HasExecutionGroup)
            {
                // EXECUTION_GROUP is part of the generic trigger UPDATE below, so nothing more is
                // needed to persist it.
                existing.ExecutionGroup = update.ExecutionGroup;
            }

            StoredTriggerState state = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            IJobDetail? job = await Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoader, loadJobType: true, cancellationToken).ConfigureAwait(false);

            if (job is null)
            {
                Throw.JobPersistenceException($"The job referenced by trigger '{triggerKey}' does not exist.");
            }

            await Delegate.UpdateTrigger(conn, existing, state, job!, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception e) when (e is not JobPersistenceException)
        {
            Throw.JobPersistenceException($"Couldn't update trigger details for '{triggerKey}': {e.Message}", e);
            return default;
        }
    }

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="ITrigger" />, or null if there is no match.</returns>
    public ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => GetTrigger(conn, triggerKey, cancellationToken),
            cancellationToken);
    }

    protected async ValueTask<IOperableTrigger?> GetTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var trigger = await Delegate.SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            return trigger;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't retrieve trigger: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get the current state of the identified <see cref="ITrigger" />.
    /// </summary>
    /// <seealso cref="TriggerState.Normal" />
    /// <seealso cref="TriggerState.Paused" />
    /// <seealso cref="TriggerState.Complete" />
    /// <seealso cref="TriggerState.Error" />
    /// <seealso cref="TriggerState.None" />
    /// <seealso cref="TriggerState.Blocked" />
    /// <seealso cref="TriggerState.Executing" />
    public ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggerState(conn, triggerKey, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Gets the state of the trigger.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    protected async ValueTask<TriggerState> GetTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TriggerExecutionState stored = await Delegate
                .SelectTriggerStateWithExecuting(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            return TriggerStateMapping.ToTriggerState(stored.State, stored.IsExecuting);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't determine state of trigger ({triggerKey}): {e.Message}", e);
            return default;
        }
    }

    public async ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return await activityTracer.Trace(
            OperationName.JobStore.ResetTriggerFromErrorState,
            () => ExecuteInLock(
                SchedulerLock.TriggerAccess,
                conn => ResetTriggerFromErrorState(conn, triggerKey, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, triggerKey.Group);
                activity.SetTag(ActivityTags.TriggerName, triggerKey.Name);
            }).ConfigureAwait(false);
    }

    private async ValueTask<bool> ResetTriggerFromErrorState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            StoredTriggerState newState = StoredTriggerState.Waiting;

            if (await Delegate.IsTriggerGroupPaused(conn, triggerKey.Group, cancellationToken).ConfigureAwait(false))
            {
                newState = StoredTriggerState.Paused;
            }

            int updated = await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
            if (updated == 0)
            {
                // no trigger with the key, or it was not in the error state
                return false;
            }

            Logger.LogInformation("Trigger {TriggerKey} reset from ERROR state to: {NewState}", triggerKey, newState);
            return true;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't reset from error state of trigger ({triggerKey}): {e.Message}", e);
            return default;
        }
    }

    /// <summary>
    /// Store the given <see cref="ICalendar" />.
    /// </summary>
    /// <param name="calendarName">The name of the calendar.</param>
    /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
    /// <param name="options">
    /// Whether an existing calendar of the same name may be over-written, and whether the triggers
    /// referencing it have their next fire time re-computed.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    ///           if a <see cref="ICalendar" /> with the same name already
    ///           exists, and replace is set to false.
    /// </exception>
    public async ValueTask AddCalendar(
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AddCalendarOptions();
        await activityTracer.Trace(
            OperationName.JobStore.AddCalendar,
            () => ExecuteInLock(
                LockOnInsert || options.UpdateTriggers ? SchedulerLock.TriggerAccess : null,
                conn => AddCalendar(conn, calendarName, calendar, options, cancellationToken),
                cancellationToken)).ConfigureAwait(false);
    }

    protected async ValueTask AddCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool existingCal = await CalendarExists(conn, calendarName, cancellationToken).ConfigureAwait(false);
            if (existingCal && !options.Replace)
            {
                Throw.ObjectAlreadyExistsException("Calendar with name '" + calendarName + "' already exists.");
            }

            if (existingCal)
            {
                if (await Delegate.UpdateCalendar(conn, calendarName, calendar, cancellationToken).ConfigureAwait(false) < 1)
                {
                    Throw.JobPersistenceException("Couldn't store calendar.  Update failed.");
                }

                if (options.UpdateTriggers)
                {
                    var triggers = await Delegate.SelectTriggersForCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false);

                    foreach (IOperableTrigger trigger in triggers)
                    {
                        trigger.UpdateWithNewCalendar(calendar, MisfireThreshold);
                        StoredTriggerState triggerState = await Delegate.SelectTriggerState(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                        if (triggerState == StoredTriggerState.Deleted)
                        {
                            continue;
                        }
                        await AddTrigger(conn, trigger, null, true, triggerState, true, false, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (await Delegate.InsertCalendar(conn, calendarName, calendar, cancellationToken).ConfigureAwait(false) < 1)
                {
                    Throw.JobPersistenceException("Couldn't store calendar.  Insert failed.");
                }
            }

            if (!Clustered)
            {
                calendarCache[calendarName] = calendar; // lazy-cache
            }
        }
        catch (IOException e)
        {
            Throw.JobPersistenceException(
                "Couldn't store calendar because the BLOB couldn't be serialized: " + e.Message, e);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't store calendar: " + e.Message, e);
        }
    }

    protected async ValueTask<bool> CalendarExists(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.CalendarExists(conn, calendarName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't determine calendar existence (" + calendarName + "): " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="ICalendar" /> with the given name.
    /// </summary>
    /// <remarks>
    /// If removal of the <see cref="ICalendar" /> would result in
    /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
    /// <see cref="JobPersistenceException" /> will be thrown.
    /// </remarks>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be removed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> if a <see cref="ICalendar" /> with the given name
    /// was found and removed from the store.
    ///</returns>
    public ValueTask<bool> DeleteCalendar(
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.DeleteCalendar,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, conn => DeleteCalendar(conn, calendarName, cancellationToken), cancellationToken));
    }

    protected async ValueTask<bool> DeleteCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await Delegate.CalendarIsReferenced(conn, calendarName, cancellationToken).ConfigureAwait(false))
            {
                Throw.JobPersistenceException("Calendar cannot be removed if it is referenced by a trigger!");
            }

            if (!Clustered)
            {
                calendarCache.Remove(calendarName);
            }

            return await Delegate.DeleteCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't remove calendar: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="ICalendar" />, or null if there is no match.</returns>
    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => GetCalendar(conn, calendarName, cancellationToken),
            cancellationToken);
    }

    protected async ValueTask<ICalendar?> GetCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        // all calendars are persistent, but we lazy-cache them during run
        // time as long as we aren't running clustered.
        ICalendar? calendar = null;
        if (!Clustered)
        {
            calendarCache.TryGetValue(calendarName, out calendar);
        }
        if (calendar is not null)
        {
            return calendar;
        }

        try
        {
            calendar = await Delegate.SelectCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false);
            if (!Clustered)
            {
                calendarCache[calendarName] = calendar; // lazy-cache...
            }
            return calendar;
        }
        catch (IOException e)
        {
            Throw.JobPersistenceException("Couldn't retrieve calendar because the BLOB couldn't be deserialized: " + e.Message, e);
            return default;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't retrieve calendar: " + e.Message, e);
            return default;
        }
    }

    protected async ValueTask<List<JobKey>> GetJobNames(ConnectionAndTransactionHolder conn, GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectJobKeysInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain job names: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Determine whether a <see cref="IJob"/> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Job exists with the given identifier</returns>
    public ValueTask<bool> Exists(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => Exists(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected async ValueTask<bool> Exists(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.JobExists(conn, jobKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't check for existence of job: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="triggerKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Trigger exists with the given identifier</returns>
    public ValueTask<bool> Exists(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => Exists(conn, triggerKey, cancellationToken), cancellationToken);
    }

    protected async ValueTask<bool> Exists(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.TriggerExists(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't check for existence of job: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    /// <remarks>
    /// </remarks>
    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.Clear,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, conn => Clear(conn, cancellationToken), cancellationToken)).ConfigureAwait(false);
    }

    protected async ValueTask Clear(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Delegate.ClearData(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Error clearing scheduling data: " + e.Message, e);
        }
    }

    protected async ValueTask<List<string>> GetTriggerGroupNames(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectTriggerGroupNames(conn, GroupMatcher<TriggerKey>.AnyGroup(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryJobs(conn, query, cancellationToken), cancellationToken);
    }

    protected async ValueTask<PagedResult<JobHeader>> QueryJobs(
        ConnectionAndTransactionHolder conn,
        JobQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectJobHeaders(conn, query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't query jobs: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryTriggers(conn, query, cancellationToken), cancellationToken);
    }

    protected async ValueTask<PagedResult<TriggerHeader>> QueryTriggers(
        ConnectionAndTransactionHolder conn,
        TriggerQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectTriggerHeaders(conn, query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't query triggers: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryJobGroups(conn, query, cancellationToken), cancellationToken);
    }

    protected async ValueTask<PagedResult<JobGroup>> QueryJobGroups(
        ConnectionAndTransactionHolder conn,
        JobGroupQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectJobGroups(conn, query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't query job groups: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryTriggerGroups(conn, query, cancellationToken), cancellationToken);
    }

    protected async ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(
        ConnectionAndTransactionHolder conn,
        TriggerGroupQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectTriggerGroups(conn, query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't query trigger groups: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryCalendarNames(conn, query, cancellationToken), cancellationToken);
    }

    protected async ValueTask<PagedResult<string>> QueryCalendarNames(
        ConnectionAndTransactionHolder conn,
        CalendarQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectCalendarNames(conn, query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't query calendar names: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<List<IJobDetail>> GetJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetJobs(conn, jobKeys, cancellationToken), cancellationToken);
    }

    protected async ValueTask<List<IJobDetail>> GetJobs(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectJobDetails(conn, jobKeys, TypeLoader, cancellationToken).ConfigureAwait(false);
        }
        catch (TypeLoadException e)
        {
            Throw.JobPersistenceException("Couldn't retrieve jobs because a required type was not found: " + e.Message, e);
            return default;
        }
        catch (IOException e)
        {
            Throw.JobPersistenceException("Couldn't retrieve jobs because the BLOB couldn't be deserialized: " + e.Message, e);
            return default;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't retrieve jobs: " + e.Message, e);
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggers(conn, triggerKeys, cancellationToken), cancellationToken);
    }

    protected async ValueTask<List<IOperableTrigger>> GetTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectTriggers(conn, triggerKeys, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't retrieve triggers: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get all of the Triggers that are associated to the given Job.
    /// </summary>
    /// <remarks>
    /// If there are no matches, a zero-length array should be returned.
    /// </remarks>
    public ValueTask<List<IOperableTrigger>> GetTriggersForJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggersForJob(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected async ValueTask<List<IOperableTrigger>> GetTriggersForJob(ConnectionAndTransactionHolder conn, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain triggers for job: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given name.
    /// </summary>
    public async ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return await activityTracer.Trace(
            OperationName.JobStore.PauseTrigger,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseTrigger(conn, triggerKey, cancellationToken), cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, triggerKey.Group);
                activity.SetTag(ActivityTags.TriggerName, triggerKey.Name);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given name.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the trigger existed in a pausable state and was moved into the
    /// paused state by this call.
    /// </returns>
    protected async ValueTask<bool> PauseTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            StoredTriggerState oldState = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (oldState is StoredTriggerState.Waiting or StoredTriggerState.Acquired)
            {
                return await Delegate.UpdateTriggerState(conn, triggerKey, StoredTriggerState.Paused, cancellationToken).ConfigureAwait(false) > 0;
            }

            if (oldState == StoredTriggerState.Blocked)
            {
                return await Delegate.UpdateTriggerState(conn, triggerKey, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false) > 0;
            }

            // missing, already paused, or in a state that cannot be paused
            return false;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't pause trigger '{triggerKey}': {e.Message}", e);
            return default;
        }
    }

    /// <summary>
    /// Pause the <see cref="IJob" /> with the given name - by
    /// pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    /// <seealso cref="ResumeJob(JobKey,CancellationToken)" />
    public async ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return await activityTracer.Trace(
            OperationName.JobStore.PauseJob,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
            {
                if (!await Exists(conn, jobKey, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                foreach (IOperableTrigger trigger in triggers)
                {
                    await PauseTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                }

                return true;
            }, cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.JobGroup, jobKey.Group);
                activity.SetTag(ActivityTags.JobName, jobKey.Name);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause all of the <see cref="IJob" />s in the given
    /// group - by pausing all of their <see cref="ITrigger" />s.
    /// </summary>
    /// <seealso cref="ResumeJobs" />
    public ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.PauseJobs,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
            {
                var groupNames = new HashSet<string>();
                var jobNames = await GetJobNames(conn, matcher, cancellationToken).ConfigureAwait(false);

                foreach (JobKey jobKey in jobNames)
                {
                    var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                    foreach (IOperableTrigger trigger in triggers)
                    {
                        await PauseTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    }
                    groupNames.Add(jobKey.Group);
                }

                return new List<string>(groupNames);
            }, cancellationToken));
    }

    /// <summary>
    /// Determines if a Trigger for the given job should be blocked.
    /// State can only transition to StatePausedBlocked/StateBlocked from
    /// StatePaused/StateWaiting respectively.
    /// </summary>
    /// <returns>StatePausedBlocked, StateBlocked, or the currentState. </returns>
    protected async ValueTask<StoredTriggerState> CheckBlockedState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        StoredTriggerState currentState,
        CancellationToken cancellationToken = default)
    {
        // State can only transition to BLOCKED from PAUSED or WAITING.
        if (currentState != StoredTriggerState.Waiting && currentState != StoredTriggerState.Paused)
        {
            return currentState;
        }

        try
        {
            var lst = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { Job = jobKey }, cancellationToken).ConfigureAwait(false);

            if (lst.Count > 0)
            {
                FiredTriggerRecord rec = lst[0];
                if (rec.JobDisallowsConcurrentExecution) // TODO: worry about failed/recovering/volatile job  states?
                {
                    return StoredTriggerState.Paused == currentState ? StoredTriggerState.PausedBlocked : StoredTriggerState.Blocked;
                }
            }

            return currentState;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't determine if trigger should be in a blocked state '" + jobKey + "': " + e.Message, e);
            return default;
        }
    }

    public async ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return await activityTracer.Trace(
            OperationName.JobStore.ResumeTrigger,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, conn => ResumeTrigger(conn, triggerKey, cancellationToken), cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, triggerKey.Group);
                activity.SetTag(ActivityTags.TriggerName, triggerKey.Name);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the
    /// given name.
    /// </summary>
    /// <remarks>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> if the trigger existed in a paused state and was resumed by this
    /// call.
    /// </returns>
    protected async ValueTask<bool> ResumeTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            StoredTriggerHeader? status = await Delegate.SelectTriggerHeader(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (status?.NextFireTimeUtc is null || status.NextFireTimeUtc == DateTimeOffset.MinValue)
            {
                return false;
            }

            if (status.State is not StoredTriggerState.Paused and not StoredTriggerState.PausedBlocked)
            {
                // not paused, nothing to resume
                return false;
            }

            bool blocked = status.State == StoredTriggerState.PausedBlocked;

            StoredTriggerState newState = await CheckBlockedState(conn, status.JobKey, StoredTriggerState.Waiting, cancellationToken).ConfigureAwait(false);

            bool misfired = false;

            if (schedulerRunning && status.NextFireTimeUtc.Value < timeProvider.GetUtcNow())
            {
                misfired = await UpdateMisfiredTrigger(conn, triggerKey, newState, forceState: true, cancellationToken).ConfigureAwait(false);
            }

            if (misfired)
            {
                return true;
            }

            if (blocked)
            {
                return await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false) > 0;
            }

            return await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StoredTriggerState.Paused, cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't resume trigger '" + triggerKey + "': " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="IJob" /> with the
    /// given name.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob"/>'s <see cref="ITrigger" /> s missed one
    /// or more fire-times, then the <see cref="ITrigger" />'s misfire
    /// instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseJob(JobKey,CancellationToken)" />
    public async ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return await activityTracer.Trace(
            OperationName.JobStore.ResumeJob,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
            {
                if (!await Exists(conn, jobKey, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                foreach (IOperableTrigger trigger in triggers)
                {
                    await ResumeTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                }

                return true;
            }, cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.JobGroup, jobKey.Group);
                activity.SetTag(ActivityTags.JobName, jobKey.Name);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="IJob" />s in
    /// the given group.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
    /// missed one or more fire-times, then the <see cref="ITrigger" />'s
    /// misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseJobs" />
    public ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.ResumeJobs,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
            {
                var jobKeys = await GetJobNames(conn, matcher, cancellationToken).ConfigureAwait(false);
                var groupNames = new HashSet<string>();

                foreach (JobKey jobKey in jobKeys)
                {
                    var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                    foreach (IOperableTrigger trigger in triggers)
                    {
                        await ResumeTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    }
                    groupNames.Add(jobKey.Group);
                }
                return groupNames.ToList();
            }, cancellationToken));
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// </summary>
    /// <seealso cref="ResumeTriggers(Quartz.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    public ValueTask<List<string>> PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.PauseTriggers,
            () => ExecuteInLock(
                SchedulerLock.TriggerAccess,
                conn => PauseTriggerGroup(conn, matcher, cancellationToken),
                cancellationToken));
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// </summary>
    protected async ValueTask<List<string>> PauseTriggerGroup(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        try
        {
            await Delegate.UpdateTriggerGroupStateFromOtherStates(conn, matcher, StoredTriggerState.Paused,
                [StoredTriggerState.Acquired, StoredTriggerState.Waiting], cancellationToken).ConfigureAwait(false);

            await Delegate.UpdateTriggerGroupStateFromOtherState(conn, matcher, StoredTriggerState.PausedBlocked,
                StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);

            var groups = new List<string>(await Delegate.SelectTriggerGroupNames(conn, matcher, cancellationToken).ConfigureAwait(false));

            // make sure to account for an exact group match for a group that doesn't yet exist
            StringOperator op = matcher.CompareWithOperator;
            if (op.Equals(StringOperator.Equality) && !groups.Contains(matcher.CompareToValue))
            {
                groups.Add(matcher.CompareToValue);
            }

            foreach (string group in groups)
            {
                if (!await Delegate.IsTriggerGroupPaused(conn, group, cancellationToken).ConfigureAwait(false))
                {
                    await Delegate.InsertPausedTriggerGroup(conn, group, cancellationToken).ConfigureAwait(false);
                }
            }

            return groups;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't pause trigger group '" + matcher + "': " + e.Message, e);
            return default;
        }
    }

    public ValueTask<List<string>> ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.ResumeTriggers,
            () => ExecuteInLock(
                SchedulerLock.TriggerAccess, conn => ResumeTriggers(conn, matcher, cancellationToken),
                cancellationToken));
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s
    /// in the given group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    protected async ValueTask<List<string>> ResumeTriggers(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Delegate.DeletePausedTriggerGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
            var groups = new HashSet<string>();

            List<TriggerKey>? keys = await Delegate.SelectTriggerKeysInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);

            foreach (TriggerKey key in keys)
            {
                await ResumeTrigger(conn, key, cancellationToken).ConfigureAwait(false);
                groups.Add(key.Group);
            }

            return [..groups];
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't pause trigger group '" + matcher + "': " + e.Message, e);
            return default;
        }
    }

    public async ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.PauseAll,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseAll(conn, cancellationToken), cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause all triggers - equivalent of calling <see cref="PauseTriggers(Quartz.GroupMatcher{Quartz.TriggerKey},CancellationToken)" />
    /// on every group.
    /// <para>
    /// When <see cref="ResumeAll(CancellationToken)" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="ResumeAll(CancellationToken)" />
    protected async ValueTask PauseAll(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        var groupNames = await GetTriggerGroupNames(conn, cancellationToken).ConfigureAwait(false);

        foreach (string groupName in groupNames)
        {
            await PauseTriggerGroup(conn, GroupMatcher<TriggerKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (!await Delegate.IsTriggerGroupPaused(conn, AdoConstants.AllGroupsPaused, cancellationToken).ConfigureAwait(false))
            {
                await Delegate.InsertPausedTriggerGroup(conn, AdoConstants.AllGroupsPaused, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't pause all trigger groups: " + e.Message, e);
        }
    }

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(Quartz.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    /// on every group.
    /// </summary>
    /// <remarks>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseAll(CancellationToken)" />
    public async ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.ResumeAll,
            () => ExecuteInLock(SchedulerLock.TriggerAccess, conn => ResumeAll(conn, cancellationToken), cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(Quartz.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    /// on every group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="PauseAll(CancellationToken)" />
    protected async ValueTask ResumeAll(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        var triggerGroupNames = await GetTriggerGroupNames(conn, cancellationToken).ConfigureAwait(false);

        foreach (string groupName in triggerGroupNames)
        {
            await ResumeTriggers(conn, GroupMatcher<TriggerKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await Delegate.DeletePausedTriggerGroup(conn, GroupMatcher<TriggerKey>.GroupEquals(AdoConstants.AllGroupsPaused), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't resume all trigger groups: " + e.Message, e);
        }
    }

    private static long ftrCtr = TimeProvider.System.GetTimestamp();

    /// <summary>
    /// Get a handle to the next N triggers to be fired, and mark them as 'reserved'
    /// by the calling scheduler.
    /// </summary>
    /// <seealso cref="ReleaseAcquiredTrigger(IOperableTrigger, CancellationToken)" />
    /// <inheritdoc />
    public virtual ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SchedulerLock? lockKind;
        if (AcquireTriggersWithinLock || request.MaxCount > 1)
        {
            lockKind = SchedulerLock.TriggerAccess;
        }
        else
        {
            lockKind = null;
        }

        return activityTracer.Trace(
            OperationName.JobStore.AcquireNextTriggers,
            () => ExecuteInLocalTransactionLock(
                lockKind,
                conn => AcquireNextTrigger(conn, request, cancellationToken),
                async (conn, result) =>
                {
                    try
                    {
                        var acquired = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken).ConfigureAwait(false);
                        var fireInstanceIds = new HashSet<string>();
                        foreach (FiredTriggerRecord ft in acquired)
                        {
                            fireInstanceIds.Add(ft.FireInstanceId!);
                        }
                        foreach (IOperableTrigger tr in result)
                        {
                            if (fireInstanceIds.Contains(tr.FireInstanceId))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                    catch (Exception e)
                    {
                        Throw.JobPersistenceException("error validating trigger acquisition", e);
                        return default;
                    }
                },
                cancellationToken: cancellationToken),
            activity => activity.SetTag(ActivityTags.BatchSize, request.MaxCount));
    }

    /// <summary>
    /// Builds the criteria <see cref="IDriverDelegate.SelectTriggersToAcquire" /> is called with when
    /// this node looks for the next triggers to fire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the override seam for acquisition filtering (see issue #2238). A derived store narrows
    /// what its own node picks up by starting from <c>base.CreateAcquisitionCriteria(request)</c> and
    /// returning a copy with the additional filters set — the criteria are a record, so <c>with</c>
    /// leaves everything the base decided in place.
    /// </para>
    /// <para>
    /// Called once per acquisition attempt, inside the store's internal retry loop, so an override
    /// runs again for every retry rather than once per <see cref="AcquireNextTriggers" /> call.
    /// </para>
    /// <para>
    /// An override may lower <see cref="TriggerAcquisitionCriteria.MaxCount" /> but must never raise it
    /// above the request's: the choice between lock-free and locked acquisition was already made from the
    /// request before this factory runs, so a raised count is only caught by post-acquisition validation
    /// and the surplus is released and retried — a performance hazard rather than corruption, but a
    /// silent one.
    /// </para>
    /// <para>
    /// <see cref="TriggerAcquisitionCriteria" />'s remarks state the contract a new filter has to
    /// keep: it is another optional property on that record, defaulting to "no additional filtering".
    /// </para>
    /// </remarks>
    /// <param name="request">What the scheduler asked this store to acquire.</param>
    protected virtual TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
    {
        // The liveness cutoff determines when a preferred node is considered dead, releasing
        // its pinned triggers to other nodes. SQL check: a node is live if
        // (now - lastCheckin) <= checkinInterval + misfireThreshold. This is equivalent to
        // CalcFailedIfAfter for healthy acquiring nodes; the formulas only diverge when the
        // acquiring node itself is unhealthy (its own checkins are late), in which case
        // CalcFailedIfAfter becomes MORE lenient while this stays fixed. Being more
        // aggressive in that edge case is the safer direction — it prevents triggers pinned
        // to a dead node from being stuck when the surviving nodes are under load.
        DateTimeOffset liveNodeCutoff = timeProvider.GetUtcNow() - ClusterCheckinMisfireThreshold;

        return new TriggerAcquisitionCriteria
        {
            NoLaterThan = request.NoLaterThan + request.TimeWindow,
            NoEarlierThan = MisfireTime,
            MaxCount = request.MaxCount,
            ExecutionLimits = request.ExecutionLimits,
            LiveNodeCutoff = liveNodeCutoff,
        };
    }

    // TODO: this really ought to return something like a FiredTriggerBundle,
    // so that the fireInstanceId doesn't have to be on the trigger...

    protected async ValueTask<List<IOperableTrigger>> AcquireNextTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> acquiredTriggers = [];
        HashSet<JobKey> acquiredJobKeysForNoConcurrentExec = [];
        const int MaxDoLoopRetry = 3;
        int currentLoopCount = 0;

        do
        {
            currentLoopCount++;
            try
            {
                // Built inside the loop, so each retry asks again and sees the time it retried at.
                TriggerAcquisitionCriteria criteria = CreateAcquisitionCriteria(request);

                List<TriggerAcquireResult> results = await Delegate.SelectTriggersToAcquire(conn, criteria, cancellationToken).ConfigureAwait(false);

                // No trigger is ready to fire yet.
                if (results.Count == 0)
                {
                    return acquiredTriggers;
                }

                DateTimeOffset batchEnd = request.NoLaterThan;

                foreach (var result in results)
                {
                    TriggerKey triggerKey = result.TriggerKey;

                    // If our trigger is no longer available, try a new one.
                    var nextTrigger = await GetTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                    if (nextTrigger is null)
                    {
                        continue; // next trigger
                    }

                    // If trigger's job is set as @DisallowConcurrentExecution, and it has already been added to result, then
                    // put it back into the timeTriggers set and continue to search for next trigger.
                    Type jobType;
                    try
                    {
                        jobType = typeLoader.LoadType(result.JobTypeName)!;
                    }
                    catch (Exception e)
                    {
                        try
                        {
                            Logger.LogError(e, "Error retrieving job, setting trigger state to ERROR.");
                            await Delegate.UpdateTriggerState(conn, triggerKey, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);

                            // A trigger whose job type will not load stops firing here and is reported
                            // nowhere else - not even through SchedulerError. Inline, as the misfire
                            // notification in this store already is.
                            await schedSignaler.NotifySchedulerListenersTriggerInError(triggerKey, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Unable to set trigger state to ERROR.");
                        }
                        continue;
                    }

                    if (ObjectUtils.IsAttributePresent(jobType, typeof(DisallowConcurrentExecutionAttribute)))
                    {
                        if (!acquiredJobKeysForNoConcurrentExec.Add(nextTrigger.JobKey))
                        {
                            continue; // next trigger
                        }

                        // Cluster-safe check: skip if job is already executing on another node
                        if (await Delegate.IsJobCurrentlyExecuting(conn, nextTrigger.JobKey, cancellationToken).ConfigureAwait(false))
                        {
                            continue;
                        }
                    }

                    var nextFireTimeUtc = nextTrigger.NextFireTimeUtc;

                    // A trigger should not return NULL on nextFireTime when fetched from DB.
                    // But for whatever reason if we do have this (BAD trigger implementation or
                    // data?), we then should log a warning and continue to next trigger.
                    // User would need to manually fix these triggers from DB as they will not
                    // able to be clean up by Quartz since we are not returning it to be processed.
                    if (nextFireTimeUtc is null)
                    {
                        Logger.LogWarning("Trigger {TriggerKey} returned null on nextFireTime and yet still exists in DB!", nextTrigger.Key);
                        continue;
                    }

                    if (nextFireTimeUtc > batchEnd)
                    {
                        break;
                    }

                    // We now have a acquired trigger, let's add to return list.
                    // If our trigger was no longer in the expected state, try a new one.
                    int rowsUpdated = await Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(conn, triggerKey, StoredTriggerState.Acquired, StoredTriggerState.Waiting, nextFireTimeUtc.Value, cancellationToken).ConfigureAwait(false);
                    if (rowsUpdated <= 0)
                    {
                        // TODO: Hum... shouldn't we log a warning here?
                        continue; // next trigger
                    }
                    nextTrigger.FireInstanceId = GetFiredTriggerRecordId();
                    await Delegate.InsertFiredTrigger(conn, nextTrigger, StoredTriggerState.Acquired, null, cancellationToken).ConfigureAwait(false);

                    if (acquiredTriggers.Count == 0)
                    {
                        var now = timeProvider.GetUtcNow();
                        var nextFireTime = nextFireTimeUtc.Value;
                        var max = now > nextFireTime ? now : nextFireTime;

                        batchEnd = max + request.TimeWindow;
                    }

                    acquiredTriggers.Add(nextTrigger);
                }

                // if we didn't end up with any trigger to fire from that first
                // batch, try again for another batch. We allow with a max retry count.
                if (acquiredTriggers.Count == 0 && currentLoopCount < MaxDoLoopRetry)
                {
                    continue;
                }

                // We are done with the while loop.
                break;
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException("Couldn't acquire next trigger: " + e.Message, e);
            }
        } while (true);

        // Return the acquired trigger list
        return acquiredTriggers;
    }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
    /// fire the given <see cref="ITrigger" />, that it had previously acquired
    /// (reserved).
    /// </summary>
    public async ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        await activityTracer.Trace(
            OperationName.JobStore.ReleaseAcquiredTrigger,
            () => RetryExecuteInLocalTransactionLock(
                SchedulerLock.TriggerAccess,
                conn => ReleaseAcquiredTrigger(conn, trigger, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, trigger.Key.Group);
                activity.SetTag(ActivityTags.TriggerName, trigger.Key.Name);
            }).ConfigureAwait(false);
    }

    protected async ValueTask ReleaseAcquiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, StoredTriggerState.Waiting, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
            await Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
            await Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't release acquired trigger: " + e.Message, e);
        }
    }

    public ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        return activityTracer.Trace(
            OperationName.JobStore.TriggersFired,
            () => ExecuteInLocalTransactionLock(
                SchedulerLock.TriggerAccess,
                async conn =>
                {
                    List<TriggerFiredResult> results = new(triggers.Count);

                    foreach (IOperableTrigger trigger in triggers)
                    {
                        TriggerFiredResult result;
                        try
                        {
                            // Clone so that trigger.Triggered() mutation doesn't affect retries
                            var triggerCopy = (IOperableTrigger) trigger.Clone();
                            var bundle = await TriggerFired(conn, triggerCopy, cancellationToken).ConfigureAwait(false);
                            result = new TriggerFiredResult(bundle);
                        }
                        catch (JobPersistenceException jpe)
                        {
                            if (IsTransient(jpe))
                            {
                                throw; // Let ExecuteInLocalTransactionLock retry the whole transaction
                            }
                            Logger.LogError(jpe, "Caught job persistence exception: {ExceptionMessage}", jpe.Message);
                            result = new TriggerFiredResult(jpe);
                        }
                        catch (Exception ex)
                        {
                            if (IsTransient(ex))
                            {
                                // Wrap as JobPersistenceException so outer retry mechanism can handle it
                                throw new JobPersistenceException("Transient error firing trigger: " + ex.Message, ex);
                            }
                            Logger.LogError(ex, "Caught exception: {ExceptionMessage}", ex.Message);
                            result = new TriggerFiredResult(ex);
                        }

                        results.Add(result);
                    }

                    return results;
                },
                async (conn, result) =>
                {
                    try
                    {
                        var acquired = await Delegate
                            .SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken)
                            .ConfigureAwait(false);
                        var executingTriggers = new HashSet<string>();
                        foreach (FiredTriggerRecord ft in acquired)
                        {
                            if (ft.FireInstanceState == StoredTriggerState.Executing)
                            {
                                executingTriggers.Add(ft.FireInstanceId);
                            }
                        }

                        foreach (TriggerFiredResult tr in result)
                        {
                            if (tr.TriggerFiredBundle is not null &&
                                executingTriggers.Contains(tr.TriggerFiredBundle.Trigger.FireInstanceId))
                            {
                                return true;
                            }
                        }

                        return false;
                    }
                    catch (Exception e)
                    {
                        Throw.JobPersistenceException("error validating trigger acquisition", e);
                        return default;
                    }
                },
                cancellationToken: cancellationToken),
            activity => activity.SetTag(ActivityTags.TriggerCount, triggers.Count));
    }

    protected async ValueTask<TriggerFiredBundle?> TriggerFired(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        IJobDetail? job;
        ICalendar? calendar = null;

        // Make sure trigger wasn't deleted, paused, or completed...
        try
        {
            // if trigger was deleted, state will be StateDeleted
            StoredTriggerState state = await Delegate.SelectTriggerState(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
            if (state != StoredTriggerState.Acquired)
            {
                return null;
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't select trigger state: " + e.Message, e);
        }

        try
        {
            job = await GetJob(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                return null;
            }
        }
        catch (JobPersistenceException jpe)
        {
            try
            {
                Logger.LogError(jpe, "Error retrieving job, setting trigger state to ERROR.");
                await Delegate.UpdateTriggerState(conn, trigger.Key, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);

                // Same as above: the trigger stops here and nothing else says so.
                await schedSignaler.NotifySchedulerListenersTriggerInError(trigger.Key, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception sqle)
            {
                Logger.LogError(sqle, "Unable to set trigger state to ERROR.");
            }
            throw;
        }

        // Cluster-safe check: prevent concurrent execution across nodes for
        // [DisallowConcurrentExecution] jobs by checking the FIRED_TRIGGERS table.
        // This runs under the TRIGGER_ACCESS lock, providing serialized access.
        // The current trigger's own fired record has JOB_NAME=null (set during
        // AcquireNextTrigger) so it won't appear in the query results.
        if (job.ConcurrentExecutionDisallowed)
        {
            try
            {
                bool alreadyExecuting = await Delegate.IsJobCurrentlyExecuting(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false);
                if (alreadyExecuting)
                {
                    Logger.LogInformation("Not firing trigger {TriggerKey} for [DisallowConcurrentExecution] job {JobKey} - already executing on another node.", trigger.Key, trigger.JobKey);
                    return null;
                }
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException($"Couldn't check concurrent execution for job '{trigger.JobKey}': " + e.Message, e);
            }
        }

        if (trigger.CalendarName is not null)
        {
            calendar = await GetCalendar(conn, trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (calendar is null)
            {
                Logger.LogWarning("Trigger {TriggerKey} references calendar '{CalendarName}', which does not exist - the fire was skipped and the trigger will not run until the calendar is added or the reference is cleared.", trigger.Key, trigger.CalendarName);
                return null;
            }
        }

        try
        {
            await Delegate.UpdateFiredTrigger(conn, trigger, StoredTriggerState.Executing, job, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't update fired trigger: " + e.Message, e);
        }

        // Auto-pin: when the preferred node is the "*" sentinel, claim the trigger by assigning this
        // node's instance id and flagging it as auto-claimed. When it is some OTHER node's id and
        // already auto-claimed, that node was stale or dead at acquisition time (the acquisition SQL
        // only releases another node's pin via the liveness fallback), so steal the pin — sticky
        // failover converges to a live node.
        // The write is a compare-and-swap against the values observed at acquire time, so a
        // concurrent change (an UpdateTriggerDetails re-pin or clear between acquisition and firing,
        // or ClusterRecover's reset to "*") wins over the claim instead of being clobbered by it.
        // Explicit pins (AUTO = false) are never re-pinned here.
        if (trigger is TriggerBase pinTrigger)
        {
            PreferredNode pin = pinTrigger.PreferredNode;
            string? rawPreferredNode = pin.StoredNode;
            bool rawPreferredNodeAuto = pin.StoredAutomatic;
            bool claimUnpinned = rawPreferredNode == StdAdoConstants.AutoPinSentinel;
            bool stealFromStaleNode = rawPreferredNode is not null
                && rawPreferredNodeAuto
                && rawPreferredNode != InstanceId;

            if (claimUnpinned || stealFromStaleNode)
            {
                PreferredNode claim = PreferredNode.ClaimedBy(InstanceId);
                int claimed = await Delegate.UpdateTriggerPreferredNodeConditional(
                    conn,
                    trigger.Key,
                    new PreferredNodeTransition { Expected = pin, New = claim },
                    cancellationToken).ConfigureAwait(false);
                if (claimed > 0)
                {
                    // Mirror the persisted value; not dirty — the row already holds it
                    pinTrigger.SetPreferredNode(claim, markDirty: false);
                }
                // else the pin changed concurrently: leave the concurrent value in place. The
                // in-memory value is stale but not dirty, so the store below will not write it
                // back; the next acquisition reloads the current value.
            }
        }

        // Read saved original fire time from trigger (populated by SelectTrigger from DB column)
        DateTimeOffset? scheduledFireTime = (trigger as TriggerBase)?.MisfiredFromFireTimeUtc;
        if (scheduledFireTime.HasValue)
        {
            // Clear so it doesn't persist beyond this firing
            await Delegate.ClearMisfireOriginalFireTime(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset? prevFireTime = trigger.PreviousFireTimeUtc;

        // call triggered - to update the trigger's next-fire-time state...
        trigger.Triggered(calendar);

        StoredTriggerState state2 = StoredTriggerState.Waiting;
        bool force = true;

        if (job.ConcurrentExecutionDisallowed)
        {
            state2 = StoredTriggerState.Blocked;
            force = false;
            try
            {
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StoredTriggerState.Blocked, StoredTriggerState.Waiting, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StoredTriggerState.Blocked, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StoredTriggerState.PausedBlocked, StoredTriggerState.Paused, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException("Couldn't update states of blocked triggers: " + e.Message, e);
            }
        }

        if (!trigger.NextFireTimeUtc.HasValue)
        {
            state2 = StoredTriggerState.Complete;
            force = true;
        }

        await AddTrigger(conn, trigger, job, true, state2, force, false, cancellationToken).ConfigureAwait(false);

        job.JobDataMap.ClearDirtyFlag();

        return new TriggerFiredBundle
        {
            JobDetail = job,
            Trigger = trigger,
            Calendar = calendar,
            Recovering = trigger.Key.Group == SchedulerConstants.DefaultRecoveryGroup,
            FireTimeUtc = timeProvider.GetUtcNow(),
            ScheduledFireTimeUtc = scheduledFireTime ?? trigger.PreviousFireTimeUtc,
            PreviousFireTimeUtc = prevFireTime,
            NextFireTimeUtc = trigger.NextFireTimeUtc,
        };
    }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler has completed the
    /// firing of the given <see cref="ITrigger" /> (and the execution its
    /// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
    /// in the given <see cref="IJobDetail" /> should be updated if the <see cref="IJob" />
    /// is stateful.
    /// </summary>
    public async ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        // Completion bookkeeping belongs to the scheduler, not to the job, and it retries a failing
        // JobPersistenceException until it succeeds. If a job body left an enlistment behind, this
        // would borrow a connection whose transaction is long gone and retry against it forever,
        // leaving the fired trigger uncleaned and its DisallowConcurrentExecution siblings blocked.
        using var suppression = AmbientConnection.Suppress();

        await activityTracer.Trace(
            OperationName.JobStore.TriggeredJobComplete,
            () => RetryExecuteInLocalTransactionLock(
                SchedulerLock.TriggerAccess,
                conn => TriggeredJobComplete(conn, trigger, jobDetail, triggerInstructionCode, cancellationToken),
                cancellationToken),
            activity =>
            {
                activity.SetTag(ActivityTags.TriggerGroup, trigger.Key.Group);
                activity.SetTag(ActivityTags.TriggerName, trigger.Key.Name);
                activity.SetTag(ActivityTags.JobGroup, jobDetail.Key.Group);
                activity.SetTag(ActivityTags.JobName, jobDetail.Key.Name);
            }).ConfigureAwait(false);

        // Deliberately after the transaction, and only if it committed: these run listener code, which
        // has no business executing inside the store's transaction or seeing a state that may roll back.
        if (triggerInstructionCode == SchedulerInstruction.SetTriggerError)
        {
            await schedSignaler.NotifySchedulerListenersTriggerInError(trigger.Key, cancellationToken).ConfigureAwait(false);
        }
        else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersError)
        {
            await schedSignaler.NotifySchedulerListenersTriggersInError(trigger.JobKey, cancellationToken).ConfigureAwait(false);
        }
    }

    protected async ValueTask TriggeredJobComplete(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        IJobDetail jobDetail,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (triggerInstructionCode == SchedulerInstruction.DeleteTrigger)
            {
                if (!trigger.NextFireTimeUtc.HasValue)
                {
                    // double check for possible reschedule within job
                    // execution, which would cancel the need to delete...
                    var stat = await Delegate.SelectTriggerHeader(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    if (stat is not null && !stat.NextFireTimeUtc.HasValue)
                    {
                        await DeleteTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await DeleteTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                }
            }
            else if (triggerInstructionCode == SchedulerInstruction.SetTriggerComplete)
            {
                await Delegate.UpdateTriggerState(conn, trigger.Key, StoredTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }
            else if (triggerInstructionCode == SchedulerInstruction.SetTriggerError)
            {
                Logger.LogInformation("Trigger {Trigger} set to ERROR state.", trigger.Key);
                await Delegate.UpdateTriggerState(conn, trigger.Key, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }
            else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersComplete)
            {
                await Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StoredTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }
            else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersError)
            {
                Logger.LogInformation("All triggers of Job {Job} set to ERROR state.", trigger.JobKey);
                await Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }

            if (jobDetail.ConcurrentExecutionDisallowed)
            {
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;

                // Check for misfired triggers that were just unblocked
                // Triggers that were blocked and have now transitioned to waiting may have misfired
                // while they were blocked. We need to handle these misfires now.
                // Note: We retrieve all triggers and check each one's state because there's no efficient
                // way to query only triggers that just transitioned from BLOCKED to WAITING.
                // However, jobs with DisallowConcurrentExecution typically have few triggers.
                var triggersForJob = await GetTriggersForJob(conn, jobDetail.Key, cancellationToken).ConfigureAwait(false);
                foreach (var trig in triggersForJob)
                {
                    // Only check triggers in WAITING state (those that were just unblocked)
                    StoredTriggerState state = await Delegate.SelectTriggerState(conn, trig.Key, cancellationToken).ConfigureAwait(false);
                    if (state == StoredTriggerState.Waiting)
                    {
                        var misfired = await UpdateMisfiredTrigger(conn, trig.Key, StoredTriggerState.Waiting, false, cancellationToken).ConfigureAwait(false);
                        if (misfired)
                        {
                            // If the trigger was misfired and has no more fire times (e.g., fire-once triggers),
                            // it was stored as COMPLETE. We need to remove it entirely so that GetTrigger
                            // returns null and the trigger doesn't linger in the database.
                            StoredTriggerState newState = await Delegate.SelectTriggerState(conn, trig.Key, cancellationToken).ConfigureAwait(false);
                            if (newState == StoredTriggerState.Complete)
                            {
                                await DeleteTrigger(conn, trig.Key, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            if (jobDetail.PersistJobDataAfterExecution)
            {
                try
                {
                    if (jobDetail.JobDataMap.Dirty)
                    {
                        await Delegate.UpdateJobData(conn, jobDetail, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (IOException e)
                {
                    Throw.JobPersistenceException("Couldn't serialize job data: " + e.Message, e);
                }
                catch (Exception e)
                {
                    Throw.JobPersistenceException("Couldn't update job data: " + e.Message, e);
                }
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't update trigger state(s): " + e.Message, e);
        }

        try
        {
            await Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't delete fired trigger: " + e.Message, e);
        }
    }

    //---------------------------------------------------------------------------
    // Management methods
    //---------------------------------------------------------------------------

    protected internal async ValueTask<RecoverMisfiredJobsResult> DoRecoverMisfires(
        Guid requestorId,
        CancellationToken cancellationToken = default)
    {
        // Misfire recovery is the scheduler own work and commits on its own schedule, so it must not
        // run inside a transaction the application owns.
        using var suppression = AmbientConnection.Suppress();

        bool transOwner = false;
        ConnectionAndTransactionHolder? conn = null;
        try
        {
            RecoverMisfiredJobsResult result = RecoverMisfiredJobsResult.NoOp;
            int staleCount = 0;

            if (LockAllOperations)
            {
                // For SQLite: acquire lock before opening connection to avoid
                // "database is locked" errors from concurrent serializable transactions.
                // Skip the double-check optimization since in-memory lock is cheap.
                transOwner = await LockHandler.ObtainLock(requestorId, null, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
                result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
                staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);

                // Before we make the potentially expensive call to acquire the
                // trigger lock, peek ahead to see if it is likely we would find
                // misfired triggers requiring recovery.
                int misfireCount = DoubleCheckLockMisfireHandler
                    ? await Delegate.CountMisfiredTriggersInState(conn, StoredTriggerState.Waiting, MisfireTime, cancellationToken).ConfigureAwait(false)
                    : int.MaxValue;

                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug("Found {MisfireCount} triggers that missed their scheduled fire-time.", misfireCount);
                }

                if (misfireCount > 0)
                {
                    transOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);

                    result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
                    staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
                }
                else if (await HasStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false))
                {
                    // Even when no misfired triggers exist, check for triggers stuck
                    // in ACQUIRED state (e.g., from a failed ReleaseAcquiredTrigger call)
                    transOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                    staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
                }
            }

            // Include stale recovery count so the caller signals the scheduler thread
            if (staleCount > 0)
            {
                int totalCount = result.ProcessedMisfiredTriggerCount + staleCount;
                DateTimeOffset earliestNewTime = result.EarliestNewTimeUtc < timeProvider.GetUtcNow()
                    ? result.EarliestNewTimeUtc
                    : timeProvider.GetUtcNow();
                result = new RecoverMisfiredJobsResult(result.HasMoreMisfiredTriggers, totalCount, earliestNewTime);
            }

            await CommitConnection(conn, false, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (JobPersistenceException jpe)
        {
            await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception e)
        {
            await RollbackConnection(conn, e, cancellationToken).ConfigureAwait(false);
            Throw.JobPersistenceException("Database error recovering from misfires.", e);
            return default;
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId, SchedulerLock.TriggerAccess, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    protected internal ValueTask SignalSchedulingChangeImmediately(
        DateTimeOffset? candidateNewNextFireTime,
        CancellationToken cancellationToken = default)
    {
        return schedSignaler.SignalSchedulingChange(candidateNewNextFireTime, cancellationToken);
    }

    /// <summary>
    /// Holds back a scheduling change signal until the transaction the application owns has completed.
    /// Signalling while our rows are still uncommitted would send the scheduler thread looking for a
    /// trigger it cannot see yet, and it would then wait out the idle interval before looking again.
    /// </summary>
    internal void SignalSchedulingChangeOnApplicationCommit(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset? candidateNewNextFireTime,
        CancellationToken cancellationToken)
    {
        void Signal(DateTimeOffset? signalTime)
        {
            // Fire and forget: the signaler only wakes the scheduler thread, and this runs from a
            // transaction completion callback that has nothing to await it.
            _ = SignalSchedulingChangeImmediately(signalTime, cancellationToken).AsTask();
        }

        var enlisted = conn.BorrowedFrom;
        if (enlisted is null)
        {
            Signal(candidateNewNextFireTime);
            return;
        }

        // Accumulate on the enlistment rather than in the handler, so every operation in the scope
        // contributes and the earliest candidate wins. Capturing one operation time in a closure would
        // let a later, sooner trigger go unannounced until the idle wait expired.
        enlisted.DeferSignal(candidateNewNextFireTime, Signal);

        // The transaction the enlistment was made under, not whatever is ambient now: an unrelated outer
        // scope governs nothing here, and handing it the signal would drop it when that scope aborts.
        var ambient = enlisted.Ambient;
        if (ambient is null)
        {
            // A bare DbTransaction reports no outcome, so the enlistment scope disposal is the only
            // moment we have; the caller is documented to dispose it after committing.
            return;
        }

        // An ambient transaction does report its outcome, and reports it after the enlistment scope is
        // disposed, so let it own the signal: nothing is raised when the application rolls back. Hooked
        // once per enlistment - a scope that schedules hundreds of jobs would otherwise accumulate that
        // many handlers and fire them all back to back, each able to knock the scheduler off its
        // acquired triggers.
        if (enlisted.AmbientSignalHooked)
        {
            return;
        }

        // Subscribe first: the add accessor throws once the transaction has been disposed, and latching
        // the flags before that would leave neither the ambient flush nor the scope fallback able to
        // raise the signal at all.
        ambient.TransactionCompleted += (_, e) =>
        {
            if (e.Transaction?.TransactionInformation.Status == System.Transactions.TransactionStatus.Committed)
            {
                enlisted.FlushSignal();
            }
        };

        enlisted.AmbientSignalHooked = true;
        enlisted.SignalOwnedByAmbient = true;
    }

    //---------------------------------------------------------------------------
    // Cluster management methods
    //---------------------------------------------------------------------------

    private bool firstCheckIn = true;

    /// <summary>
    /// When this node last recorded that it is alive. Internal: it is bookkeeping the check-in loop
    /// owns, and a subclass writing it would move the moment every other node decides this one died.
    /// </summary>
    internal DateTimeOffset LastCheckin { get; set; }

    protected internal async ValueTask<bool> DoCheckin(
        Guid requestorId,
        CancellationToken cancellationToken = default)
    {
        // Cluster check-in has to run in a transaction of its own to avoid deadlocking under recovery,
        // so it must never borrow a connection the application enlisted.
        using var suppression = AmbientConnection.Suppress();

        int maxRetries = MaxTransientRetries;
        int totalAttempts = maxRetries + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            bool transOwner = false;
            bool transStateOwner = false;
            bool recovered = false;

            ConnectionAndTransactionHolder conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
            try
            {
                // Other than the first time, always checkin first to make sure there is
                // work to be done before we acquire the lock (since that is expensive,
                // and is almost never necessary).  This must be done in a separate
                // transaction to prevent a deadlock under recovery conditions.
                List<SchedulerStateRecord>? failedRecords = null;
                if (!firstCheckIn)
                {
                    failedRecords = await ClusterCheckIn(conn, cancellationToken).ConfigureAwait(false);
                    await CommitConnection(conn, true, cancellationToken).ConfigureAwait(false);
                }

                if (firstCheckIn || failedRecords is not null && failedRecords.Count > 0)
                {
                    transStateOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.StateAccess, cancellationToken).ConfigureAwait(false);

                    // Now that we own the lock, make sure we still have work to do.
                    // The first time through, we also need to make sure we update/create our state record
                    if (firstCheckIn)
                    {
                        failedRecords = await ClusterCheckIn(conn, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        failedRecords = await FindFailedInstances(conn, cancellationToken).ConfigureAwait(false);
                    }

                    if (failedRecords.Count > 0)
                    {
                        transOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                        //getLockHandler().obtainLock(conn, LockJobAccess);

                        await ClusterRecover(conn, failedRecords, cancellationToken).ConfigureAwait(false);
                        recovered = true;
                    }
                }

                await CommitConnection(conn, false, cancellationToken).ConfigureAwait(false);

                firstCheckIn = false;
                return recovered;
            }
            catch (JobPersistenceException jpe)
            {
                await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
                if (attempt < totalAttempts && IsTransient(jpe))
                {
                    Logger.LogWarning(jpe, "Transient exception on attempt {Attempt} of {TotalAttempts} in DoCheckin, will retry after {RetryInterval}", attempt, totalAttempts, TransientRetryInterval);
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                try
                {
                    await ReleaseLock(requestorId, SchedulerLock.TriggerAccess, transOwner, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await ReleaseLock(requestorId, SchedulerLock.StateAccess, transStateOwner, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Delay before the next attempt
            await Task.Delay(TransientRetryInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        Throw.InvalidOperationException("DoCheckin retry loop exited unexpectedly");
        return default;
    }

    /// <summary>
    /// Get a list of all scheduler instances in the cluster that may have failed.
    /// This includes this scheduler if it is checking in for the first time.
    /// </summary>
    protected async ValueTask<List<SchedulerStateRecord>> FindFailedInstances(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<SchedulerStateRecord> failedInstances = [];
            bool foundThisScheduler = false;

            var states = await Delegate.SelectSchedulerStateRecords(conn, instanceId: null, cancellationToken).ConfigureAwait(false);

            foreach (SchedulerStateRecord rec in states)
            {
                // find own record...
                if (rec.SchedulerInstanceId == InstanceId)
                {
                    foundThisScheduler = true;
                    if (firstCheckIn)
                    {
                        failedInstances.Add(rec);
                    }
                }
                else
                {
                    // find failed instances...
                    if (CalcFailedIfAfter(rec) < timeProvider.GetUtcNow())
                    {
                        failedInstances.Add(rec);
                    }
                }
            }

            // The first time through, also check for orphaned fired triggers.
            if (firstCheckIn)
            {
                failedInstances.AddRange(await FindOrphanedFailedInstances(conn, states, cancellationToken).ConfigureAwait(false));
            }

            // If not the first time but we didn't find our own instance, then
            // Someone must have done recovery for us.
            if (!foundThisScheduler && !firstCheckIn)
            {
                // TODO: revisit when handle self-failed-out impl'ed (see TODO in clusterCheckIn() below)
                Logger.LogWarning(
                    "This scheduler instance ({InstanceId}) is still " +
                    "active but was recovered by another instance in the cluster.  " +
                    "This may cause inconsistent behavior.", InstanceId);
            }

            return failedInstances;
        }
        catch (Exception e)
        {
            LastCheckin = timeProvider.GetUtcNow();
            Throw.JobPersistenceException("Failure identifying failed instances when checking-in: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Create dummy <see cref="SchedulerStateRecord" /> objects for fired triggers
    /// that have no scheduler state record.  Checkin timestamp and interval are
    /// left as zero on these dummy <see cref="SchedulerStateRecord" /> objects.
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="schedulerStateRecords">List of all current <see cref="SchedulerStateRecord" />s</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask<List<SchedulerStateRecord>> FindOrphanedFailedInstances(
        ConnectionAndTransactionHolder conn,
        List<SchedulerStateRecord> schedulerStateRecords,
        CancellationToken cancellationToken)
    {
        List<SchedulerStateRecord> orphanedInstances = [];

        var names = await Delegate.SelectFiredTriggerInstanceNames(conn, cancellationToken).ConfigureAwait(false);
        if (names.Count > 0)
        {
            var allFiredTriggerInstanceNames = new HashSet<string>(names);
            foreach (SchedulerStateRecord rec in schedulerStateRecords)
            {
                allFiredTriggerInstanceNames.Remove(rec.SchedulerInstanceId);
            }

            foreach (string name in allFiredTriggerInstanceNames)
            {
                SchedulerStateRecord orphanedInstance = new(name, CheckinTimestamp: default, CheckinInterval: default);
                orphanedInstances.Add(orphanedInstance);

                Logger.LogWarning("Found orphaned fired triggers for instance: {SchedulerInstanceId}", orphanedInstance.SchedulerInstanceId);
            }
        }

        return orphanedInstances;
    }

    protected DateTimeOffset CalcFailedIfAfter(SchedulerStateRecord rec)
    {
        TimeSpan passed = timeProvider.GetUtcNow() - LastCheckin;
        TimeSpan ts = rec.CheckinInterval > passed ? rec.CheckinInterval : passed;
        return rec.CheckinTimestamp.Add(ts).Add(ClusterCheckinMisfireThreshold);
    }

    protected async ValueTask<List<SchedulerStateRecord>> ClusterCheckIn(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        var failedInstances = await FindFailedInstances(conn, cancellationToken).ConfigureAwait(false);
        try
        {
            // TODO: handle self-failed-out

            // check in...
            var checkinTime = timeProvider.GetUtcNow();
            if (await Delegate.UpdateSchedulerState(conn, InstanceId, checkinTime, cancellationToken).ConfigureAwait(false) == 0)
            {
                await Delegate.InsertSchedulerState(conn, InstanceId, checkinTime, ClusterCheckinInterval, cancellationToken).ConfigureAwait(false);
            }
            LastCheckin = checkinTime;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Failure updating scheduler state when checking-in: " + e.Message, e);
        }

        return failedInstances;
    }

    protected async ValueTask ClusterRecover(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<SchedulerStateRecord> failedInstances,
        CancellationToken cancellationToken = default)
    {
        if (failedInstances.Count > 0)
        {
            long recoverIds = timeProvider.GetTimestamp();

            LogWarnIfNonZero(failedInstances.Count,
                "ClusterManager: detected " + failedInstances.Count + " failed or restarted instances.");
            try
            {
                foreach (SchedulerStateRecord rec in failedInstances)
                {
                    Logger.LogInformation("ClusterManager: Scanning for instance {SchedulerInstanceId}'s failed in-progress jobs.", rec.SchedulerInstanceId);

                    var firedTriggerRecs = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = rec.SchedulerInstanceId }, cancellationToken).ConfigureAwait(false);

                    int acquiredCount = 0;
                    int recoveredCount = 0;
                    int otherCount = 0;

                    var triggerKeys = new HashSet<TriggerKey>();

                    // Determine whether to preserve EXECUTING fired trigger records for
                    // DisallowConcurrentExecution jobs. On the first detection the node may
                    // still be alive, so we preserve the record and give it a grace period.
                    // Once the grace period expires (elapsed time exceeds two failure detection
                    // cycles), full cleanup is performed. This decision is derived entirely from
                    // DB state so all cluster nodes make the same choice (#2817).
                    bool isOrphanedInstance = rec.CheckinInterval == default && rec.CheckinTimestamp == default;
                    bool canDeferRecovery;
                    if (isOrphanedInstance)
                    {
                        canDeferRecovery = false;
                    }
                    else
                    {
                        TimeSpan elapsed = timeProvider.GetUtcNow() - rec.CheckinTimestamp;
                        TimeSpan gracePeriod = rec.CheckinInterval.Add(rec.CheckinInterval).Add(ClusterCheckinMisfireThreshold);
                        canDeferRecovery = elapsed < gracePeriod;
                    }
                    HashSet<string>? preservedFireInstanceIds = null;
                    int deferredCount = 0;

                    foreach (FiredTriggerRecord ftRec in firedTriggerRecs)
                    {
                        TriggerKey tKey = ftRec.TriggerKey;
                        JobKey? jKey = ftRec.JobKey;

                        triggerKeys.Add(tKey);

                        // For timed-out (non-orphan) instances on first detection, preserve
                        // EXECUTING records for DisallowConcurrentExecution jobs. The node may
                        // still be alive and running the job. If it truly died, on the second
                        // detection (after the grace period) full cleanup will be performed.
                        if (canDeferRecovery
                            && ftRec.FireInstanceState == StoredTriggerState.Executing
                            && ftRec.JobDisallowsConcurrentExecution)
                        {
                            preservedFireInstanceIds ??= [];
                            preservedFireInstanceIds.Add(ftRec.FireInstanceId);
                            deferredCount++;
                            Logger.LogInformation(
                                "ClusterManager: Deferring recovery of [DisallowConcurrentExecution] job {JobKey} " +
                                "(fired trigger {FireInstanceId}) — may still be executing on instance {SchedulerInstanceId}.",
                                jKey, ftRec.FireInstanceId, rec.SchedulerInstanceId);
                            continue;
                        }

                        // release blocked triggers..
                        if (ftRec.FireInstanceState == StoredTriggerState.Blocked)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                        }
                        else if (ftRec.FireInstanceState == StoredTriggerState.PausedBlocked)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false);
                        }

                        // release acquired triggers..
                        if (ftRec.FireInstanceState == StoredTriggerState.Acquired)
                        {
                            await Delegate.UpdateTriggerStateFromOtherState(conn, tKey, StoredTriggerState.Waiting, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                            acquiredCount++;
                        }
                        else if (ftRec.JobRequestsRecovery)
                        {
                            // handle jobs marked for recovery that were not fully
                            // executed..
                            if (await JobExists(conn, jKey!, cancellationToken).ConfigureAwait(false))
                            {
                                SimpleTriggerImpl rcvryTrig =
                                    new SimpleTriggerImpl(
                                        $"recover_{rec.SchedulerInstanceId}_{recoverIds++}",
                                        SchedulerConstants.DefaultRecoveryGroup, ftRec.FireTimestamp);

                                rcvryTrig.JobKey = jKey!;
                                rcvryTrig.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow;
                                rcvryTrig.Priority = ftRec.Priority;
                                JobDataMap jd = await Delegate.SelectTriggerJobDataMap(conn, tKey, cancellationToken).ConfigureAwait(false);
                                jd[SchedulerConstants.FailedJobOriginalTriggerName] = tKey.Name;
                                jd[SchedulerConstants.FailedJobOriginalTriggerGroup] = tKey.Group;
                                jd[SchedulerConstants.FailedJobOriginalTriggerFireTime] = Convert.ToString(ftRec.FireTimestamp, CultureInfo.InvariantCulture);
                                rcvryTrig.JobDataMap = jd;

                                rcvryTrig.ComputeFirstFireTimeUtc(null);
                                await AddTrigger(conn, rcvryTrig, null, false, StoredTriggerState.Waiting, false, true, cancellationToken).ConfigureAwait(false);
                                recoveredCount++;
                            }
                            else
                            {
                                Logger.LogWarning("ClusterManager: failed job {JobKey} no longer exists, cannot schedule recovery.", jKey);
                                otherCount++;
                            }
                        }
                        else
                        {
                            otherCount++;
                        }

                        // free up stateful job's triggers
                        if (ftRec.JobDisallowsConcurrentExecution)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    // Delete fired triggers, preserving EXECUTING records for
                    // DisallowConcurrentExecution jobs on timed-out (non-orphan) instances
                    if (preservedFireInstanceIds is { Count: > 0 })
                    {
                        foreach (FiredTriggerRecord ftRec in firedTriggerRecs)
                        {
                            if (!preservedFireInstanceIds.Contains(ftRec.FireInstanceId))
                            {
                                await Delegate.DeleteFiredTrigger(conn, ftRec.FireInstanceId, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery { InstanceId = rec.SchedulerInstanceId }, cancellationToken).ConfigureAwait(false);
                    }

                    // Check if any of the fired triggers we just deleted were the last fired trigger
                    // records of a COMPLETE trigger.
                    int completeCount = 0;
                    foreach (TriggerKey triggerKey in triggerKeys)
                    {
                        if (await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false) == StoredTriggerState.Complete)
                        {
                            var firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { Trigger = triggerKey }, cancellationToken).ConfigureAwait(false);
                            if (firedTriggers.Count == 0)
                            {
                                if (await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))
                                {
                                    completeCount++;
                                }
                            }
                        }
                    }
                    LogWarnIfNonZero(acquiredCount,
                        "ClusterManager: ......Freed " + acquiredCount + " acquired trigger(s).");
                    LogWarnIfNonZero(completeCount,
                        "ClusterManager: ......Deleted " + completeCount + " complete triggers(s).");
                    LogWarnIfNonZero(recoveredCount,
                        "ClusterManager: ......Scheduled " + recoveredCount +
                        " recoverable job(s) for recovery.");
                    LogWarnIfNonZero(otherCount,
                        "ClusterManager: ......Cleaned-up " + otherCount + " other failed job(s).");
                    LogWarnIfNonZero(deferredCount,
                        "ClusterManager: ......Deferred recovery of " + deferredCount + " executing [DisallowConcurrentExecution] job(s).");

                    if (rec.SchedulerInstanceId != InstanceId)
                    {
                        if (preservedFireInstanceIds is { Count: > 0 })
                        {
                            // Don't delete scheduler state — keep it with the stale timestamp so
                            // the instance continues to be detected as failed. As elapsed time
                            // grows past the grace period, the next recovery will do full cleanup.
                        }
                        else
                        {
                            // Sticky failover: release only AUTO-CLAIMED pins from the dead node
                            // (explicit pins are left untouched so the original node reclaims them
                            // when it returns). Resetting to the "*" sentinel rather than to another
                            // node lets any eligible node claim the trigger on its next fire, which
                            // correctly respects execution group limits. This must happen before the
                            // state row is deleted, and relies on the already-confirmed dead-node
                            // detection from FindFailedInstances.
                            int repinned = await Delegate.RepinTriggersFromDeadNode(
                                conn, rec.SchedulerInstanceId, StdAdoConstants.AutoPinSentinel, cancellationToken).ConfigureAwait(false);
                            if (repinned > 0)
                            {
                                Logger.LogInformation("ClusterManager: Released {Count} auto-pinned trigger(s) from dead node '{InstanceId}' for re-acquisition.", repinned, rec.SchedulerInstanceId);
                            }

                            await Delegate.DeleteSchedulerState(conn, rec.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException("Failure recovering jobs: " + e.Message, e);
            }
        }
    }

    private void LogWarnIfNonZero(int val, string warning)
    {
#pragma warning disable CA2254
        if (val > 0)
        {
            Logger.LogInformation(warning);
        }
        else
        {
            Logger.LogDebug(warning);
        }
#pragma warning restore CA2254
    }

    /// <summary>
    /// Cleanup the given database connection.  This means restoring
    /// any modified auto commit or transaction isolation connection
    /// attributes, and then closing the underlying connection.
    /// </summary>
    ///
    /// <remarks>
    /// This is separate from closeConnection() because the Spring
    /// integration relies on being able to overload closeConnection() and
    /// expects the same connection back that it originally returned
    /// from the datasource.
    /// </remarks>
    /// <seealso cref="CloseConnection(ConnectionAndTransactionHolder, CancellationToken)" />
    protected static async ValueTask CleanupConnection(
        ConnectionAndTransactionHolder? conn,
        CancellationToken cancellationToken = default)
    {
        if (conn is not null)
        {
            // Hand the enlisted connection back so the next operation on this flow can claim it.
            // Released through the holder rather than by looking the enlistment up again, which with
            // nested scopes can resolve to a different entry than the one that was claimed.
            conn.BorrowedFrom?.Release();

            await CloseConnection(conn, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the supplied connection.
    /// </summary>
    /// <param name="cth">(Optional)</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected static async ValueTask CloseConnection(
        ConnectionAndTransactionHolder cth,
        CancellationToken cancellationToken = default)
    {
        await cth.Close(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rollback the supplied connection.
    /// </summary>
    protected async ValueTask RollbackConnection(
        ConnectionAndTransactionHolder? cth,
        Exception cause,
        CancellationToken cancellationToken = default)
    {
        if (cth is null)
        {
            // db might be down or similar
            Logger.LogInformation("ConnectionAndTransactionHolder passed to RollbackConnection was null, ignoring");
            return;
        }

        await cth.Rollback(IsTransient(cause), cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Taken from https://github.com/aspnet/EntityFrameworkCore/blob/d59be61006d78d507dea07a9779c3c4103821ca3/src/EFCore.SqlServer/Storage/Internal/SqlServerTransientExceptionDetector.cs
    /// and merged with https://docs.microsoft.com/en-us/azure/sql-database/sql-database-develop-error-messages
    ///
    /// Copied from EFCore because it states "not intended to be used directly from your code" and we don't
    /// want EF leaking into Quartz.
    /// </summary>
    /// <param name="ex"></param>
    /// <returns>If the exception is identified as transient.</returns>
    protected virtual bool IsTransient(Exception ex)
    {
        var isTransientProperty = ex.GetType().GetProperty("IsTransient");
        if (isTransientProperty is not null)
        {
            try
            {
                return (bool) (isTransientProperty.GetValue(ex) ?? false);
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            if (InspectSqlException(ex))
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            if (IsSqliteBusyOrLocked(ex) || (ex.InnerException is not null && IsSqliteBusyOrLocked(ex.InnerException)))
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return ex is TimeoutException;
    }

    private static bool InspectSqlException(Exception ex)
    {
        var sqlException = ex.GetType().GetProperty("Errors") is not null
            ? ex
            : ex?.InnerException;

        var errors = (IEnumerable?) sqlException?.GetType().GetProperty("Errors")?.GetValue(sqlException);
        if (sqlException is null || errors is null)
        {
            return false;
        }

        // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlexception?view=netframework-4.7.2
        // "SqlException always contains at least one instance of SqlError"
        foreach (var err in errors)
        {
            if (err is null)
            {
                continue;
            }

            var errorNumber = Convert.ToInt32(err.GetType().GetProperty("Number")?.GetValue(err));
            switch (errorNumber)
            {
                // SQL Error Code: 49920
                // Cannot process request. Too many operations in progress for subscription "%ld".
                // The service is busy processing multiple requests for this subscription.
                // Requests are currently blocked for resource optimization. Query sys.dm_operation_status for operation status.
                // Wait until pending requests are complete or delete one of your pending requests and retry your request later.
                case 49920:
                // SQL Error Code: 49919
                // Cannot process create or update request. Too many create or update operations in progress for subscription "%ld".
                // The service is busy processing multiple create or update requests for your subscription or server.
                // Requests are currently blocked for resource optimization. Query sys.dm_operation_status for pending operations.
                // Wait till pending create or update requests are complete or delete one of your pending requests and
                // retry your request later.
                case 49919:
                // SQL Error Code: 49918
                // Cannot process request. Not enough resources to process request.
                // The service is currently busy.Please retry the request later.
                case 49918:
                // SQL Error Code: 41839
                // Transaction exceeded the maximum number of commit dependencies.
                case 41839:
                // SQL Error Code: 41325
                // The current transaction failed to commit due to a serializable validation failure.
                case 41325:
                // SQL Error Code: 41305
                // The current transaction failed to commit due to a repeatable read validation failure.
                case 41305:
                // SQL Error Code: 41302
                // The current transaction attempted to update a record that has been updated since the transaction started.
                case 41302:
                // SQL Error Code: 41301
                // Dependency failure: a dependency was taken on another transaction that later failed to commit.
                case 41301:
                // SQL Error Code: 40613
                // Database XXXX on server YYYY is not currently available. Please retry the connection later.
                // If the problem persists, contact customer support, and provide them the session tracing ID of ZZZZZ.
                case 40613:
                // SQL Error Code: 40501
                // The service is currently busy. Retry the request after 10 seconds. Code: (reason code to be decoded).
                case 40501:
                // SQL Error Code: 40197
                // The service has encountered an error processing your request. Please try again.
                case 40197:
                // SQL Error Code: 10929
                // Resource ID: %d. The %s minimum guarantee is %d, maximum limit is %d and the current usage for the database is %d.
                // However, the server is currently too busy to support requests greater than %d for this database.
                // For more information, see http://go.microsoft.com/fwlink/?LinkId=267637. Otherwise, please try again.
                case 10929:
                // SQL Error Code: 10928
                // Resource ID: %d. The %s limit for the database is %d and has been reached. For more information,
                // see http://go.microsoft.com/fwlink/?LinkId=267637.
                case 10928:
                // SQL Error Code: 10060
                // A network-related or instance-specific error occurred while establishing a connection to SQL Server.
                // The server was not found or was not accessible. Verify that the instance name is correct and that SQL Server
                // is configured to allow remote connections. (provider: TCP Provider, error: 0 - A connection attempt failed
                // because the connected party did not properly respond after a period of time, or established connection failed
                // because connected host has failed to respond.)"}
                case 10060:
                // SQL Error Code: 10054
                // A transport-level error has occurred when sending the request to the server.
                // (provider: TCP Provider, error: 0 - An existing connection was forcibly closed by the remote host.)
                case 10054:
                // SQL Error Code: 10053
                // A transport-level error has occurred when receiving results from the server.
                // An established connection was aborted by the software in your host machine.
                case 10053:
                // SQL Error Code: 1205
                // Deadlock
                case 1205:
                // SQL Error Code: 233
                // The client was unable to establish a connection because of an error during connection initialization process before login.
                // Possible causes include the following: the client tried to connect to an unsupported version of SQL Server;
                // the server was too busy to accept new connections; or there was a resource limitation (insufficient memory or maximum
                // allowed connections) on the server. (provider: TCP Provider, error: 0 - An existing connection was forcibly closed by
                // the remote host.)
                case 233:
                // SQL Error Code: 121
                // The semaphore timeout period has expired
                case 121:
                // SQL Error Code: 64
                // A connection was successfully established with the server, but then an error occurred during the login process.
                // (provider: TCP Provider, error: 0 - The specified network name is no longer available.)
                case 64:
                // DBNETLIB Error Code: 20
                // The instance of SQL Server you attempted to connect to does not support encryption.
                case 20:
                // Login to read - secondary failed due to long wait on 'HADR_DATABASE_WAIT_FOR_TRANSITION_TO_VERSIONING'.
                // The replica is not available for login because row versions are missing for transactions that were in-flight
                // when the replica was recycled.The issue can be resolved by rolling back or committing the active transactions on
                // the primary replica.Occurrences of this condition can be minimized by avoiding long write transactions on the primary.
                case 4221:
                // Cannot open database "%.*ls" requested by the login. The login failed
                case 4060:
                // SQL Error Code: 11001
                // A network-related or instance-specific error occurred while establishing a connection to SQL Server.
                // The server was not found or was not accessible. Verify that the instance name is correct and that SQL
                // Server is configured to allow remote connections. (provider: TCP Provider, error: 0 - No such host is known.)
                case 11001:
                    return true;
                    // This exception can be thrown even if the operation completed succesfully, so it's safer to let the application fail.
                    // DBNETLIB Error Code: -2
                    // Timeout expired. The timeout period elapsed prior to completion of the operation or the server is not responding. The statement has been terminated.
                    //case -2:
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the exception is a SQLite BUSY (error code 5) or LOCKED (error code 6) error.
    /// Uses reflection since Quartz does not take a direct dependency on SQLite libraries.
    /// </summary>
    private static bool IsSqliteBusyOrLocked(Exception ex)
    {
        string typeName = ex.GetType().Name;
        if (typeName is not "SqliteException" and not "SQLiteException")
        {
            return false;
        }

        // Microsoft.Data.Sqlite: SqliteException.SqliteErrorCode
        var sqliteErrorCodeProp = ex.GetType().GetProperty("SqliteErrorCode");
        if (sqliteErrorCodeProp is not null)
        {
            int code = Convert.ToInt32(sqliteErrorCodeProp.GetValue(ex));
            return code is 5 /* SQLITE_BUSY */ or 6 /* SQLITE_LOCKED */;
        }

        // System.Data.SQLite: SQLiteException.ResultCode (enum)
        var resultCodeProp = ex.GetType().GetProperty("ResultCode");
        if (resultCodeProp is not null)
        {
            string? codeValue = resultCodeProp.GetValue(ex)?.ToString();
            return codeValue is "Busy" or "Locked";
        }

        return false;
    }

    /// <summary>
    /// Commit the supplied connection.
    /// </summary>
    /// <param name="cth">The CTH.</param>
    /// <param name="openNewTransaction">if set to <c>true</c> opens a new transaction.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>JobPersistenceException thrown if a SQLException occurs when the </throws>
    protected async ValueTask CommitConnection(
        ConnectionAndTransactionHolder cth,
        bool openNewTransaction,
        CancellationToken cancellationToken = default)
    {
        if (cth is null)
        {
            Logger.LogDebug("ConnectionAndTransactionHolder passed to CommitConnection was null, ignoring");
            return;
        }
        await cth.Commit(openNewTransaction, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the given callback in a transaction, taking no lock.
    /// </summary>
    /// <remarks>
    /// Forwards to <see cref="ExecuteInLock{T}" /> with no lock — except under
    /// <see cref="LockAllOperations" />, where every operation including reads has to be serialized.
    /// </remarks>
    protected ValueTask<T> ExecuteWithoutLock<T>(
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default)
    {
        // For SQLite, all operations must be serialized to avoid "database is locked" errors.
        // Route read operations through the same lock as write operations.
        SchedulerLock? lockKind = LockAllOperations ? SchedulerLock.TriggerAccess : null;
        return ExecuteInLock(lockKind, txCallback, cancellationToken);
    }

    /// <summary>
    /// Execute the given callback having acquired the given lock, when it produces no result.
    /// </summary>
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected async ValueTask ExecuteInLock(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLock<object?>(lockKind, async conn =>
        {
            await txCallback(conn).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the given callback having acquired the given lock.
    /// Depending on the JobStore, the surrounding transaction may be
    /// assumed to be already present (managed).
    /// </summary>
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected abstract ValueTask<T> ExecuteInLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Keep retrying <see cref="ExecuteInLocalTransactionLock{T}" /> until it succeeds or the store
    /// shuts down, for work the scheduler cannot simply abandon — cluster recovery and misfire
    /// handling, whose failure is almost always a database that is temporarily gone.
    /// </summary>
    protected async ValueTask RetryExecuteInLocalTransactionLock(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        await RetryExecuteInLocalTransactionLock<object?>(lockKind, async holder =>
        {
            await txCallback(holder).ConfigureAwait(false);
            return null;
        }, requestorId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="RetryExecuteInLocalTransactionLock(SchedulerLock?, Func{ConnectionAndTransactionHolder, ValueTask}, CancellationToken)" />
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired.
    /// </param>
    /// <param name="txCallback">The callback to execute after having acquired the given lock.</param>
    /// <param name="requestorId">
    /// The identity the lock is taken under. Pass the one an outer attempt used, so that a nested
    /// retry is recognised as the same owner rather than deadlocking against itself.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected async ValueTask<T> RetryExecuteInLocalTransactionLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        Guid? requestorId = null,
        CancellationToken cancellationToken = default)
    {
        for (int retry = 1; !shutdown; retry++)
        {
            try
            {
                return await ExecuteInLocalTransactionLock(lockKind, txCallback, txValidator: null, requestorId, cancellationToken).ConfigureAwait(false);
            }
            catch (JobPersistenceException jpe)
            {
                if (retry % RetryableActionErrorLogThreshold == 0)
                {
                    await schedSignaler.NotifySchedulerListenersError("An error occurred during retry", jpe, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "RetryExecuteInLocalTransactionLock: RuntimeException {ExceptionMessage}", e.Message);
            }

            // retry every N seconds (the db connection must be failed)
            await Task.Delay(DbRetryInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        Throw.InvalidOperationException("JobStore is shutdown - aborting retry");
        return default;
    }

    /// <summary>
    /// Execute the given callback having optionally acquired the given lock, on a connection and
    /// transaction this store owns and commits itself, when the callback produces no result.
    /// </summary>
    protected async ValueTask ExecuteInLocalTransactionLock(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLocalTransactionLock<object?>(lockKind, async conn =>
        {
            await txCallback(conn).ConfigureAwait(false);
            return null;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the given callback having optionally acquired the given lock, on a connection and
    /// transaction this store owns and commits itself.
    /// </summary>
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction of this store's own.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="txValidator">
    /// Asked, when the commit fails, whether the work landed anyway. Trigger acquisition uses it: a
    /// commit that reported an error but did reach the database must not be retried, or the same
    /// triggers are acquired twice.
    /// </param>
    /// <param name="requestorId">
    /// The identity the lock is taken under. Defaults to the caller id of the current operation, and
    /// otherwise to a fresh one.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected async ValueTask<T> ExecuteInLocalTransactionLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        Func<ConnectionAndTransactionHolder, T, ValueTask<bool>>? txValidator = null,
        Guid? requestorId = null,
        CancellationToken cancellationToken = default)
    {
        if (requestorId is null)
        {
            requestorId = Core.Context.CallerId.Value;
            if (requestorId is null)
            {
                requestorId = Guid.NewGuid();
            }
        }

        // Retrying inside a transaction the application owns is pointless and harmful: the first failure
        // has already doomed that transaction on most providers, so a second attempt would only pile
        // another error on top of it. Let the caller decide what to do instead.
        bool applicationOwnedTransaction = InApplicationOwnedTransaction;
        int maxRetries = applicationOwnedTransaction ? 0 : MaxTransientRetries;
        int totalAttempts = maxRetries + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            bool transOwner = false;
            ConnectionAndTransactionHolder? conn = null;
            try
            {
                if (lockKind is not null)
                {
                    // If we aren't using db locks, then delay getting DB connection
                    // until after acquiring the lock since it isn't needed.
                    if (LockHandler.RequiresConnection)
                    {
                        conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
                    }

                    transOwner = await LockHandler.ObtainLock(requestorId.Value, conn, lockKind.Value, cancellationToken).ConfigureAwait(false);
                }

                if (conn is null)
                {
                    conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
                }

                T result = await txCallback(conn).ConfigureAwait(false);
                try
                {
                    await CommitConnection(conn, false, cancellationToken).ConfigureAwait(false);
                }
                catch (JobPersistenceException jpe)
                {
                    await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
                    if (txValidator is null)
                    {
                        throw;
                    }
                    if (!await RetryExecuteInLocalTransactionLock(
                            lockKind,
                            async connection => await txValidator(connection, result).ConfigureAwait(false),
                            requestorId,
                            cancellationToken).ConfigureAwait(false))
                    {
                        throw;
                    }
                }

                DateTimeOffset? sigTime = conn.SignalSchedulingChangeOnTxCompletion;

                // Arrange a signal for after the commit even when the job store did not ask for one:
                // QuartzScheduler notifies the scheduler thread as soon as the store call returns, which
                // here is still before the application commits, so that notification finds nothing and
                // the thread settles down for a whole idle interval. Taking the lock stands in for "this
                // may have changed the schedule" - doing it for reads as well would signal an unknown
                // earlier time on every query and keep bouncing acquired triggers back to waiting. That
                // proxy does not hold once LockAllOperations routes reads through the lock too, so there
                // we fall back to an explicit request only.
                // Asked of the holder rather than the registry: a subclass overriding GetConnection can
                // return one it opened itself even while an enlistment exists on this flow.
                if (conn.BorrowedFrom is not null
                    && (sigTime is not null || lockKind is not null && !LockAllOperations))
                {
                    SignalSchedulingChangeOnApplicationCommit(conn, sigTime, cancellationToken);
                }
                else if (sigTime is not null)
                {
                    await SignalSchedulingChangeImmediately(sigTime, cancellationToken).ConfigureAwait(false);
                }

                return result;
            }
            catch (JobPersistenceException jpe)
            {
                await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
                if (attempt < totalAttempts && IsTransient(jpe))
                {
                    Logger.LogWarning(jpe, "Transient exception on attempt {Attempt} of {TotalAttempts} in ExecuteInLocalTransactionLock, will retry after {RetryInterval}", attempt, totalAttempts, TransientRetryInterval);
                }
                else
                {
                    throw;
                }
            }
            catch (Exception e)
            {
                await RollbackConnection(conn, e, cancellationToken).ConfigureAwait(false);
                Throw.JobPersistenceException("Unexpected runtime exception: " + e.Message, e);
                return default;
            }
            finally
            {
                try
                {
                    await ReleaseLock(requestorId.Value, lockKind, transOwner, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
                }
            }

            // Delay before the next attempt
            await Task.Delay(TransientRetryInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        Throw.InvalidOperationException("ExecuteInLocalTransactionLock retry loop exited unexpectedly");
        return default;
    }
}
