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

using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Contains base functionality for ADO.NET-based JobStore implementations.
/// </summary>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal abstract partial class AdoJobStoreBase : IJobStore
{
    private readonly bool useProperties;
    private readonly Dictionary<string, ICalendar?> calendarCache = [];
    private readonly IDriverDelegate driverDelegate;
    private readonly Func<Exception, bool>? configuredIsTransient;
    private TimeSpan misfireThreshold = TimeSpan.FromMinutes(1); // one minute
    private readonly TimeSpan? misfirehandlerFrequence;

    private ClusterManager? clusterManager;
    private MisfireHandler? misfireHandler;
    private readonly ITypeLoader typeLoader;
    private readonly ISchedulerSignaler signaler;
    internal readonly TimeProvider timeProvider;

    private volatile bool schedulerRunning;
    private volatile bool shutdown;
    private readonly ITriggerPersistenceDelegate[] triggerPersistenceDelegates;

    /// <summary>
    /// The instruments this store's cluster check-in and recovery publish on.
    /// </summary>
    /// <remarks>
    /// Assigned by the registration that resolves the store into a scheduler's resources, so that these
    /// measurements land on the same container-scoped meter the execution instruments do. It cannot be a
    /// constructor argument: <see cref="Diagnostics.Meters"/> is internal and this constructor is public,
    /// and the store is built by five different registrations. A store constructed by hand keeps the
    /// process-wide instruments, which is what a hand-built scheduler's execution metrics use too.
    /// </remarks>
    internal Meters Meters { get; set; } = Meters.Shared;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdoJobStoreBase"/> class.
    /// </summary>
    /// <param name="dependencies">
    /// Everything this store is built from. A derived store takes the same argument and chains it, so
    /// that a dependency added to <see cref="AdoJobStoreDependencies" /> reaches every store without
    /// any of them changing shape.
    /// </param>
    protected AdoJobStoreBase(AdoJobStoreDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        signaler = dependencies.SchedulerSignaler;
        ObjectSerializer = dependencies.ObjectSerializer;
        typeLoader = dependencies.TypeLoader;
        timeProvider = dependencies.TimeProvider;
        InstanceName = dependencies.SchedulerOptions.Value.InstanceName;
        InstanceId = dependencies.SchedulerOptions.Value.InstanceId;

        // The container has a logger factory and fills this in, which is what makes the store, its
        // cluster manager, its misfire handler, its delegate and its lock handler log in an application
        // that never touched LogProvider. A store built by hand is handed nothing and keeps reading the
        // ambient factory, which is what it did before there was anything to inject.
        LoggerFactory = dependencies.LoggerFactory ?? LogProviderLoggerFactory.Instance;

        // Created from the runtime type, so LocalTransactionJobStore and ExternalTransactionJobStore log
        // under their own names rather than everything arriving as AdoJobStoreBase.
        Logger = LoggerFactory.CreateLogger(GetType().FullName!);

        // One logger for every unit of work this store opens rather than one each: a holder is created
        // per operation, and CreateLogger<T> allocates.
        ConnectionLogger = LoggerFactory.CreateLogger<ConnectionAndTransactionHolder>();

        var options = dependencies.StoreOptions.Value;
        DataSource = options.DataSource;
        TablePrefix = options.TablePrefix ?? "";
        useProperties = options.StoreJobDataAsStrings;
        MisfireThreshold = options.MisfireThreshold;
        misfirehandlerFrequence = options.MisfireHandlerFrequency;
        MaxMisfiresToHandleAtATime = options.MaxMisfiresToHandleAtATime;

        var clustering = dependencies.ClusteringOptions.Value;
        Clustered = clustering.Enabled;
        ClusterCheckinInterval = clustering.CheckinInterval;
        ClusterCheckinMisfireThreshold = clustering.CheckinMisfireThreshold;

        DbRetryInterval = options.DbRetryInterval;
        MaxTransientRetries = options.MaxTransientRetries;
        TransientRetryInterval = options.TransientRetryInterval;
        RetryableActionErrorLogThreshold = options.RetryableActionErrorLogThreshold;
        configuredIsTransient = options.IsTransient;
        UseDbLocks = options.UseDbLocks;
        LockOnInsert = options.LockOnInsert;
        AcquireTriggersWithinLock = options.AcquireTriggersWithinLock;
        TransactionIsolationLevel = options.TransactionIsolationLevel;
        AcceptEnlistedTransactions = options.AcceptEnlistedTransactions;
        DoubleCheckLockMisfireHandler = options.DoubleCheckLockMisfireHandler;
        UseBackgroundThreads = options.UseBackgroundThreads;
        SchemaProvisioning = options.SchemaProvisioning;
        SelectWithLockSql = options.SelectWithLockSql;
        CommandTimeout = options.CommandTimeout;

        // Registered through UseTriggerPersistenceDelegate<T>() (or translated from the legacy
        // quartz.jobStore.driverDelegateInitString key by the property bridge) and handed to the driver
        // delegate when it is initialized.
        triggerPersistenceDelegates = dependencies.TriggerPersistenceDelegates?.ToArray() ?? [];

        // The store uses the provider it was given, and nothing else needs to be told about it: the
        // container is the registry, keyed by scheduler name, so two schedulers whose data sources
        // happen to share a name cannot end up talking to each other's database.
        DbProvider = dependencies.DbProvider;

        // The delegate and lock handler are chosen by configuration and built by the container, rather
        // than loaded from a type name here.
        driverDelegate = dependencies.DriverDelegate;

        // A lock handler is only injected when one was chosen explicitly. Left null, Initialize picks
        // between database row locks and an in-process monitor once the delegate and clustering settings
        // are known — a decision that cannot be made at registration time, because it depends on which
        // database this store turns out to be talking to.
        LockHandler = dependencies.LockHandler!;
    }

    /// <summary>
    /// The name of the data source this store reads and writes through.
    /// </summary>
    internal string DataSource { get; } = "";

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    internal ILogger Logger { get; }

    /// <summary>
    /// The factory everything this store owns creates its loggers from — the cluster manager, the
    /// misfire handler, the units of work, and through the two initialization contexts the driver
    /// delegate and the lock handler.
    /// </summary>
    internal ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// The logger every unit of work this store opens reports its connection and transaction failures
    /// through, created once because a unit of work is created per operation.
    /// </summary>
    internal ILogger<ConnectionAndTransactionHolder> ConnectionLogger { get; }

    /// <summary>
    /// The prefix pre-pended to all table names.
    /// </summary>
    internal string TablePrefix { get; }

    /// <summary>
    /// The instance id of the scheduler (unique within a cluster).
    /// </summary>
    /// <remarks>
    /// Read from configuration at construction and settled in <see cref="Initialize" />, which is the
    /// first point at which a generated id exists — the store is built before the generator has run, and
    /// its rows are keyed by the value.
    /// </remarks>
    internal string InstanceId { get; private set; } = "";

    /// <summary>
    /// The name of the scheduler, shared by every node of a cluster.
    /// </summary>
    /// <inheritdoc cref="InstanceId" path="/remarks" />
    internal string InstanceName { get; private set; } = "";

    /// <summary>
    /// The number of retries before an error is logged for recovery operations.
    /// </summary>
    internal int RetryableActionErrorLogThreshold { get; }

    /// <summary>
    /// The serializer that turns job data and calendars into what the database stores.
    /// </summary>
    internal IObjectSerializer? ObjectSerializer { get; }

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
    internal TimeSpan ClusterCheckinMisfireThreshold { get; }

    /// <summary>
    /// The maximum number of misfired triggers that the misfire handling
    /// thread will try to recover at one time (within one transaction).  The
    /// default is 20.
    /// </summary>
    internal int MaxMisfiresToHandleAtATime { get; }

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
    internal int MaxTransientRetries { get; }

    /// <summary>
    /// The delay between automatic retries for transient database
    /// exceptions (such as deadlocks).
    /// </summary>
    /// <remarks>
    /// Defaults to 1 second. This is intentionally shorter than <see cref="DbRetryInterval"/>
    /// because transient errors like deadlocks resolve quickly and the retry should be
    /// near-immediate. <see cref="TimeSpan.Zero"/> means no delay between retries.
    /// </remarks>
    internal TimeSpan TransientRetryInterval { get; }

    /// <summary>
    /// Whether this instance uses database-based thread synchronization.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="AdoJobStoreOptions.UseDbLocks" />, and turned on by
    /// <see cref="Initialize" /> for a configuration that cannot work without it - clustering,
    /// enlisted transactions, and container-managed transactions.
    /// </remarks>
    internal bool UseDbLocks { get; set; }

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
    internal bool LockOnInsert { get; } = true;

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
        internal set
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
    /// The isolation level the transactions this store begins for itself run at, or
    /// <see langword="null" /> for Quartz's default of <see cref="IsolationLevel.ReadCommitted" />.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="AdoJobStoreOptions.TransactionIsolationLevel" />, and forced to
    /// <see cref="IsolationLevel.Serializable" /> by <see cref="Initialize" /> for SQLite, which needs
    /// it.
    /// </remarks>
    internal IsolationLevel? TransactionIsolationLevel { get; set; }

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
    internal bool AcquireTriggersWithinLock { get; set; }

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
    /// Read only when this store builds a database-locking handler for itself: a handler supplied
    /// through <c>UseLockHandler</c> was given its statement by whoever built it, and
    /// <see cref="Initialize" /> warns when both are configured.
    /// </remarks>
    /// <seealso cref="SelectForUpdateLockHandler" />
    internal string? SelectWithLockSql { get; set; }

    /// <summary>
    /// How long a statement this store issues may run before the provider cancels it, or
    /// <see langword="null" /> to leave each provider's own default in place.
    /// </summary>
    /// <remarks>
    /// Configured through <see cref="AdoJobStoreOptions.CommandTimeout" />, and handed on to both the
    /// driver delegate and the lock handler so that every statement — including the one that takes the
    /// row lock — is bounded by the same value.
    /// </remarks>
    internal TimeSpan? CommandTimeout { get; }

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
    internal bool DoubleCheckLockMisfireHandler { get; }

    /// <summary>
    /// What this store does about its schema when it starts: nothing, verify it, or create what is
    /// missing and then verify it. Defaults to <see cref="Quartz.SchemaProvisioning.Validate" />.
    /// </summary>
    internal SchemaProvisioning SchemaProvisioning { get; } = SchemaProvisioning.Validate;

    public TimeSpan GetAcquireRetryDelay(int failureCount) => DbRetryInterval;

    protected DbMetadata DbMetadata => DbProvider.Metadata;

    /// <summary>
    /// Hands the container-supplied delegate the settings it needs, which are only complete once the
    /// store has been configured.
    /// </summary>
    private void InitializeDelegate()
    {
        driverDelegate.Initialize(new DriverDelegateContext
        {
            UseProperties = CanUseProperties,
            TablePrefix = TablePrefix,
            SchedulerName = InstanceName,
            InstanceId = InstanceId,
            DbProvider = DbProvider,
            TypeLoader = typeLoader,
            ObjectSerializer = ObjectSerializer,
            TriggerPersistenceDelegates = triggerPersistenceDelegates,
            TimeProvider = timeProvider,
            CommandTimeout = CommandTimeout,
            LoggerFactory = LoggerFactory,
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
    internal IDbProvider DbProvider { get; }

    internal ILockHandler LockHandler { get; set; } = null!;

    /// <summary>
    /// Get whether String-only properties will be handled in JobDataMaps.
    /// </summary>
    internal bool CanUseProperties => useProperties;

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
    /// used, in order to give it a chance to Initialize.
    /// </summary>
    public virtual async ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(DataSource))
        {
            Throw.SchedulerConfigException("DataSource name not set.");
        }

        // The constructor read the configured identity, which is all there was at the time; this is the
        // settled one, and the only place it differs is a generated instance id, which does not exist
        // until the generator has run. Applied before InitializeDelegate, because the delegate and the
        // lock handler are both built with it below and every row this store writes is keyed by it.
        InstanceName = identity.SchedulerName;
        InstanceId = identity.InstanceId;

        LastCheckin = timeProvider.GetUtcNow();
        InitializeDelegate();

        if (Delegate is SQLiteDelegate && LockHandler is not SqliteLockHandler)
        {
            Logger.SqliteLockHandlerSubstituted();
            LockHandler = new SqliteLockHandler();
        }

        if (Delegate is SQLiteDelegate)
        {
            if (Clustered)
            {
                Throw.InvalidConfigurationException("SQLite cannot be used as clustered mode due to locking problems");
            }
            if (!AcquireTriggersWithinLock)
            {
                Logger.SqliteAcquireTriggersWithinLockForced();
                AcquireTriggersWithinLock = true;
            }
            if (TransactionIsolationLevel != IsolationLevel.Serializable)
            {
                // Not a default but a requirement: concurrent SQLite transactions at a lower level fail
                // with "database is locked", so an explicit lower level is overridden rather than kept.
                Logger.SqliteSerializableIsolationForced();
                TransactionIsolationLevel = IsolationLevel.Serializable;
            }
            if (!LockAllOperations)
            {
                Logger.SqliteLockAllOperationsForced();
                LockAllOperations = true;
            }
        }

        // The job store own connections still honour this; a connection the application enlisted was
        // begun at whatever level the application chose, and cannot be changed after the fact.
        if (AcceptEnlistedTransactions && TransactionIsolationLevel is not null && Delegate is not SQLiteDelegate)
        {
            Logger.IsolationLevelIgnoredForEnlistedTransactions();
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

            // The same applies when the application owns the transaction: InProcessLockHandler releases its
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
                        Logger.SqlServerLockSqlDefaulted(DefaultLockSql);
                        SelectWithLockSql = DefaultLockSql;
                    }
                }

                if (Delegate is PostgreSQLDelegate)
                {
                    LockHandler = new PostgreSqlSelectForUpdateLockHandler(TablePrefix, InstanceName, SelectWithLockSql, DbProvider);
                }
                else
                {
                    LockHandler = new SelectForUpdateLockHandler(TablePrefix, InstanceName, SelectWithLockSql, DbProvider);
                }

                Logger.UsingDatabaseLocking(LockHandler.GetType().Name);
            }
            else
            {
                LockHandler = new InProcessLockHandler();
                Logger.UsingMonitorLocking(LockHandler.GetType().Name);
            }
        }
        else
        {
            // A lock handler that was chosen explicitly carries its own statement, through its
            // constructor. SelectWithLockSql only ever reached a handler this store built for itself, so
            // configuring both leaves the statement doing nothing and the drift is invisible.
            if (SelectWithLockSql is not null)
            {
                if (LockHandler is SqliteLockHandler)
                {
                    // SQLite arrives here with a handler the store installed rather than one the caller
                    // chose, so this is a property of the database rather than a configuration to undo.
                    Logger.RowLockStatementIgnoredBySqlite();
                }
                else
                {
                    Logger.RowLockStatementIgnoredBySuppliedHandler(LockHandler.GetType().Name);
                }
            }

            // be ready to give a friendly warning if locks would be released before the application commits
            if (AcceptEnlistedTransactions && !LockHandler.RequiresConnection)
            {
                if (LockHandler is SqliteLockHandler)
                {
                    // SQLite gets this handler unconditionally, before the upgrade to database locks
                    // can be applied, and it cannot be swapped for one - so this is a property of the
                    // combination rather than something to reconfigure away.
                    Logger.EnlistedTransactionsWithSqlite();
                }
                else
                {
                    Logger.EnlistedTransactionsWithInProcessLocking(LockHandler.GetType().Name);
                }
            }

            // be ready to give a friendly warning if SQL Server is used and sub-optimal locking
            if (LockHandler is UpdateRowLockHandler and not SqlServerMemoryOptimizedUpdateRowLockHandler && Delegate is SqlServerDelegate)
            {
                Logger.SqlServerCouldUseRowLocking();
            }
            // be ready to give a friendly warning if SQL Server provider and wrong delegate
            Type? connectionType = DbProvider.ExpectedConnectionType();
            if (connectionType?.Namespace is not null
                && connectionType.Namespace.Contains("SqlClient")
                && connectionType.Name == "SqlConnection"
                && !(Delegate is SqlServerDelegate))
            {
                Logger.SqlServerProviderWithoutSqlServerDelegate();
            }
        }

        // The lock handler learns which scheduler it locks for from the store, on both construction
        // paths: a handler the store built itself is told the same identity its constructor arguments
        // carried, and a handler the container or configuration supplied would otherwise query
        // QRTZ_LOCKS with a null scheduler name, whatever the store is actually configured with.
        LockHandler.Initialize(new LockHandlerContext
        {
            SchedulerName = InstanceName,
            InstanceId = InstanceId,
            TablePrefix = TablePrefix,
            TimeProvider = timeProvider,
            CommandTimeout = CommandTimeout,
            LoggerFactory = LoggerFactory,
        });

        if (SchemaProvisioning == SchemaProvisioning.CreateIfMissing)
        {
            await CreateSchema(cancellationToken).ConfigureAwait(false);
        }

        if (SchemaProvisioning != SchemaProvisioning.None)
        {
            try
            {
                var objectCount = await ExecuteWithoutLock<int>(conn => Delegate.ValidateSchema(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
                Logger.SchemaValidated(objectCount);
            }
            catch (Exception ex)
            {
                // Every answer named, in the order a reader wants them: have Quartz create the schema,
                // create it yourself, or say you have taken responsibility for it. The typed option is
                // spelled first because that is what a 4.x application configures; the flat key still
                // works and is what an application migrating from 3.x already has.
                string error = "Database schema validation failed"
                               + (string.IsNullOrEmpty(TablePrefix) ? "." : $" under table prefix '{TablePrefix}'.")
                               + " Either let Quartz create the objects it needs — UsePersistentStore(store => store.ProvisionSchema()),"
                               + " which sets AdoJobStoreOptions.SchemaProvisioning to CreateIfMissing —"
                               + " or create them yourself from the scripts in database/tables/ for your database."
                               + " Setting SchemaProvisioning to None turns this check off, which says the schema is"
                               + " your responsibility rather than that it is present."
                               + " The legacy flat key for the same setting is quartz.jobStore.schemaProvisioning.";

                throw new SchedulerException(error, ex);
            }
        }

    }

    /// <summary>How many times the schema is created before the failure is reported as one.</summary>
    /// <remarks>
    /// More than one because a create that failed is usually a race, and a retry is not passive
    /// waiting: the script is guarded throughout, so re-running it skips whatever the other node
    /// finished and makes whatever it has not reached yet. Two nodes converge in a round or two, and
    /// ten of them are a few seconds of tolerance for a database that creates a table slowly.
    /// </remarks>
    private const int SchemaCreationAttempts = 10;

    private static readonly TimeSpan SchemaCreationRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Creates whatever the schema is missing, and treats a failure as a lost race until the schema
    /// itself says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Several nodes of a cluster start at once, and only one of them can be the node that creates a
    /// given object — the rest see whatever their database says about that, which every dialect spells
    /// differently and some spell the same way as a permission failure. Firebird does not even spell it
    /// consistently with itself: it serializes DDL through its own catalog and answers the loser with a
    /// primary-key violation on <c>RDB$RELATIONS</c> or with a deadlock, depending on timing. So the
    /// outcome is decided by asking the schema rather than by reading the exception.
    /// </para>
    /// <para>
    /// And asked more than once, because "the winner has finished" and "the winner is halfway through"
    /// look identical from here: the first thing the loser sees is a table it could not create, and the
    /// next thing is a table the winner has not created yet. Both are transient. Each attempt runs the
    /// whole guarded script again, so the two nodes are not waiting for each other so much as filling
    /// in each other's gaps, and they converge in a round or two.
    /// </para>
    /// <para>
    /// Under the same <c>ExecuteWithoutLock</c> as validation, which takes no lock on any dialect but
    /// SQLite. It cannot take one: <c>QRTZ_LOCKS</c> is one of the tables this is here to create.
    /// </para>
    /// </remarks>
    private async ValueTask CreateSchema(CancellationToken cancellationToken)
    {
        Exception? creationFailure = null;
        Exception? validationFailure = null;

        // Asked before anything is made, because the common case by far is that there is nothing to
        // make: a restart, or a node joining a cluster whose schema is already there. The script is
        // guarded throughout, so running it would be a no-op — but it would still be announced, and
        // "Created the schema objects missing" at every start of every node is not what happened.
        try
        {
            int existingObjectCount = await ExecuteWithoutLock<int>(
                conn => Delegate.ValidateSchema(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
            Logger.SchemaAlreadyComplete(TablePrefix, existingObjectCount);
            return;
        }
        catch (Exception failure)
        {
            // Something is missing, or the schema cannot be read at all. Either way, make it — and if
            // making it fails, this is the failure that says why the schema was not usable to begin with.
            validationFailure = failure;
        }

        for (int attempt = 1; attempt <= SchemaCreationAttempts; attempt++)
        {
            try
            {
                await ExecuteWithoutLock<object?>(async conn =>
                {
                    await Delegate.CreateSchema(conn, cancellationToken).ConfigureAwait(false);
                    return null;
                }, cancellationToken).ConfigureAwait(false);

                Logger.SchemaCreated(TablePrefix);
                return;
            }
            catch (Exception failure)
            {
                creationFailure = failure;
            }

            try
            {
                await ExecuteWithoutLock<int>(conn => Delegate.ValidateSchema(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
                Logger.SchemaCreatedByAnotherNode(TablePrefix, creationFailure);
                return;
            }
            catch (Exception failure)
            {
                validationFailure = failure;
            }

            if (attempt < SchemaCreationAttempts)
            {
                Logger.SchemaCreationRetrying(TablePrefix, attempt, SchemaCreationAttempts, creationFailure);
                await Task.Delay(SchemaCreationRetryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new SchedulerException(
            $"Could not create the database schema in {SchemaCreationAttempts} attempts, and the schema is"
            + " not there to be used either."
            + " Either grant the account Quartz connects with permission to create tables and indexes,"
            + $" or run {SchemaScriptName()} by hand and drop back to SchemaProvisioning.Validate."
            + " Why the creation failed is this exception's inner exception; why the schema is still"
            + $" unusable is: {validationFailure?.Message}",
            creationFailure);
    }

    /// <summary>
    /// The fresh-install script for the database this store is talking to, named so that a reader who
    /// cannot grant DDL knows exactly which file to run.
    /// </summary>
    /// <remarks>
    /// Off the delegate rather than the provider, because the delegate is what the dialect was chosen
    /// as. A delegate outside this list is somebody else's, and Quartz does not know which file its
    /// database wants.
    /// </remarks>
    private string SchemaScriptName()
    {
        return Delegate switch
        {
            SqlServerDelegate => "database/tables/tables_sqlServer.sql",
            PostgreSQLDelegate => "database/tables/tables_postgres.sql",
            MySQLDelegate => "database/tables/tables_mysql_innodb.sql",
            OracleDelegate => "database/tables/tables_oracle.sql",
            SQLiteDelegate => "database/tables/tables_sqlite.sql",
            FirebirdDelegate => "database/tables/tables_firebird.sql",
            _ => "the script for your database under database/tables/",
        };
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
            clusterManager = new ClusterManager(this, LoggerFactory.CreateLogger<ClusterManager>());
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
                Logger.JobRecoveryFailed(se.Message, se);
                Throw.SchedulerConfigException("Failure occurred during job recovery.", se);
            }
        }

        misfireHandler = new MisfireHandler(this, LoggerFactory.CreateLogger<MisfireHandler>());
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
            Logger.DatabaseShutdownFailed(sqle);
        }

        // Last, after the store's own work: a handler is entitled to assume the store has stopped
        // asking for locks by the time it is told to close, and whatever it opened - a Redis
        // multiplexer, a semaphore - outlives the scheduler until it is. A handler that throws on the
        // way down is logged rather than allowed to abandon the rest of the shutdown, which is the
        // same treatment the provider above gets.
        await ShutdownLockHandler(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells the lock handler to release what it opened, and reports rather than propagates a failure.
    /// </summary>
    /// <remarks>
    /// The handler is null only for a store that was never initialized, which a host that failed
    /// during startup can still shut down.
    /// </remarks>
    private async ValueTask ShutdownLockHandler(CancellationToken cancellationToken)
    {
        if (LockHandler is null)
        {
            return;
        }

        try
        {
            await LockHandler.Shutdown(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LockHandlerShutdownFailed(LockHandler.GetType().Name, e);
        }
    }

    /// <summary>
    /// Indicates whether this job store supports persistence.
    /// </summary>
    /// <value></value>
    /// <returns></returns>
    public bool SupportsPersistence => true;
}
