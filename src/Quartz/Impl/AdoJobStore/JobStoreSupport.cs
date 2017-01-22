#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

using Common.Logging;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
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
    public abstract class JobStoreSupport : AdoConstants, IJobStore
    {
        protected const string LockTriggerAccess = "TRIGGER_ACCESS";
        protected const string LockStateAccess = "STATE_ACCESS";

        private string dataSource;
        private string tablePrefix = DefaultTablePrefix;
        private bool useProperties;
        private string instanceId;
        private string instanceName;
        protected string delegateTypeName;
        protected Type delegateType;
        protected readonly Dictionary<string, ICalendar> calendarCache = new Dictionary<string, ICalendar>();
        private IDriverDelegate driverDelegate;
        private TimeSpan misfireThreshold = TimeSpan.FromMinutes(1); // one minute

        private bool lockOnInsert = true;
        private ClusterManager clusterManagementThread;
        private MisfireHandler misfireHandler;
        private ITypeLoadHelper typeLoadHelper;
        private ISchedulerSignaler schedSignaler;
        private readonly ILog log;
        private IObjectSerializer objectSerializer = new DefaultObjectSerializer();
        private IThreadExecutor threadExecutor = new DefaultThreadExecutor();

        private volatile bool schedulerRunning;
        private volatile bool shutdown;

        private IDbConnectionManager connectionManager = DBConnectionManager.Instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobStoreSupport"/> class.
        /// </summary>
        protected JobStoreSupport()
        {
            this.RetryableActionErrorLogThreshold = 4;
            DoubleCheckLockMisfireHandler = true;
            ClusterCheckinInterval = TimeSpan.FromMilliseconds(7500);
            MaxMisfiresToHandleAtATime = 20;
            DbRetryInterval = TimeSpan.FromSeconds(15);
            log = LogManager.GetLogger(GetType());
            delegateType = typeof(StdAdoDelegate);
        }

        /// <summary> 
        /// Get or set the datasource name.
        /// </summary>
        public virtual string DataSource
        {
            get { return dataSource; }
            set { dataSource = value; }
        }

        /// <summary> 
        /// Get or set the database connection manager.
        /// </summary>
        public virtual IDbConnectionManager ConnectionManager
        {
            get { return connectionManager; }
            set { connectionManager = value; }
        }

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        protected ILog Log
        {
            get { return log; }
        }

        /// <summary> 
        /// Get or sets the prefix that should be pre-pended to all table names.
        /// </summary>
        public virtual string TablePrefix
        {
            get { return tablePrefix; }

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
        public virtual string UseProperties
        {
            set
            {
                if (value == null)
                {
                    value = "false";
                }

                useProperties = Boolean.Parse(value);
            }
        }

        /// <summary>
        /// Get or set the instance Id of the Scheduler (must be unique within a cluster).
        /// </summary>
        public virtual string InstanceId
        {
            get { return instanceId; }
            set { instanceId = value; }
        }

        /// <summary>
        /// Get or set the instance Id of the Scheduler (must be unique within this server instance).
        /// </summary>
        public virtual string InstanceName
        {
            get { return instanceName; }
            set { instanceName = value; }
        }

        public int ThreadPoolSize
        {
            set { }
        }

        /// <summary>
        /// Gets or sets the number of retries before an error is logged for recovery operations.
        /// </summary>
        public int RetryableActionErrorLogThreshold { get; set; }


        public IThreadExecutor ThreadExecutor
        {
            get { return threadExecutor; }
            set { threadExecutor = value; }
        }

        public IObjectSerializer ObjectSerializer
        {
            set { objectSerializer = value; }
        }

        public virtual long EstimatedTimeToReleaseAndAcquireTrigger
        {
            get { return 70; }
        }

        /// <summary> 
        /// Get or set whether this instance is part of a cluster.
        /// </summary>
        public virtual bool Clustered { get; set; }

        /// <summary>
        /// Get or set the frequency at which this instance "checks-in"
        /// with the other instances of the cluster. -- Affects the rate of
        /// detecting failed instances.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public virtual TimeSpan ClusterCheckinInterval { get; set; }

        /// <summary>
        /// Get or set the maximum number of misfired triggers that the misfire handling
        /// thread will try to recover at one time (within one transaction).  The
        /// default is 20.
        /// </summary>
        public virtual int MaxMisfiresToHandleAtATime { get; set; }

        /// <summary>
        /// Gets or sets the database retry interval.
        /// </summary>
        /// <value>The db retry interval.</value>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public virtual TimeSpan DbRetryInterval { get; set; }

        /// <summary>
        /// Get or set whether this instance should use database-based thread
        /// synchronization.
        /// </summary>
        public virtual bool UseDBLocks { get; set; }

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
        public virtual bool LockOnInsert
        {
            get { return lockOnInsert; }
            set { lockOnInsert = value; }
        }

        /// <summary> 
        /// The time span by which a trigger must have missed its
        /// next-fire-time, in order for it to be considered "misfired" and thus
        /// have its misfire instruction applied.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public virtual TimeSpan MisfireThreshold
        {
            get { return misfireThreshold; }
            set
            {
                if (value.TotalMilliseconds < 1)
                {
                    throw new ArgumentException("MisfireThreshold must be larger than 0");
                }
                misfireThreshold = value;
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
        public virtual string DriverDelegateType
        {
            get { return delegateTypeName; }
            set
            {
                lock (this)
                {
                    delegateTypeName = value;
                }
            }
        }

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

        protected virtual ITypeLoadHelper TypeLoadHelper
        {
            get { return typeLoadHelper; }
        }

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

        protected DbMetadata DbMetadata
        {
            get { return ConnectionManager.GetDbMetadata(DataSource); }
        }

        protected abstract ConnectionAndTransactionHolder GetNonManagedTXConnection();

        /// <summary>
        /// Gets the connection and starts a new transaction.
        /// </summary>
        /// <returns></returns>
        protected virtual ConnectionAndTransactionHolder GetConnection()
        {
            IDbConnection conn;
            IDbTransaction tx;
            try
            {
                conn = ConnectionManager.GetConnection(DataSource);
                conn.Open();
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    string.Format("Failed to obtain DB connection from data source '{0}': {1}", DataSource, e), e);
            }
            if (conn == null)
            {
                throw new JobPersistenceException(string.Format("Could not get connection from DataSource '{0}'", DataSource));
            }

            try
            {
                if (TxIsolationLevelSerializable)
                {
                    tx = conn.BeginTransaction(IsolationLevel.Serializable);
                }
                else
                {
                    // default
                    tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                }
            }
            catch (Exception e)
            {
                conn.Close();
                throw new JobPersistenceException("Failure setting up connection.", e);
            }

            return new ConnectionAndTransactionHolder(conn, tx);
        }

        protected virtual DateTimeOffset MisfireTime
        {
            get
            {
                DateTimeOffset misfireTime = SystemTime.UtcNow();
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
                            if (delegateTypeName != null)
                            {
                                delegateType = TypeLoadHelper.LoadType(delegateTypeName);
                            }

                            IDbProvider dbProvider = ConnectionManager.GetDbProvider(DataSource);
                            var args = new DelegateInitializationArgs();
                            args.UseProperties = CanUseProperties;
                            args.Logger = log;
                            args.TablePrefix = tablePrefix;
                            args.InstanceName = instanceName;
                            args.InstanceId = instanceId;
                            args.DbProvider = dbProvider;
                            args.TypeLoadHelper = typeLoadHelper;
                            args.ObjectSerializer = objectSerializer;
                            args.InitString = DriverDelegateInitString;

                            ConstructorInfo ctor = delegateType.GetConstructor(new Type[0]);
                            if (ctor == null)
                            {
                                throw new InvalidConfigurationException("Configured delegate does not have public constructor that takes no arguments");
                            }

                            driverDelegate = (IDriverDelegate)ctor.Invoke(null);
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

        private IDbProvider DbProvider
        {
            get { return ConnectionManager.GetDbProvider(DataSource); }
        }

        protected internal virtual ISemaphore LockHandler { get; set; }

        /// <summary>
        /// Get whether String-only properties will be handled in JobDataMaps.
        /// </summary>
        public virtual bool CanUseProperties
        {
            get { return useProperties; }
        }

        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
        /// used, in order to give it a chance to Initialize.
        /// </summary>
        public virtual void Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler s)
        {
            if (dataSource == null)
            {
                throw new SchedulerConfigException("DataSource name not set.");
            }

            typeLoadHelper = loadHelper;
            schedSignaler = s;


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
                            const string DefaultLockSql = "SELECT * FROM {0}LOCKS WITH (UPDLOCK,ROWLOCK) WHERE " + ColumnSchedulerName + " = {1} AND LOCK_NAME = @lockName";
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
                if (DbProvider != null && DbProvider.Metadata.ConnectionType == typeof(SqlConnection) && !(Delegate is SqlServerDelegate))
                {
                    Log.Warn("Detected usage of SQL Server provider without SqlServerDelegate, SqlServerDelegate would provide better performance");
                }
            }
        }

        /// <seealso cref="IJobStore.SchedulerStarted()" />
        public virtual void SchedulerStarted()
        {
            if (Clustered)
            {
                clusterManagementThread = new ClusterManager(this);
                clusterManagementThread.Initialize();
            }
            else
            {
                try
                {
                    RecoverJobs();
                }
                catch (SchedulerException se)
                {
                    throw new SchedulerConfigException("Failure occurred during job recovery.", se);
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
        public void SchedulerPaused()
        {
            schedulerRunning = false;
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the JobStore that
        /// the scheduler has resumed after being paused.
        /// </summary>
        public void SchedulerResumed()
        {
            schedulerRunning = true;
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
        /// it should free up all of it's resources because the scheduler is
        /// shutting down.
        /// </summary>
        public virtual void Shutdown()
        {
            shutdown = true;

            if (misfireHandler != null)
            {
                misfireHandler.Shutdown();
                try
                {
                    misfireHandler.Join();
                }
                catch (ThreadInterruptedException)
                {
                }
            }

            if (clusterManagementThread != null)
            {
                clusterManagementThread.Shutdown();
                try
                {
                    clusterManagementThread.Join();
                }
                catch (ThreadInterruptedException)
                {
                }
            }

            try
            {
                ConnectionManager.Shutdown(DataSource);
            }
            catch (Exception sqle)
            {
                Log.Warn("Database connection Shutdown unsuccessful.", sqle);
            }
        }

        /// <summary>
        /// Indicates whether this job store supports persistence.
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        public virtual bool SupportsPersistence
        {
            get { return true; }
        }


        protected virtual void ReleaseLock(string lockName, bool doIt)
        {
            if (doIt)
            {
                try
                {
                    LockHandler.ReleaseLock(lockName);
                }
                catch (LockException le)
                {
                    Log.Error("Error returning lock: " + le.Message, le);
                }
            }
        }

        /// <summary>
        /// Will recover any failed or misfired jobs and clean up the data store as
        /// appropriate.
        /// </summary>
        protected virtual void RecoverJobs()
        {
            ExecuteInNonManagedTXLock(LockTriggerAccess, conn => RecoverJobs(conn));
        }

        /// <summary>
        /// Will recover any failed or misfired jobs and clean up the data store as
        /// appropriate.
        /// </summary>
        protected virtual void RecoverJobs(ConnectionAndTransactionHolder conn)
        {
            try
            {
                // update inconsistent job states
                int rows =
                    Delegate.UpdateTriggerStatesFromOtherStates(conn, StateWaiting, StateAcquired,
                                                                StateBlocked);

                rows +=
                    Delegate.UpdateTriggerStatesFromOtherStates(conn, StatePaused,
                                                                StatePausedBlocked,
                                                                StatePausedBlocked);

                Log.Info("Freed " + rows + " triggers from 'acquired' / 'blocked' state.");

                // clean up misfired jobs
                RecoverMisfiredJobs(conn, true);

                // recover jobs marked for recovery that were not fully executed
                IList<IOperableTrigger> recoveringJobTriggers = Delegate.SelectTriggersForRecoveringJobs(conn);
                Log.Info("Recovering " + recoveringJobTriggers.Count +
                         " jobs that were in-progress at the time of the last shut-down.");

                foreach (IOperableTrigger trigger in recoveringJobTriggers)
                {
                    if (JobExists(conn, trigger.JobKey))
                    {
                        trigger.ComputeFirstFireTimeUtc(null);
                        StoreTrigger(conn, trigger, null, false, StateWaiting, false, true);
                    }
                }
                Log.Info("Recovery complete.");

                // remove lingering 'complete' triggers...
                IList<TriggerKey> triggersInState = Delegate.SelectTriggersInState(conn, StateComplete);
                for (int i = 0; triggersInState != null && i < triggersInState.Count; i++)
                {
                    RemoveTrigger(conn, triggersInState[i]);
                }
                if (triggersInState != null)
                {
                    Log.Info(string.Format(CultureInfo.InvariantCulture, "Removed {0} 'complete' triggers.", triggersInState.Count));
                }

                // clean up any fired trigger entries
                int n = Delegate.DeleteFiredTriggers(conn);
                Log.Info("Removed " + n + " stale fired job entries.");
            }
            catch (JobPersistenceException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't recover jobs: " + e.Message, e);
            }
        }

        //private int lastRecoverCount = 0;


        public virtual RecoverMisfiredJobsResult RecoverMisfiredJobs(ConnectionAndTransactionHolder conn,
                                                                     bool recovering)
        {
            // If recovering, we want to handle all of the misfired
            // triggers right away.
            int maxMisfiresToHandleAtATime = (recovering) ? -1 : MaxMisfiresToHandleAtATime;

            IList<TriggerKey> misfiredTriggers = new List<TriggerKey>();
            DateTimeOffset earliestNewTime = DateTimeOffset.MaxValue;

            // We must still look for the MISFIRED state in case triggers were left 
            // in this state when upgrading to this version that does not support it. 
            bool hasMoreMisfiredTriggers =
                Delegate.HasMisfiredTriggersInState(conn, StateWaiting, MisfireTime,
                                                    maxMisfiresToHandleAtATime, misfiredTriggers);

            if (hasMoreMisfiredTriggers)
            {
                Log.Info(
                    "Handling the first " + misfiredTriggers.Count +
                    " triggers that missed their scheduled fire-time.  " +
                    "More misfired triggers remain to be processed.");
            }
            else if (misfiredTriggers.Count > 0)
            {
                Log.Info(
                    "Handling " + misfiredTriggers.Count +
                    " trigger(s) that missed their scheduled fire-time.");
            }
            else
            {
                Log.Debug(
                    "Found 0 triggers that missed their scheduled fire-time.");
                return RecoverMisfiredJobsResult.NoOp;
            }

            foreach (TriggerKey triggerKey in misfiredTriggers)
            {
                IOperableTrigger trig = RetrieveTrigger(conn, triggerKey);

                if (trig == null)
                {
                    continue;
                }

                DoUpdateOfMisfiredTrigger(conn, trig, false, StateWaiting, recovering);

                DateTimeOffset? nextTime = trig.GetNextFireTimeUtc();
                if (nextTime.HasValue && nextTime.Value < earliestNewTime)
                {
                    earliestNewTime = nextTime.Value;
                }
            }

            return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
        }


        protected virtual bool UpdateMisfiredTrigger(ConnectionAndTransactionHolder conn,
                                                     TriggerKey triggerKey, string newStateIfNotComplete,
                                                     bool forceState)
        {
            try
            {
                IOperableTrigger trig = RetrieveTrigger(conn, triggerKey);

                DateTimeOffset misfireTime = SystemTime.UtcNow();
                if (MisfireThreshold > TimeSpan.Zero)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
                }

                if (trig.GetNextFireTimeUtc().Value > misfireTime)
                {
                    return false;
                }

                DoUpdateOfMisfiredTrigger(conn, trig, forceState, newStateIfNotComplete, false);

                return true;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    string.Format("Couldn't update misfired trigger '{0}': {1}", triggerKey, e.Message), e);
            }
        }


        private void DoUpdateOfMisfiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trig,
                                               bool forceState, string newStateIfNotComplete, bool recovering)
        {
            ICalendar cal = null;
            if (trig.CalendarName != null)
            {
                cal = RetrieveCalendar(conn, trig.CalendarName);
            }

            schedSignaler.NotifyTriggerListenersMisfired(trig);

            trig.UpdateAfterMisfire(cal);

            if (!trig.GetNextFireTimeUtc().HasValue)
            {
                StoreTrigger(conn, trig, null, true, StateComplete, forceState, recovering);
                schedSignaler.NotifySchedulerListenersFinalized(trig);
            }
            else
            {
                StoreTrigger(conn, trig, null, true, newStateIfNotComplete, forceState, recovering);
            }
        }

        /// <summary>
        /// Store the given <see cref="IJobDetail" /> and <see cref="IOperableTrigger" />.
        /// </summary>
        /// <param name="newJob">Job to be stored.</param>
        /// <param name="newTrigger">Trigger to be stored.</param>
        public void StoreJobAndTrigger(IJobDetail newJob, IOperableTrigger newTrigger)
        {
            ExecuteInLock<object>((LockOnInsert) ? LockTriggerAccess : null, conn =>
                                                                         {
                                                                             StoreJob(conn, newJob, false);
                                                                             StoreTrigger(conn, newTrigger, newJob, false, StateWaiting, false, false);
                                                                             return null;
                                                                         });
        }

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public bool IsJobGroupPaused(string groupName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public bool IsTriggerGroupPaused(string groupName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Stores the given <see cref="IJobDetail" />.
        /// </summary>
        /// <param name="newJob">The <see cref="IJobDetail" /> to be stored.</param>
        /// <param name="replaceExisting">
        /// If <see langword="true" />, any <see cref="IJob" /> existing in the
        /// <see cref="IJobStore" /> with the same name &amp; group should be over-written.
        /// </param>
        public void StoreJob(IJobDetail newJob, bool replaceExisting)
        {
            ExecuteInLock(
                (LockOnInsert || replaceExisting) ? LockTriggerAccess : null,
                conn => StoreJob(conn, newJob, replaceExisting));
        }

        /// <summary> <para>
        /// Insert or update a job.
        /// </para>
        /// </summary>
        protected virtual void StoreJob(ConnectionAndTransactionHolder conn,
                                        IJobDetail newJob,
                                        bool replaceExisting)
        {
            bool existingJob = JobExists(conn, newJob.Key);
            try
            {
                if (existingJob)
                {
                    if (!replaceExisting)
                    {
                        throw new ObjectAlreadyExistsException(newJob);
                    }
                    Delegate.UpdateJobDetail(conn, newJob);
                }
                else
                {
                    Delegate.InsertJobDetail(conn, newJob);
                }
            }
            catch (IOException e)
            {
                throw new JobPersistenceException("Couldn't store job: " + e.Message, e);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't store job: " + e.Message, e);
            }
        }

        /// <summary>
        /// Check existence of a given job.
        /// </summary>
        protected virtual bool JobExists(ConnectionAndTransactionHolder conn, JobKey jobKey)
        {
            try
            {
                return Delegate.JobExists(conn, jobKey);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine job existence (" + jobKey + "): " + e.Message, e);
            }
        }


        /// <summary>
        /// Store the given <see cref="ITrigger" />.
        /// </summary>
        /// <param name="newTrigger">The <see cref="ITrigger" /> to be stored.</param>
        /// <param name="replaceExisting">
        /// If <see langword="true" />, any <see cref="ITrigger" /> existing in
        /// the <see cref="IJobStore" /> with the same name &amp; group should
        /// be over-written.
        /// </param>
        /// <exception cref="ObjectAlreadyExistsException">
        /// if a <see cref="ITrigger" /> with the same name/group already
        /// exists, and replaceExisting is set to false.
        /// </exception>
        public void StoreTrigger(IOperableTrigger newTrigger, bool replaceExisting)
        {
            ExecuteInLock(
                (LockOnInsert || replaceExisting) ? LockTriggerAccess : null,
                conn => StoreTrigger(conn, newTrigger, null, replaceExisting, StateWaiting, false, false));
        }

        /// <summary>
        /// Insert or update a trigger.
        /// </summary>
        protected virtual void StoreTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger newTrigger, IJobDetail job, bool replaceExisting, string state, bool forceState, bool recovering)
        {
            bool existingTrigger = TriggerExists(conn, newTrigger.Key);


            if ((existingTrigger) && (!replaceExisting))
            {
                throw new ObjectAlreadyExistsException(newTrigger);
            }

            try
            {
                if (!forceState)
                {
                    bool shouldBepaused = Delegate.IsTriggerGroupPaused(conn, newTrigger.Key.Group);

                    if (!shouldBepaused)
                    {
                        shouldBepaused = Delegate.IsTriggerGroupPaused(conn, AllGroupsPaused);

                        if (shouldBepaused)
                        {
                            Delegate.InsertPausedTriggerGroup(conn, newTrigger.Key.Group);
                        }
                    }

                    if (shouldBepaused &&
                        (state.Equals(StateWaiting) || state.Equals(StateAcquired)))
                    {
                        state = StatePaused;
                    }
                }

                if (job == null)
                {
                    job = RetrieveJob(conn, newTrigger.JobKey);
                }
                if (job == null)
                {
                    throw new JobPersistenceException("The job (" + newTrigger.JobKey +
                                                      ") referenced by the trigger does not exist.");
                }
                if (job.ConcurrentExecutionDisallowed && !recovering)
                {
                    state = CheckBlockedState(conn, job.Key, state);
                }
                if (existingTrigger)
                {
                    Delegate.UpdateTrigger(conn, newTrigger, state, job);
                }
                else
                {
                    Delegate.InsertTrigger(conn, newTrigger, state, job);
                }
            }
            catch (Exception e)
            {
                string message = String.Format("Couldn't store trigger '{0}' for '{1}' job: {2}", newTrigger.Key, newTrigger.JobKey, e.Message);
                throw new JobPersistenceException(message, e);
            }
        }

        /// <summary>
        /// Check existence of a given trigger.
        /// </summary>
        protected virtual bool TriggerExists(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            try
            {
                return Delegate.TriggerExists(conn, triggerKey);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine trigger existence (" + triggerKey + "): " + e.Message, e);
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
        public bool RemoveJob(JobKey jobKey)
        {
            return ExecuteInLock(LockTriggerAccess, conn => RemoveJob(conn, jobKey, true));
        }


        protected virtual bool RemoveJob(ConnectionAndTransactionHolder conn,
                                         JobKey jobKey, bool activeDeleteSafe)
        {
            try
            {
                IList<TriggerKey> jobTriggers = Delegate.SelectTriggerNamesForJob(conn, jobKey);

                foreach (TriggerKey jobTrigger in jobTriggers)
                {
                    DeleteTriggerAndChildren(conn, jobTrigger);
                }

                return DeleteJobAndChildren(conn, jobKey);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't remove job: " + e.Message, e);
            }
        }


        public bool RemoveJobs(IList<JobKey> jobKeys)
        {
            return ExecuteInLock(
                LockTriggerAccess,
                conn =>
                {
                    bool allFound = true;

                    // TODO: make this more efficient with a true bulk operation...
                    foreach (JobKey jobKey in jobKeys)
                    {
                        allFound = RemoveJob(conn, jobKey, true) && allFound;
                    }

                    return allFound;
                });
        }

        public bool RemoveTriggers(IList<TriggerKey> triggerKeys)
        {
            return ExecuteInLock(
                LockTriggerAccess,
                conn =>
                {
                    bool allFound = true;

                    // TODO: make this more efficient with a true bulk operation...
                    foreach (TriggerKey triggerKey in triggerKeys)
                    {
                        allFound = RemoveTrigger(conn, triggerKey) && allFound;
                    }

                    return allFound;
                });
        }

        public void StoreJobsAndTriggers(IDictionary<IJobDetail, Collection.ISet<ITrigger>> triggersAndJobs, bool replace)
        {
            ExecuteInLock(
                (LockOnInsert || replace) ? LockTriggerAccess : null,
                delegate(ConnectionAndTransactionHolder conn)
                {
                    // TODO: make this more efficient with a true bulk operation...
                    foreach (IJobDetail job in triggersAndJobs.Keys)
                    {
                        StoreJob(conn, job, replace);
                        foreach (ITrigger trigger in triggersAndJobs[job])
                        {
                            StoreTrigger(conn, (IOperableTrigger)trigger, job, replace, StateWaiting, false, false);
                        }
                    }
                });
        }



        /// <summary>
        /// Delete a job and its listeners.
        /// </summary>
        /// <seealso cref="JobStoreSupport.RemoveJob(ConnectionAndTransactionHolder, JobKey, bool)" />
        /// <seealso cref="RemoveTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail)" />
        private bool DeleteJobAndChildren(ConnectionAndTransactionHolder conn, JobKey key)
        {
            return (Delegate.DeleteJobDetail(conn, key) > 0);
        }

        /// <summary>
        /// Delete a trigger, its listeners, and its Simple/Cron/BLOB sub-table entry.
        /// </summary>
        /// <seealso cref="RemoveJob(ConnectionAndTransactionHolder, JobKey, bool)" />
        /// <seealso cref="RemoveTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail)" />
        /// <seealso cref="ReplaceTrigger(ConnectionAndTransactionHolder, TriggerKey, IOperableTrigger)" />
        private bool DeleteTriggerAndChildren(ConnectionAndTransactionHolder conn, TriggerKey key)
        {
            IDriverDelegate del = Delegate;
            return (del.DeleteTrigger(conn, key) > 0);
        }

        /// <summary>
        /// Retrieve the <see cref="IJobDetail" /> for the given
        /// <see cref="IJob" />.
        /// </summary>
        /// <param name="jobKey">The key identifying the job.</param>
        /// <returns>The desired <see cref="IJob" />, or null if there is no match.</returns>
        public IJobDetail RetrieveJob(JobKey jobKey)
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => RetrieveJob(conn, jobKey));
        }

        protected virtual IJobDetail RetrieveJob(ConnectionAndTransactionHolder conn, JobKey jobKey)
        {
            try
            {
                IJobDetail job = Delegate.SelectJobDetail(conn, jobKey, TypeLoadHelper);
                return job;
            }
            catch (TypeLoadException e)
            {
                throw new JobPersistenceException("Couldn't retrieve job because a required type was not found: " + e.Message, e);
            }
            catch (IOException e)
            {
                throw new JobPersistenceException("Couldn't retrieve job because the BLOB couldn't be deserialized: " + e.Message, e);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't retrieve job: " + e.Message, e);
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
        /// <returns>
        /// <see langword="true" /> if a <see cref="ITrigger" /> with the given
        /// name &amp; group was found and removed from the store.
        ///</returns>
        public bool RemoveTrigger(TriggerKey triggerKey)
        {
            return ExecuteInLock(LockTriggerAccess, conn => RemoveTrigger(conn, triggerKey));
        }


        protected virtual bool RemoveTrigger(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            return RemoveTrigger(conn, triggerKey, null);
        }

        protected virtual bool RemoveTrigger(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, IJobDetail job)
        {
            bool removedTrigger;
            try
            {
                // this must be called before we delete the trigger, obviously
                // we use fault tolerant type loading as we only want to delete things
                if (job == null)
                {
                    job = Delegate.SelectJobForTrigger(conn, triggerKey, new NoOpJobTypeLoader(), false);
                }

                removedTrigger = DeleteTriggerAndChildren(conn, triggerKey);

                if (null != job && !job.Durable)
                {
                    int numTriggers = Delegate.SelectNumTriggersForJob(conn, job.Key);
                    if (numTriggers == 0)
                    {
                        // Don't call RemoveJob() because we don't want to check for
                        // triggers again.
                        if (DeleteJobAndChildren(conn, job.Key))
                        {
                            schedSignaler.NotifySchedulerListenersJobDeleted(job.Key);
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

        private class NoOpJobTypeLoader : ITypeLoadHelper
        {
            public void Initialize()
            {
            }

            public Type LoadType(string name)
            {
                return typeof(NoOpJob);
            }

            public Uri GetResource(string name)
            {
                return null;
            }

            public Stream GetResourceAsStream(string name)
            {
                return null;
            }
        }

        /// <see cref="IJobStore.ReplaceTrigger(TriggerKey, IOperableTrigger)" />
        public bool ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger)
        {
            return
                ExecuteInLock(LockTriggerAccess, conn => ReplaceTrigger(conn, triggerKey, newTrigger));
        }

        protected virtual bool ReplaceTrigger(ConnectionAndTransactionHolder conn,
                                             TriggerKey triggerKey, IOperableTrigger newTrigger)
        {
            try
            {
                // this must be called before we delete the trigger, obviously
                IJobDetail job = Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoadHelper);

                if (job == null)
                {
                    return false;
                }

                if (!newTrigger.JobKey.Equals(job.Key))
                {
                    throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                }

                bool removedTrigger = DeleteTriggerAndChildren(conn, triggerKey);

                StoreTrigger(conn, newTrigger, job, false, StateWaiting, false, false);

                return removedTrigger;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
            }
        }

        /// <summary>
        /// Retrieve the given <see cref="ITrigger" />.
        /// </summary>
        /// <param name="triggerKey">The key identifying the trigger.</param>
        /// <returns>The desired <see cref="ITrigger" />, or null if there is no match.</returns>
        public IOperableTrigger RetrieveTrigger(TriggerKey triggerKey)
        {
            return ExecuteWithoutLock( // no locks necessary for read...
                                 conn => RetrieveTrigger(conn, triggerKey));
        }

        protected virtual IOperableTrigger RetrieveTrigger(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            try
            {
                IOperableTrigger trigger = Delegate.SelectTrigger(conn, triggerKey);
                return trigger;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't retrieve trigger: " + e.Message, e);
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
        public TriggerState GetTriggerState(TriggerKey triggerKey)
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetTriggerState(conn, triggerKey));
        }

        /// <summary>
        /// Gets the state of the trigger.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="triggerKey">The key identifying the trigger.</param>
        /// <returns></returns>
        public virtual TriggerState GetTriggerState(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            try
            {
                string ts = Delegate.SelectTriggerState(conn, triggerKey);

                if (ts == null)
                {
                    return TriggerState.None;
                }

                if (ts.Equals(StateDeleted))
                {
                    return TriggerState.None;
                }

                if (ts.Equals(StateComplete))
                {
                    return TriggerState.Complete;
                }

                if (ts.Equals(StatePaused))
                {
                    return TriggerState.Paused;
                }

                if (ts.Equals(StatePausedBlocked))
                {
                    return TriggerState.Paused;
                }

                if (ts.Equals(StateError))
                {
                    return TriggerState.Error;
                }

                if (ts.Equals(StateBlocked))
                {
                    return TriggerState.Blocked;
                }

                return TriggerState.Normal;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine state of trigger (" + triggerKey + "): " + e.Message, e);
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
        /// <exception cref="ObjectAlreadyExistsException">
        ///           if a <see cref="ICalendar" /> with the same name already
        ///           exists, and replaceExisting is set to false.
        /// </exception>
        public void StoreCalendar(string calName, ICalendar calendar, bool replaceExisting,
                                  bool updateTriggers)
        {
            ExecuteInLock(
                (LockOnInsert || updateTriggers) ? LockTriggerAccess : null,
                conn => StoreCalendar(conn, calName, calendar, replaceExisting, updateTriggers));
        }

        protected virtual void StoreCalendar(ConnectionAndTransactionHolder conn,
                                             string calName,
                                             ICalendar calendar, bool replaceExisting, bool updateTriggers)
        {
            try
            {
                bool existingCal = CalendarExists(conn, calName);
                if (existingCal && !replaceExisting)
                {
                    throw new ObjectAlreadyExistsException("Calendar with name '" + calName + "' already exists.");
                }

                if (existingCal)
                {
                    if (Delegate.UpdateCalendar(conn, calName, calendar) < 1)
                    {
                        throw new JobPersistenceException("Couldn't store calendar.  Update failed.");
                    }

                    if (updateTriggers)
                    {
                        IList<IOperableTrigger> triggers = Delegate.SelectTriggersForCalendar(conn, calName);

                        foreach (IOperableTrigger trigger in triggers)
                        {
                            trigger.UpdateWithNewCalendar(calendar, MisfireThreshold);
                            StoreTrigger(conn, trigger, null, true, StateWaiting, false, false);
                        }
                    }
                }
                else
                {
                    if (Delegate.InsertCalendar(conn, calName, calendar) < 1)
                    {
                        throw new JobPersistenceException("Couldn't store calendar.  Insert failed.");
                    }
                }

                if (!Clustered)
                {
                    calendarCache[calName] = calendar; // lazy-cache
                }
            }
            catch (IOException e)
            {
                throw new JobPersistenceException(
                    "Couldn't store calendar because the BLOB couldn't be serialized: " + e.Message, e);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't store calendar: " + e.Message, e);
            }
        }


        protected virtual bool CalendarExists(ConnectionAndTransactionHolder conn, string calName)
        {
            try
            {
                return Delegate.CalendarExists(conn, calName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine calendar existence (" + calName + "): " + e.Message, e);
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
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be removed.</param>
        /// <returns>
        /// <see langword="true" /> if a <see cref="ICalendar" /> with the given name
        /// was found and removed from the store.
        ///</returns>
        public bool RemoveCalendar(string calName)
        {
            return ExecuteInLock(LockTriggerAccess, conn => RemoveCalendar(conn, calName));
        }

        protected virtual bool RemoveCalendar(ConnectionAndTransactionHolder conn,
                                              string calName)
        {
            try
            {
                if (Delegate.CalendarIsReferenced(conn, calName))
                {
                    throw new JobPersistenceException("Calender cannot be removed if it referenced by a trigger!");
                }

                if (!Clustered)
                {
                    calendarCache.Remove(calName);
                }

                return (Delegate.DeleteCalendar(conn, calName) > 0);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't remove calendar: " + e.Message, e);
            }
        }

        /// <summary>
        /// Retrieve the given <see cref="ITrigger" />.
        /// </summary>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
        /// <returns>The desired <see cref="ICalendar" />, or null if there is no match.</returns>
        public ICalendar RetrieveCalendar(string calName)
        {
            return ExecuteWithoutLock( // no locks necessary for read...
                                   conn => RetrieveCalendar(conn, calName));
        }

        protected virtual ICalendar RetrieveCalendar(ConnectionAndTransactionHolder conn, string calName)
        {
            // all calendars are persistent, but we lazy-cache them during run
            // time as long as we aren't running clustered.
            ICalendar cal = null;
            if (!Clustered)
            {
                calendarCache.TryGetValue(calName, out cal);
            }
            if (cal != null)
            {
                return cal;
            }

            try
            {
                cal = Delegate.SelectCalendar(conn, calName);
                if (!Clustered)
                {
                    calendarCache[calName] = cal; // lazy-cache...
                }
                return cal;
            }
            catch (IOException e)
            {
                throw new JobPersistenceException(
                    "Couldn't retrieve calendar because the BLOB couldn't be deserialized: " + e.Message, e);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't retrieve calendar: " + e.Message, e);
            }
        }


        /// <summary>
        /// Get the number of <see cref="IJob" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        public int GetNumberOfJobs()
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetNumberOfJobs(conn));
        }

        protected virtual int GetNumberOfJobs(ConnectionAndTransactionHolder conn)
        {
            try
            {
                return Delegate.SelectNumJobs(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain number of jobs: " + e.Message, e);
            }
        }

        /// <summary>
        /// Get the number of <see cref="ITrigger" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        public int GetNumberOfTriggers()
        {
            return ExecuteWithoutLock( // no locks necessary for read...
                             conn => GetNumberOfTriggers(conn));
        }

        protected virtual int GetNumberOfTriggers(ConnectionAndTransactionHolder conn)
        {
            try
            {
                return Delegate.SelectNumTriggers(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain number of triggers: " + e.Message, e);
            }
        }

        /// <summary>
        /// Get the number of <see cref="ICalendar" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        public int GetNumberOfCalendars()
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetNumberOfCalendars(conn));
        }

        protected virtual int GetNumberOfCalendars(ConnectionAndTransactionHolder conn)
        {
            try
            {
                return Delegate.SelectNumCalendars(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain number of calendars: " + e.Message, e);
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
        public Collection.ISet<JobKey> GetJobKeys(GroupMatcher<JobKey> matcher)
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetJobNames(conn, matcher));
        }

        protected virtual Collection.ISet<JobKey> GetJobNames(ConnectionAndTransactionHolder conn, GroupMatcher<JobKey> matcher)
        {
            Collection.ISet<JobKey> jobNames;

            try
            {
                jobNames = Delegate.SelectJobsInGroup(conn, matcher);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain job names: " + e.Message, e);
            }

            return jobNames;
        }

        /// <summary>
        /// Determine whether a <see cref="ICalendar" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="calName">the identifier to check for</param>
        /// <returns>true if a calendar exists with the given identifier</returns>
        public bool CalendarExists(string calName)
        {
            return ExecuteWithoutLock( // no locks necessary for read...
                              conn => CheckExists(conn, calName));
        }

        protected bool CheckExists(ConnectionAndTransactionHolder conn, string calName)
        {
            try
            {
                return Delegate.CalendarExists(conn, calName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't check for existence of job: " + e.Message, e);
            }
        }

        /// <summary>
        /// Determine whether a <see cref="IJob"/> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobKey">the identifier to check for</param>
        /// <returns>true if a Job exists with the given identifier</returns>
        public bool CheckExists(JobKey jobKey)
        {
            return ExecuteWithoutLock( // no locks necessary for read...
                              conn => CheckExists(conn, jobKey));
        }

        protected bool CheckExists(ConnectionAndTransactionHolder conn, JobKey jobKey)
        {
            try
            {
                return Delegate.JobExists(conn, jobKey);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't check for existence of job: " + e.Message, e);
            }
        }

        /// <summary>
        /// Determine whether a <see cref="ITrigger" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="triggerKey">the identifier to check for</param>
        /// <returns>true if a Trigger exists with the given identifier</returns>
        public bool CheckExists(TriggerKey triggerKey)
        {
            return ExecuteWithoutLock( // no locks necessary for read...
                              conn => CheckExists(conn, triggerKey));
        }

        protected bool CheckExists(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            try
            {
                return Delegate.TriggerExists(conn, triggerKey);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't check for existence of job: " + e.Message, e);
            }
        }

        /// <summary>
        /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
        /// <see cref="ICalendar" />s.
        /// </summary>
        /// <remarks>
        /// </remarks>
        public void ClearAllSchedulingData()
        {
            ExecuteInLock(LockTriggerAccess, conn => ClearAllSchedulingData(conn));
        }

        protected void ClearAllSchedulingData(ConnectionAndTransactionHolder conn)
        {
            try
            {
                Delegate.ClearData(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Error clearing scheduling data: " + e.Message, e);
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
        public Collection.ISet<TriggerKey> GetTriggerKeys(GroupMatcher<TriggerKey> matcher)
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetTriggerNames(conn, matcher));
        }

        protected virtual Collection.ISet<TriggerKey> GetTriggerNames(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher)
        {
            Collection.ISet<TriggerKey> triggerNames;

            try
            {
                triggerNames = Delegate.SelectTriggersInGroup(conn, matcher);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain trigger names: " + e.Message, e);
            }

            return triggerNames;
        }


        /// <summary>
        /// Get the names of all of the <see cref="IJob" />
        /// groups.
        /// </summary>
        /// 
        /// <remarks>
        /// If there are no known group names, the result should be a zero-length
        /// array (not <see langword="null" />).
        /// </remarks>
        public IList<string> GetJobGroupNames()
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetJobGroupNames(conn));
        }

        protected virtual IList<string> GetJobGroupNames(ConnectionAndTransactionHolder conn)
        {
            IList<string> groupNames;

            try
            {
                groupNames = Delegate.SelectJobGroups(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain job groups: " + e.Message, e);
            }

            return groupNames;
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
        public IList<string> GetTriggerGroupNames()
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetTriggerGroupNames(conn));
        }

        protected virtual IList<string> GetTriggerGroupNames(ConnectionAndTransactionHolder conn)
        {
            IList<string> groupNames;

            try
            {
                groupNames = Delegate.SelectTriggerGroups(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
            }

            return groupNames;
        }


        /// <summary>
        /// Get the names of all of the <see cref="ICalendar" /> s
        /// in the <see cref="IJobStore" />.
        /// </summary>
        /// <remarks>
        /// If there are no Calendars in the given group name, the result should be
        /// a zero-length array (not <see langword="null" />).
        /// </remarks>
        public IList<string> GetCalendarNames()
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetCalendarNames(conn));
        }

        protected virtual IList<string> GetCalendarNames(ConnectionAndTransactionHolder conn)
        {
            try
            {
                return Delegate.SelectCalendars(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
            }
        }


        /// <summary>
        /// Get all of the Triggers that are associated to the given Job.
        /// </summary>
        /// <remarks>
        /// If there are no matches, a zero-length array should be returned.
        /// </remarks>
        public IList<IOperableTrigger> GetTriggersForJob(JobKey jobKey)
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetTriggersForJob(conn, jobKey));
        }

        protected virtual IList<IOperableTrigger> GetTriggersForJob(ConnectionAndTransactionHolder conn, JobKey jobKey)
        {
            IList<IOperableTrigger> array;

            try
            {
                array = Delegate.SelectTriggersForJob(conn, jobKey);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain triggers for job: " + e.Message, e);
            }

            return array;
        }

        /// <summary>
        /// Pause the <see cref="ITrigger" /> with the given name.
        /// </summary>
        public void PauseTrigger(TriggerKey triggerKey)
        {
            ExecuteInLock(LockTriggerAccess, conn => PauseTrigger(conn, triggerKey));
        }

        /// <summary>
        /// Pause the <see cref="ITrigger" /> with the given name.
        /// </summary>
        public virtual void PauseTrigger(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            try
            {
                string oldState = Delegate.SelectTriggerState(conn, triggerKey);

                if (oldState.Equals(StateWaiting) || oldState.Equals(StateAcquired))
                {
                    Delegate.UpdateTriggerState(conn, triggerKey, StatePaused);
                }
                else if (oldState.Equals(StateBlocked))
                {
                    Delegate.UpdateTriggerState(conn, triggerKey, StatePausedBlocked);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't pause trigger '" + triggerKey + "': " + e.Message, e);
            }
        }


        /// <summary>
        /// Pause the <see cref="IJob" /> with the given name - by
        /// pausing all of its current <see cref="ITrigger" />s.
        /// </summary>
        /// <seealso cref="ResumeJob(JobKey)" />
        public virtual void PauseJob(JobKey jobKey)
        {
            ExecuteInLock(LockTriggerAccess,
                conn =>
                {
                    IList<IOperableTrigger> triggers = GetTriggersForJob(conn, jobKey);
                    foreach (IOperableTrigger trigger in triggers)
                    {
                        PauseTrigger(conn, trigger.Key);
                    }
                });
        }

        /// <summary>
        /// Pause all of the <see cref="IJob" />s in the given
        /// group - by pausing all of their <see cref="ITrigger" />s.
        /// </summary>
        /// <seealso cref="ResumeJobs" />
        public virtual IList<string> PauseJobs(GroupMatcher<JobKey> matcher)
        {
            return ExecuteInLock(LockTriggerAccess, conn =>
                {
                    Collection.ISet<string> groupNames = new Collection.HashSet<string>();
                    Collection.ISet<JobKey> jobNames = GetJobNames(conn, matcher);

                    foreach (JobKey jobKey in jobNames)
                    {
                        IList<IOperableTrigger> triggers = GetTriggersForJob(conn, jobKey);
                        foreach (IOperableTrigger trigger in triggers)
                        {
                            PauseTrigger(conn, trigger.Key);
                        }
                        groupNames.Add(jobKey.Group);
                    }

                    return new List<string>(groupNames);
                });
        }

        /// <summary>
        /// Determines if a Trigger for the given job should be blocked.  
        /// State can only transition to StatePausedBlocked/StateBlocked from 
        /// StatePaused/StateWaiting respectively.
        /// </summary>
        /// <returns>StatePausedBlocked, StateBlocked, or the currentState. </returns>
        protected virtual string CheckBlockedState(ConnectionAndTransactionHolder conn, JobKey jobKey, string currentState)
        {
            // State can only transition to BLOCKED from PAUSED or WAITING.
            if ((currentState.Equals(StateWaiting) == false) &&
                (currentState.Equals(StatePaused) == false))
            {
                return currentState;
            }

            try
            {
                IList<FiredTriggerRecord> lst = Delegate.SelectFiredTriggerRecordsByJob(conn, jobKey.Name, jobKey.Group);

                if (lst.Count > 0)
                {
                    FiredTriggerRecord rec = lst[0];
                    if (rec.JobDisallowsConcurrentExecution) // TODO: worry about failed/recovering/volatile job  states?
                    {
                        return (StatePaused.Equals(currentState)) ? StatePausedBlocked : StateBlocked;
                    }
                }

                return currentState;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine if trigger should be in a blocked state '" + jobKey + "': " + e.Message, e);
            }
        }

        public virtual void ResumeTrigger(TriggerKey triggerKey)
        {
            ExecuteInLock(LockTriggerAccess, conn => ResumeTrigger(conn, triggerKey));
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="ITrigger" /> with the
        /// given name.
        /// </summary>
        /// <remarks>
        /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </remarks>
        public virtual void ResumeTrigger(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            try
            {
                TriggerStatus status = Delegate.SelectTriggerStatus(conn, triggerKey);

                if (status == null || !status.NextFireTimeUtc.HasValue || status.NextFireTimeUtc == DateTimeOffset.MinValue)
                {
                    return;
                }

                bool blocked = StatePausedBlocked.Equals(status.Status);

                string newState = CheckBlockedState(conn, status.JobKey, StateWaiting);

                bool misfired = false;

                if (schedulerRunning && status.NextFireTimeUtc.Value < SystemTime.UtcNow())
                {
                    misfired = UpdateMisfiredTrigger(conn, triggerKey, newState, true);
                }

                if (!misfired)
                {
                    if (blocked)
                    {
                        Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StatePausedBlocked);
                    }
                    else
                    {
                        Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StatePaused);
                    }
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't resume trigger '" + triggerKey + "': " + e.Message, e);
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
        /// <seealso cref="PauseJob(JobKey)" />
        public virtual void ResumeJob(JobKey jobKey)
        {
            ExecuteInLock(LockTriggerAccess, conn =>
                                                 {
                                                     IList<IOperableTrigger> triggers = GetTriggersForJob(conn, jobKey);
                                                     foreach (IOperableTrigger trigger in triggers)
                                                     {
                                                         ResumeTrigger(conn, trigger.Key);
                                                     }
                                                 });
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
        public virtual Collection.ISet<string> ResumeJobs(GroupMatcher<JobKey> matcher)
        {
            return ExecuteInLock(LockTriggerAccess, conn =>
                {
                    Collection.ISet<JobKey> jobKeys = GetJobNames(conn, matcher);
                    Collection.ISet<String> groupNames = new Collection.HashSet<string>();

                    foreach (JobKey jobKey in jobKeys)
                    {
                        IList<IOperableTrigger> triggers = GetTriggersForJob(conn, jobKey);
                        foreach (IOperableTrigger trigger in triggers)
                        {
                            ResumeTrigger(conn, trigger.Key);
                        }
                        groupNames.Add(jobKey.Group);
                    }
                    return groupNames;
                });
        }

        /// <summary>
        /// Pause all of the <see cref="ITrigger" />s in the given group.
        /// </summary>
        /// <seealso cref="ResumeTriggers(Quartz.Impl.Matchers.GroupMatcher{Quartz.TriggerKey})" />
        public virtual Collection.ISet<string> PauseTriggers(GroupMatcher<TriggerKey> matcher)
        {
            return ExecuteInLock(LockTriggerAccess, conn => PauseTriggerGroup(conn, matcher));
        }

        /// <summary>
        /// Pause all of the <see cref="ITrigger" />s in the given group.
        /// </summary>
        public virtual Collection.ISet<string> PauseTriggerGroup(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher)
        {
            try
            {
                Delegate.UpdateTriggerGroupStateFromOtherStates(conn, matcher, StatePaused,
                                                                StateAcquired, StateWaiting,
                                                                StateWaiting);

                Delegate.UpdateTriggerGroupStateFromOtherState(conn, matcher, StatePausedBlocked,
                                                               StateBlocked);

                IList<String> groups = Delegate.SelectTriggerGroups(conn, matcher);

                // make sure to account for an exact group match for a group that doesn't yet exist
                StringOperator op = matcher.CompareWithOperator;
                if (op.Equals(StringOperator.Equality) && !groups.Contains(matcher.CompareToValue))
                {
                    groups.Add(matcher.CompareToValue);
                }

                foreach (string group in groups)
                {
                    if (!Delegate.IsTriggerGroupPaused(conn, group))
                    {
                        Delegate.InsertPausedTriggerGroup(conn, group);
                    }
                }

                return new Collection.HashSet<string>(groups);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't pause trigger group '" + matcher + "': " + e.Message, e);
            }
        }


        public Collection.ISet<string> GetPausedTriggerGroups()
        {
            // no locks necessary for read...
            return ExecuteWithoutLock(conn => GetPausedTriggerGroups(conn));
        }

        /// <summary> 
        /// Pause all of the <see cref="ITrigger" />s in the
        /// given group.
        /// </summary>
        public virtual Collection.ISet<string> GetPausedTriggerGroups(ConnectionAndTransactionHolder conn)
        {
            try
            {
                return Delegate.SelectPausedTriggerGroups(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't determine paused trigger groups: " + e.Message, e);
            }
        }


        public virtual IList<string> ResumeTriggers(GroupMatcher<TriggerKey> matcher)
        {
            return ExecuteInLock(LockTriggerAccess, conn => ResumeTriggers(conn, matcher));
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="ITrigger" />s
        /// in the given group.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual IList<string> ResumeTriggers(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher)
        {
            try
            {
                Delegate.DeletePausedTriggerGroup(conn, matcher);
                Collection.HashSet<string> groups = new Collection.HashSet<string>();

                Collection.ISet<TriggerKey> keys = Delegate.SelectTriggersInGroup(conn, matcher);

                foreach (TriggerKey key in keys)
                {
                    ResumeTrigger(conn, key);
                    groups.Add(key.Group);
                }

                return new List<string>(groups);

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
                throw new JobPersistenceException("Couldn't pause trigger group '" + matcher + "': " + e.Message, e);
            }
        }

        public virtual void PauseAll()
        {
            ExecuteInLock(LockTriggerAccess, conn => PauseAll(conn));
        }

        /// <summary>
        /// Pause all triggers - equivalent of calling <see cref="PauseTriggers(GroupMatcher{TriggerKey})" />
        /// on every group.
        /// <para>
        /// When <see cref="ResumeAll()" /> is called (to un-pause), trigger misfire
        /// instructions WILL be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="ResumeAll()" />
        /// <seealso cref="String" />
        public virtual void PauseAll(ConnectionAndTransactionHolder conn)
        {
            IList<string> groupNames = GetTriggerGroupNames(conn);

            foreach (string groupName in groupNames)
            {
                PauseTriggerGroup(conn, GroupMatcher<TriggerKey>.GroupEquals(groupName));
            }

            try
            {
                if (!Delegate.IsTriggerGroupPaused(conn, AllGroupsPaused))
                {
                    Delegate.InsertPausedTriggerGroup(conn, AllGroupsPaused);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't pause all trigger groups: " + e.Message, e);
            }
        }


        /// <summary>
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(Quartz.Impl.Matchers.GroupMatcher{Quartz.TriggerKey})" />
        /// on every group.
        /// </summary>
        /// <remarks>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </remarks>
        /// <seealso cref="PauseAll()" />
        public virtual void ResumeAll()
        {
            ExecuteInLock(LockTriggerAccess, conn => ResumeAll(conn));
        }

        /// <summary>
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(Quartz.Impl.Matchers.GroupMatcher{Quartz.TriggerKey})" />
        /// on every group.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="PauseAll()" />
        public virtual void ResumeAll(ConnectionAndTransactionHolder conn)
        {
            IList<string> triggerGroupNames = GetTriggerGroupNames(conn);

            foreach (string groupName in triggerGroupNames)
            {
                ResumeTriggers(conn, GroupMatcher<TriggerKey>.GroupEquals(groupName));
            }

            try
            {
                Delegate.DeletePausedTriggerGroup(conn, AllGroupsPaused);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't resume all trigger groups: " + e.Message, e);
            }
        }

        private static long ftrCtr = SystemTime.UtcNow().Ticks;


        /// <summary>
        /// Get a handle to the next N triggers to be fired, and mark them as 'reserved'
        /// by the calling scheduler.
        /// </summary>
        /// <seealso cref="ReleaseAcquiredTrigger(IOperableTrigger)" />
        public virtual IList<IOperableTrigger> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow)
        {
            string lockName;
            if (AcquireTriggersWithinLock || maxCount > 1)
            {
                lockName = LockTriggerAccess;
            }
            else
            {
                lockName = null;
            }

            return ExecuteInNonManagedTXLock(lockName,
                conn => AcquireNextTrigger(conn, noLaterThan, maxCount, timeWindow),
                (conn, result) =>
                {
                    try
                    {
                        IList<FiredTriggerRecord> acquired = Delegate.SelectInstancesFiredTriggerRecords(conn, InstanceId);
                        var fireInstanceIds = new HashSet<string>();
                        foreach (FiredTriggerRecord ft in acquired)
                        {
                            fireInstanceIds.Add(ft.FireInstanceId);
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
                        throw new JobPersistenceException("error validating trigger acquisition", e);
                    }
                });
        }

        // TODO: this really ought to return something like a FiredTriggerBundle,
        // so that the fireInstanceId doesn't have to be on the trigger...

        protected virtual IList<IOperableTrigger> AcquireNextTrigger(ConnectionAndTransactionHolder conn, DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow)
        {
            if (timeWindow < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("timeWindow");
            }

            List<IOperableTrigger> acquiredTriggers = new List<IOperableTrigger>();
            Collection.ISet<JobKey> acquiredJobKeysForNoConcurrentExec = new Collection.HashSet<JobKey>();
            const int MaxDoLoopRetry = 3;
            int currentLoopCount = 0;

            do
            {
                currentLoopCount++;
                try
                {
                    IList<TriggerKey> keys = Delegate.SelectTriggerToAcquire(conn, noLaterThan + timeWindow, MisfireTime, maxCount);

                    // No trigger is ready to fire yet.
                    if (keys == null || keys.Count == 0)
                    {
                        return acquiredTriggers;
                    }

                    DateTimeOffset batchEnd = noLaterThan;

                    foreach (TriggerKey triggerKey in keys)
                    {
                        // If our trigger is no longer available, try a new one.
                        IOperableTrigger nextTrigger = RetrieveTrigger(conn, triggerKey);
                        if (nextTrigger == null)
                        {
                            continue; // next trigger
                        }

                        // If trigger's job is set as @DisallowConcurrentExecution, and it has already been added to result, then
                        // put it back into the timeTriggers set and continue to search for next trigger.
                        JobKey jobKey = nextTrigger.JobKey;
                        IJobDetail job;
                        try
                        {
                            job = RetrieveJob(conn, jobKey);
                        }
                        catch (JobPersistenceException jpe)
                        {
                            try
                            {
                                Log.Error("Error retrieving job, setting trigger state to ERROR.", jpe);
                                Delegate.UpdateTriggerState(conn, triggerKey, StateError);
                            }
                            catch (Exception ex)
                            {
                                Log.Error("Unable to set trigger state to ERROR.", ex);
                            }
                            continue;
                        }

                        if (job.ConcurrentExecutionDisallowed)
                        {
                            if (acquiredJobKeysForNoConcurrentExec.Contains(jobKey))
                            {
                                continue; // next trigger
                            }
                            else
                            {
                                acquiredJobKeysForNoConcurrentExec.Add(jobKey);
                            }
                        }

                        if (nextTrigger.GetNextFireTimeUtc() > batchEnd)
                        {
                            break;
                        }

                        // We now have a acquired trigger, let's add to return list.
                        // If our trigger was no longer in the expected state, try a new one.
                        int rowsUpdated = Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, StateAcquired, StateWaiting);
                        if (rowsUpdated <= 0)
                        {
                            // TODO: Hum... shouldn't we log a warning here?
                            continue; // next trigger
                        }
                        nextTrigger.FireInstanceId = GetFiredTriggerRecordId();
                        Delegate.InsertFiredTrigger(conn, nextTrigger, StateAcquired, null);

                        if (acquiredTriggers.Count == 0)
                        {
                            var now = SystemTime.UtcNow();
                            var nextFireTime = nextTrigger.GetNextFireTimeUtc().GetValueOrDefault(DateTimeOffset.MinValue);
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
                    throw new JobPersistenceException("Couldn't acquire next trigger: " + e.Message, e);
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
        public void ReleaseAcquiredTrigger(IOperableTrigger trigger)
        {
            RetryExecuteInNonManagedTXLock(LockTriggerAccess, conn => ReleaseAcquiredTrigger(conn, trigger));
        }

        protected virtual void ReleaseAcquiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trigger)
        {
            try
            {
                Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, StateWaiting, StateAcquired);
                Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't release acquired trigger: " + e.Message, e);
            }
        }


        public virtual IList<TriggerFiredResult> TriggersFired(IList<IOperableTrigger> triggers)
        {
            return ExecuteInNonManagedTXLock(LockTriggerAccess,
                conn =>
                {
                    List<TriggerFiredResult> results = new List<TriggerFiredResult>();

                    TriggerFiredResult result;
                    foreach (IOperableTrigger trigger in triggers)
                    {
                        try
                        {
                            TriggerFiredBundle bundle = TriggerFired(conn, trigger);
                            result = new TriggerFiredResult(bundle);
                        }
                        catch (JobPersistenceException jpe)
                        {
                            log.ErrorFormat("Caught job persistence exception: " + jpe.Message, jpe);
                            result = new TriggerFiredResult(jpe);
                        }
                        catch (Exception ex)
                        {
                            log.ErrorFormat("Caught exception: " + ex.Message, ex);
                            result = new TriggerFiredResult(ex);
                        }
                        results.Add(result);
                    }

                    return results;
                },
                (conn, result) =>
                {
                    try
                    {
                        IList<FiredTriggerRecord> acquired = Delegate.SelectInstancesFiredTriggerRecords(conn, InstanceId);
                        var executingTriggers = new HashSet<string>();
                        foreach (FiredTriggerRecord ft in acquired)
                        {
                            if (StateExecuting.Equals(ft.FireInstanceState))
                            {
                                executingTriggers.Add(ft.FireInstanceId);
                            }
                        }
                        foreach (TriggerFiredResult tr in result)
                        {
                            if (tr.TriggerFiredBundle != null && executingTriggers.Contains(tr.TriggerFiredBundle.Trigger.FireInstanceId))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("error validating trigger acquisition", e);
                    }
                });
        }

        protected virtual TriggerFiredBundle TriggerFired(ConnectionAndTransactionHolder conn, IOperableTrigger trigger)
        {
            IJobDetail job;
            ICalendar cal = null;

            // Make sure trigger wasn't deleted, paused, or completed...
            try
            {
                // if trigger was deleted, state will be StateDeleted
                string state = Delegate.SelectTriggerState(conn, trigger.Key);
                if (!state.Equals(StateAcquired))
                {
                    return null;
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't select trigger state: " + e.Message, e);
            }

            try
            {
                job = RetrieveJob(conn, trigger.JobKey);
                if (job == null)
                {
                    return null;
                }
            }
            catch (JobPersistenceException jpe)
            {
                try
                {
                    Log.Error("Error retrieving job, setting trigger state to ERROR.", jpe);
                    Delegate.UpdateTriggerState(conn, trigger.Key, StateError);
                }
                catch (Exception sqle)
                {
                    Log.Error("Unable to set trigger state to ERROR.", sqle);
                }
                throw;
            }

            if (trigger.CalendarName != null)
            {
                cal = RetrieveCalendar(conn, trigger.CalendarName);
                if (cal == null)
                {
                    return null;
                }
            }

            try
            {
                Delegate.UpdateFiredTrigger(conn, trigger, StateExecuting, job);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't update fired trigger: " + e.Message, e);
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
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StateBlocked, StateWaiting);
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StateBlocked, StateAcquired);
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Key, StatePausedBlocked, StatePaused);
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException("Couldn't update states of blocked triggers: " + e.Message, e);
                }
            }

            if (!trigger.GetNextFireTimeUtc().HasValue)
            {
                state2 = StateComplete;
                force = true;
            }

            StoreTrigger(conn, trigger, job, true, state2, force, false);

            job.JobDataMap.ClearDirtyFlag();

            return new TriggerFiredBundle(
                job,
                trigger,
                cal,
                trigger.Key.Group.Equals(SchedulerConstants.DefaultRecoveryGroup),
                SystemTime.UtcNow(),
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
        public virtual void TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail,
                                                 SchedulerInstruction triggerInstCode)
        {
            RetryExecuteInNonManagedTXLock(LockTriggerAccess, conn => TriggeredJobComplete(conn, trigger, jobDetail, triggerInstCode));
        }

        protected virtual void TriggeredJobComplete(ConnectionAndTransactionHolder conn,
                                                    IOperableTrigger trigger,
                                                    IJobDetail jobDetail, SchedulerInstruction triggerInstCode)
        {
            try
            {
                if (triggerInstCode == SchedulerInstruction.DeleteTrigger)
                {
                    if (!trigger.GetNextFireTimeUtc().HasValue)
                    {
                        // double check for possible reschedule within job 
                        // execution, which would cancel the need to delete...
                        TriggerStatus stat = Delegate.SelectTriggerStatus(conn, trigger.Key);
                        if (stat != null && !stat.NextFireTimeUtc.HasValue)
                        {
                            RemoveTrigger(conn, trigger.Key, jobDetail);
                        }
                    }
                    else
                    {
                        RemoveTrigger(conn, trigger.Key, jobDetail);
                        SignalSchedulingChangeOnTxCompletion(SchedulerConstants.SchedulingSignalDateTime);
                    }
                }
                else if (triggerInstCode == SchedulerInstruction.SetTriggerComplete)
                {
                    Delegate.UpdateTriggerState(conn, trigger.Key, StateComplete);
                    SignalSchedulingChangeOnTxCompletion(SchedulerConstants.SchedulingSignalDateTime);
                }
                else if (triggerInstCode == SchedulerInstruction.SetTriggerError)
                {
                    Log.Info("Trigger " + trigger.Key + " set to ERROR state.");
                    Delegate.UpdateTriggerState(conn, trigger.Key, StateError);
                    SignalSchedulingChangeOnTxCompletion(SchedulerConstants.SchedulingSignalDateTime);
                }
                else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersComplete)
                {
                    Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StateComplete);
                    SignalSchedulingChangeOnTxCompletion(SchedulerConstants.SchedulingSignalDateTime);
                }
                else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersError)
                {
                    Log.Info("All triggers of Job " + trigger.JobKey + " set to ERROR state.");
                    Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StateError);
                    SignalSchedulingChangeOnTxCompletion(SchedulerConstants.SchedulingSignalDateTime);
                }

                if (jobDetail.ConcurrentExecutionDisallowed)
                {
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, StateWaiting, StateBlocked);
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, StatePaused, StatePausedBlocked);
                    SignalSchedulingChangeOnTxCompletion(SchedulerConstants.SchedulingSignalDateTime);
                }
                if (jobDetail.PersistJobDataAfterExecution)
                {
                    try
                    {
                        if (jobDetail.JobDataMap.Dirty)
                        {
                            Delegate.UpdateJobData(conn, jobDetail);
                        }
                    }
                    catch (IOException e)
                    {
                        throw new JobPersistenceException("Couldn't serialize job data: " + e.Message, e);
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("Couldn't update job data: " + e.Message, e);
                    }
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't update trigger state(s): " + e.Message, e);
            }

            try
            {
                Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't delete fired trigger: " + e.Message, e);
            }
        }

        //---------------------------------------------------------------------------
        // Management methods
        //---------------------------------------------------------------------------


        protected RecoverMisfiredJobsResult DoRecoverMisfires()
        {
            bool transOwner = false;
            ConnectionAndTransactionHolder conn = GetNonManagedTXConnection();
            try
            {
                RecoverMisfiredJobsResult result = RecoverMisfiredJobsResult.NoOp;

                // Before we make the potentially expensive call to acquire the 
                // trigger lock, peek ahead to see if it is likely we would find
                // misfired triggers requiring recovery.
                int misfireCount = (DoubleCheckLockMisfireHandler)
                                       ? Delegate.CountMisfiredTriggersInState(conn, StateWaiting, MisfireTime)
                                       : Int32.MaxValue;

                if (misfireCount == 0)
                {
                    Log.Debug(
                        "Found 0 triggers that missed their scheduled fire-time.");
                }
                else
                {
                    transOwner = LockHandler.ObtainLock(DbProvider.Metadata, conn, LockTriggerAccess);

                    result = RecoverMisfiredJobs(conn, false);
                }

                CommitConnection(conn, false);
                return result;
            }
            catch (JobPersistenceException)
            {
                RollbackConnection(conn);
                throw;
            }
            catch (Exception e)
            {
                RollbackConnection(conn);
                throw new JobPersistenceException("Database error recovering from misfires.", e);
            }
            finally
            {
                try
                {
                    ReleaseLock(LockTriggerAccess, transOwner);
                }
                finally
                {
                    CleanupConnection(conn);
                }
            }
        }


        private const string KeySignalChangeForTxCompletion = "sigChangeForTxCompletion";

        protected virtual void SignalSchedulingChangeOnTxCompletion(DateTimeOffset? candidateNewNextFireTime)
        {
            DateTimeOffset? sigTime = LogicalThreadContext.GetData<DateTimeOffset?>(KeySignalChangeForTxCompletion);
            if (sigTime == null && candidateNewNextFireTime.HasValue)
            {
                LogicalThreadContext.SetData(KeySignalChangeForTxCompletion, candidateNewNextFireTime);
            }
            else
            {
                if (sigTime == null || candidateNewNextFireTime < sigTime)
                {
                    LogicalThreadContext.SetData(KeySignalChangeForTxCompletion, candidateNewNextFireTime);
                }
            }
        }

        protected virtual DateTimeOffset? ClearAndGetSignalSchedulingChangeOnTxCompletion()
        {
            DateTimeOffset? t = LogicalThreadContext.GetData<DateTimeOffset?>(KeySignalChangeForTxCompletion);
            LogicalThreadContext.FreeNamedDataSlot(KeySignalChangeForTxCompletion);
            return t;
        }

        protected virtual void SignalSchedulingChangeImmediately(DateTimeOffset? candidateNewNextFireTime)
        {
            schedSignaler.SignalSchedulingChange(candidateNewNextFireTime);
        }

        //---------------------------------------------------------------------------
        // Cluster management methods
        //---------------------------------------------------------------------------

        protected bool firstCheckIn = true;

        protected DateTimeOffset lastCheckin = SystemTime.UtcNow();

        protected virtual bool DoCheckin()
        {
            bool transOwner = false;
            bool transStateOwner = false;
            bool recovered = false;

            ConnectionAndTransactionHolder conn = GetNonManagedTXConnection();
            try
            {
                // Other than the first time, always checkin first to make sure there is 
                // work to be done before we acquire the lock (since that is expensive, 
                // and is almost never necessary).  This must be done in a separate
                // transaction to prevent a deadlock under recovery conditions.
                IList<SchedulerStateRecord> failedRecords = null;
                if (!firstCheckIn)
                {
                    failedRecords = ClusterCheckIn(conn);
                    CommitConnection(conn, true);
                }

                if (firstCheckIn || (failedRecords != null && failedRecords.Count > 0))
                {
                    LockHandler.ObtainLock(DbProvider.Metadata, conn, LockStateAccess);
                    transStateOwner = true;

                    // Now that we own the lock, make sure we still have work to do. 
                    // The first time through, we also need to make sure we update/create our state record
                    failedRecords = (firstCheckIn) ? ClusterCheckIn(conn) : FindFailedInstances(conn);

                    if (failedRecords.Count > 0)
                    {
                        LockHandler.ObtainLock(DbProvider.Metadata, conn, LockTriggerAccess);
                        //getLockHandler().obtainLock(conn, LockJobAccess);
                        transOwner = true;

                        ClusterRecover(conn, failedRecords);
                        recovered = true;
                    }
                }

                CommitConnection(conn, false);
            }
            catch (JobPersistenceException)
            {
                RollbackConnection(conn);
                throw;
            }
            finally
            {
                try
                {
                    ReleaseLock(LockTriggerAccess, transOwner);
                }
                finally
                {
                    try
                    {
                        ReleaseLock(LockStateAccess, transStateOwner);
                    }
                    finally
                    {
                        CleanupConnection(conn);
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
        protected virtual IList<SchedulerStateRecord> FindFailedInstances(ConnectionAndTransactionHolder conn)
        {
            try
            {
                List<SchedulerStateRecord> failedInstances = new List<SchedulerStateRecord>();
                bool foundThisScheduler = false;

                IList<SchedulerStateRecord> states = Delegate.SelectSchedulerStateRecords(conn, null);

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
                    failedInstances.AddRange(FindOrphanedFailedInstances(conn, states));
                }

                // If not the first time but we didn't find our own instance, then
                // Someone must have done recovery for us.
                if ((foundThisScheduler == false) && (firstCheckIn == false))
                {
                    // TODO: revisit when handle self-failed-out impl'ed (see TODO in clusterCheckIn() below)
                    Log.Warn(
                        "This scheduler instance (" + InstanceId + ") is still " +
                        "active but was recovered by another instance in the cluster.  " +
                        "This may cause inconsistent behavior.");
                }

                return failedInstances;
            }
            catch (Exception e)
            {
                lastCheckin = SystemTime.UtcNow();
                throw new JobPersistenceException("Failure identifying failed instances when checking-in: "
                                                  + e.Message, e);
            }
        }

        /// <summary>
        /// Create dummy <see cref="SchedulerStateRecord" /> objects for fired triggers
        /// that have no scheduler state record.  Checkin timestamp and interval are
        /// left as zero on these dummy <see cref="SchedulerStateRecord" /> objects.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="schedulerStateRecords">List of all current <see cref="SchedulerStateRecord" />s</param>
        private IList<SchedulerStateRecord> FindOrphanedFailedInstances(ConnectionAndTransactionHolder conn, IList<SchedulerStateRecord> schedulerStateRecords)
        {
            IList<SchedulerStateRecord> orphanedInstances = new List<SchedulerStateRecord>();

            Collection.ISet<string> allFiredTriggerInstanceNames = Delegate.SelectFiredTriggerInstanceNames(conn);
            if (allFiredTriggerInstanceNames.Count > 0)
            {
                foreach (SchedulerStateRecord rec in schedulerStateRecords)
                {
                    allFiredTriggerInstanceNames.Remove(rec.SchedulerInstanceId);
                }

                foreach (string name in allFiredTriggerInstanceNames)
                {
                    SchedulerStateRecord orphanedInstance = new SchedulerStateRecord();
                    orphanedInstance.SchedulerInstanceId = name;

                    orphanedInstances.Add(orphanedInstance);

                    Log.Warn("Found orphaned fired triggers for instance: " + orphanedInstance.SchedulerInstanceId);
                }
            }

            return orphanedInstances;
        }

        protected DateTimeOffset CalcFailedIfAfter(SchedulerStateRecord rec)
        {
            TimeSpan passed = SystemTime.UtcNow() - lastCheckin;
            TimeSpan ts = rec.CheckinInterval > passed ? rec.CheckinInterval : passed;
            return rec.CheckinTimestamp.Add(ts).Add(TimeSpan.FromMilliseconds(7500));
        }

        protected virtual IList<SchedulerStateRecord> ClusterCheckIn(ConnectionAndTransactionHolder conn)
        {
            IList<SchedulerStateRecord> failedInstances = FindFailedInstances(conn);
            try
            {
                // TODO: handle self-failed-out


                // check in...
                lastCheckin = SystemTime.UtcNow();
                if (Delegate.UpdateSchedulerState(conn, InstanceId, lastCheckin) == 0)
                {
                    Delegate.InsertSchedulerState(conn, InstanceId, lastCheckin, ClusterCheckinInterval);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Failure updating scheduler state when checking-in: " + e.Message, e);
            }

            return failedInstances;
        }


        protected virtual void ClusterRecover(ConnectionAndTransactionHolder conn, IList<SchedulerStateRecord> failedInstances)
        {
            if (failedInstances.Count > 0)
            {
                long recoverIds = SystemTime.UtcNow().Ticks;

                LogWarnIfNonZero(failedInstances.Count,
                                 "ClusterManager: detected " + failedInstances.Count + " failed or restarted instances.");
                try
                {
                    foreach (SchedulerStateRecord rec in failedInstances)
                    {
                        Log.Info("ClusterManager: Scanning for instance \"" + rec.SchedulerInstanceId +
                                 "\"'s failed in-progress jobs.");

                        IList<FiredTriggerRecord> firedTriggerRecs =
                            Delegate.SelectInstancesFiredTriggerRecords(conn, rec.SchedulerInstanceId);

                        int acquiredCount = 0;
                        int recoveredCount = 0;
                        int otherCount = 0;

                        Collection.HashSet<TriggerKey> triggerKeys = new Collection.HashSet<TriggerKey>();

                        foreach (FiredTriggerRecord ftRec in firedTriggerRecs)
                        {
                            TriggerKey tKey = ftRec.TriggerKey;
                            JobKey jKey = ftRec.JobKey;

                            triggerKeys.Add(tKey);

                            // release blocked triggers..
                            if (ftRec.FireInstanceState.Equals(StateBlocked))
                            {
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey,
                                                                                 StateWaiting,
                                                                                 StateBlocked);
                            }
                            else if (ftRec.FireInstanceState.Equals(StatePausedBlocked))
                            {
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey,
                                                                                 StatePaused,
                                                                                 StatePausedBlocked);
                            }

                            // release acquired triggers..
                            if (ftRec.FireInstanceState.Equals(StateAcquired))
                            {
                                Delegate.UpdateTriggerStateFromOtherState(conn, tKey, StateWaiting, StateAcquired);
                                acquiredCount++;
                            }
                            else if (ftRec.JobRequestsRecovery)
                            {
                                // handle jobs marked for recovery that were not fully
                                // executed..
                                if (JobExists(conn, jKey))
                                {
                                    SimpleTriggerImpl rcvryTrig =
                                        new SimpleTriggerImpl(
                                            "recover_" + rec.SchedulerInstanceId + "_" + Convert.ToString(recoverIds++, CultureInfo.InvariantCulture),
                                            SchedulerConstants.DefaultRecoveryGroup, ftRec.FireTimestamp);

                                    rcvryTrig.JobName = jKey.Name;
                                    rcvryTrig.JobGroup = jKey.Group;
                                    rcvryTrig.MisfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;
                                    rcvryTrig.Priority = ftRec.Priority;
                                    JobDataMap jd = Delegate.SelectTriggerJobDataMap(conn, tKey);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerName, tKey.Name);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerGroup, tKey.Group);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerFiretime, Convert.ToString(ftRec.FireTimestamp, CultureInfo.InvariantCulture));
                                    rcvryTrig.JobDataMap = jd;

                                    rcvryTrig.ComputeFirstFireTimeUtc(null);
                                    StoreTrigger(conn, rcvryTrig, null, false, StateWaiting, false, true);
                                    recoveredCount++;
                                }
                                else
                                {
                                    Log.Warn("ClusterManager: failed job '" + jKey +
                                             "' no longer exists, cannot schedule recovery.");
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
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey, StateWaiting, StateBlocked);
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey, StatePaused, StatePausedBlocked);
                            }
                        }

                        Delegate.DeleteFiredTriggers(conn, rec.SchedulerInstanceId);


                        // Check if any of the fired triggers we just deleted were the last fired trigger
                        // records of a COMPLETE trigger.
                        int completeCount = 0;
                        foreach (TriggerKey triggerKey in triggerKeys)
                        {
                            if (
                                Delegate.SelectTriggerState(conn, triggerKey).Equals(StateComplete))
                            {
                                IList<FiredTriggerRecord> firedTriggers = Delegate.SelectFiredTriggerRecords(conn, triggerKey.Name, triggerKey.Group);
                                if (firedTriggers.Count == 0)
                                {
                                    if (RemoveTrigger(conn, triggerKey))
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


                        if (rec.SchedulerInstanceId.Equals(InstanceId) == false)
                        {
                            Delegate.DeleteSchedulerState(conn, rec.SchedulerInstanceId);
                        }
                    }
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException("Failure recovering jobs: " + e.Message, e);
                }
            }
        }

        protected virtual void LogWarnIfNonZero(int val, string warning)
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
            if (cth.Connection != null)
            {
                try
                {
                    cth.Connection.Close();
                }
                catch (Exception e)
                {
                    Log.Error(
                        "Unexpected exception closing Connection." +
                        "  This is often due to a Connection being returned after or during shutdown.", e);
                }
            }
        }

        /// <summary>
        /// Rollback the supplied connection.
        /// </summary>
        /// <param name="cth">(Optional)
        /// </param>
        /// <throws>  JobPersistenceException thrown if a SQLException occurs when the </throws>
        /// <summary> connection is rolled back
        /// </summary>
        protected virtual void RollbackConnection(ConnectionAndTransactionHolder cth)
        {
            if (cth == null)
            {
                // db might be down or similar
                log.Info("ConnectionAndTransactionHolder passed to RollbackConnection was null, ignoring");
                return;
            }

            if (cth.Transaction != null)
            {
                try
                {
                    CheckNotZombied(cth);
                    cth.Transaction.Rollback();
                }
                catch (Exception e)
                {
                    Log.Error("Couldn't rollback ADO.NET connection. " + e.Message, e);
                }
            }
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
                log.Debug("ConnectionAndTransactionHolder passed to CommitConnection was null, ignoring");
                return;
            }

            if (cth.Transaction != null)
            {
                try
                {
                    CheckNotZombied(cth);
                    IsolationLevel il = cth.Transaction.IsolationLevel;
                    cth.Transaction.Commit();
                    if (openNewTransaction)
                    {
                        // open new transaction to go with
                        cth.Transaction = cth.Connection.BeginTransaction(il);
                    }
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException("Couldn't commit ADO.NET transaction. " + e.Message, e);
                }
            }
        }

        /// <summary>
        /// Execute the given callback in a transaction. Depending on the JobStore, 
        /// the surrounding transaction may be assumed to be already present 
        /// (managed).  
        /// </summary>
        /// <remarks>
        /// This method just forwards to ExecuteInLock() with a null lockName.
        /// </remarks>
        protected T ExecuteWithoutLock<T>(Func<ConnectionAndTransactionHolder, T> txCallback)
        {
            return ExecuteInLock(null, txCallback);
        }

        protected void ExecuteInLock(string lockName, Action<ConnectionAndTransactionHolder> txCallback)
        {
            ExecuteInLock<object>(lockName, conn =>
                                            {
                                                txCallback(conn);
                                                return null;
                                            });
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
        protected abstract T ExecuteInLock<T>(string lockName, Func<ConnectionAndTransactionHolder, T> txCallback);

        protected void RetryExecuteInNonManagedTXLock(string lockName, Action<ConnectionAndTransactionHolder> txCallback)
        {
            RetryExecuteInNonManagedTXLock<object>(lockName, holder =>
                                                             {
                                                                 txCallback(holder);
                                                                 return null;
                                                             });
        }

        protected virtual T RetryExecuteInNonManagedTXLock<T>(string lockName, Func<ConnectionAndTransactionHolder, T> txCallback)
        {
            for (int retry = 1; !shutdown; retry++)
            {
                try
                {
                    return ExecuteInNonManagedTXLock(lockName, txCallback, null);
                }
                catch (JobPersistenceException jpe)
                {
                    if (retry % this.RetryableActionErrorLogThreshold == 0)
                    {
                        schedSignaler.NotifySchedulerListenersError("An error occurred while " + txCallback, jpe);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("retryExecuteInNonManagedTXLock: RuntimeException " + e.Message, e);
                }
                try
                {
                    Thread.Sleep(DbRetryInterval); // retry every N seconds (the db connection must be failed)
                }
                catch (ThreadInterruptedException e)
                {
                    throw new InvalidOperationException("Received interrupted exception", e);
                }
            }

            throw new InvalidOperationException("JobStore is shutdown - aborting retry");
        }

        protected void ExecuteInNonManagedTXLock(string lockName, Action<ConnectionAndTransactionHolder> txCallback)
        {
            ExecuteInNonManagedTXLock<object>(lockName, conn =>
                                                        {
                                                            txCallback(conn);
                                                            return null;
                                                        });
        }


        protected T ExecuteInNonManagedTXLock<T>(string lockName, Func<ConnectionAndTransactionHolder, T> txCallback)
        {
            return ExecuteInNonManagedTXLock(lockName, txCallback, null);
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
        protected T ExecuteInNonManagedTXLock<T>(string lockName, Func<ConnectionAndTransactionHolder, T> txCallback, Func<ConnectionAndTransactionHolder, T, bool> txValidator)
        {
            bool transOwner = false;
            ConnectionAndTransactionHolder conn = null;
            try
            {
                if (lockName != null)
                {
                    // If we aren't using db locks, then delay getting DB connection 
                    // until after acquiring the lock since it isn't needed.
                    if (LockHandler.RequiresConnection)
                    {
                        conn = GetNonManagedTXConnection();
                    }

                    transOwner = LockHandler.ObtainLock(DbProvider.Metadata, conn, lockName);
                }

                if (conn == null)
                {
                    conn = GetNonManagedTXConnection();
                }

                T result = txCallback(conn);
                try
                {
                    CommitConnection(conn, false);
                }
                catch (JobPersistenceException)
                {
                    RollbackConnection(conn);
                    if (txValidator == null || !RetryExecuteInNonManagedTXLock(lockName, connection => txValidator(connection, result)))
                    {
                        throw;
                    }
                }

                DateTimeOffset? sigTime = ClearAndGetSignalSchedulingChangeOnTxCompletion();
                if (sigTime != null)
                {
                    SignalSchedulingChangeImmediately(sigTime);
                }

                return result;
            }
            catch (JobPersistenceException)
            {
                RollbackConnection(conn);
                throw;
            }
            catch (Exception e)
            {
                RollbackConnection(conn);
                throw new JobPersistenceException("Unexpected runtime exception: " + e.Message, e);
            }
            finally
            {
                try
                {
                    ReleaseLock(lockName, transOwner);
                }
                finally
                {
                    CleanupConnection(conn);
                }
            }
        }

        private static void CheckNotZombied(ConnectionAndTransactionHolder cth)
        {
            if (cth == null)
            {
                throw new ArgumentNullException("cth", "Connection-transaction pair cannot be null");
            }

            if (cth.Transaction != null && cth.Transaction.Connection == null)
            {
                throw new DataException("Transaction not connected, or was disconnected");
            }
        }

        /////////////////////////////////////////////////////////////////////////////
        //
        // ClusterManager Thread
        //
        /////////////////////////////////////////////////////////////////////////////
        internal class ClusterManager : QuartzThread
        {
            private readonly JobStoreSupport jobStoreSupport;
            private volatile bool shutdown;
            private int numFails;

            internal ClusterManager(JobStoreSupport jobStoreSupport)
            {
                this.jobStoreSupport = jobStoreSupport;
                Priority = ThreadPriority.AboveNormal;
                Name = string.Format("QuartzScheduler_{0}-{1}_ClusterManager", jobStoreSupport.instanceName, jobStoreSupport.instanceId);
                IsBackground = jobStoreSupport.MakeThreadsDaemons;
            }

            public virtual void Initialize()
            {
                Manage();

                IThreadExecutor executor = jobStoreSupport.ThreadExecutor;
                executor.Execute(this);
            }

            public virtual void Shutdown()
            {
                shutdown = true;
                Interrupt();
            }

            private bool Manage()
            {
                bool res = false;
                try
                {
                    res = jobStoreSupport.DoCheckin();

                    numFails = 0;
                    jobStoreSupport.Log.Debug("ClusterManager: Check-in complete.");
                }
                catch (Exception e)
                {
                    if (numFails % this.jobStoreSupport.RetryableActionErrorLogThreshold == 0)
                    {
                        jobStoreSupport.Log.Error("ClusterManager: Error managing cluster: " + e.Message, e);
                    }
                    numFails++;
                }
                return res;
            }

            public override void Run()
            {
                while (!shutdown)
                {
                    if (!shutdown)
                    {
                        TimeSpan timeToSleep = jobStoreSupport.ClusterCheckinInterval;
                        TimeSpan transpiredTime = SystemTime.UtcNow() - jobStoreSupport.lastCheckin;
                        timeToSleep = timeToSleep - transpiredTime;
                        if (timeToSleep <= TimeSpan.Zero)
                        {
                            timeToSleep = TimeSpan.FromMilliseconds(100);
                        }

                        if (numFails > 0)
                        {
                            timeToSleep = jobStoreSupport.DbRetryInterval > timeToSleep ? jobStoreSupport.DbRetryInterval : timeToSleep;
                        }

                        try
                        {
                            Thread.Sleep(timeToSleep);
                        }
                        catch (ThreadInterruptedException)
                        {
                        }


                        if (!shutdown && Manage())
                        {
                            jobStoreSupport.SignalSchedulingChangeImmediately(SchedulerConstants.SchedulingSignalDateTime);
                        }
                    } //while !Shutdown
                }
            }
        }

        /////////////////////////////////////////////////////////////////////////////
        //
        // MisfireHandler Thread
        //
        /////////////////////////////////////////////////////////////////////////////
        internal class MisfireHandler : QuartzThread
        {
            private readonly JobStoreSupport jobStoreSupport;
            private volatile bool shutdown;
            private int numFails;

            internal MisfireHandler(JobStoreSupport jobStoreSupport)
            {
                this.jobStoreSupport = jobStoreSupport;
                Name = string.Format(CultureInfo.InvariantCulture, "QuartzScheduler_{0}-{1}_MisfireHandler", jobStoreSupport.instanceName, jobStoreSupport.instanceId);
                IsBackground = jobStoreSupport.MakeThreadsDaemons;
            }

            public virtual void Initialize()
            {
                IThreadExecutor executor = jobStoreSupport.ThreadExecutor;
                executor.Execute(this);
            }

            public virtual void Shutdown()
            {
                shutdown = true;
                Interrupt();
            }

            private RecoverMisfiredJobsResult Manage()
            {
                try
                {
                    jobStoreSupport.Log.Debug("MisfireHandler: scanning for misfires...");

                    RecoverMisfiredJobsResult res = jobStoreSupport.DoRecoverMisfires();
                    numFails = 0;
                    return res;
                }
                catch (Exception e)
                {
                    if (numFails % this.jobStoreSupport.RetryableActionErrorLogThreshold == 0)
                    {
                        jobStoreSupport.Log.Error(
                            "MisfireHandler: Error handling misfires: "
                            + e.Message, e);
                    }
                    numFails++;
                }
                return RecoverMisfiredJobsResult.NoOp;
            }

            public override void Run()
            {
                while (!shutdown)
                {
                    DateTimeOffset sTime = SystemTime.UtcNow();

                    RecoverMisfiredJobsResult recoverMisfiredJobsResult = Manage();

                    if (recoverMisfiredJobsResult.ProcessedMisfiredTriggerCount > 0)
                    {
                        jobStoreSupport.SignalSchedulingChangeImmediately(recoverMisfiredJobsResult.EarliestNewTime);
                    }

                    if (!shutdown)
                    {
                        TimeSpan timeToSleep = TimeSpan.FromMilliseconds(50); // At least a short pause to help balance threads
                        if (!recoverMisfiredJobsResult.HasMoreMisfiredTriggers)
                        {
                            timeToSleep = jobStoreSupport.MisfireThreshold - (SystemTime.UtcNow() - sTime);
                            if (timeToSleep <= TimeSpan.Zero)
                            {
                                timeToSleep = TimeSpan.FromMilliseconds(50);
                            }

                            if (numFails > 0)
                            {
                                timeToSleep = jobStoreSupport.DbRetryInterval > timeToSleep ? jobStoreSupport.DbRetryInterval : timeToSleep;
                            }
                        }

                        try
                        {
                            Thread.Sleep(timeToSleep);
                        }
                        catch (ThreadInterruptedException)
                        {
                        }
                    } //while !shutdown
                }
            }

            // EOF
        }

        /// <summary>
        /// Helper class for returning the composite result of trying
        /// to recover misfired jobs.
        /// </summary>
        public class RecoverMisfiredJobsResult
        {
            public static readonly RecoverMisfiredJobsResult NoOp = new RecoverMisfiredJobsResult(false, 0, DateTimeOffset.MaxValue);

            private readonly bool hasMoreMisfiredTriggers;
            private readonly int processedMisfiredTriggerCount;
            private readonly DateTimeOffset earliestNewTimeUtc;

            /// <summary>
            /// Initializes a new instance of the <see cref="RecoverMisfiredJobsResult"/> class.
            /// </summary>
            /// <param name="hasMoreMisfiredTriggers">if set to <c>true</c> [has more misfired triggers].</param>
            /// <param name="processedMisfiredTriggerCount">The processed misfired trigger count.</param>
            /// <param name="earliestNewTimeUtc"></param>
            public RecoverMisfiredJobsResult(bool hasMoreMisfiredTriggers, int processedMisfiredTriggerCount, DateTimeOffset earliestNewTimeUtc)
            {
                this.hasMoreMisfiredTriggers = hasMoreMisfiredTriggers;
                this.processedMisfiredTriggerCount = processedMisfiredTriggerCount;
                this.earliestNewTimeUtc = earliestNewTimeUtc;
            }

            /// <summary>
            /// Gets a value indicating whether this instance has more misfired triggers.
            /// </summary>
            /// <value>
            /// 	<c>true</c> if this instance has more misfired triggers; otherwise, <c>false</c>.
            /// </value>
            public bool HasMoreMisfiredTriggers
            {
                get { return hasMoreMisfiredTriggers; }
            }

            /// <summary>
            /// Gets the processed misfired trigger count.
            /// </summary>
            /// <value>The processed misfired trigger count.</value>
            public int ProcessedMisfiredTriggerCount
            {
                get { return processedMisfiredTriggerCount; }
            }

            public DateTimeOffset EarliestNewTime
            {
                get { return earliestNewTimeUtc; }
            }
        }
    }
}