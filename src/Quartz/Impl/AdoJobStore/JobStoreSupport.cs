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

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Contains base functionality for ADO.NET-based JobStore implementations.
    /// </summary>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public abstract class JobStoreSupport : PersistentJobStore<ConnectionAndTransactionHolder>, IClusterManagementOperations, IMisfireHandlerOperations
    {
        protected const string LockTriggerAccess = "TRIGGER_ACCESS";
        protected const string LockStateAccess = "STATE_ACCESS";

        private string tablePrefix = AdoConstants.DefaultTablePrefix;
        private bool useProperties;
        protected Type delegateType;
        protected readonly Dictionary<string, ICalendar> calendarCache = new Dictionary<string, ICalendar>();
        private IDriverDelegate driverDelegate;
        private IsolationLevel isolationLevel = IsolationLevel.ReadCommitted;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobStoreSupport"/> class.
        /// </summary>
        protected JobStoreSupport()
        {
            delegateType = typeof (StdAdoDelegate);
        }

        /// <summary>
        /// Get or set the datasource name.
        /// </summary>
        public string DataSource { get; set; }

        /// <summary>
        /// Get or set the database connection manager.
        /// </summary>
        public IDbConnectionManager ConnectionManager { get; set; } = DBConnectionManager.Instance;

        /// <summary>
        /// Get or sets the prefix that should be pre-pended to all table names.
        /// </summary>
        public string TablePrefix
        {
            get => tablePrefix;
            set
            {
                if (value == null)
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
                if (value == null)
                {
                    value = "false";
                }

                useProperties = bool.Parse(value);
            }
        }

        public IObjectSerializer ObjectSerializer { get; set; }

        /// <inheritdoc />
        public override long EstimatedTimeToReleaseAndAcquireTrigger { get; } = 70;

        /// <summary>
        /// Gets or sets the database retry interval, alias for <see cref="DbRetryInterval"/>.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan DbRetryInterval
        {
            get => RetryInterval;
            set => RetryInterval = value;
        }

        /// <summary>
        /// Get or set whether this instance should use database-based thread
        /// synchronization.
        /// </summary>
        public bool UseDBLocks { get; set; }

        /// <summary>
        /// Set the transaction isolation level of DB connections to sequential.
        /// </summary>
        public bool TxIsolationLevelSerializable
        {
            get => isolationLevel == IsolationLevel.Serializable;
            set => isolationLevel = value ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted;
        }

        /// <summary>
        /// Get or set the ADO.NET driver delegate class name.
        /// </summary>
        public virtual string DriverDelegateType { get; set; }

        /// <summary>
        /// The driver delegate's initialization string.
        /// </summary>
        public string DriverDelegateInitString { get; set; }

        /// <summary>
        /// set the SQL statement to use to select and lock a row in the "locks"
        /// table.
        /// </summary>
        /// <seealso cref="StdRowLockSemaphore" />
        public virtual string SelectWithLockSQL { get; set; }

        protected DbMetadata DbMetadata => ConnectionManager.GetDbMetadata(DataSource);

        protected abstract Task<ConnectionAndTransactionHolder> GetNonManagedTXConnection();

        /// <summary>
        /// Gets the connection and starts a new transaction.
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<ConnectionAndTransactionHolder> GetConnection()
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
                throw new JobPersistenceException($"Failed to obtain DB connection from data source '{DataSource}': {e}", e);
            }
            if (conn == null)
            {
                throw new JobPersistenceException($"Could not get connection from DataSource '{DataSource}'");
            }

            try
            {
                tx = conn.BeginTransaction(isolationLevel);
            }
            catch (Exception e)
            {
                conn.Close();
                throw new JobPersistenceException("Failure setting up connection.", e);
            }

            return new ConnectionAndTransactionHolder(conn, tx);
        }

        /// <summary>
        /// Get the driver delegate for DB operations.
        /// </summary>
        protected virtual IDriverDelegate Delegate
        {
            get
            {
                lock (this)
                {
                    if (driverDelegate == null)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(DriverDelegateType))
                            {
                                delegateType = TypeLoadHelper.LoadType(DriverDelegateType);
                            }

                            IDbProvider dbProvider = ConnectionManager.GetDbProvider(DataSource);
                            var args = new DelegateInitializationArgs();
                            args.UseProperties = CanUseProperties;
                            args.TablePrefix = tablePrefix;
                            args.InstanceName = InstanceName;
                            args.InstanceId = InstanceId;
                            args.DbProvider = dbProvider;
                            args.TypeLoadHelper = TypeLoadHelper;
                            args.ObjectSerializer = ObjectSerializer;
                            args.InitString = DriverDelegateInitString;

                            ConstructorInfo ctor = delegateType.GetConstructor(new Type[0]);
                            if (ctor == null)
                            {
                                throw new InvalidConfigurationException("Configured delegate does not have public constructor that takes no arguments");
                            }

                            driverDelegate = (IDriverDelegate) ctor.Invoke(null);
                            driverDelegate.Initialize(args);
                        }
                        catch (Exception e)
                        {
                            throw new NoSuchDelegateException("Couldn't instantiate delegate: " + e.Message, e);
                        }
                    }
                }
                return driverDelegate;
            }
        }

        private IDbProvider DbProvider => ConnectionManager.GetDbProvider(DataSource);

        protected internal virtual ISemaphore LockHandler { get; set; }

        /// <summary>
        /// Get whether String-only properties will be handled in JobDataMaps.
        /// </summary>
        public virtual bool CanUseProperties => useProperties;

        public override Task Initialize(
            ITypeLoadHelper loadHelper,
            ISchedulerSignaler schedulerSignaler,
            CancellationToken cancellationToken = default)
        {
            base.Initialize(loadHelper, schedulerSignaler, cancellationToken);

            if (string.IsNullOrWhiteSpace(DataSource))
            {
                throw new SchedulerConfigException("DataSource name not set.");
            }

            if (Delegate is SQLiteDelegate && (LockHandler == null || LockHandler.GetType() != typeof(UpdateLockRowSemaphore)))
            {
                Log.Info("Detected SQLite usage, changing to use UpdateLockRowSemaphore");
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
                    throw new InvalidConfigurationException("SQLite cannot be used as clustered mode due to locking problems");
                }
                if (!AcquireTriggersWithinLock)
                {
                    Log.Info("With SQLite we need to set AcquireTriggersWithinLock to true, changing");
                    AcquireTriggersWithinLock = true;

                }
                if (!TxIsolationLevelSerializable)
                {
                    Log.Info("Detected usage of SQLiteDelegate - defaulting 'txIsolationLevelSerializable' to 'true'");
                    TxIsolationLevelSerializable = true;
                }
            }

            // If the user hasn't specified an explicit lock handler, then
            // choose one based on CMT/Clustered/UseDBLocks.
            if (LockHandler == null)
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
                        if (SelectWithLockSQL == null)
                        {
                            const string DefaultLockSql = "SELECT * FROM {0}LOCKS WITH (UPDLOCK,ROWLOCK) WHERE " + AdoConstants.ColumnSchedulerName + " = {1} AND LOCK_NAME = @lockName";
                            Log.InfoFormat("Detected usage of SqlServerDelegate - defaulting 'selectWithLockSQL' to '" + DefaultLockSql + "'.", TablePrefix, "'" + InstanceName + "'");
                            SelectWithLockSQL = DefaultLockSql;
                        }
                    }

                    Log.Info("Using db table-based data access locking (synchronization).");
                    LockHandler = new StdRowLockSemaphore(TablePrefix, InstanceName, SelectWithLockSQL, DbProvider);
                }
                else
                {
                    Log.Info("Using thread monitor-based data access locking (synchronization).");
                    LockHandler = new SimpleSemaphore();
                }
            }
            else
            {
                // be ready to give a friendly warning if SQL Server is used and sub-optimal locking
                if (LockHandler is UpdateLockRowSemaphore && Delegate is SqlServerDelegate)
                {
                    Log.Warn("Detected usage of SqlServerDelegate and UpdateLockRowSemaphore, removing 'quartz.jobStore.lockHandler.type' would allow more efficient SQL Server specific (UPDLOCK,ROWLOCK) row access");
                }
                // be ready to give a friendly warning if SQL Server provider and wrong delegate
                if (DbProvider != null && DbProvider.Metadata.ConnectionType == typeof (SqlConnection) && !(Delegate is SqlServerDelegate))
                {
                    Log.Warn("Detected usage of SQL Server provider without SqlServerDelegate, SqlServerDelegate would provide better performance");
                }
            }

            return TaskUtil.CompletedTask;
        }
        
        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
        /// it should free up all of it's resources because the scheduler is
        /// shutting down.
        /// </summary>
        public override async Task Shutdown(CancellationToken cancellationToken = default)
        {
            await base.Shutdown(cancellationToken).ConfigureAwait(false);
            try
            {
                ConnectionManager.Shutdown(DataSource);
            }
            catch (Exception sqle)
            {
                Log.WarnException("Database connection Shutdown unsuccessful.", sqle);
            }
        }

        protected virtual async Task ReleaseLock(
            Guid requestorId,
            LockType lockType,
            CancellationToken cancellationToken)
        {
            try
            {
                await LockHandler.ReleaseLock(requestorId, GetLockName(lockType), cancellationToken).ConfigureAwait(false);
            }
            catch (LockException le)
            {
                Log.ErrorException("Error returning lock: " + le.Message, le);
            }
        }
        
        protected override Task<bool> GetMisfiredTriggersInWaitingState(
            ConnectionAndTransactionHolder conn,
            int count,
            List<TriggerKey> resultList,
            CancellationToken cancellationToken) => Delegate.HasMisfiredTriggersInState(conn, AdoConstants.StateWaiting, MisfireTime, count, resultList, cancellationToken);

        protected override Task<int> UpdateTriggerStatesFromOtherStates(
            ConnectionAndTransactionHolder conn,
            InternalTriggerState newState,
            InternalTriggerState[] oldStates,
            CancellationToken cancellationToken)
            => Delegate.UpdateTriggerStatesFromOtherStates(
                conn,
                GetStateName(newState),
                GetStateName(oldStates[0]),
                GetStateName(oldStates.Length > 1 ? oldStates[1] : oldStates[0]),
                cancellationToken);

        protected override Task<IReadOnlyCollection<TriggerKey>> GetTriggersInState(
            ConnectionAndTransactionHolder conn,
            InternalTriggerState state,
            CancellationToken cancellationToken) => Delegate.SelectTriggersInState(conn, GetStateName(state), cancellationToken);

        protected override Task<int> DeleteFiredTriggers(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.DeleteFiredTriggers(conn, cancellationToken);
        
        protected override Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForRecoveringJobs(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectTriggersForRecoveringJobs(conn, cancellationToken);

        protected override Task DoStoreJob(
            ConnectionAndTransactionHolder conn,
            IJobDetail newJob,
            bool existingJob,
            CancellationToken cancellationToken)
            => existingJob
                ? Delegate.UpdateJobDetail(conn, newJob, cancellationToken)
                : Delegate.InsertJobDetail(conn, newJob, cancellationToken);

        /// <summary>
        /// Check existence of a given job.
        /// </summary>
        protected override Task<bool> JobExists(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken) => Delegate.JobExists(conn, jobKey, cancellationToken);

        /// <summary>
        /// Insert or update a trigger.
        /// </summary>
        protected override Task StoreTrigger(
            ConnectionAndTransactionHolder conn,
            bool existingTrigger,
            IOperableTrigger newTrigger,
            InternalTriggerState state,
            IJobDetail job,
            CancellationToken cancellationToken)
            => existingTrigger
                ? Delegate.UpdateTrigger(conn, newTrigger, GetStateName(state), job, cancellationToken)
                : Delegate.InsertTrigger(conn, newTrigger, GetStateName(state), job, cancellationToken);

        protected override Task<bool> TriggerExists(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken) => Delegate.TriggerExists(conn, triggerKey, cancellationToken);

        protected override Task<bool> IsTriggerGroupPaused(
            ConnectionAndTransactionHolder conn,
            string groupName,
            CancellationToken cancellationToken) => Delegate.IsTriggerGroupPaused(conn, groupName, cancellationToken);

        protected override Task AddPausedTriggerGroup(
            ConnectionAndTransactionHolder conn, 
            string groupName, 
            CancellationToken cancellationToken) => Delegate.InsertPausedTriggerGroup(conn, groupName, cancellationToken);

        protected override Task<IReadOnlyCollection<FiredTriggerRecord>> SelectFiredTriggerRecordsByJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey, 
            CancellationToken cancellationToken) => Delegate.SelectFiredTriggerRecordsByJob(conn, jobKey.Name, jobKey.Group, cancellationToken);

        protected override async Task<bool> DeleteJobAndChildren(
            ConnectionAndTransactionHolder conn,
            JobKey key,
            CancellationToken cancellationToken) => await Delegate.DeleteJobDetail(conn, key, cancellationToken).ConfigureAwait(false) > 0;

        protected override async Task<bool> DeleteTriggerAndChildren(
            ConnectionAndTransactionHolder conn,
            TriggerKey key,
            CancellationToken cancellationToken) => await Delegate.DeleteTrigger(conn, key, cancellationToken).ConfigureAwait(false) > 0;

        protected override Task<IJobDetail> RetrieveJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken) => Delegate.SelectJobDetail(conn, jobKey, TypeLoadHelper, cancellationToken);

        protected override async Task<bool> RemoveTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            IJobDetail job,
            CancellationToken cancellationToken)
        {
            bool removedTrigger;
            try
            {
                // this must be called before we delete the trigger, obviously
                // we use fault tolerant type loading as we only want to delete things
                if (job == null)
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
                            await SchedulerSignaler.NotifySchedulerListenersJobDeleted(job.Key, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
            }

            return removedTrigger;
        }

        private class NullJobTypeLoader : ITypeLoadHelper
        {
            public void Initialize()
            {
            }

            public Type LoadType(string name)
            {
                return null;
            }
        }

        protected override Task<IJobDetail> GetJobForTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken) => Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoadHelper, cancellationToken);

        protected override Task<IOperableTrigger> RetrieveTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken) => Delegate.SelectTrigger(conn, triggerKey, cancellationToken);

        protected override async Task<TriggerState> GetTriggerState(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            string ts = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            switch (ts)
            {
                case null:
                case AdoConstants.StateDeleted:
                    return TriggerState.None;
                case AdoConstants.StateComplete:
                    return TriggerState.Complete;
                case AdoConstants.StatePaused:
                case AdoConstants.StatePausedBlocked:
                    return TriggerState.Paused;
                case AdoConstants.StateError:
                    return TriggerState.Error;
                case AdoConstants.StateBlocked:
                    return TriggerState.Blocked;
            }

            return TriggerState.Normal;
        }

        protected override async Task<InternalTriggerState> GetInternalTriggerState(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            string ts = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            return StdAdoDelegate.GetStateFromString(ts);
        }

        protected override Task UpdateFiredTrigger(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger trigger,
            InternalTriggerState state,
            IJobDetail job,
            CancellationToken cancellationToken) => Delegate.UpdateFiredTrigger(conn, trigger, GetStateName(state), job, cancellationToken);

        protected override Task<IReadOnlyCollection<FiredTriggerRecord>> GetInstancesFiredTriggerRecords(
            ConnectionAndTransactionHolder conn,
            string instanceId,
            CancellationToken cancellationToken) => Delegate.SelectInstancesFiredTriggerRecords(conn, instanceId, cancellationToken);

        protected override async Task StoreCalendar(
            ConnectionAndTransactionHolder conn,
            string name,
            ICalendar calendar, 
            bool replaceExisting, 
            bool updateTriggers, 
            CancellationToken cancellationToken)
        {
            bool existingCal = await CalendarExists(conn, name, cancellationToken).ConfigureAwait(false);
            if (existingCal && !replaceExisting)
            {
                throw new ObjectAlreadyExistsException("Calendar with name '" + name + "' already exists.");
            }

            if (existingCal)
            {
                if (await Delegate.UpdateCalendar(conn, name, calendar, cancellationToken).ConfigureAwait(false) < 1)
                {
                    throw new JobPersistenceException("Couldn't store calendar.  Update failed.");
                }

                if (updateTriggers)
                {
                    var triggers = await Delegate.SelectTriggersForCalendar(conn, name, cancellationToken).ConfigureAwait(false);

                    foreach (IOperableTrigger trigger in triggers)
                    {
                        trigger.UpdateWithNewCalendar(calendar, MisfireThreshold);
                        await StoreTrigger(conn, trigger, null, true, InternalTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (await Delegate.InsertCalendar(conn, name, calendar, cancellationToken).ConfigureAwait(false) < 1)
                {
                    throw new JobPersistenceException("Couldn't store calendar.  Insert failed.");
                }
            }

            if (!Clustered)
            {
                calendarCache[name] = calendar; // lazy-cache
            }
        }

        protected override async Task<bool> RemoveCalendar(
            ConnectionAndTransactionHolder conn,
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            if (await Delegate.CalendarIsReferenced(conn, calendarName, cancellationToken).ConfigureAwait(false))
            {
                throw new JobPersistenceException("Calender cannot be removed if it referenced by a trigger!");
            }

            if (!Clustered)
            {
                calendarCache.Remove(calendarName);
            }

            return await Delegate.DeleteCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false) > 0;
        }

        protected override async Task<ICalendar> RetrieveCalendar(
            ConnectionAndTransactionHolder conn,
            string calName,
            CancellationToken cancellationToken)
        {
            // all calendars are persistent, but we lazy-cache them during run
            // time as long as we aren't running clustered.
            ICalendar calendar = null;
            if (!Clustered)
            {
                calendarCache.TryGetValue(calName, out calendar);
            }
            if (calendar != null)
            {
                return calendar;
            }

            calendar = await Delegate.SelectCalendar(conn, calName, cancellationToken).ConfigureAwait(false);
            if (!Clustered)
            {
                calendarCache[calName] = calendar; // lazy-cache...
            }

            return calendar;
        }

        protected override Task<int> GetNumberOfJobs(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectNumJobs(conn, cancellationToken);

        protected override Task<int> GetNumberOfTriggers(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectNumTriggers(conn, cancellationToken);

        protected override Task<int> GetNumberOfCalendars(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectNumCalendars(conn, cancellationToken);

        protected override Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            ConnectionAndTransactionHolder conn,
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken) => Delegate.SelectJobsInGroup(conn, matcher, cancellationToken);

        protected override Task<bool> CalendarExists(
            ConnectionAndTransactionHolder conn,
            string calName,
            CancellationToken cancellationToken) => Delegate.CalendarExists(conn, calName, cancellationToken);

        protected override Task ClearAllSchedulingData(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.ClearData(conn, cancellationToken);

        protected override Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(
            ConnectionAndTransactionHolder conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken) => Delegate.SelectTriggersInGroup(conn, matcher, cancellationToken);

        protected override Task<IReadOnlyCollection<string>> GetJobGroupNames(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectJobGroups(conn, cancellationToken);

        protected override Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectTriggerGroups(conn, cancellationToken);

        protected override Task<IReadOnlyCollection<string>> GetCalendarNames(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectCalendars(conn, cancellationToken);

        protected override Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken) => Delegate.SelectTriggersForJob(conn, jobKey, cancellationToken);

        protected override Task<IReadOnlyCollection<TriggerKey>> GetTriggerNamesForJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken) => Delegate.SelectTriggerNamesForJob(conn, jobKey, cancellationToken);

        protected override async Task<IReadOnlyCollection<string>> PauseTriggerGroup(
            ConnectionAndTransactionHolder conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken)
        {
            try
            {
                await Delegate.UpdateTriggerGroupStateFromOtherStates(
                    conn, 
                    matcher, 
                    newState: AdoConstants.StatePaused,
                    oldState1: AdoConstants.StateAcquired, 
                    oldState2: AdoConstants.StateWaiting,
                    oldState3: AdoConstants.StateWaiting, 
                    cancellationToken).ConfigureAwait(false);

                await Delegate.UpdateTriggerGroupStateFromOtherState(
                    conn, 
                    matcher,
                    newState: AdoConstants.StatePausedBlocked,
                    oldState: AdoConstants.StateBlocked, 
                    cancellationToken).ConfigureAwait(false);

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

                return new ReadOnlyCompatibleHashSet<string>(groups);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't pause trigger group '" + matcher + "': " + e.Message, e);
            }
        }

        protected override Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.SelectPausedTriggerGroups(conn, cancellationToken);

        protected override async Task<IReadOnlyCollection<string>> ResumeTriggers(
            ConnectionAndTransactionHolder conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken)
        {
            await Delegate.DeletePausedTriggerGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
            var groups = new ReadOnlyCompatibleHashSet<string>();

            IReadOnlyCollection<TriggerKey> keys = await Delegate.SelectTriggersInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);

            foreach (TriggerKey key in keys)
            {
                await ResumeTrigger(conn, key, cancellationToken).ConfigureAwait(false);
                groups.Add(key.Group);
            }

            return groups;

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

        protected override Task ClearAllTriggerGroupsPausedFlag(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken) => Delegate.DeletePausedTriggerGroup(conn, AllGroupsPaused, cancellationToken);

        protected override Task AddFiredTrigger(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger trigger, 
            InternalTriggerState state, 
            IJobDetail jobDetail, 
            CancellationToken cancellationToken) => Delegate.InsertFiredTrigger(conn, trigger, GetStateName(state), jobDetail, cancellationToken);

        protected override Task<IReadOnlyCollection<TriggerKey>> GetTriggerToAcquire(
            ConnectionAndTransactionHolder conn, 
            DateTimeOffset noLaterThan, 
            DateTimeOffset noEarlierThan, 
            int maxCount,
            CancellationToken cancellationToken) => Delegate.SelectTriggerToAcquire(conn, noLaterThan, noEarlierThan, maxCount, cancellationToken);

        //---------------------------------------------------------------------------
        // Management methods
        //---------------------------------------------------------------------------

        async Task<RecoverMisfiredJobsResult> IMisfireHandlerOperations.RecoverMisfires(
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
                    ? await Delegate.CountMisfiredTriggersInState(conn, AdoConstants.StateWaiting, MisfireTime, cancellationToken).ConfigureAwait(false)
                    : int.MaxValue;

                if (Log.IsDebugEnabled())
                {
                    Log.DebugFormat("Found {0} triggers that missed their scheduled fire-time.", misfireCount);
                }

                if (misfireCount > 0)
                {
                    transOwner = await LockHandler.ObtainLock(requestorId, conn, LockTriggerAccess, cancellationToken).ConfigureAwait(false);

                    result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
                }

                CommitConnection(conn, false);
                return result;
            }
            catch (JobPersistenceException jpe)
            {
                RollbackConnection(conn, jpe);
                throw;
            }
            catch (Exception e)
            {
                RollbackConnection(conn, e);
                throw new JobPersistenceException("Database error recovering from misfires.", e);
            }
            finally
            {
                if (transOwner)
                {
                    try
                    {
                        await ReleaseLock(requestorId, LockType.TriggerAccess, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        CleanupConnection(conn);
                    }
                }
            }
        }

        //---------------------------------------------------------------------------
        // Cluster management methods
        //---------------------------------------------------------------------------

        protected bool firstCheckIn = true;

        public DateTimeOffset LastCheckin { get; protected set; } = SystemTime.UtcNow();

        async Task<bool> IClusterManagementOperations.CheckCluster(Guid requestorId, CancellationToken cancellationToken)
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
                IReadOnlyList<SchedulerStateRecord> failedRecords = null;
                if (!firstCheckIn)
                {
                    failedRecords = await ClusterCheckIn(conn, cancellationToken).ConfigureAwait(false);
                    CommitConnection(conn, true);
                }

                if (firstCheckIn || failedRecords != null && failedRecords.Count > 0)
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

                CommitConnection(conn, false);
            }
            catch (JobPersistenceException jpe)
            {
                RollbackConnection(conn, jpe);
                throw;
            }
            finally
            {
                try
                {
                    if (transOwner)
                    {
                        await ReleaseLock(requestorId, LockType.TriggerAccess, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (transStateOwner)
                    {
                        try
                        {
                            await ReleaseLock(requestorId, LockType.StateAccess, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            CleanupConnection(conn);
                        }
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
        protected virtual async Task<IReadOnlyList<SchedulerStateRecord>> FindFailedInstances(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken = default)
        {
            try
            {
                List<SchedulerStateRecord> failedInstances = new List<SchedulerStateRecord>();
                bool foundThisScheduler = false;

                var states = await Delegate.SelectSchedulerStateRecords(conn, null, cancellationToken).ConfigureAwait(false);

                foreach (SchedulerStateRecord rec in states)
                {
                    // find own record...
                    if (rec.SchedulerInstanceId.Equals(InstanceId))
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
                        if (CalcFailedIfAfter(rec) < SystemTime.UtcNow())
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
                    Log.Warn($"This scheduler instance ({InstanceId}) is still active but was recovered by another instance in the cluster.  This may cause inconsistent behavior.");
                }

                return failedInstances;
            }
            catch (Exception e)
            {
                LastCheckin = SystemTime.UtcNow();
                throw new JobPersistenceException($"Failure identifying failed instances when checking-in: {e.Message}", e);
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
        private async Task<IReadOnlyList<SchedulerStateRecord>> FindOrphanedFailedInstances(
            ConnectionAndTransactionHolder conn,
            IReadOnlyCollection<SchedulerStateRecord> schedulerStateRecords,
            CancellationToken cancellationToken)
        {
            List<SchedulerStateRecord> orphanedInstances = new List<SchedulerStateRecord>();

            var names = await Delegate.SelectFiredTriggerInstanceNames(conn, cancellationToken).ConfigureAwait(false);
            var allFiredTriggerInstanceNames = new HashSet<string>(names);
            if (allFiredTriggerInstanceNames.Count > 0)
            {
                foreach (SchedulerStateRecord rec in schedulerStateRecords)
                {
                    allFiredTriggerInstanceNames.Remove(rec.SchedulerInstanceId);
                }

                foreach (string name in allFiredTriggerInstanceNames)
                {
                    var orphanedInstance = new SchedulerStateRecord
                    {
                        SchedulerInstanceId = name
                    };

                    orphanedInstances.Add(orphanedInstance);

                    Log.Warn("Found orphaned fired triggers for instance: " + orphanedInstance.SchedulerInstanceId);
                }
            }

            return orphanedInstances;
        }

        protected DateTimeOffset CalcFailedIfAfter(SchedulerStateRecord rec)
        {
            TimeSpan passed = SystemTime.UtcNow() - LastCheckin;
            TimeSpan ts = rec.CheckinInterval > passed ? rec.CheckinInterval : passed;
            return rec.CheckinTimestamp.Add(ts).Add(ClusterCheckinMisfireThreshold);
        }

        protected virtual async Task<IReadOnlyList<SchedulerStateRecord>> ClusterCheckIn(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken = default)
        {
            var failedInstances = await FindFailedInstances(conn, cancellationToken).ConfigureAwait(false);
            try
            {
                // TODO: handle self-failed-out

                // check in...
                LastCheckin = SystemTime.UtcNow();
                if (await Delegate.UpdateSchedulerState(conn, InstanceId, LastCheckin, cancellationToken).ConfigureAwait(false) == 0)
                {
                    await Delegate.InsertSchedulerState(conn, InstanceId, LastCheckin, ClusterCheckinInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Failure updating scheduler state when checking-in: " + e.Message, e);
            }

            return failedInstances;
        }

        protected virtual async Task ClusterRecover(
            ConnectionAndTransactionHolder conn,
            IReadOnlyList<SchedulerStateRecord> failedInstances,
            CancellationToken cancellationToken = default)
        {
            void LogWarnIfNonZero(int val, string warning)
            {
                if (val > 0)
                {
                    Log.Info(warning);
                }
                else
                {
                    Log.Debug(warning);
                }
            }
            
            if (failedInstances.Count > 0)
            {
                long recoverIds = SystemTime.UtcNow().Ticks;

                LogWarnIfNonZero(failedInstances.Count, $"ClusterManager: detected {failedInstances.Count} failed or restarted instances.");
                try
                {
                    foreach (SchedulerStateRecord rec in failedInstances)
                    {
                        Log.Info($"ClusterManager: Scanning for instance '{rec.SchedulerInstanceId}'s failed in-progress jobs.");

                        var firedTriggerRecs = await Delegate.SelectInstancesFiredTriggerRecords(conn, rec.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);

                        int acquiredCount = 0;
                        int recoveredCount = 0;
                        int otherCount = 0;

                        var triggerKeys = new HashSet<TriggerKey>();

                        foreach (FiredTriggerRecord ftRec in firedTriggerRecs)
                        {
                            TriggerKey tKey = ftRec.TriggerKey;
                            JobKey jKey = ftRec.JobKey;

                            triggerKeys.Add(tKey);

                            // release blocked triggers..
                            if (ftRec.FireInstanceState == InternalTriggerState.Blocked)
                            {
                                await UpdateTriggerStatesForJobFromOtherState(conn, jKey, InternalTriggerState.Waiting, InternalTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                            }
                            else if (ftRec.FireInstanceState == InternalTriggerState.PausedAndBlocked)
                            {
                                await UpdateTriggerStatesForJobFromOtherState(conn, jKey, InternalTriggerState.Paused, InternalTriggerState.PausedAndBlocked, cancellationToken).ConfigureAwait(false);
                            }

                            // release acquired triggers..
                            if (ftRec.FireInstanceState == InternalTriggerState.Acquired)
                            {
                                await UpdateTriggerStateFromOtherStates(conn, tKey, InternalTriggerState.Waiting, new [] { InternalTriggerState.Acquired }, cancellationToken).ConfigureAwait(false);
                                acquiredCount++;
                            }
                            else if (ftRec.JobRequestsRecovery)
                            {
                                // handle jobs marked for recovery that were not fully
                                // executed..
                                if (await JobExists(conn, jKey, cancellationToken).ConfigureAwait(false))
                                {
                                    var recoveryTrigger = new SimpleTriggerImpl(
                                        $"recover_{rec.SchedulerInstanceId}_{recoverIds++}",
                                        SchedulerConstants.DefaultRecoveryGroup,
                                        ftRec.FireTimestamp);

                                    recoveryTrigger.JobName = jKey.Name;
                                    recoveryTrigger.JobGroup = jKey.Group;
                                    recoveryTrigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;
                                    recoveryTrigger.Priority = ftRec.Priority;
                                    JobDataMap jd = await Delegate.SelectTriggerJobDataMap(conn, tKey, cancellationToken).ConfigureAwait(false);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerName, tKey.Name);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerGroup, tKey.Group);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerFiretime, Convert.ToString(ftRec.FireTimestamp, CultureInfo.InvariantCulture));
                                    recoveryTrigger.JobDataMap = jd;

                                    recoveryTrigger.ComputeFirstFireTimeUtc(null);
                                    await StoreTrigger(conn, recoveryTrigger, null, false, InternalTriggerState.Waiting, false, true, cancellationToken).ConfigureAwait(false);
                                    recoveredCount++;
                                }
                                else
                                {
                                    Log.Warn($"ClusterManager: failed job '{jKey}' no longer exists, cannot schedule recovery.");
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
                                await UpdateTriggerStatesForJobFromOtherState(conn, jKey, InternalTriggerState.Waiting, InternalTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                                await UpdateTriggerStatesForJobFromOtherState(conn, jKey, InternalTriggerState.Paused, InternalTriggerState.PausedAndBlocked, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        await Delegate.DeleteFiredTriggers(conn, rec.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);

                        // Check if any of the fired triggers we just deleted were the last fired trigger
                        // records of a COMPLETE trigger.
                        int completeCount = 0;
                        foreach (TriggerKey triggerKey in triggerKeys)
                        {
                            var triggerState = await GetTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                            if (TriggerState.Complete == triggerState)
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
                        LogWarnIfNonZero(acquiredCount, $"ClusterManager: ......Freed {acquiredCount} acquired trigger(s).");
                        LogWarnIfNonZero(completeCount, $"ClusterManager: ......Deleted {completeCount} complete triggers(s).");
                        LogWarnIfNonZero(recoveredCount, $"ClusterManager: ......Scheduled {recoveredCount} recoverable job(s) for recovery.");
                        LogWarnIfNonZero(otherCount, $"ClusterManager: ......Cleaned-up {otherCount} other failed job(s).");

                        if (rec.SchedulerInstanceId != InstanceId)
                        {
                            await Delegate.DeleteSchedulerState(conn, rec.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException($"Failure recovering jobs: {e.Message}", e);
                }
            }
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
        protected virtual void CleanupConnection(ConnectionAndTransactionHolder conn)
        {
            if (conn != null)
            {
                CloseConnection(conn);
            }
        }

        /// <summary>
        /// Closes the supplied connection.
        /// </summary>
        /// <param name="cth">(Optional)</param>
        protected virtual void CloseConnection(ConnectionAndTransactionHolder cth)
        {
            cth.Close();
        }

        /// <summary>
        /// Rollback the supplied connection.
        /// </summary>
        protected virtual void RollbackConnection(ConnectionAndTransactionHolder cth, Exception cause)
        {
            if (cth == null)
            {
                // db might be down or similar
                Log.Info("ConnectionAndTransactionHolder passed to RollbackConnection was null, ignoring");
                return;
            }

            cth.Rollback(IsTransient(cause));
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
            var sqlException = ex as SqlException ?? ex?.InnerException as SqlException;

            if (sqlException != null)
            {
                // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlexception?view=netframework-4.7.2
                // "SqlException always contains at least one instance of SqlError"
                foreach (SqlError err in sqlException.Errors)
                {
                    switch (err.Number)
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

            return ex is TimeoutException;
        }

        /// <summary>
        /// Commit the supplied connection.
        /// </summary>
        /// <param name="cth">The CTH.</param>
        /// <param name="openNewTransaction">if set to <c>true</c> opens a new transaction.</param>
        /// <throws>JobPersistenceException thrown if a SQLException occurs when the </throws>
        protected virtual void CommitConnection(ConnectionAndTransactionHolder cth, bool openNewTransaction)
        {
            if (cth == null)
            {
                Log.Debug("ConnectionAndTransactionHolder passed to CommitConnection was null, ignoring");
                return;
            }
            cth.Commit(openNewTransaction);
        }

        protected override Task UpdateTriggerStatesForJobFromOtherState(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            InternalTriggerState newState,
            InternalTriggerState oldState,
            CancellationToken cancellationToken)
            => Delegate.UpdateTriggerStatesForJobFromOtherState(
                conn,
                jobKey,
                GetStateName(newState),
                GetStateName(oldState),
                cancellationToken);

        protected override Task UpdateJobData(
            ConnectionAndTransactionHolder conn, 
            IJobDetail jobDetail, 
            CancellationToken cancellationToken) => Delegate.UpdateJobData(conn, jobDetail, cancellationToken);

        protected override Task DeleteFiredTrigger(
            ConnectionAndTransactionHolder conn,
            string triggerFireInstanceId, 
            CancellationToken cancellationToken) => Delegate.DeleteFiredTrigger(conn, triggerFireInstanceId, cancellationToken);

        protected override Task UpdateTriggerStatesForJob(
            ConnectionAndTransactionHolder conn, 
            JobKey triggerJobKey,
            InternalTriggerState state, 
            CancellationToken cancellationToken) => Delegate.UpdateTriggerStatesForJob(conn, triggerJobKey, GetStateName(state), cancellationToken);

        protected override Task UpdateTriggerState(
            ConnectionAndTransactionHolder conn, 
            TriggerKey triggerKey, 
            InternalTriggerState state,
            CancellationToken cancellationToken) => Delegate.UpdateTriggerState(conn, triggerKey, GetStateName(state), cancellationToken);

        /// <inheritdoc />
        protected override async Task<T> ExecuteInNonManagedTXLock<T>(
            LockType lockType,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            Func<ConnectionAndTransactionHolder, T, Task<bool>> txValidator,
            CancellationToken cancellationToken)
        {
            bool transOwner = false;
            Guid requestorId = Guid.NewGuid();
            ConnectionAndTransactionHolder conn = null;
            try
            {
                if (lockType != LockType.None)
                {
                    // If we aren't using db locks, then delay getting DB connection
                    // until after acquiring the lock since it isn't needed.
                    if (LockHandler.RequiresConnection)
                    {
                        conn = await GetNonManagedTXConnection().ConfigureAwait(false);
                    }

                    transOwner = await LockHandler.ObtainLock(requestorId, conn, GetLockName(lockType), cancellationToken).ConfigureAwait(false);
                }

                if (conn == null)
                {
                    conn = await GetNonManagedTXConnection().ConfigureAwait(false);
                }

                T result = await txCallback(conn).ConfigureAwait(false);
                try
                {
                    CommitConnection(conn, false);
                }
                catch (JobPersistenceException jpe)
                {
                    RollbackConnection(conn, jpe);
                    if (txValidator == null)
                    {
                        throw;
                    }
                    if (!await RetryExecuteInNonManagedTXLock(
                        lockType,
                        async connection => await txValidator(connection, result).ConfigureAwait(false),
                        cancellationToken).ConfigureAwait(false))
                    {
                        throw;
                    }
                }

                DateTimeOffset? sigTime = conn.SignalSchedulingChangeOnTxCompletion;
                if (sigTime != null)
                {
                    await SchedulerSignaler.SignalSchedulingChange(sigTime, CancellationToken.None).ConfigureAwait(false);
                }

                return result;
            }
            catch (JobPersistenceException jpe)
            {
                RollbackConnection(conn, jpe);
                throw;
            }
            catch (Exception e)
            {
                RollbackConnection(conn, e);
                throw new JobPersistenceException("Unexpected runtime exception: " + e.Message, e);
            }
            finally
            {
                if (transOwner)
                {
                    try
                    {
                        await ReleaseLock(requestorId, lockType, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        CleanupConnection(conn);
                    }
                }
            }
        }

        protected override IClusterManagementOperations ClusterManagementOperations => this;

        protected override IMisfireHandlerOperations MisfireHandlerOperations => this;

        protected override Task<TriggerStatus> GetTriggerStatus(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            return Delegate.SelectTriggerStatus(conn, triggerKey, cancellationToken);
        }

        protected override Task<int> UpdateTriggerStateFromOtherStates(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            InternalTriggerState newState,
            InternalTriggerState[] oldStates,
            CancellationToken cancellationToken)
        {
            string newStateString = GetStateName(newState);
            string oldState1 = GetStateName(oldStates[0]);
            string oldState2 = oldStates.Length > 1 ? GetStateName(oldStates[1]) : oldState1;
            string oldState3 = oldStates.Length > 2 ? GetStateName(oldStates[2]) : oldState2;

            return Delegate.UpdateTriggerStateFromOtherStates(
                conn, 
                triggerKey,
                newStateString, 
                oldState1, 
                oldState2, 
                oldState3, 
                cancellationToken);
        }

        protected override Task<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            InternalTriggerState newState,
            InternalTriggerState oldState,
            DateTimeOffset nextFireTime,
            CancellationToken cancellationToken)
        {
            return Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                conn,
                triggerKey,
                GetStateName(newState),
                GetStateName(oldState),
                nextFireTime,
                cancellationToken);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected string GetLockName(LockType lockType)
        {
            if (lockType == LockType.None)
            {
                return null;
            }

            if (lockType == LockType.TriggerAccess)
            {
                return LockTriggerAccess;
            }

            if (lockType == LockType.StateAccess)
            {
                return LockStateAccess;
            }

            ThrowArgumentOutOfRangeException(nameof(lockType), lockType);
            return null;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetStateName(InternalTriggerState state)
        {
            switch (state)
            {
                case InternalTriggerState.Waiting:
                    return AdoConstants.StateWaiting;
                case InternalTriggerState.Acquired:
                    return AdoConstants.StateAcquired;
                case InternalTriggerState.Executing:
                    return AdoConstants.StateExecuting;
                case InternalTriggerState.Complete:
                    return AdoConstants.StateComplete;
                case InternalTriggerState.Paused:
                    return AdoConstants.StatePaused;
                case InternalTriggerState.Blocked:
                    return AdoConstants.StateBlocked;
                case InternalTriggerState.PausedAndBlocked:
                    return AdoConstants.StatePausedBlocked;
                case InternalTriggerState.Error:
                    return AdoConstants.StateError;
            }

            ThrowArgumentOutOfRangeException(nameof(state), state);
            return null;
        }

        private static void ThrowArgumentOutOfRangeException<T>(string paramName, T value)
        {
            throw new ArgumentOutOfRangeException(paramName, value, null);
        }
    }
}