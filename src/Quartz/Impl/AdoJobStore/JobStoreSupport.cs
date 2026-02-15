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

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Diagnostics;
using Quartz.Spi;
using Quartz.Util;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Contains base functionality for ADO.NET-based JobStore implementations.
/// </summary>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public abstract class JobStoreSupport : AdoConstants, IJobStore
{
    protected internal const string LockTriggerAccess = "TRIGGER_ACCESS";
    protected internal const string LockStateAccess = "STATE_ACCESS";

    private string tablePrefix = DefaultTablePrefix;
    private bool useProperties;
    private Type delegateType;
    private readonly Dictionary<string, ICalendar?> calendarCache = [];
    private IDriverDelegate driverDelegate = null!;
    private TimeSpan misfireThreshold = TimeSpan.FromMinutes(1); // one minute
    private TimeSpan? misfirehandlerFrequence;

    private ClusterManager? clusterManager;
    private MisfireHandler? misfireHandler;
    private ITypeLoadHelper typeLoadHelper = null!;
    private ISchedulerSignaler schedSignaler = null!;
    internal TimeProvider timeProvider = TimeProvider.System;

    private volatile bool schedulerRunning;
    private volatile bool shutdown;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobStoreSupport"/> class.
    /// </summary>
    protected JobStoreSupport()
    {
        RetryableActionErrorLogThreshold = 4;
        DoubleCheckLockMisfireHandler = true;
        ClusterCheckinInterval = TimeSpan.FromMilliseconds(7500);
        ClusterCheckinMisfireThreshold = TimeSpan.FromMilliseconds(7500);
        MaxMisfiresToHandleAtATime = 20;
        DbRetryInterval = TimeSpan.FromSeconds(15);
        Logger = LogProvider.CreateLogger<JobStoreSupport>();
        delegateType = typeof(StdAdoDelegate);
        ConnectionManager = DBConnectionManager.Instance;
    }

    /// <summary>
    /// Get or set the datasource name.
    /// </summary>
    public string DataSource { get; set; } = "";

    /// <summary>
    /// Get or set the database connection manager.
    /// </summary>
    public IDbConnectionManager ConnectionManager { get; set; }

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    internal ILogger<JobStoreSupport> Logger { get; }

    /// <summary>
    /// Get or sets the prefix that should be pre-pended to all table names.
    /// </summary>
    public string TablePrefix
    {
        get => tablePrefix;
        set
        {
            if (value is null)
            {
                value = "";
            }

            tablePrefix = value;
        }
    }

    /// <summary>
    /// Set whether string-only properties will be handled in JobDataMaps.
    /// </summary>
    public string UseProperties
    {
        set
        {
            if (value is null)
            {
                value = "false";
            }

            useProperties = bool.Parse(value);
        }
    }

    /// <summary>
    /// Get or set the instance Id of the Scheduler (must be unique within a cluster).
    /// </summary>
    public string InstanceId { get; set; } = "";

    /// <summary>
    /// Get or set the instance Id of the Scheduler (must be unique within this server instance).
    /// </summary>
    public string InstanceName { get; set; } = "";

    int IJobStore.ThreadPoolSize
    {
        set { }
    }

    TimeProvider IJobStore.TimeProvider
    {
        set => timeProvider = value;
    }

    /// <summary>
    /// Gets or sets the number of retries before an error is logged for recovery operations.
    /// </summary>
    public int RetryableActionErrorLogThreshold { get; set; }

    public IObjectSerializer? ObjectSerializer { get; set; }

    public virtual long EstimatedTimeToReleaseAndAcquireTrigger { get; } = 70;

    /// <summary>
    /// Get or set whether this instance is part of a cluster.
    /// </summary>
    public bool Clustered { get; set; }

    /// <summary>
    /// Get or set the frequency at which this instance "checks-in"
    /// with the other instances of the cluster. -- Affects the rate of
    /// detecting failed instances.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan ClusterCheckinInterval { get; set; }

    /// <summary>
    /// The time span by which a check-in must have missed its
    /// next-fire-time, in order for it to be considered "misfired" and thus
    /// other scheduler instances in a cluster can consider a "misfired" scheduler
    /// instance as failed or dead.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan ClusterCheckinMisfireThreshold { get; set; }

    /// <summary>
    /// Get or set the maximum number of misfired triggers that the misfire handling
    /// thread will try to recover at one time (within one transaction).  The
    /// default is 20.
    /// </summary>
    public int MaxMisfiresToHandleAtATime { get; set; }

    /// <summary>
    /// Gets or sets the database retry interval.
    /// </summary>
    /// <value>The db retry interval.</value>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan DbRetryInterval { get; set; }

    /// <summary>
    /// Get or set whether this instance should use database-based thread
    /// synchronization.
    /// </summary>
    public bool UseDBLocks { get; set; }

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
    public virtual bool LockOnInsert { get; set; } = true;

    /// <summary>
    /// The time span by which a trigger must have missed its
    /// next-fire-time, in order for it to be considered "misfired" and thus
    /// have its misfire instruction applied.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public virtual TimeSpan MisfireThreshold
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
    /// How often should the misfire handler check for misfires. Defaults to
    /// <see cref="MisfireThreshold"/>.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public virtual TimeSpan MisfireHandlerFrequency
    {
        get { return misfirehandlerFrequence.GetValueOrDefault(MisfireThreshold); }
        set
        {
            if (value.TotalMilliseconds < 1)
            {
                Throw.ArgumentException("MisfireHandlerFrequency must be larger than 0");
            }
            misfirehandlerFrequence = value;
        }
    }

    /// <summary>
    /// Don't call set autocommit(false) on connections obtained from the
    /// DataSource. This can be helpful in a few situations, such as if you
    /// have a driver that complains if it is called when it is already off.
    /// </summary>
    public virtual bool DontSetAutoCommitFalse { get; set; }

    /// <summary>
    /// Set the transaction isolation level of DB connections to sequential.
    /// </summary>
    public virtual bool TxIsolationLevelSerializable { get; set; }

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
    public bool AcquireTriggersWithinLock { get; set; }

    /// <summary>
    /// Get or set the ADO.NET driver delegate class name.
    /// </summary>
    public virtual string DriverDelegateType { get; set; } = null!;

    /// <summary>
    /// The driver delegate's initialization string.
    /// </summary>
    public string? DriverDelegateInitString { get; set; }

    /// <summary>
    /// set the SQL statement to use to select and lock a row in the "locks"
    /// table.
    /// </summary>
    /// <seealso cref="StdRowLockSemaphore" />
    public virtual string? SelectWithLockSQL { get; set; }

    protected virtual ITypeLoadHelper TypeLoadHelper => typeLoadHelper;

    /// <summary>
    /// Get whether the threads spawned by this JobStore should be
    /// marked as daemon.  Possible threads include the <see cref="MisfireHandler" />
    /// and the <see cref="ClusterManager"/>.
    /// </summary>
    /// <returns></returns>
    public bool MakeThreadsDaemons { get; set; }

    /// <summary>
    /// Get whether to check to see if there are Triggers that have misfired
    /// before actually acquiring the lock to recover them.  This should be
    /// set to false if the majority of the time, there are misfired
    /// Triggers.
    /// </summary>
    /// <returns></returns>
    public bool DoubleCheckLockMisfireHandler { get; set; }

    /// <summary>
    /// Whether to perform a schema check on scheduler startup and try to determine if correct tables are in place.
    /// Defaults to true.
    /// </summary>
    public bool PerformSchemaValidation { get; set; } = true;

    public virtual TimeSpan GetAcquireRetryDelay(int failureCount) => DbRetryInterval;

    protected DbMetadata DbMetadata => ConnectionManager.GetDbMetadata(DataSource);

    protected abstract ValueTask<ConnectionAndTransactionHolder> GetNonManagedTXConnection();

    /// <summary>
    /// Gets the connection and starts a new transaction.
    /// </summary>
    /// <returns></returns>
    protected virtual async ValueTask<ConnectionAndTransactionHolder> GetConnection()
    {
        DbConnection conn;
        DbTransaction tx;
        try
        {
            conn = ConnectionManager.GetConnection(DataSource);
            await conn.OpenAsync().ConfigureAwait(false);
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
                tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable).ConfigureAwait(false);
            }
            else
            {
                tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted).ConfigureAwait(false);
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

    protected virtual DateTimeOffset MisfireTime
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

    protected virtual string GetFiredTriggerRecordId()
    {
        Interlocked.Increment(ref ftrCtr);
        return InstanceId + ftrCtr;
    }

    /// <summary>
    /// Get the driver delegate for DB operations.
    /// </summary>
#pragma warning disable CA1716
    protected virtual IDriverDelegate Delegate
#pragma warning restore CA1716
    {
        get
        {
            lock (this)
            {
                if (driverDelegate is null)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(DriverDelegateType))
                        {
                            delegateType = TypeLoadHelper.LoadType(DriverDelegateType)!;
                        }

                        IDbProvider dbProvider = ConnectionManager.GetDbProvider(DataSource);
                        var args = new DelegateInitializationArgs();
                        args.UseProperties = CanUseProperties;
                        args.TablePrefix = tablePrefix;
                        args.InstanceName = InstanceName;
                        args.InstanceId = InstanceId;
                        args.DbProvider = dbProvider;
                        args.TypeLoadHelper = typeLoadHelper;
                        args.ObjectSerializer = ObjectSerializer;
                        args.InitString = DriverDelegateInitString;

                        var ctor = delegateType.GetConstructor(Type.EmptyTypes);
                        if (ctor is null)
                        {
                            Throw.InvalidConfigurationException("Configured delegate does not have public constructor that takes no arguments");
                        }

                        driverDelegate = (IDriverDelegate) ctor.Invoke(null);
                        driverDelegate.Initialize(args);
                    }
                    catch (Exception e)
                    {
                        Throw.NoSuchDelegateException("Couldn't instantiate delegate: " + e.Message, e);
                    }
                }
            }
            return driverDelegate;
        }
    }

    private IDbProvider DbProvider => ConnectionManager.GetDbProvider(DataSource);

    protected internal virtual ISemaphore LockHandler { get; set; } = null!;

    /// <summary>
    /// Get whether String-only properties will be handled in JobDataMaps.
    /// </summary>
    public virtual bool CanUseProperties => useProperties;

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
    /// used, in order to give it a chance to Initialize.
    /// </summary>
    public virtual async ValueTask Initialize(
        ITypeLoadHelper loadHelper,
        ISchedulerSignaler signaler,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DataSource))
        {
            Throw.SchedulerConfigException("DataSource name not set.");
        }

        LastCheckin = timeProvider.GetUtcNow();
        typeLoadHelper = loadHelper;
        schedSignaler = signaler;

        if (Delegate is SQLiteDelegate && (LockHandler is null || LockHandler.GetType() != typeof(UpdateLockRowSemaphore)))
        {
            Logger.LogInformation("Detected SQLite usage, changing to use UpdateLockRowSemaphore");
            var lockHandler = new UpdateLockRowSemaphore(DbProvider)
            {
                SchedName = InstanceName,
                TablePrefix = TablePrefix
            };
            LockHandler = lockHandler;
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
        }

        // If the user hasn't specified an explicit lock handler, then
        // choose one based on CMT/Clustered/UseDBLocks.
        if (LockHandler is null)
        {
            // If the user hasn't specified an explicit lock handler,
            // then we *must* use DB locks with clustering
            if (Clustered)
            {
                UseDBLocks = true;
            }

            if (UseDBLocks)
            {
                if (Delegate is SqlServerDelegate)
                {
                    if (SelectWithLockSQL is null)
                    {
                        const string DefaultLockSql = "SELECT * FROM {0}LOCKS WITH (UPDLOCK,ROWLOCK) WHERE " + ColumnSchedulerName + " = @schedulerName AND LOCK_NAME = @lockName";
                        Logger.LogInformation("Detected usage of SqlServerDelegate - defaulting 'selectWithLockSQL' to '{DefaultLockSql}'.", DefaultLockSql);
                        SelectWithLockSQL = DefaultLockSql;
                    }
                }

                Logger.LogInformation("Using db table-based data access locking (synchronization).");
                LockHandler = new StdRowLockSemaphore(TablePrefix, InstanceName, SelectWithLockSQL, DbProvider);
            }
            else
            {
                Logger.LogInformation("Using thread monitor-based data access locking (synchronization).");
                LockHandler = new SimpleSemaphore();
            }
        }
        else
        {
            // be ready to give a friendly warning if SQL Server is used and sub-optimal locking
            if (LockHandler is UpdateLockRowSemaphore && Delegate is SqlServerDelegate)
            {
                Logger.LogWarning("Detected usage of SqlServerDelegate and UpdateLockRowSemaphore, removing 'quartz.jobStore.lockHandler.type' would allow more efficient SQL Server specific (UPDLOCK,ROWLOCK) row access");
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

        if (PerformSchemaValidation && driverDelegate is StdAdoDelegate adoDelegate)
        {
            try
            {
                var objectCount = await ExecuteWithoutLock<int>(conn => adoDelegate.ValidateSchema(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
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
    public virtual async ValueTask SchedulerStarted(
        CancellationToken cancellationToken = default)
    {
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
            ConnectionManager.Shutdown(DataSource);
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
    public virtual bool SupportsPersistence => true;

    protected virtual async ValueTask ReleaseLock(
        Guid requestorId,
        string lockName,
        bool doIt,
        CancellationToken cancellationToken)
    {
        if (doIt)
        {
            try
            {
                await LockHandler.ReleaseLock(requestorId, lockName, cancellationToken).ConfigureAwait(false);
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
    protected virtual ValueTask<bool> RecoverJobs(CancellationToken cancellationToken)
    {
        return ExecuteInNonManagedTXLock(
            LockTriggerAccess,
            conn => RecoverJobs(conn, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Will recover any failed or misfired jobs and clean up the data store as
    /// appropriate.
    /// </summary>
    protected virtual async ValueTask RecoverJobs(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // update inconsistent job states
            int rows = await Delegate.UpdateTriggerStatesFromOtherStates(conn, StateWaiting, StateAcquired, StateBlocked, cancellationToken).ConfigureAwait(false);

            rows += await Delegate.UpdateTriggerStatesFromOtherStates(conn, StatePaused,
                StatePausedBlocked,
                StatePausedBlocked, cancellationToken).ConfigureAwait(false);

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
                    await StoreTrigger(conn, trigger, null, false, StateWaiting, false, true, cancellationToken).ConfigureAwait(false);
                }
            }
            Logger.LogInformation("Recovery complete.");

            // remove lingering 'complete' triggers...
            var triggersInState = await Delegate.SelectTriggersInState(conn, StateComplete, cancellationToken).ConfigureAwait(false);
            foreach (var trigger in triggersInState)
            {
                await RemoveTrigger(conn, trigger, cancellationToken).ConfigureAwait(false);
            }
            Logger.LogInformation("Removed  {Count} 'complete' triggers.", triggersInState.Count);

            // clean up any fired trigger entries
            int n = await Delegate.DeleteFiredTriggers(conn, cancellationToken).ConfigureAwait(false);
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

    public virtual async ValueTask<RecoverMisfiredJobsResult> RecoverMisfiredJobs(
        ConnectionAndTransactionHolder conn,
        bool recovering,
        CancellationToken cancellationToken = default)
    {
        // If recovering, we want to handle all of the misfired
        // triggers right away.
        int maxMisfiresToHandleAtATime = recovering ? -1 : MaxMisfiresToHandleAtATime;

        List<TriggerKey> misfiredTriggers = new List<TriggerKey>();
        DateTimeOffset earliestNewTime = DateTimeOffset.MaxValue;

        // We must still look for the MISFIRED state in case triggers were left
        // in this state when upgrading to this version that does not support it.
        bool hasMoreMisfiredTriggers =
            await Delegate.HasMisfiredTriggersInState(conn, StateWaiting, MisfireTime, maxMisfiresToHandleAtATime, misfiredTriggers, cancellationToken).ConfigureAwait(false);

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
            Logger.LogInformation(
                "Found 0 triggers that missed their scheduled fire-time.");
            return RecoverMisfiredJobsResult.NoOp;
        }

        foreach (TriggerKey triggerKey in misfiredTriggers)
        {
            IOperableTrigger? trig;
            try
            {
                trig = await RetrieveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error retrieving the misfired trigger: '{TriggerKey}'", triggerKey);
                continue;
            }

            if (trig is null)
            {
                continue;
            }

            try
            {
                await DoUpdateOfMisfiredTrigger(conn, trig, false, StateWaiting, recovering).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error updating misfired trigger: '{TriggerKey}'", trig.Key);
                continue;
            }

            DateTimeOffset? nextTime = trig.GetNextFireTimeUtc();
            if (nextTime.HasValue && nextTime.Value < earliestNewTime)
            {
                earliestNewTime = nextTime.Value;
            }
        }

        return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
    }

    protected virtual async ValueTask<bool> UpdateMisfiredTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newStateIfNotComplete,
        bool forceState,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var trig = (await RetrieveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))!;

            DateTimeOffset misfireTime = timeProvider.GetUtcNow();
            if (MisfireThreshold > TimeSpan.Zero)
            {
                misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
            }

            if (trig.GetNextFireTimeUtc().GetValueOrDefault() > misfireTime)
            {
                return false;
            }

            await DoUpdateOfMisfiredTrigger(conn, trig, forceState, newStateIfNotComplete, false).ConfigureAwait(false);

            return true;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't update misfired trigger '{triggerKey}': {e.Message}", e);
            return false;
        }
    }

    private async ValueTask DoUpdateOfMisfiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trig,
        bool forceState, string newStateIfNotComplete, bool recovering)
    {
        ICalendar? cal = null;
        if (trig.CalendarName is not null)
        {
            cal = await RetrieveCalendar(conn, trig.CalendarName).ConfigureAwait(false);
        }

        await schedSignaler.NotifyTriggerListenersMisfired(trig).ConfigureAwait(false);

        trig.UpdateAfterMisfire(cal);

        if (!trig.GetNextFireTimeUtc().HasValue)
        {
            await StoreTrigger(conn, trig, null, true, StateComplete, forceState, recovering).ConfigureAwait(false);
            await schedSignaler.NotifySchedulerListenersFinalized(trig).ConfigureAwait(false);
        }
        else
        {
            await StoreTrigger(conn, trig, null, true, newStateIfNotComplete, forceState, recovering).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Store the given <see cref="IJobDetail" /> and <see cref="IOperableTrigger" />.
    /// </summary>
    /// <param name="job">Job to be stored.</param>
    /// <param name="trigger">Trigger to be stored.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask StoreJobAndTrigger(
        IJobDetail job,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLock<object?>(LockOnInsert ? LockTriggerAccess : null, async conn =>
        {
            await StoreJob(conn, job, false, cancellationToken).ConfigureAwait(false);
            await StoreTrigger(conn, trigger, job, false, StateWaiting, false, false, cancellationToken).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns true if the given JobGroup is paused.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    public ValueTask<bool> IsJobGroupPaused(
        string group,
        CancellationToken cancellationToken = default)
    {
        Throw.NotImplementedException();
        return new ValueTask<bool>(false);
    }

    /// <summary>
    /// Returns true if the given TriggerGroup is paused.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    public ValueTask<bool> IsTriggerGroupPaused(
        string group,
        CancellationToken cancellationToken = default)
    {
        Throw.NotImplementedException();
        return new ValueTask<bool>(false);
    }

    /// <summary>
    /// Stores the given <see cref="IJobDetail" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="replaceExisting">
    ///     If <see langword="true" />, any <see cref="IJob" /> existing in the
    ///     <see cref="IJobStore" /> with the same name &amp; group should be over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask StoreJob(IJobDetail job, bool replaceExisting, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || replaceExisting ? LockTriggerAccess : null,
            conn => StoreJob(conn, job, replaceExisting, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary> <para>
    /// Insert or update a job.
    /// </para>
    /// </summary>
    protected virtual async ValueTask StoreJob(
        ConnectionAndTransactionHolder conn,
        IJobDetail newJob,
        bool replaceExisting,
        CancellationToken cancellationToken = default)
    {
        bool existingJob = await JobExists(conn, newJob.Key, cancellationToken).ConfigureAwait(false);
        try
        {
            if (existingJob)
            {
                if (!replaceExisting)
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
    protected virtual async ValueTask<bool> JobExists(
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
    /// <param name="replaceExisting">
    ///     If <see langword="true" />, any <see cref="ITrigger" /> existing in
    ///     the <see cref="IJobStore" /> with the same name &amp; group should
    ///     be over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    /// if a <see cref="ITrigger" /> with the same name/group already
    /// exists, and replaceExisting is set to false.
    /// </exception>
    public async ValueTask StoreTrigger(IOperableTrigger trigger, bool replaceExisting, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || replaceExisting ? LockTriggerAccess : null,
            conn => StoreTrigger(conn, trigger, null, replaceExisting, StateWaiting, false, false, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Insert or update a trigger.
    /// </summary>
    protected virtual async ValueTask StoreTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger newTrigger,
        IJobDetail? job,
        bool replaceExisting,
        string state,
        bool forceState,
        bool recovering,
        CancellationToken cancellationToken = default)
    {
        bool existingTrigger = await TriggerExists(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false);

        if (existingTrigger && !replaceExisting)
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
                    shouldBepaused = await Delegate.IsTriggerGroupPaused(conn, AllGroupsPaused, cancellationToken).ConfigureAwait(false);

                    if (shouldBepaused)
                    {
                        await Delegate.InsertPausedTriggerGroup(conn, newTrigger.Key.Group, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (shouldBepaused && state is StateWaiting or StateAcquired)
                {
                    state = StatePaused;
                }
            }

            if (job is null)
            {
                job = await RetrieveJob(conn, newTrigger.JobKey, cancellationToken).ConfigureAwait(false);
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
    protected virtual async ValueTask<bool> TriggerExists(
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
    public ValueTask<bool> RemoveJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(LockTriggerAccess, conn => RemoveJob(conn, jobKey, true, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<bool> RemoveJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        bool activeDeleteSafe,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var jobTriggers = await Delegate.SelectTriggerNamesForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);

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

    public ValueTask<bool> RemoveJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            LockTriggerAccess, async conn =>
            {
                bool allFound = true;

                // TODO: make this more efficient with a true bulk operation...
                foreach (JobKey jobKey in jobKeys)
                {
                    allFound = await RemoveJob(conn, jobKey, true, cancellationToken).ConfigureAwait(false) && allFound;
                }

                return allFound;
            }, cancellationToken);
    }

    public ValueTask<bool> RemoveTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            LockTriggerAccess,
            async conn =>
            {
                bool allFound = true;

                // TODO: make this more efficient with a true bulk operation...
                foreach (TriggerKey triggerKey in triggerKeys)
                {
                    allFound = await RemoveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false) && allFound;
                }

                return allFound;
            }, cancellationToken);
    }

    public async ValueTask StoreJobsAndTriggers(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || replace ? LockTriggerAccess : null, async conn =>
            {
                // TODO: make this more efficient with a true bulk operation...
                foreach (var pair in triggersAndJobs)
                {
                    var job = pair.Key;
                    var triggers = pair.Value;
                    await StoreJob(conn, job, replace, cancellationToken).ConfigureAwait(false);
                    foreach (var trigger in triggers)
                    {
                        await StoreTrigger(conn, (IOperableTrigger) trigger, job, replace, StateWaiting, false, false, cancellationToken).ConfigureAwait(false);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete a job and its listeners.
    /// </summary>
    /// <seealso cref="JobStoreSupport.RemoveJob(ConnectionAndTransactionHolder, JobKey, bool, CancellationToken)" />
    /// <seealso cref="RemoveTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
    private async ValueTask<bool> DeleteJobAndChildren(
        ConnectionAndTransactionHolder conn,
        JobKey key,
        CancellationToken cancellationToken)
    {
        return await Delegate.DeleteJobDetail(conn, key, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Delete a trigger, its listeners, and its Simple/Cron/BLOB sub-table entry.
    /// </summary>
    /// <seealso cref="RemoveJob(ConnectionAndTransactionHolder, JobKey, bool, CancellationToken)" />
    /// <seealso cref="RemoveTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
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
            await Delegate.DeleteFiredTriggers(conn, key, cancellationToken).ConfigureAwait(false);
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
    public ValueTask<IJobDetail?> RetrieveJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => RetrieveJob(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<IJobDetail?> RetrieveJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var job = await Delegate.SelectJobDetail(conn, jobKey, TypeLoadHelper, cancellationToken).ConfigureAwait(false);
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
    public ValueTask<bool> RemoveTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            LockTriggerAccess,
            conn => RemoveTrigger(conn, triggerKey, cancellationToken),
            cancellationToken);
    }

    protected virtual ValueTask<bool> RemoveTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return RemoveTrigger(conn, triggerKey, null, cancellationToken);
    }

    protected virtual async ValueTask<bool> RemoveTrigger(
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
                job = await Delegate.SelectJobForTrigger(conn, triggerKey, new NullJobTypeLoader(), false, cancellationToken).ConfigureAwait(false);
            }

            removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (null != job && !job.Durable)
            {
                int numTriggers = await Delegate.SelectNumTriggersForJob(conn, job.Key, cancellationToken).ConfigureAwait(false);
                if (numTriggers == 0)
                {
                    // Don't call RemoveJob() because we don't want to check for
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

    private sealed class NullJobTypeLoader : ITypeLoadHelper
    {
        public void Initialize()
        {
        }

        public Type? LoadType(string? name)
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
        return ExecuteInLock(LockTriggerAccess,
            conn => ReplaceTrigger(conn, triggerKey, trigger, cancellationToken),
            cancellationToken);
    }

    protected virtual async ValueTask<bool> ReplaceTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IOperableTrigger newTrigger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // this must be called before we delete the trigger, obviously
            var job = await Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoadHelper, cancellationToken).ConfigureAwait(false);

            if (job is null)
            {
                return false;
            }

            if (!newTrigger.JobKey.Equals(job.Key))
            {
                Throw.JobPersistenceException("New trigger is not related to the same job as the old trigger.");
            }

            bool removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            await StoreTrigger(conn, newTrigger, job, false, StateWaiting, false, false, cancellationToken).ConfigureAwait(false);

            return removedTrigger;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="ITrigger" />, or null if there is no match.</returns>
    public ValueTask<IOperableTrigger?> RetrieveTrigger(TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => RetrieveTrigger(conn, triggerKey, cancellationToken),
            cancellationToken);
    }

    protected virtual async ValueTask<IOperableTrigger?> RetrieveTrigger(
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
    protected virtual async ValueTask<TriggerState> GetTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string ts = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (ts is null)
            {
                return TriggerState.None;
            }

            if (ts == StateDeleted)
            {
                return TriggerState.None;
            }

            if (ts == (StateComplete))
            {
                return TriggerState.Complete;
            }

            if (ts == StatePaused)
            {
                return TriggerState.Paused;
            }

            if (ts == StatePausedBlocked)
            {
                return TriggerState.Paused;
            }

            if (ts == StateError)
            {
                return TriggerState.Error;
            }

            if (ts == StateBlocked)
            {
                return TriggerState.Blocked;
            }

            return TriggerState.Normal;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't determine state of trigger ({triggerKey}): {e.Message}", e);
            return default;
        }
    }

    public async ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockTriggerAccess,
            conn => ResetTriggerFromErrorState(conn, triggerKey, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ResetTriggerFromErrorState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var newState = StateWaiting;

            if (await Delegate.IsTriggerGroupPaused(conn, triggerKey.Group, cancellationToken).ConfigureAwait(false))
            {
                newState = StatePaused;
            }

            await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StateError, cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Trigger {TriggerKey} reset from ERROR state to: {NewState}", triggerKey, newState);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't reset from error state of trigger ({triggerKey}): {e.Message}", e);
        }
    }

    /// <summary>
    /// Store the given <see cref="ICalendar" />.
    /// </summary>
    /// <param name="calName">The name of the calendar.</param>
    /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
    /// <param name="replaceExisting">
    /// If <see langword="true" />, any <see cref="ICalendar" /> existing
    /// in the <see cref="IJobStore" /> with the same name &amp; group
    /// should be over-written.
    /// </param>
    /// <param name="updateTriggers"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    ///           if a <see cref="ICalendar" /> with the same name already
    ///           exists, and replaceExisting is set to false.
    /// </exception>
    public async ValueTask StoreCalendar(
        string calName,
        ICalendar calendar,
        bool replaceExisting,
        bool updateTriggers,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || updateTriggers ? LockTriggerAccess : null,
            async conn => await StoreCalendar(conn, calName, calendar, replaceExisting, updateTriggers, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    protected virtual async ValueTask StoreCalendar(
        ConnectionAndTransactionHolder conn,
        string calName,
        ICalendar calendar,
        bool replaceExisting,
        bool updateTriggers,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool existingCal = await CalendarExists(conn, calName, cancellationToken).ConfigureAwait(false);
            if (existingCal && !replaceExisting)
            {
                Throw.ObjectAlreadyExistsException("Calendar with name '" + calName + "' already exists.");
            }

            if (existingCal)
            {
                if (await Delegate.UpdateCalendar(conn, calName, calendar, cancellationToken).ConfigureAwait(false) < 1)
                {
                    Throw.JobPersistenceException("Couldn't store calendar.  Update failed.");
                }

                if (updateTriggers)
                {
                    var triggers = await Delegate.SelectTriggersForCalendar(conn, calName, cancellationToken).ConfigureAwait(false);

                    foreach (IOperableTrigger trigger in triggers)
                    {
                        trigger.UpdateWithNewCalendar(calendar, MisfireThreshold);
                        await StoreTrigger(conn, trigger, null, true, StateWaiting, false, false, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (await Delegate.InsertCalendar(conn, calName, calendar, cancellationToken).ConfigureAwait(false) < 1)
                {
                    Throw.JobPersistenceException("Couldn't store calendar.  Insert failed.");
                }
            }

            if (!Clustered)
            {
                calendarCache[calName] = calendar; // lazy-cache
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

    protected virtual async ValueTask<bool> CalendarExists(
        ConnectionAndTransactionHolder conn,
        string calName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.CalendarExists(conn, calName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't determine calendar existence (" + calName + "): " + e.Message, e);
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
    /// <param name="name">The name of the <see cref="ICalendar" /> to be removed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> if a <see cref="ICalendar" /> with the given name
    /// was found and removed from the store.
    ///</returns>
    public ValueTask<bool> RemoveCalendar(
        string name,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(LockTriggerAccess, conn => RemoveCalendar(conn, name, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<bool> RemoveCalendar(
        ConnectionAndTransactionHolder conn,
        string calName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await Delegate.CalendarIsReferenced(conn, calName, cancellationToken).ConfigureAwait(false))
            {
                Throw.JobPersistenceException("Calender cannot be removed if it referenced by a trigger!");
            }

            if (!Clustered)
            {
                calendarCache.Remove(calName);
            }

            return await Delegate.DeleteCalendar(conn, calName, cancellationToken).ConfigureAwait(false) > 0;
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
    /// <param name="name">The name of the <see cref="ICalendar" /> to be retrieved.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="ICalendar" />, or null if there is no match.</returns>
    public ValueTask<ICalendar?> RetrieveCalendar(string name, CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => RetrieveCalendar(conn, name, cancellationToken),
            cancellationToken);
    }

    protected virtual async ValueTask<ICalendar?> RetrieveCalendar(
        ConnectionAndTransactionHolder conn,
        string calName,
        CancellationToken cancellationToken = default)
    {
        // all calendars are persistent, but we lazy-cache them during run
        // time as long as we aren't running clustered.
        ICalendar? cal = null;
        if (!Clustered)
        {
            calendarCache.TryGetValue(calName, out cal);
        }
        if (cal is not null)
        {
            return cal;
        }

        try
        {
            cal = await Delegate.SelectCalendar(conn, calName, cancellationToken).ConfigureAwait(false);
            if (!Clustered)
            {
                calendarCache[calName] = cal; // lazy-cache...
            }
            return cal;
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

    /// <summary>
    /// Get the number of <see cref="IJob" /> s that are
    /// stored in the <see cref="IJobStore" />.
    /// </summary>
    public ValueTask<int> GetNumberOfJobs(CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetNumberOfJobs(conn, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<int> GetNumberOfJobs(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectNumJobs(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain number of jobs: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get the number of <see cref="ITrigger" /> s that are
    /// stored in the <see cref="IJobStore" />.
    /// </summary>
    public ValueTask<int> GetNumberOfTriggers(CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => GetNumberOfTriggers(conn, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<int> GetNumberOfTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectNumTriggers(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain number of triggers: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get the number of <see cref="ICalendar" /> s that are
    /// stored in the <see cref="IJobStore" />.
    /// </summary>
    public ValueTask<int> GetNumberOfCalendars(CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetNumberOfCalendars(conn, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<int> GetNumberOfCalendars(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectNumCalendars(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain number of calendars: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get the names of all of the <see cref="IJob" /> s that
    /// have the given group name.
    /// </summary>
    /// <remarks>
    /// If there are no jobs in the given group name, the result should be a
    /// zero-length array (not <see langword="null" />).
    /// </remarks>
    public ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetJobNames(conn, matcher, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<List<JobKey>> GetJobNames(ConnectionAndTransactionHolder conn, GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectJobsInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain job names: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Determine whether a <see cref="ICalendar" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="name">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a calendar exists with the given identifier</returns>
    public ValueTask<bool> CalendarExists(
        string name,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => CheckExists(conn, name, cancellationToken),
            cancellationToken);
    }

    protected async ValueTask<bool> CheckExists(
        ConnectionAndTransactionHolder conn,
        string calName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.CalendarExists(conn, calName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't check for existence of job: " + e.Message, e);
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
    public ValueTask<bool> CheckExists(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => CheckExists(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected async ValueTask<bool> CheckExists(
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
    public ValueTask<bool> CheckExists(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => CheckExists(conn, triggerKey, cancellationToken), cancellationToken);
    }

    protected async ValueTask<bool> CheckExists(
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
    public async ValueTask ClearAllSchedulingData(CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(LockTriggerAccess, conn => ClearAllSchedulingData(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    protected async ValueTask ClearAllSchedulingData(
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

    /// <summary>
    /// Get the names of all of the <see cref="ITrigger" /> s
    /// that have the given group name.
    /// </summary>
    /// <remarks>
    /// If there are no triggers in the given group name, the result should be a
    /// zero-length array (not <see langword="null" />).
    /// </remarks>
    public ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggerNames(conn, matcher, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<List<TriggerKey>> GetTriggerNames(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectTriggersInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain trigger names: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get the names of all of the <see cref="IJob" />
    /// groups.
    /// </summary>
    /// <remarks>
    /// If there are no known group names, the result should be a zero-length array (not <see langword="null" />).
    /// </remarks>
    public ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetJobGroupNames(conn, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<List<string>> GetJobGroupNames(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectJobGroups(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain job groups: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get the names of all of the <see cref="ITrigger" />
    /// groups.
    /// </summary>
    ///
    /// <remarks>
    /// If there are no known group names, the result should be a zero-length
    /// array (not <see langword="null" />).
    /// </remarks>
    public ValueTask<List<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggerGroupNames(conn, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<List<string>> GetTriggerGroupNames(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectTriggerGroups(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Get the names of all of the <see cref="ICalendar" /> s
    /// in the <see cref="IJobStore" />.
    /// </summary>
    /// <remarks>
    /// If there are no Calendars in the given group name, the result should be
    /// a zero-length array (not <see langword="null" />).
    /// </remarks>
    public ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetCalendarNames(conn, cancellationToken), cancellationToken);
    }

    protected virtual async ValueTask<List<string>> GetCalendarNames(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectCalendars(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
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

    protected virtual async ValueTask<List<IOperableTrigger>> GetTriggersForJob(ConnectionAndTransactionHolder conn, JobKey jobKey, CancellationToken cancellationToken = default)
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
    public async ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(LockTriggerAccess, conn => PauseTrigger(conn, triggerKey, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given name.
    /// </summary>
    public virtual async ValueTask PauseTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string oldState = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (oldState is StateWaiting or StateAcquired)
            {
                await Delegate.UpdateTriggerState(conn, triggerKey, StatePaused, cancellationToken).ConfigureAwait(false);
            }
            else if (oldState == StateBlocked)
            {
                await Delegate.UpdateTriggerState(conn, triggerKey, StatePausedBlocked, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Couldn't pause trigger '{triggerKey}': {e.Message}", e);
        }
    }

    /// <summary>
    /// Pause the <see cref="IJob" /> with the given name - by
    /// pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    /// <seealso cref="ResumeJob(JobKey,CancellationToken)" />
    public virtual async ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(LockTriggerAccess, async conn =>
        {
            var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
            foreach (IOperableTrigger trigger in triggers)
            {
                await PauseTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause all of the <see cref="IJob" />s in the given
    /// group - by pausing all of their <see cref="ITrigger" />s.
    /// </summary>
    /// <seealso cref="ResumeJobs" />
    public virtual ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(LockTriggerAccess, async conn =>
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
        }, cancellationToken);
    }

    /// <summary>
    /// Determines if a Trigger for the given job should be blocked.
    /// State can only transition to StatePausedBlocked/StateBlocked from
    /// StatePaused/StateWaiting respectively.
    /// </summary>
    /// <returns>StatePausedBlocked, StateBlocked, or the currentState. </returns>
    protected virtual async ValueTask<string> CheckBlockedState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        string currentState,
        CancellationToken cancellationToken = default)
    {
        // State can only transition to BLOCKED from PAUSED or WAITING.
        if (currentState != StateWaiting && currentState != StatePaused)
        {
            return currentState;
        }

        try
        {
            var lst = await Delegate.SelectFiredTriggerRecordsByJob(conn, jobKey.Name, jobKey.Group, cancellationToken).ConfigureAwait(false);

            if (lst.Count > 0)
            {
                FiredTriggerRecord rec = lst[0];
                if (rec.JobDisallowsConcurrentExecution) // TODO: worry about failed/recovering/volatile job  states?
                {
                    return StatePaused == currentState ? StatePausedBlocked : StateBlocked;
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

    public virtual async ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(LockTriggerAccess, conn => ResumeTrigger(conn, triggerKey, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the
    /// given name.
    /// </summary>
    /// <remarks>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    public virtual async ValueTask ResumeTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TriggerStatus? status = await Delegate.SelectTriggerStatus(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (status?.NextFireTimeUtc is null || status.NextFireTimeUtc == DateTimeOffset.MinValue)
            {
                return;
            }

            bool blocked = StatePausedBlocked == status.Status;

            string newState = await CheckBlockedState(conn, status.JobKey, StateWaiting, cancellationToken).ConfigureAwait(false);

            bool misfired = false;

            if (schedulerRunning && status.NextFireTimeUtc.Value < timeProvider.GetUtcNow())
            {
                misfired = await UpdateMisfiredTrigger(conn, triggerKey, newState, forceState: true, cancellationToken).ConfigureAwait(false);
            }

            if (!misfired)
            {
                if (blocked)
                {
                    await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StatePausedBlocked, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StatePaused, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't resume trigger '" + triggerKey + "': " + e.Message, e);
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
    public virtual async ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(LockTriggerAccess, async conn =>
        {
            var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
            foreach (IOperableTrigger trigger in triggers)
            {
                await ResumeTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
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
    public virtual ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(LockTriggerAccess, async conn =>
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
        }, cancellationToken);
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// </summary>
    /// <seealso cref="ResumeTriggers(Quartz.Impl.Matchers.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    public virtual ValueTask<List<string>> PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            LockTriggerAccess,
            conn => PauseTriggerGroup(conn, matcher, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// </summary>
    public virtual async ValueTask<List<string>> PauseTriggerGroup(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        try
        {
            await Delegate.UpdateTriggerGroupStateFromOtherStates(conn, matcher, StatePaused,
                StateAcquired, StateWaiting,
                StateWaiting, cancellationToken).ConfigureAwait(false);

            await Delegate.UpdateTriggerGroupStateFromOtherState(conn, matcher, StatePausedBlocked,
                StateBlocked, cancellationToken).ConfigureAwait(false);

            var groups = new List<string>(await Delegate.SelectTriggerGroups(conn, matcher, cancellationToken).ConfigureAwait(false));

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

    public ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetPausedTriggerGroups(conn, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the
    /// given group.
    /// </summary>
    public virtual async ValueTask<List<string>> GetPausedTriggerGroups(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Delegate.SelectPausedTriggerGroups(conn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't determine paused trigger groups: " + e.Message, e);
            return default;
        }
    }

    public virtual ValueTask<List<string>> ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            LockTriggerAccess, conn => ResumeTriggers(conn, matcher, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s
    /// in the given group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    public virtual async ValueTask<List<string>> ResumeTriggers(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Delegate.DeletePausedTriggerGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
            var groups = new HashSet<string>();

            List<TriggerKey>? keys = await Delegate.SelectTriggersInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);

            foreach (TriggerKey key in keys)
            {
                await ResumeTrigger(conn, key, cancellationToken).ConfigureAwait(false);
                groups.Add(key.Group);
            }

            return [..groups];

            // TODO: find an efficient way to resume triggers (better than the
            // above)... logic below is broken because of
            // findTriggersToBeBlocked()
            /*
             * int res =
             * getDelegate().UpdateTriggerGroupStateFromOtherState(conn,
             * groupName, StateWaiting, StatePaused);
             *
             * if(res > 0) {
             *
             * long misfireTime = System.currentTimeMillis();
             * if(getMisfireThreshold() > 0) misfireTime -=
             * getMisfireThreshold();
             *
             * Key[] misfires =
             * getDelegate().SelectMisfiredTriggersInGroupInState(conn,
             * groupName, StateWaiting, misfireTime);
             *
             * List blockedTriggers = findTriggersToBeBlocked(conn,
             * groupName);
             *
             * Iterator itr = blockedTriggers.iterator(); while(itr.hasNext()) {
             * Key key = (Key)itr.next();
             * getDelegate().UpdateTriggerState(conn, key.getName(),
             * key.getGroup(), StateBlocked); }
             *
             * for(int i=0; i < misfires.length; i++) {               String
             * newState = StateWaiting;
             * if(blockedTriggers.contains(misfires[i])) newState =
             * StateBlocked; UpdateMisfiredTrigger(conn,
             * misfires[i].getName(), misfires[i].getGroup(), newState, true); } }
             */
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't pause trigger group '" + matcher + "': " + e.Message, e);
            return default;
        }
    }

    public virtual async ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(LockTriggerAccess, conn => PauseAll(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause all triggers - equivalent of calling <see cref="PauseTriggers(Quartz.Impl.Matchers.GroupMatcher{Quartz.TriggerKey},CancellationToken)" />
    /// on every group.
    /// <para>
    /// When <see cref="ResumeAll(CancellationToken)" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="ResumeAll(CancellationToken)" />
    public virtual async ValueTask PauseAll(
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
            if (!await Delegate.IsTriggerGroupPaused(conn, AllGroupsPaused, cancellationToken).ConfigureAwait(false))
            {
                await Delegate.InsertPausedTriggerGroup(conn, AllGroupsPaused, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't pause all trigger groups: " + e.Message, e);
        }
    }

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(Quartz.Impl.Matchers.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    /// on every group.
    /// </summary>
    /// <remarks>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseAll(CancellationToken)" />
    public virtual async ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(LockTriggerAccess, conn => ResumeAll(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(Quartz.Impl.Matchers.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    /// on every group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="PauseAll(CancellationToken)" />
    public virtual async ValueTask ResumeAll(
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
            await Delegate.DeletePausedTriggerGroup(conn, AllGroupsPaused, cancellationToken).ConfigureAwait(false);
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
    public virtual ValueTask<List<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken cancellationToken = default)
    {
        string? lockName;
        if (AcquireTriggersWithinLock || maxCount > 1)
        {
            lockName = LockTriggerAccess;
        }
        else
        {
            lockName = null;
        }

        return ExecuteInNonManagedTXLock(
            lockName,
            conn => AcquireNextTrigger(conn, noLaterThan, maxCount, timeWindow, cancellationToken), async (conn, result) =>
            {
                try
                {
                    var acquired = await Delegate.SelectInstancesFiredTriggerRecords(conn, InstanceId, cancellationToken).ConfigureAwait(false);
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
            cancellationToken);
    }

    // TODO: this really ought to return something like a FiredTriggerBundle,
    // so that the fireInstanceId doesn't have to be on the trigger...

    protected virtual async ValueTask<List<IOperableTrigger>> AcquireNextTrigger(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset noLaterThan,
        int maxCount,
        TimeSpan timeWindow,
        CancellationToken cancellationToken = default)
    {
        if (timeWindow < TimeSpan.Zero)
        {
            Throw.ArgumentOutOfRangeException(nameof(timeWindow));
        }

        List<IOperableTrigger> acquiredTriggers = [];
        HashSet<JobKey> acquiredJobKeysForNoConcurrentExec = [];
        const int MaxDoLoopRetry = 3;
        int currentLoopCount = 0;

        do
        {
            currentLoopCount++;
            try
            {
                var results = await Delegate.SelectTriggerToAcquire(conn, noLaterThan + timeWindow, MisfireTime, maxCount, cancellationToken).ConfigureAwait(false);

                // No trigger is ready to fire yet.
                if (results.Count == 0)
                {
                    return acquiredTriggers;
                }

                DateTimeOffset batchEnd = noLaterThan;

                foreach (var result in results)
                {
                    var triggerKey = new TriggerKey(result.TriggerName, result.TriggerGroup);

                    // If our trigger is no longer available, try a new one.
                    var nextTrigger = await RetrieveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                    if (nextTrigger is null)
                    {
                        continue; // next trigger
                    }

                    // If trigger's job is set as @DisallowConcurrentExecution, and it has already been added to result, then
                    // put it back into the timeTriggers set and continue to search for next trigger.
                    Type jobType;
                    try
                    {
                        jobType = typeLoadHelper.LoadType(result.JobType)!;
                    }
                    catch (JobPersistenceException jpe)
                    {
                        try
                        {
                            Logger.LogError(jpe, "Error retrieving job, setting trigger state to ERROR.");
                            await Delegate.UpdateTriggerState(conn, triggerKey, StateError, cancellationToken).ConfigureAwait(false);
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
                    }

                    var nextFireTimeUtc = nextTrigger.GetNextFireTimeUtc();

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
                    int rowsUpdated = await Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(conn, triggerKey, StateAcquired, StateWaiting, nextFireTimeUtc.Value, cancellationToken).ConfigureAwait(false);
                    if (rowsUpdated <= 0)
                    {
                        // TODO: Hum... shouldn't we log a warning here?
                        continue; // next trigger
                    }
                    nextTrigger.FireInstanceId = GetFiredTriggerRecordId();
                    await Delegate.InsertFiredTrigger(conn, nextTrigger, StateAcquired, null, cancellationToken).ConfigureAwait(false);

                    if (acquiredTriggers.Count == 0)
                    {
                        var now = timeProvider.GetUtcNow();
                        var nextFireTime = nextFireTimeUtc.Value;
                        var max = now > nextFireTime ? now : nextFireTime;

                        batchEnd = max + timeWindow;
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
        await RetryExecuteInNonManagedTXLock(
            LockTriggerAccess,
            conn => ReleaseAcquiredTrigger(conn, trigger, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    protected virtual async ValueTask ReleaseAcquiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, StateWaiting, StateAcquired, cancellationToken).ConfigureAwait(false);
            await Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, StateWaiting, StateBlocked, cancellationToken).ConfigureAwait(false);
            await Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't release acquired trigger: " + e.Message, e);
        }
    }

    public virtual ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        return ExecuteInNonManagedTXLock(
            LockTriggerAccess,
            async conn =>
            {
                List<TriggerFiredResult> results = new(triggers.Count);

                foreach (IOperableTrigger trigger in triggers)
                {
                    TriggerFiredResult result;
                    try
                    {
                        var bundle = await TriggerFired(conn, trigger, cancellationToken).ConfigureAwait(false);
                        result = new TriggerFiredResult(bundle);
                    }
                    catch (JobPersistenceException jpe)
                    {
                        Logger.LogError(jpe, "Caught job persistence exception: {ExceptionMessage}", jpe.Message);
                        result = new TriggerFiredResult(jpe);
                    }
                    catch (Exception ex)
                    {
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
                        .SelectInstancesFiredTriggerRecords(conn, InstanceId, cancellationToken)
                        .ConfigureAwait(false);
                    var executingTriggers = new HashSet<string>();
                    foreach (FiredTriggerRecord ft in acquired)
                    {
                        if (StateExecuting == ft.FireInstanceState)
                        {
                            executingTriggers.Add(ft.FireInstanceId!);
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
            cancellationToken);
    }

    protected virtual async ValueTask<TriggerFiredBundle?> TriggerFired(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        IJobDetail? job;
        ICalendar? cal = null;

        // Make sure trigger wasn't deleted, paused, or completed...
        try
        {
            // if trigger was deleted, state will be StateDeleted
            string state = await Delegate.SelectTriggerState(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
            if (state != StateAcquired)
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
            job = await RetrieveJob(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false);
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
                await Delegate.UpdateTriggerState(conn, trigger.Key, StateError, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception sqle)
            {
                Logger.LogError(sqle, "Unable to set trigger state to ERROR.");
            }
            throw;
        }

        if (trigger.CalendarName is not null)
        {
            cal = await RetrieveCalendar(conn, trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (cal is null)
            {
                return null;
            }
        }

        try
        {
            await Delegate.UpdateFiredTrigger(conn, trigger, StateExecuting, job, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Couldn't update fired trigger: " + e.Message, e);
        }

        DateTimeOffset? prevFireTime = trigger.GetPreviousFireTimeUtc();

        // call triggered - to update the trigger's next-fire-time state...
        trigger.Triggered(cal);

        string state2 = StateWaiting;
        bool force = true;

        if (job.ConcurrentExecutionDisallowed)
        {
            state2 = StateBlocked;
            force = false;
            try
            {
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StateBlocked, StateWaiting, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StateBlocked, StateAcquired, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StatePausedBlocked, StatePaused, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException("Couldn't update states of blocked triggers: " + e.Message, e);
            }
        }

        if (!trigger.GetNextFireTimeUtc().HasValue)
        {
            state2 = StateComplete;
            force = true;
        }

        await StoreTrigger(conn, trigger, job, true, state2, force, false, cancellationToken).ConfigureAwait(false);

        job.JobDataMap.ClearDirtyFlag();

        return new TriggerFiredBundle(
            job,
            trigger,
            cal,
            jobIsRecovering: trigger.Key.Group == SchedulerConstants.DefaultRecoveryGroup,
            timeProvider.GetUtcNow(),
            trigger.GetPreviousFireTimeUtc(),
            prevFireTime,
            trigger.GetNextFireTimeUtc());
    }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler has completed the
    /// firing of the given <see cref="ITrigger" /> (and the execution its
    /// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
    /// in the given <see cref="IJobDetail" /> should be updated if the <see cref="IJob" />
    /// is stateful.
    /// </summary>
    public virtual async ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken cancellationToken = default)
    {
        await RetryExecuteInNonManagedTXLock(
            LockTriggerAccess,
            conn => TriggeredJobComplete(conn, trigger, jobDetail, triggerInstCode, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    protected virtual async ValueTask TriggeredJobComplete(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        IJobDetail jobDetail,
        SchedulerInstruction triggerInstCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (triggerInstCode == SchedulerInstruction.DeleteTrigger)
            {
                if (!trigger.GetNextFireTimeUtc().HasValue)
                {
                    // double check for possible reschedule within job
                    // execution, which would cancel the need to delete...
                    var stat = await Delegate.SelectTriggerStatus(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    if (stat is not null && !stat.NextFireTimeUtc.HasValue)
                    {
                        await RemoveTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await RemoveTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                }
            }
            else if (triggerInstCode == SchedulerInstruction.SetTriggerComplete)
            {
                await Delegate.UpdateTriggerState(conn, trigger.Key, StateComplete, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }
            else if (triggerInstCode == SchedulerInstruction.SetTriggerError)
            {
                Logger.LogInformation("Trigger {Trigger} set to ERROR state.", trigger.Key);
                await Delegate.UpdateTriggerState(conn, trigger.Key, StateError, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }
            else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersComplete)
            {
                await Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StateComplete, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }
            else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersError)
            {
                Logger.LogInformation("All triggers of Job {Job} set to ERROR state.", trigger.JobKey);
                await Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StateError, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            }

            if (jobDetail.ConcurrentExecutionDisallowed)
            {
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, StateWaiting, StateBlocked, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, StatePaused, StatePausedBlocked, cancellationToken).ConfigureAwait(false);
                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
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
        CancellationToken cancellationToken)
    {
        bool transOwner = false;
        ConnectionAndTransactionHolder conn = await GetNonManagedTXConnection().ConfigureAwait(false);
        try
        {
            RecoverMisfiredJobsResult result = RecoverMisfiredJobsResult.NoOp;

            // Before we make the potentially expensive call to acquire the
            // trigger lock, peek ahead to see if it is likely we would find
            // misfired triggers requiring recovery.
            int misfireCount = DoubleCheckLockMisfireHandler
                ? await Delegate.CountMisfiredTriggersInState(conn, StateWaiting, MisfireTime, cancellationToken).ConfigureAwait(false)
                : int.MaxValue;

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Found {MisfireCount} triggers that missed their scheduled fire-time.", misfireCount);
            }

            if (misfireCount > 0)
            {
                transOwner = await LockHandler.ObtainLock(requestorId, conn, LockTriggerAccess, cancellationToken).ConfigureAwait(false);

                result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
            }

            await CommitConnection(conn, false).ConfigureAwait(false);
            return result;
        }
        catch (JobPersistenceException jpe)
        {
            await RollbackConnection(conn, jpe).ConfigureAwait(false);
            throw;
        }
        catch (Exception e)
        {
            await RollbackConnection(conn, e).ConfigureAwait(false);
            Throw.JobPersistenceException("Database error recovering from misfires.", e);
            return default;
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId, LockTriggerAccess, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CleanupConnection(conn).ConfigureAwait(false);
            }
        }
    }

    protected internal virtual ValueTask SignalSchedulingChangeImmediately(DateTimeOffset? candidateNewNextFireTime)
    {
        return schedSignaler.SignalSchedulingChange(candidateNewNextFireTime);
    }

    //---------------------------------------------------------------------------
    // Cluster management methods
    //---------------------------------------------------------------------------

    private bool firstCheckIn = true;

    protected internal DateTimeOffset LastCheckin { get; set; }

    protected internal virtual async ValueTask<bool> DoCheckin(
        Guid requestorId,
        CancellationToken cancellationToken = default)
    {
        bool transOwner = false;
        bool transStateOwner = false;
        bool recovered = false;

        ConnectionAndTransactionHolder conn = await GetNonManagedTXConnection().ConfigureAwait(false);
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
                await CommitConnection(conn, true).ConfigureAwait(false);
            }

            if (firstCheckIn || failedRecords is not null && failedRecords.Count > 0)
            {
                await LockHandler.ObtainLock(requestorId, conn, LockStateAccess, cancellationToken).ConfigureAwait(false);
                transStateOwner = true;

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
                    await LockHandler.ObtainLock(requestorId, conn, LockTriggerAccess, cancellationToken).ConfigureAwait(false);
                    //getLockHandler().obtainLock(conn, LockJobAccess);
                    transOwner = true;

                    await ClusterRecover(conn, failedRecords, cancellationToken).ConfigureAwait(false);
                    recovered = true;
                }
            }

            await CommitConnection(conn, false).ConfigureAwait(false);
        }
        catch (JobPersistenceException jpe)
        {
            await RollbackConnection(conn, jpe).ConfigureAwait(false);
            throw;
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId, LockTriggerAccess, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await ReleaseLock(requestorId, LockStateAccess, transStateOwner, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await CleanupConnection(conn).ConfigureAwait(false);
                }
            }
        }

        firstCheckIn = false;

        return recovered;
    }

    /// <summary>
    /// Get a list of all scheduler instances in the cluster that may have failed.
    /// This includes this scheduler if it is checking in for the first time.
    /// </summary>
    protected virtual async ValueTask<List<SchedulerStateRecord>> FindFailedInstances(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<SchedulerStateRecord> failedInstances = [];
            bool foundThisScheduler = false;

            var states = await Delegate.SelectSchedulerStateRecords(conn, instanceName: null, cancellationToken).ConfigureAwait(false);

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

    protected virtual async ValueTask<List<SchedulerStateRecord>> ClusterCheckIn(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        var failedInstances = await FindFailedInstances(conn, cancellationToken).ConfigureAwait(false);
        try
        {
            // TODO: handle self-failed-out

            // check in...
            LastCheckin = timeProvider.GetUtcNow();
            if (await Delegate.UpdateSchedulerState(conn, InstanceId, LastCheckin, cancellationToken).ConfigureAwait(false) == 0)
            {
                await Delegate.InsertSchedulerState(conn, InstanceId, LastCheckin, ClusterCheckinInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Failure updating scheduler state when checking-in: " + e.Message, e);
        }

        return failedInstances;
    }

    protected virtual async ValueTask ClusterRecover(
        ConnectionAndTransactionHolder conn,
        List<SchedulerStateRecord> failedInstances,
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

                    var firedTriggerRecs = await Delegate.SelectInstancesFiredTriggerRecords(conn, rec.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);

                    int acquiredCount = 0;
                    int recoveredCount = 0;
                    int otherCount = 0;

                    var triggerKeys = new HashSet<TriggerKey>();

                    foreach (FiredTriggerRecord ftRec in firedTriggerRecs)
                    {
                        TriggerKey tKey = ftRec.TriggerKey!;
                        JobKey? jKey = ftRec.JobKey;

                        triggerKeys.Add(tKey);

                        // release blocked triggers..
                        if (ftRec.FireInstanceState == StateBlocked)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StateWaiting, StateBlocked, cancellationToken).ConfigureAwait(false);
                        }
                        else if (ftRec.FireInstanceState == StatePausedBlocked)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StatePaused, StatePausedBlocked, cancellationToken).ConfigureAwait(false);
                        }

                        // release acquired triggers..
                        if (ftRec.FireInstanceState == StateAcquired)
                        {
                            await Delegate.UpdateTriggerStateFromOtherState(conn, tKey, StateWaiting, StateAcquired, cancellationToken).ConfigureAwait(false);
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
                                rcvryTrig.MisfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;
                                rcvryTrig.Priority = ftRec.Priority;
                                JobDataMap jd = await Delegate.SelectTriggerJobDataMap(conn, tKey, cancellationToken).ConfigureAwait(false);
                                jd.Put(SchedulerConstants.FailedJobOriginalTriggerName, tKey.Name);
                                jd.Put(SchedulerConstants.FailedJobOriginalTriggerGroup, tKey.Group);
                                jd.Put(SchedulerConstants.FailedJobOriginalTriggerFiretime, Convert.ToString(ftRec.FireTimestamp, CultureInfo.InvariantCulture));
                                rcvryTrig.JobDataMap = jd;

                                rcvryTrig.ComputeFirstFireTimeUtc(null);
                                await StoreTrigger(conn, rcvryTrig, null, false, StateWaiting, false, true, cancellationToken).ConfigureAwait(false);
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
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StateWaiting, StateBlocked, cancellationToken).ConfigureAwait(false);
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey!, StatePaused, StatePausedBlocked, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    await Delegate.DeleteFiredTriggers(conn, rec.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);

                    // Check if any of the fired triggers we just deleted were the last fired trigger
                    // records of a COMPLETE trigger.
                    int completeCount = 0;
                    foreach (TriggerKey triggerKey in triggerKeys)
                    {
                        if (await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false) == StateComplete)
                        {
                            var firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, triggerKey.Name, triggerKey.Group, cancellationToken).ConfigureAwait(false);
                            if (firedTriggers.Count == 0)
                            {
                                if (await RemoveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))
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

                    if (rec.SchedulerInstanceId != InstanceId)
                    {
                        await Delegate.DeleteSchedulerState(conn, rec.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException("Failure recovering jobs: " + e.Message, e);
            }
        }
    }

    protected virtual void LogWarnIfNonZero(int val, string warning)
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
    /// <seealso cref="CloseConnection(ConnectionAndTransactionHolder)" />
    protected virtual async ValueTask CleanupConnection(ConnectionAndTransactionHolder? conn)
    {
        if (conn is not null)
        {
            await CloseConnection(conn).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the supplied connection.
    /// </summary>
    /// <param name="cth">(Optional)</param>
    protected virtual async ValueTask CloseConnection(ConnectionAndTransactionHolder cth)
    {
        await cth.Close().ConfigureAwait(false);
    }

    /// <summary>
    /// Rollback the supplied connection.
    /// </summary>
    protected virtual async ValueTask RollbackConnection(ConnectionAndTransactionHolder? cth, Exception cause)
    {
        if (cth is null)
        {
            // db might be down or similar
            Logger.LogInformation("ConnectionAndTransactionHolder passed to RollbackConnection was null, ignoring");
            return;
        }

        await cth.Rollback(IsTransient(cause)).ConfigureAwait(false);
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
    /// Commit the supplied connection.
    /// </summary>
    /// <param name="cth">The CTH.</param>
    /// <param name="openNewTransaction">if set to <c>true</c> opens a new transaction.</param>
    /// <throws>JobPersistenceException thrown if a SQLException occurs when the </throws>
    protected virtual async ValueTask CommitConnection(ConnectionAndTransactionHolder cth, bool openNewTransaction)
    {
        if (cth is null)
        {
            Logger.LogDebug("ConnectionAndTransactionHolder passed to CommitConnection was null, ignoring");
            return;
        }
        await cth.Commit(openNewTransaction).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the given callback in a transaction. Depending on the JobStore,
    /// the surrounding transaction may be assumed to be already present
    /// (managed).
    /// </summary>
    /// <remarks>
    /// This method just forwards to ExecuteInLock() with a null lockName.
    /// </remarks>
    protected ValueTask<T> ExecuteWithoutLock<T>(
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(null, txCallback, cancellationToken);
    }

    protected ValueTask<object> ExecuteInLock(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock<object>(lockName, async conn =>
        {
            await txCallback(conn).ConfigureAwait(false);
            return null!;
        }, cancellationToken);
    }

    /// <summary>
    /// Execute the given callback having acquired the given lock.
    /// Depending on the JobStore, the surrounding transaction may be
    /// assumed to be already present (managed).
    /// </summary>
    /// <param name="lockName">
    /// The name of the lock to acquire, for example
    /// "TRIGGER_ACCESS".  If null, then no lock is acquired, but the
    /// lockCallback is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected abstract ValueTask<T> ExecuteInLock<T>(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default);

    protected virtual ValueTask<object> RetryExecuteInNonManagedTXLock(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        return RetryExecuteInNonManagedTXLock<object>(lockName, async holder =>
        {
            await txCallback(holder).ConfigureAwait(false);
            return null!;
        }, requestorId: null, cancellationToken);
    }

    protected virtual async ValueTask<T> RetryExecuteInNonManagedTXLock<T>(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        Guid? requestorId,
        CancellationToken cancellationToken = default)
    {
        for (int retry = 1; !shutdown; retry++)
        {
            try
            {
                return await ExecuteInNonManagedTXLock(lockName, txCallback, txValidator: null, requestorId, cancellationToken).ConfigureAwait(false);
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
                Logger.LogError(e, "retryExecuteInNonManagedTXLock: RuntimeException {ExceptionMessage}", e.Message);
            }

            // retry every N seconds (the db connection must be failed)
            await Task.Delay(DbRetryInterval, cancellationToken).ConfigureAwait(false);
        }

        Throw.InvalidOperationException("JobStore is shutdown - aborting retry");
        return default;
    }

    protected ValueTask<bool> ExecuteInNonManagedTXLock(
        string lockName,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken)
    {
        return ExecuteInNonManagedTXLock(lockName, async conn =>
        {
            await txCallback(conn).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    protected ValueTask<T> ExecuteInNonManagedTXLock<T>(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken)
    {
        return ExecuteInNonManagedTXLock(lockName, txCallback, null, cancellationToken);
    }

    /// <summary>
    /// Execute the given callback having optionally acquired the given lock.
    /// This uses the non-managed transaction connection.
    /// </summary>
    /// <param name="lockName">
    /// The name of the lock to acquire, for example
    /// "TRIGGER_ACCESS".  If null, then no lock is acquired, but the
    /// lockCallback is still executed in a non-managed transaction.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="txValidator"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected ValueTask<T> ExecuteInNonManagedTXLock<T>(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        Func<ConnectionAndTransactionHolder, T, ValueTask<bool>>? txValidator,
        CancellationToken cancellationToken) => ExecuteInNonManagedTXLock(lockName, txCallback, txValidator, requestorId: null, cancellationToken);

    protected async ValueTask<T> ExecuteInNonManagedTXLock<T>(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        Func<ConnectionAndTransactionHolder, T, ValueTask<bool>>? txValidator,
        Guid? requestorId,
        CancellationToken cancellationToken)
    {
        if (requestorId is null)
        {
            requestorId = Core.Context.CallerId.Value;
            if (requestorId is null)
            {
                requestorId = Guid.NewGuid();
            }
        }

        bool transOwner = false;
        ConnectionAndTransactionHolder? conn = null;
        try
        {
            if (lockName is not null)
            {
                // If we aren't using db locks, then delay getting DB connection
                // until after acquiring the lock since it isn't needed.
                if (LockHandler.RequiresConnection)
                {
                    conn = await GetNonManagedTXConnection().ConfigureAwait(false);
                }

                transOwner = await LockHandler.ObtainLock(requestorId.Value, conn, lockName, cancellationToken).ConfigureAwait(false);
            }

            if (conn is null)
            {
                conn = await GetNonManagedTXConnection().ConfigureAwait(false);
            }

            T result = await txCallback(conn).ConfigureAwait(false);
            try
            {
                await CommitConnection(conn, false).ConfigureAwait(false);
            }
            catch (JobPersistenceException jpe)
            {
                await RollbackConnection(conn, jpe).ConfigureAwait(false);
                if (txValidator is null)
                {
                    throw;
                }
                if (!await RetryExecuteInNonManagedTXLock(
                        lockName,
                        async connection => await txValidator(connection, result).ConfigureAwait(false),
                        requestorId,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }
            }

            DateTimeOffset? sigTime = conn.SignalSchedulingChangeOnTxCompletion;
            if (sigTime is not null)
            {
                await SignalSchedulingChangeImmediately(sigTime).ConfigureAwait(false);
            }

            return result;
        }
        catch (JobPersistenceException jpe)
        {
            await RollbackConnection(conn, jpe).ConfigureAwait(false);
            throw;
        }
        catch (Exception e)
        {
            await RollbackConnection(conn, e).ConfigureAwait(false);
            Throw.JobPersistenceException("Unexpected runtime exception: " + e.Message, e);
            return default;
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId.Value, lockName!, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CleanupConnection(conn).ConfigureAwait(false);
            }
        }
    }
}