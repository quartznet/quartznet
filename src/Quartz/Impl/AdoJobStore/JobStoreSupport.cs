#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

using Common.Logging;

using Quartz.Core;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Spi;
using Quartz.Util;

#if NET_35
using KeyHashSet = System.Collections.Generic.HashSet<Quartz.Util.Key>;
#else
using KeyHashSet = Quartz.Collection.HashSet<Quartz.Util.Key>;
#endif

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Utility class to keep track of both active transaction
    /// and connection.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class ConnectionAndTransactionHolder
    {
        private readonly IDbConnection connection;
        private IDbTransaction transaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionAndTransactionHolder"/> class.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        public ConnectionAndTransactionHolder(IDbConnection connection, IDbTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        /// <summary>
        /// Gets or sets the connection.
        /// </summary>
        /// <value>The connection.</value>
        public IDbConnection Connection
        {
            get { return connection; }
        }

        /// <summary>
        /// Gets or sets the transaction.
        /// </summary>
        /// <value>The transaction.</value>
        public IDbTransaction Transaction
        {
            get { return transaction; }
            set { transaction = value; }
        }
    }

    /// <summary>
    /// Contains base functionality for ADO.NET-based JobStore implementations.
    /// </summary>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public abstract class JobStoreSupport : AdoConstants, IJobStore
    {
        protected const string LockTriggerAccess = "TRIGGER_ACCESS";
        protected const string LockJobAccess = "JOB_ACCESS";
        protected const string LockCalendarAccess = "CALENDAR_ACCESS";
        protected const string LockStateAccess = "STATE_ACCESS";
        protected const string LockMisfireAccess = "MISFIRE_ACCESS";

        private string dataSource;
        private string tablePrefix = DefaultTablePrefix;
        private bool useProperties;
        private string instanceId;
        private string instanceName;
        protected string delegateTypeName;
        protected Type delegateType;
        protected Dictionary<string, ICalendar> calendarCache = new Dictionary<string, ICalendar>();
        private IDriverDelegate driverDelegate;
        private TimeSpan misfireThreshold = TimeSpan.FromMinutes(1); // one minute
        private bool dontSetAutoCommitFalse;
        private bool clustered;
        private bool useDBLocks;

        private bool lockOnInsert = true;
        private ISemaphore lockHandler; // set in Initialize() method...
        private string selectWithLockSQL;
        private TimeSpan clusterCheckinInterval = TimeSpan.FromMilliseconds(7500);
        private ClusterManager clusterManagementThread;
        private MisfireHandler misfireHandler;
        private ITypeLoadHelper typeLoadHelper;
        private ISchedulerSignaler signaler;
        protected int maxToRecoverAtATime = 20;
        private bool setTxIsolationLevelSequential;
        private TimeSpan dbRetryInterval = TimeSpan.FromSeconds(10);
        private bool acquireTriggersWithinLock = false;
        private bool makeThreadsDaemons;
        private bool doubleCheckLockMisfireHandler = true;
        private readonly ILog log;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobStoreSupport"/> class.
        /// </summary>
        protected JobStoreSupport()
        {
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

        public virtual long EstimatedTimeToReleaseAndAcquireTrigger
        {
            get { return 70; }
        }

        /// <summary> 
        /// Get or set whether this instance is part of a cluster.
        /// </summary>
        public virtual bool Clustered
        {
            get { return clustered; }
            set { clustered = value; }
        }

        /// <summary>
        /// Get or set the frequency at which this instance "checks-in"
        /// with the other instances of the cluster. -- Affects the rate of
        /// detecting failed instances.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public virtual TimeSpan ClusterCheckinInterval
        {
            get { return clusterCheckinInterval; }
            set { clusterCheckinInterval = value; }
        }

        /// <summary>
        /// Get or set the maximum number of misfired triggers that the misfire handling
        /// thread will try to recover at one time (within one transaction).  The
        /// default is 20.
        /// </summary>
        public virtual int MaxMisfiresToHandleAtATime
        {
            get { return maxToRecoverAtATime; }
            set { maxToRecoverAtATime = value; }
        }

        /// <summary>
        /// Gets or sets the database retry interval.
        /// </summary>
        /// <value>The db retry interval.</value>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public virtual TimeSpan DbRetryInterval
        {
            get { return dbRetryInterval; }
            set { dbRetryInterval = value; }
        }

        /// <summary>
        /// Get or set whether this instance should use database-based thread
        /// synchronization.
        /// </summary>
        public virtual bool UseDBLocks
        {
            get { return useDBLocks; }
            set { useDBLocks = value; }
        }

        /// <summary> 
        /// Whether or not to obtain locks when inserting new jobs/triggers.  
        /// Defaults to <see langword="true" />, which is safest - some db's (such as 
        /// MS SQLServer) seem to require this to avoid deadlocks under high load,
        /// while others seem to do fine without.  
        /// </summary>
        /// <remarks>
        /// Setting this property to <see langword="false" /> will provide a 
        /// significant performance increase during the addition of new jobs 
        /// and triggers.
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
        /// DataSource. This can be helpfull in a few situations, such as if you
        /// have a driver that complains if it is called when it is already off.
        /// </summary>
        public virtual bool DontSetAutoCommitFalse
        {
            get { return dontSetAutoCommitFalse; }
            set { dontSetAutoCommitFalse = value; }
        }

        /// <summary> 
        /// Set the transaction isolation level of DB connections to sequential.
        /// </summary>
        public virtual bool TxIsolationLevelSerializable
        {
            get { return setTxIsolationLevelSequential; }
            set { setTxIsolationLevelSequential = value; }
        }

        /// <summary>
        /// Whether or not the query and update to acquire a Trigger for firing
        /// should be performed after obtaining an explicit DB lock (to avoid 
        /// possible race conditions on the trigger's db row).  This is
        /// is considered unnecessary for most databases (due to the nature of
        ///  the SQL update that is performed), and therefore a superfluous performance hit.
        /// </summary>
        public bool AcquireTriggersWithinLock
        {
            get { return acquireTriggersWithinLock; }
            set { acquireTriggersWithinLock = value; }
        }

        /// <summary> 
        /// Get or set the ADO.NET driver delegate class name.
        /// </summary>
        public virtual string DriverDelegateType
        {
            get { return delegateTypeName; }
            set { delegateTypeName = value; }
        }

        /// <summary>
        /// set the SQL statement to use to select and lock a row in the "locks"
        /// table.
        /// </summary>
        /// <seealso cref="StdRowLockSemaphore" />
        public virtual string SelectWithLockSQL
        {
            get { return selectWithLockSQL; }
            set { selectWithLockSQL = value; }
        }

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
        public bool MakeThreadsDaemons
        {
            get { return makeThreadsDaemons; }
            set { makeThreadsDaemons = value; }
        }


        /// <summary>
        /// Get whether to check to see if there are Triggers that have misfired
        /// before actually acquiring the lock to recover them.  This should be 
        /// set to false if the majority of the time, there are are misfired
        /// Triggers.
        /// </summary>
        /// <returns></returns>
        public bool DoubleCheckLockMisfireHandler
        {
            get { return doubleCheckLockMisfireHandler; }
            set { doubleCheckLockMisfireHandler = value; }
        }

        protected DbMetadata DbMetadata
        {
            get { return DBConnectionManager.Instance.GetDbMetadata(DataSource); }
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
                conn = DBConnectionManager.Instance.GetConnection(DataSource);
                conn.Open();
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    string.Format("Failed to obtain DB connection from data source '{0}': {1}", DataSource, e), e,
                    SchedulerException.ErrorPersistenceCriticalFailure);
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

        protected virtual DateTime MisfireTime
        {
            get
            {
                DateTime misfireTime = SystemTime.UtcNow();
                if (MisfireThreshold > TimeSpan.Zero)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
                }

                return misfireTime;
            }
        }

        protected virtual string FiredTriggerRecordId
        {
            get
            {
                Interlocked.Increment(ref ftrCtr);
                return InstanceId + ftrCtr;
            }
        }

        /// <summary>
        /// Get the driver delegate for DB operations.
        /// </summary>
        protected virtual IDriverDelegate Delegate
        {
            get
            {
                if (driverDelegate == null)
                {
                    try
                    {
                        if (delegateTypeName != null)
                        {
                            delegateType = TypeLoadHelper.LoadType(delegateTypeName);
                        }

                        ConstructorInfo ctor;
                        Object[] ctorParams;
                        IDbProvider dbProvider = DBConnectionManager.Instance.GetDbProvider(DataSource);
                        if (CanUseProperties)
                        {
                            Type[] ctorParamTypes =
                                new Type[]
                                    {
                                        typeof (ILog), typeof (String), typeof (String), typeof (IDbProvider),
                                        typeof (Boolean)
                                    };
                            ctor = delegateType.GetConstructor(ctorParamTypes);
                            ctorParams = new Object[] { Log, tablePrefix, instanceId, dbProvider, CanUseProperties };
                        }
                        else
                        {
                            Type[] ctorParamTypes =
                                new Type[] { typeof(ILog), typeof(String), typeof(String), typeof(IDbProvider) };
                            ctor = delegateType.GetConstructor(ctorParamTypes);
                            ctorParams = new Object[] { Log, tablePrefix, instanceId, dbProvider };
                        }

                        driverDelegate = (IDriverDelegate)ctor.Invoke(ctorParams);
                    }
                    catch (Exception e)
                    {
                        throw new NoSuchDelegateException("Couldn't instantiate delegate: " + e.Message, e);
                    }
                }

                return driverDelegate;
            }
        }

        private IDbProvider DbProvider
        {
            get { return DBConnectionManager.Instance.GetDbProvider(DataSource); }
        }

        protected internal virtual ISemaphore LockHandler
        {
            get { return lockHandler; }
            set { lockHandler = value; }
        }

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
            signaler = s;


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
                    Log.Info("Using db table-based data access locking (synchronization).");
                    LockHandler = new StdRowLockSemaphore(TablePrefix, SelectWithLockSQL, DbProvider);
                }
                else
                {
                    Log.Info("Using thread monitor-based data access locking (synchronization).");
                    LockHandler = new SimpleSemaphore();
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
                    throw new SchedulerConfigException("Failure occured during job recovery.", se);
                }
            }

            misfireHandler = new MisfireHandler(this);
            misfireHandler.Initialize();
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
        /// it should free up all of it's resources because the scheduler is
        /// shutting down.
        /// </summary>
        public virtual void Shutdown()
        {
            if (clusterManagementThread != null)
            {
                clusterManagementThread.Shutdown();
            }

            if (misfireHandler != null)
            {
                misfireHandler.Shutdown();
            }

            try
            {
                DBConnectionManager.Instance.Shutdown(DataSource);
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


        protected virtual void ReleaseLock(ConnectionAndTransactionHolder cth, string lockName, bool doIt)
        {
            if (doIt && cth != null)
            {
                try
                {
                    LockHandler.ReleaseLock(cth, lockName);
                }
                catch (LockException le)
                {
                    Log.Error("Error returning lock: " + le.Message, le);
                }
            }
        }

        /// <summary>
        /// Removes all volatile data.
        /// </summary>
        protected virtual void CleanVolatileTriggerAndJobs()
        {
            ExecuteInNonManagedTXLock(LockTriggerAccess, new CleanVolatileTriggerAndJobsCallback(this));
        }

        protected class CleanVolatileTriggerAndJobsCallback : CallbackSupport, IVoidTransactionCallback
        {
            public CleanVolatileTriggerAndJobsCallback(JobStoreSupport js)
                : base(js)
            {
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.CleanVolatileTriggerAndJobs(conn);
            }
        }

        protected class CallbackSupport
        {
            protected JobStoreSupport js;


            public CallbackSupport(JobStoreSupport js)
            {
                this.js = js;
            }
        }


        /// <summary>
        /// Removes all volatile data.
        /// </summary>
        protected virtual void CleanVolatileTriggerAndJobs(ConnectionAndTransactionHolder conn)
        {
            try
            {
                // find volatile jobs & triggers...
                Key[] volatileTriggers = Delegate.SelectVolatileTriggers(conn);
                Key[] volatileJobs = Delegate.SelectVolatileJobs(conn);

                for (int i = 0; i < volatileTriggers.Length; i++)
                {
                    RemoveTrigger(conn, null, volatileTriggers[i].Name, volatileTriggers[i].Group);
                }
                Log.Info("Removed " + volatileTriggers.Length + " Volatile Trigger(s).");

                for (int i = 0; i < volatileJobs.Length; i++)
                {
                    RemoveJob(conn, null, volatileJobs[i].Name, volatileJobs[i].Group, true);
                }
                Log.Info("Removed " + volatileJobs.Length + " Volatile Job(s).");

                // clean up any fired trigger entries
                Delegate.DeleteVolatileFiredTriggers(conn);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't clean volatile data: " + e.Message, e);
            }
        }

        /// <summary>
        /// Will recover any failed or misfired jobs and clean up the data store as
        /// appropriate.
        /// </summary>
        protected virtual void RecoverJobs()
        {
            ExecuteInNonManagedTXLock(LockTriggerAccess, new RecoverJobsCallback(this));
        }

        protected class RecoverJobsCallback : CallbackSupport, IVoidTransactionCallback
        {
            public RecoverJobsCallback(JobStoreSupport js)
                : base(js)
            {
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.RecoverJobs(conn);
            }
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
                Trigger[] recoveringJobTriggers = Delegate.SelectTriggersForRecoveringJobs(conn);
                Log.Info("Recovering " + recoveringJobTriggers.Length +
                         " jobs that were in-progress at the time of the last shut-down.");

                for (int i = 0; i < recoveringJobTriggers.Length; ++i)
                {
                    if (JobExists(conn, recoveringJobTriggers[i].JobName, recoveringJobTriggers[i].JobGroup))
                    {
                        recoveringJobTriggers[i].ComputeFirstFireTimeUtc(null);
                        StoreTrigger(conn, null, recoveringJobTriggers[i], null, false, StateWaiting, false, true);
                    }
                }
                Log.Info("Recovery complete.");

                // remove lingering 'complete' triggers...
                Key[] ct = Delegate.SelectTriggersInState(conn, StateComplete);
                for (int i = 0; ct != null && i < ct.Length; i++)
                {
                    RemoveTrigger(conn, null, ct[i].Name, ct[i].Group);
                }
                if (ct != null)
                {
                    Log.Info(string.Format(CultureInfo.InvariantCulture, "Removed {0} 'complete' triggers.", ct.Length));
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

            IList<Key> misfiredTriggers = new List<Key>();
            DateTime earliestNewTime = DateTime.MaxValue;

            // We must still look for the MISFIRED state in case triggers were left 
            // in this state when upgrading to this version that does not support it. 
            bool hasMoreMisfiredTriggers =
                Delegate.SelectMisfiredTriggersInStates(conn, StateMisfired, StateWaiting, MisfireTime,
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

            foreach (Key triggerKey in misfiredTriggers)
            {
                Trigger trig = RetrieveTrigger(conn, triggerKey.Name, triggerKey.Group);

                if (trig == null)
                {
                    continue;
                }

                DoUpdateOfMisfiredTrigger(conn, null, trig, false, StateWaiting, recovering);

                DateTime? nextTime = trig.GetNextFireTimeUtc();
                if (nextTime.HasValue && nextTime.Value < earliestNewTime)
                {
                    earliestNewTime = nextTime.Value;
                }
            }

            return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
        }


        protected virtual bool UpdateMisfiredTrigger(ConnectionAndTransactionHolder conn,
                                                              SchedulingContext ctxt, string triggerName,
                                                              string groupName, string newStateIfNotComplete,
                                                              bool forceState)
        {
            try
            {
                Trigger trig = RetrieveTrigger(conn, triggerName, groupName);

                DateTime misfireTime = SystemTime.UtcNow();
                if (MisfireThreshold > TimeSpan.Zero)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
                }

                if (trig.GetNextFireTimeUtc().Value > misfireTime)
                {
                    return false;
                }

                DoUpdateOfMisfiredTrigger(conn, ctxt, trig, forceState, newStateIfNotComplete, false);

                signaler.NotifySchedulerListenersFinalized(trig);

                return true;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    string.Format("Couldn't update misfired trigger '{0}.{1}': {2}", groupName, triggerName, e.Message), e);
            }
        }


        private void DoUpdateOfMisfiredTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt, Trigger trig,
                                               bool forceState, string newStateIfNotComplete, bool recovering)
        {
            ICalendar cal = null;
            if (trig.CalendarName != null)
            {
                cal = RetrieveCalendar(conn, ctxt, trig.CalendarName);
            }

            signaler.NotifyTriggerListenersMisfired(trig);

            trig.UpdateAfterMisfire(cal);

            if (!trig.GetNextFireTimeUtc().HasValue)
            {
                StoreTrigger(conn, ctxt, trig, null, true, StateComplete, forceState, recovering);
            }
            else
            {
                StoreTrigger(conn, ctxt, trig, null, true, newStateIfNotComplete, forceState, false);
            }
        }

        /// <summary>
        /// Store the given <see cref="JobDetail" /> and <see cref="Trigger" />.
        /// </summary>
        /// <param name="ctxt">SchedulingContext</param>
        /// <param name="newJob">Job to be stored.</param>
        /// <param name="newTrigger">Trigger to be stored.</param>
        public void StoreJobAndTrigger(SchedulingContext ctxt, JobDetail newJob, Trigger newTrigger)
        {
            ExecuteInLock((LockOnInsert) ? LockTriggerAccess : null,
                          new StoreJobAndTriggerCallback(this, newJob, newTrigger, ctxt));
        }

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="ctxt"></param>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public bool IsJobGroupPaused(SchedulingContext ctxt, string groupName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="ctxt"></param>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public bool IsTriggerGroupPaused(SchedulingContext ctxt, string groupName)
        {
            throw new NotImplementedException();
        }

        protected class StoreJobAndTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly JobDetail newJob;
            private readonly Trigger newTrigger;
            private readonly SchedulingContext ctxt;

            public StoreJobAndTriggerCallback(JobStoreSupport js, JobDetail newJob, Trigger newTrigger,
                                              SchedulingContext ctxt)
                : base(js)
            {
                this.newJob = newJob;
                this.newTrigger = newTrigger;
                this.ctxt = ctxt;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                if (newJob.Volatile && !newTrigger.Volatile)
                {
                    JobPersistenceException jpe =
                        new JobPersistenceException(
                            "Cannot associate non-volatile trigger with a volatile job!");
                    jpe.ErrorCode = SchedulerException.ErrorClientError;
                    throw jpe;
                }

                js.StoreJob(conn, ctxt, newJob, false);
                js.StoreTrigger(conn, ctxt, newTrigger, newJob, false,
                                StateWaiting, false, false);
            }
        }


        /// <summary>
        /// Stores the given <see cref="JobDetail" />.
        /// </summary>
        /// <param name="ctxt"></param>
        /// <param name="newJob">The <see cref="JobDetail" /> to be stored.</param>
        /// <param name="replaceExisting">
        /// If <see langword="true" />, any <see cref="IJob" /> existing in the
        /// <see cref="IJobStore" /> with the same name &amp; group should be over-written.
        /// </param>
        public void StoreJob(SchedulingContext ctxt, JobDetail newJob, bool replaceExisting)
        {
            ExecuteInLock(
                (LockOnInsert || replaceExisting) ? LockTriggerAccess : null,
                new StoreJobCallback(this, ctxt, newJob, replaceExisting));
        }

        protected class StoreJobCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly JobDetail newJob;
            private readonly bool replaceExisting;

            public StoreJobCallback(JobStoreSupport js, SchedulingContext ctxt, JobDetail newJob, bool replaceExisting)
                : base(js)
            {
                this.ctxt = ctxt;
                this.newJob = newJob;
                this.replaceExisting = replaceExisting;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.StoreJob(conn, ctxt, newJob, replaceExisting);
            }
        }

        /// <summary> <p>
        /// Insert or update a job.
        /// </p>
        /// </summary>
        protected virtual void StoreJob(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                 JobDetail newJob,
                                                 bool replaceExisting)
        {
            if (newJob.Volatile && Clustered)
            {
                Log.Info("note: volatile jobs are effectively non-volatile in a clustered environment.");
            }

            bool existingJob = JobExists(conn, newJob.Name, newJob.Group);
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
        protected virtual bool JobExists(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            try
            {
                return Delegate.JobExists(conn, jobName, groupName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine job existence (" + groupName + "." + jobName + "): " + e.Message, e);
            }
        }


        /// <summary>
        /// Store the given <see cref="Trigger" />.
        /// </summary>
        /// <param name="ctxt"></param>
        /// <param name="newTrigger">The <see cref="Trigger" /> to be stored.</param>
        /// <param name="replaceExisting">
        /// If <see langword="true" />, any <see cref="Trigger" /> existing in
        /// the <see cref="IJobStore" /> with the same name &amp; group should
        /// be over-written.
        /// </param>
        /// <exception cref="ObjectAlreadyExistsException">
        /// if a <see cref="Trigger" /> with the same name/group already
        /// exists, and replaceExisting is set to false.
        /// </exception>
        public void StoreTrigger(SchedulingContext ctxt, Trigger newTrigger, bool replaceExisting)
        {
            ExecuteInLock(
                (LockOnInsert || replaceExisting) ? LockTriggerAccess : null,
                new StoreTriggerCallback(this, ctxt, newTrigger, replaceExisting));
        }

        protected class StoreTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly Trigger newTrigger;
            private readonly bool replaceExisting;

            public StoreTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, Trigger newTrigger,
                                        bool replaceExisting)
                : base(js)
            {
                this.ctxt = ctxt;
                this.newTrigger = newTrigger;
                this.replaceExisting = replaceExisting;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.StoreTrigger(conn, ctxt, newTrigger, null, replaceExisting, StateWaiting, false, false);
            }
        }

        /// <summary>
        /// Insert or update a trigger.
        /// </summary>
        protected virtual void StoreTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                     Trigger newTrigger,
                                                     JobDetail job, bool replaceExisting, string state, bool forceState,
                                                     bool recovering)
        {
            if (newTrigger.Volatile && Clustered)
            {
                Log.Info("note: volatile triggers are effectively non-volatile in a clustered environment.");
            }

            bool existingTrigger = TriggerExists(conn, newTrigger.Name, newTrigger.Group);


            if ((existingTrigger) && (!replaceExisting))
            {
                throw new ObjectAlreadyExistsException(newTrigger);
            }

            try
            {
                if (!forceState)
                {
                    bool shouldBepaused = Delegate.IsTriggerGroupPaused(conn, newTrigger.Group);

                    if (!shouldBepaused)
                    {
                        shouldBepaused = Delegate.IsTriggerGroupPaused(conn, AllGroupsPaused);

                        if (shouldBepaused)
                        {
                            Delegate.InsertPausedTriggerGroup(conn, newTrigger.Group);
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
                    job = Delegate.SelectJobDetail(conn, newTrigger.JobName, newTrigger.JobGroup, TypeLoadHelper);
                }
                if (job == null)
                {
                    throw new JobPersistenceException("The job (" + newTrigger.FullJobName +
                                                      ") referenced by the trigger does not exist.");
                }
                if (job.Volatile && !newTrigger.Volatile)
                {
                    throw new JobPersistenceException("It does not make sense to " +
                                                      "associate a non-volatile Trigger with a volatile Job!");
                }

                if (job.Stateful && !recovering)
                {
                    state = CheckBlockedState(conn, ctxt, job.Name, job.Group, state);
                }
                if (existingTrigger)
                {
                    if (newTrigger is SimpleTrigger && !newTrigger.HasAdditionalProperties)
                    {
                        Delegate.UpdateSimpleTrigger(conn, (SimpleTrigger)newTrigger);
                    }
                    else if (newTrigger is CronTrigger && !newTrigger.HasAdditionalProperties)
                    {
                        Delegate.UpdateCronTrigger(conn, (CronTrigger)newTrigger);
                    }
                    else
                    {
                        Delegate.UpdateBlobTrigger(conn, newTrigger);
                    }
                    Delegate.UpdateTrigger(conn, newTrigger, state, job);
                }
                else
                {
                    Delegate.InsertTrigger(conn, newTrigger, state, job);
                    if (newTrigger is SimpleTrigger && !newTrigger.HasAdditionalProperties)
                    {
                        Delegate.InsertSimpleTrigger(conn, (SimpleTrigger)newTrigger);
                    }
                    else if (newTrigger is CronTrigger && !newTrigger.HasAdditionalProperties)
                    {
                        Delegate.InsertCronTrigger(conn, (CronTrigger)newTrigger);
                    }
                    else
                    {
                        Delegate.InsertBlobTrigger(conn, newTrigger);
                    }
                }
            }
            catch (Exception e)
            {
                string message = String.Format("Couldn't store trigger '{0}' for '{1}' job: {2}", newTrigger.Name, newTrigger.JobName, e.Message);
                throw new JobPersistenceException(message, e);
            }
        }

        /// <summary>
        /// Check existence of a given trigger.
        /// </summary>
        protected virtual bool TriggerExists(ConnectionAndTransactionHolder conn, string triggerName,
                                                      string groupName)
        {
            try
            {
                return Delegate.TriggerExists(conn, triggerName, groupName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine trigger existence (" + groupName + "." + triggerName + "): " + e.Message, e);
            }
        }

        /// <summary>
        /// Remove (delete) the <see cref="IJob" /> with the given
        /// name, and any <see cref="Trigger" /> s that reference
        /// it.
        /// </summary>
        /// 
        /// <remarks>
        /// If removal of the <see cref="IJob" /> results in an empty group, the
        /// group should be removed from the <see cref="IJobStore" />'s list of
        /// known group names.
        /// </remarks>
        /// 
        /// <param name="jobName">The name of the <see cref="IJob" /> to be removed.</param>
        /// <param name="groupName">The group name of the <see cref="IJob" /> to be removed.</param>
        /// <returns>
        /// <see langword="true" /> if a <see cref="IJob" /> with the given name &amp;
        /// group was found and removed from the store.
        /// </returns>
        public bool RemoveJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            return (bool)ExecuteInLock(
                              LockTriggerAccess,
                              new RemoveJobCallback(this, ctxt, jobName, groupName));
        }

        protected class RemoveJobCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string jobName;
            private readonly string groupName;


            public RemoveJobCallback(JobStoreSupport js, SchedulingContext ctxt, string jobName, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.jobName = jobName;
                this.groupName = groupName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.RemoveJob(conn, ctxt, jobName, groupName, true);
            }
        }

        protected virtual bool RemoveJob(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                  string jobName,
                                                  string groupName, bool activeDeleteSafe)
        {
            try
            {
                Key[] jobTriggers = Delegate.SelectTriggerNamesForJob(conn, jobName, groupName);

                for (int i = 0; i < jobTriggers.Length; ++i)
                {
                    DeleteTriggerAndChildren(conn, jobTriggers[i].Name, jobTriggers[i].Group);
                }

                return DeleteJobAndChildren(conn, ctxt, jobName, groupName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't remove job: " + e.Message, e);
            }
        }


        /// <summary>
        /// Delete a job and its listeners.
        /// </summary>
        /// <seealso cref="JobStoreSupport.RemoveJob(ConnectionAndTransactionHolder, SchedulingContext, string, string, bool)" />
        /// <seealso cref="RemoveTrigger(ConnectionAndTransactionHolder, SchedulingContext, string, string)" />
        private bool DeleteJobAndChildren(ConnectionAndTransactionHolder conn, SchedulingContext ctxt, string jobName,
                                          string groupName)
        {
            Delegate.DeleteJobListeners(conn, jobName, groupName);

            return (Delegate.DeleteJobDetail(conn, jobName, groupName) > 0);
        }

        /// <summary>
        /// Delete a trigger, its listeners, and its Simple/Cron/BLOB sub-table entry.
        /// </summary>
        /// <seealso cref="RemoveJob(ConnectionAndTransactionHolder, SchedulingContext, string, string, bool)" />
        /// <seealso cref="RemoveTrigger(ConnectionAndTransactionHolder, SchedulingContext, string, string)" />
        /// <seealso cref="ReplaceTrigger(ConnectionAndTransactionHolder, SchedulingContext, string, string, Trigger)" />
        private bool DeleteTriggerAndChildren(ConnectionAndTransactionHolder conn, string triggerName,
                                              string triggerGroupName)
        {
            IDriverDelegate del = Delegate;

            // Once it succeeds in deleting one sub-table entry it will not try the others.
            if ((del.DeleteSimpleTrigger(conn, triggerName, triggerGroupName) == 0) &&
                (del.DeleteCronTrigger(conn, triggerName, triggerGroupName) == 0))
            {
                del.DeleteBlobTrigger(conn, triggerName, triggerGroupName);
            }

            del.DeleteTriggerListeners(conn, triggerName, triggerGroupName);

            return (del.DeleteTrigger(conn, triggerName, triggerGroupName) > 0);
        }

        /// <summary>
        /// Retrieve the <see cref="JobDetail" /> for the given
        /// <see cref="IJob" />.
        /// </summary>
        /// <param name="jobName">The name of the <see cref="IJob" /> to be retrieved.</param>
        /// <param name="groupName">The group name of the <see cref="IJob" /> to be retrieved.</param>
        /// <returns>The desired <see cref="IJob" />, or null if there is no match.</returns>
        public JobDetail RetrieveJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            // no locks necessary for read...
            return (JobDetail)ExecuteWithoutLock(new RetrieveJobCallback(this, ctxt, jobName, groupName));
        }

        protected class RetrieveJobCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string jobName;
            private readonly string groupName;

            public RetrieveJobCallback(JobStoreSupport js, SchedulingContext ctxt, string jobName, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.jobName = jobName;
                this.groupName = groupName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.RetrieveJob(conn, ctxt, jobName, groupName);
            }
        }


        protected virtual JobDetail RetrieveJob(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                         string jobName,
                                                         string groupName)
        {
            try
            {
                JobDetail job = Delegate.SelectJobDetail(conn, jobName, groupName, TypeLoadHelper);
                String[] listeners = Delegate.SelectJobListeners(conn, jobName, groupName);
                for (int i = 0; i < listeners.Length; ++i)
                {
                    job.AddJobListener(listeners[i]);
                }

                return job;
            }

            catch (IOException e)
            {
                throw new JobPersistenceException(
                    "Couldn't retrieve job because the BLOB couldn't be deserialized: " + e.Message, e,
                    SchedulerException.ErrorPersistenceJobDoesNotExist);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't retrieve job: " + e.Message, e);
            }
        }


        /// <summary>
        /// Remove (delete) the <see cref="Trigger" /> with the
        /// given name.
        /// </summary>
        /// 
        /// <remarks>
        /// <p>
        /// If removal of the <see cref="Trigger" /> results in an empty group, the
        /// group should be removed from the <see cref="IJobStore" />'s list of
        /// known group names.
        /// </p>
        /// 
        /// <p>
        /// If removal of the <see cref="Trigger" /> results in an 'orphaned' <see cref="IJob" />
        /// that is not 'durable', then the <see cref="IJob" /> should be deleted
        /// also.
        /// </p>
        /// </remarks>
        /// <param name="triggerName">The name of the <see cref="Trigger" /> to be removed.</param>
        /// <param name="groupName">The group name of the <see cref="Trigger" /> to be removed.</param>
        /// <returns>
        /// <see langword="true" /> if a <see cref="Trigger" /> with the given
        /// name &amp; group was found and removed from the store.
        ///</returns>
        public bool RemoveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            return (bool)ExecuteInLock(
                              LockTriggerAccess,
                              new RemoveTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class RemoveTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string triggerName;
            private readonly string groupName;

            public RemoveTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, string triggerName,
                                         string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.triggerName = triggerName;
                this.groupName = groupName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.RemoveTrigger(conn, ctxt, triggerName, groupName);
            }
        }

        protected virtual bool RemoveTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                      string triggerName,
                                                      string groupName)
        {
            bool removedTrigger;
            try
            {
                // this must be called before we delete the trigger, obviously
                // we use fault tolerant type loading as we only want to delete things
                JobDetail job = Delegate.SelectJobForTrigger(conn, triggerName, groupName, new NoOpJobTypeLoader());

                removedTrigger = DeleteTriggerAndChildren(conn, triggerName, groupName);

                if (null != job && !job.Durable)
                {
                    int numTriggers = Delegate.SelectNumTriggersForJob(conn, job.Name, job.Group);
                    if (numTriggers == 0)
                    {
                        // Don't call RemoveJob() because we don't want to check for
                        // triggers again.
                        DeleteJobAndChildren(conn, ctxt, job.Name, job.Group);
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
                return typeof(Quartz.Job.NoOpJob);
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

        /// <see cref="IJobStore.ReplaceTrigger(SchedulingContext, string, string, Trigger)" />
        public bool ReplaceTrigger(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger)
        {
            return
                (bool)
                ExecuteInLock(LockTriggerAccess,
                              new ReplaceTriggerCallback(this, ctxt, triggerName, groupName, newTrigger));
        }

        protected class ReplaceTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string triggerName;
            private readonly string groupName;
            private readonly Trigger newTrigger;

            public ReplaceTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, string triggerName,
                                          string groupName, Trigger newTrigger)
                : base(js)
            {
                this.ctxt = ctxt;
                this.triggerName = triggerName;
                this.groupName = groupName;
                this.newTrigger = newTrigger;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.ReplaceTrigger(conn, ctxt, triggerName, groupName, newTrigger);
            }
        }

        protected virtual bool ReplaceTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                       string triggerName,
                                                       string groupName, Trigger newTrigger)
        {
            try
            {
                // this must be called before we delete the trigger, obviously
                JobDetail job = Delegate.SelectJobForTrigger(conn, triggerName, groupName, TypeLoadHelper);

                if (job == null)
                {
                    return false;
                }

                if (!newTrigger.JobName.Equals(job.Name) || !newTrigger.JobGroup.Equals(job.Group))
                {
                    throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                }

                bool removedTrigger = DeleteTriggerAndChildren(conn, triggerName, groupName);

                StoreTrigger(conn, ctxt, newTrigger, job, false, StateWaiting, false, false);

                return removedTrigger;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
            }
        }

        /// <summary>
        /// Retrieve the given <see cref="Trigger" />.
        /// </summary>
        /// <param name="triggerName">The name of the <see cref="Trigger" /> to be retrieved.</param>
        /// <param name="groupName">The group name of the <see cref="Trigger" /> to be retrieved.</param>
        /// <returns>The desired <see cref="Trigger" />, or null if there is no match.</returns>
        public Trigger RetrieveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            return (Trigger)ExecuteWithoutLock( // no locks necessary for read...
                                 new RetrieveTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class RetrieveTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string triggerName;
            private readonly string groupName;

            public RetrieveTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, string triggerName,
                                           string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.triggerName = triggerName;
                this.groupName = groupName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.RetrieveTrigger(conn, ctxt, triggerName, groupName);
            }
        }

        protected virtual Trigger RetrieveTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                           string triggerName,
                                                           string groupName)
        {
            return RetrieveTrigger(conn, triggerName, groupName);
        }

        protected virtual Trigger RetrieveTrigger(ConnectionAndTransactionHolder conn, string triggerName,
                                                           string groupName)
        {
            try
            {
                Trigger trigger = Delegate.SelectTrigger(conn, triggerName, groupName);
                if (trigger == null)
                {
                    return null;
                }

                // In case Trigger was BLOB, clear out any listeners that might 
                // have been serialized.
                trigger.ClearAllTriggerListeners();

                String[] listeners = Delegate.SelectTriggerListeners(conn, triggerName, groupName);
                for (int i = 0; i < listeners.Length; ++i)
                {
                    trigger.AddTriggerListener(listeners[i]);
                }

                return trigger;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't retrieve trigger: " + e.Message, e);
            }
        }


        /// <summary>
        /// Get the current state of the identified <see cref="Trigger" />.
        /// </summary>
        /// <seealso cref="TriggerState.Normal" />
        /// <seealso cref="TriggerState.Paused" />
        /// <seealso cref="TriggerState.Complete" />
        /// <seealso cref="TriggerState.Error" />
        /// <seealso cref="TriggerState.None" />
        public TriggerState GetTriggerState(SchedulingContext ctxt, string triggerName, string groupName)
        {
            // no locks necessary for read...
            return (TriggerState)ExecuteWithoutLock(new GetTriggerStateCallback(this, ctxt, triggerName, groupName));
        }

        protected class GetTriggerStateCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string triggerName;
            private readonly string groupName;

            public GetTriggerStateCallback(JobStoreSupport js, SchedulingContext ctxt, string triggerName,
                                           string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.triggerName = triggerName;
                this.groupName = groupName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetTriggerState(conn, ctxt, triggerName, groupName);
            }
        }


        /// <summary>
        /// Gets the state of the trigger.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="ctxt">The CTXT.</param>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
        public virtual TriggerState GetTriggerState(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                           string triggerName, string groupName)
        {
            try
            {
                String ts = Delegate.SelectTriggerState(conn, triggerName, groupName);

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
                    return TriggerState.Blocked;
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
                    "Couldn't determine state of trigger (" + groupName + "." + triggerName + "): " + e.Message, e);
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
        /// <exception cref="ObjectAlreadyExistsException">
        ///           if a <see cref="ICalendar" /> with the same name already
        ///           exists, and replaceExisting is set to false.
        /// </exception>
        public void StoreCalendar(SchedulingContext ctxt, string calName, ICalendar calendar, bool replaceExisting,
                                  bool updateTriggers)
        {
            ExecuteInLock(
                (LockOnInsert || updateTriggers) ? LockTriggerAccess : null,
                new StoreCalendarCallback(this, ctxt, calName, calendar, replaceExisting, updateTriggers));
        }

        protected class StoreCalendarCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string calName;
            private readonly ICalendar calendar;
            private readonly bool replaceExisting;
            private readonly bool updateTriggers;

            public StoreCalendarCallback(JobStoreSupport js, SchedulingContext ctxt, string calName, ICalendar calendar,
                                         bool replaceExisting, bool updateTriggers)
                : base(js)
            {
                this.ctxt = ctxt;
                this.calName = calName;
                this.calendar = calendar;
                this.replaceExisting = replaceExisting;
                this.updateTriggers = updateTriggers;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.StoreCalendar(conn, ctxt, calName, calendar, replaceExisting, updateTriggers);
            }
        }

        protected virtual void StoreCalendar(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
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
                        Trigger[] trigs = Delegate.SelectTriggersForCalendar(conn, calName);

                        for (int i = 0; i < trigs.Length; i++)
                        {
                            trigs[i].UpdateWithNewCalendar(calendar, MisfireThreshold);
                            StoreTrigger(conn, ctxt, trigs[i], null, true, StateWaiting, false, false);
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
                    calendarCache[calName] = calendar; // lazy-cache}
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
        /// <see cref="Trigger" />s pointing to non-existent calendars, then a
        /// <see cref="JobPersistenceException" /> will be thrown.
        /// </remarks>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be removed.</param>
        /// <returns>
        /// <see langword="true" /> if a <see cref="ICalendar" /> with the given name
        /// was found and removed from the store.
        ///</returns>
        public bool RemoveCalendar(SchedulingContext ctxt, string calName)
        {
            return (bool)ExecuteInLock(LockTriggerAccess, new RemoveCalendarCallback(this, ctxt, calName));
        }

        protected class RemoveCalendarCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string calName;


            public RemoveCalendarCallback(JobStoreSupport js, SchedulingContext ctxt, string calName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.calName = calName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.RemoveCalendar(conn, ctxt, calName);
            }
        }

        protected virtual bool RemoveCalendar(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
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
        /// Retrieve the given <see cref="Trigger" />.
        /// </summary>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
        /// <returns>The desired <see cref="ICalendar" />, or null if there is no match.</returns>
        public ICalendar RetrieveCalendar(SchedulingContext ctxt, string calName)
        {
            return (ICalendar)ExecuteWithoutLock( // no locks necessary for read...
                                   new RetrieveCalendarCallback(this, ctxt, calName));
        }

        protected class RetrieveCalendarCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string calName;


            public RetrieveCalendarCallback(JobStoreSupport js, SchedulingContext ctxt, string calName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.calName = calName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.RetrieveCalendar(conn, ctxt, calName);
            }
        }

        protected virtual ICalendar RetrieveCalendar(ConnectionAndTransactionHolder conn,
                                                              SchedulingContext ctxt, string calName)
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
        public int GetNumberOfJobs(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (int)ExecuteWithoutLock(new GetNumberOfJobsCallback(this, ctxt));
        }

        protected class GetNumberOfJobsCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;

            public GetNumberOfJobsCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetNumberOfJobs(conn, ctxt);
            }
        }

        protected virtual int GetNumberOfJobs(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
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
        /// Get the number of <see cref="Trigger" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        public int GetNumberOfTriggers(SchedulingContext ctxt)
        {
            return (int)ExecuteWithoutLock( // no locks necessary for read...
                             new GetNumberOfTriggersCallback(this, ctxt));
        }

        protected class GetNumberOfTriggersCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;


            public GetNumberOfTriggersCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetNumberOfTriggers(conn, ctxt);
            }
        }


        protected virtual int GetNumberOfTriggers(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
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
        public int GetNumberOfCalendars(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (int)ExecuteWithoutLock(new GetNumberOfCalendarsCallback(this, ctxt));
        }

        protected class GetNumberOfCalendarsCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;

            public GetNumberOfCalendarsCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetNumberOfCalendars(conn, ctxt);
            }
        }

        protected virtual int GetNumberOfCalendars(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
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
        public string[] GetJobNames(SchedulingContext ctxt, string groupName)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetJobNamesCallback(this, ctxt, groupName));
        }

        protected class GetJobNamesCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string groupName;


            public GetJobNamesCallback(JobStoreSupport js, SchedulingContext ctxt, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.groupName = groupName;
            }

            public Object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetJobNames(conn, ctxt, groupName);
            }
        }

        protected virtual String[] GetJobNames(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                        string groupName)
        {
            String[] jobNames;

            try
            {
                jobNames = Delegate.SelectJobsInGroup(conn, groupName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain job names: " + e.Message, e);
            }

            return jobNames;
        }


        /// <summary>
        /// Get the names of all of the <see cref="Trigger" /> s
        /// that have the given group name.
        /// </summary>
        /// <remarks>
        /// If there are no triggers in the given group name, the result should be a
        /// zero-length array (not <see langword="null" />).
        /// </remarks>
        public string[] GetTriggerNames(SchedulingContext ctxt, string groupName)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetTriggerNamesCallback(this, ctxt, groupName));
        }

        protected class GetTriggerNamesCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string groupName;


            public GetTriggerNamesCallback(JobStoreSupport js, SchedulingContext ctxt, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.groupName = groupName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetTriggerNames(conn, ctxt, groupName);
            }
        }

        protected virtual string[] GetTriggerNames(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                            string groupName)
        {
            String[] trigNames;

            try
            {
                trigNames = Delegate.SelectTriggersInGroup(conn, groupName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain trigger names: " + e.Message, e);
            }

            return trigNames;
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
        public String[] GetJobGroupNames(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetJobGroupNamesCallback(this, ctxt));
        }

        protected class GetJobGroupNamesCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;


            public GetJobGroupNamesCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public Object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetJobGroupNames(conn, ctxt);
            }
        }


        protected virtual String[] GetJobGroupNames(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
        {
            String[] groupNames;

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
        /// Get the names of all of the <see cref="Trigger" />
        /// groups.
        /// </summary>
        /// 
        /// <remarks>
        /// If there are no known group names, the result should be a zero-length
        /// array (not <see langword="null" />).
        /// </remarks>
        public String[] GetTriggerGroupNames(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (String[])ExecuteWithoutLock(new GetTriggerGroupNamesCallback(this, ctxt));
        }

        protected class GetTriggerGroupNamesCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;


            public GetTriggerGroupNamesCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetTriggerGroupNames(conn, ctxt);
            }
        }


        protected virtual String[] GetTriggerGroupNames(ConnectionAndTransactionHolder conn,
                                                                 SchedulingContext ctxt)
        {
            String[] groupNames;

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
        public string[] GetCalendarNames(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetCalendarNamesCallback(this, ctxt));
        }

        protected class GetCalendarNamesCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;

            public GetCalendarNamesCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetCalendarNames(conn, ctxt);
            }
        }

        protected virtual string[] GetCalendarNames(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
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
        public Trigger[] GetTriggersForJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            // no locks necessary for read...
            return (Trigger[])ExecuteWithoutLock(new GetTriggersForJobCallback(this, ctxt, jobName, groupName));
        }

        protected class GetTriggersForJobCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string jobName;
            private readonly string groupName;


            public GetTriggersForJobCallback(JobStoreSupport js, SchedulingContext ctxt, string jobName,
                                             string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.jobName = jobName;
                this.groupName = groupName;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetTriggersForJob(conn, ctxt, jobName, groupName);
            }
        }


        protected virtual Trigger[] GetTriggersForJob(ConnectionAndTransactionHolder conn,
                                                               SchedulingContext ctxt, string jobName,
                                                               string groupName)
        {
            Trigger[] array;

            try
            {
                array = Delegate.SelectTriggersForJob(conn, jobName, groupName);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain triggers for job: " + e.Message, e);
            }

            return array;
        }

        /// <summary>
        /// Pause the <see cref="Trigger" /> with the given name.
        /// </summary>
        /// <seealso cref="ResumeTrigger(SchedulingContext, string, string)" />
        public void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new PauseTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class PauseTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string triggerName;
            private readonly string groupName;


            public PauseTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, string triggerName, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.triggerName = triggerName;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.PauseTrigger(conn, ctxt, triggerName, groupName);
            }
        }

        /// <summary>
        /// Pause the <see cref="Trigger" /> with the given name.
        /// </summary>
        /// <seealso cref="SchedulingContext()" />
        public virtual void PauseTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt, string triggerName,
                                         string groupName)
        {
            try
            {
                String oldState = Delegate.SelectTriggerState(conn, triggerName, groupName);

                if (oldState.Equals(StateWaiting) || oldState.Equals(StateAcquired))
                {
                    Delegate.UpdateTriggerState(conn, triggerName, groupName, StatePaused);
                }
                else if (oldState.Equals(StateBlocked))
                {
                    Delegate.UpdateTriggerState(conn, triggerName, groupName, StatePausedBlocked);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't pause trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
            }
        }


        /// <summary>
        /// Pause the <see cref="IJob" /> with the given name - by
        /// pausing all of its current <see cref="Trigger" />s.
        /// </summary>
        /// <seealso cref="ResumeJob(SchedulingContext, string, string)" />
        public virtual void PauseJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new PauseJobCallback(this, ctxt, jobName, groupName));
        }

        protected class PauseJobCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string jobName;
            private readonly string groupName;

            public PauseJobCallback(JobStoreSupport js, SchedulingContext ctxt, string jobName, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.jobName = jobName;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                Trigger[] triggers = js.GetTriggersForJob(conn, ctxt, jobName, groupName);
                for (int j = 0; j < triggers.Length; j++)
                {
                    js.PauseTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
                }
            }
        }

        /// <summary>
        /// Pause all of the <see cref="IJob" />s in the given
        /// group - by pausing all of their <see cref="Trigger" />s.
        /// </summary>
        /// <seealso cref="ResumeJobGroup(SchedulingContext, string)" />
        public virtual void PauseJobGroup(SchedulingContext ctxt, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new PauseJobGroupCallback(this, ctxt, groupName));
        }

        protected class PauseJobGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string groupName;

            public PauseJobGroupCallback(JobStoreSupport js, SchedulingContext ctxt, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                string[] jobNames = js.GetJobNames(conn, ctxt, groupName);

                for (int i = 0; i < jobNames.Length; i++)
                {
                    Trigger[] triggers = js.GetTriggersForJob(conn, ctxt, jobNames[i], groupName);
                    for (int j = 0; j < triggers.Length; j++)
                    {
                        js.PauseTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a Trigger for the given job should be blocked.  
        /// State can only transition to StatePausedBlocked/StateBlocked from 
        /// StatePaused/StateWaiting respectively.
        /// </summary>
        /// <returns>StatePausedBlocked, StateBlocked, or the currentState. </returns>
        protected virtual string CheckBlockedState(
            ConnectionAndTransactionHolder conn, SchedulingContext ctxt, string jobName,
            string jobGroupName, string currentState)
        {
            // State can only transition to BLOCKED from PAUSED or WAITING.
            if ((currentState.Equals(StateWaiting) == false) &&
                (currentState.Equals(StatePaused) == false))
            {
                return currentState;
            }

            try
            {
                IList<FiredTriggerRecord> lst = Delegate.SelectFiredTriggerRecordsByJob(conn,
                                                                    jobName, jobGroupName);

                if (lst.Count > 0)
                {
                    FiredTriggerRecord rec = lst[0];
                    if (rec.JobIsStateful)
                    {
                        // TODO: worry about
                        // failed/recovering/volatile job
                        // states?
                        return (StatePaused.Equals(currentState)) ? StatePausedBlocked : StateBlocked;
                    }
                }

                return currentState;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't determine if trigger should be in a blocked state '"
                    + jobGroupName + "."
                    + jobName + "': "
                    + e.Message, e);
            }
        }


        protected virtual string GetNewStatusForTrigger(ConnectionAndTransactionHolder conn,
                                                                 SchedulingContext ctxt, string jobName,
                                                                 string groupName)
        {
            try
            {
                String newState = StateWaiting;

                IList<FiredTriggerRecord> lst = Delegate.SelectFiredTriggerRecordsByJob(conn, jobName, groupName);

                if (lst.Count > 0)
                {
                    FiredTriggerRecord rec = lst[0];
                    if (rec.JobIsStateful)
                    {
                        // TODO: worry about
                        // failed/recovering/volatile job
                        // states?
                        newState = StateBlocked;
                    }
                }

                return newState;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't determine state for new trigger: " + e.Message, e);
            }
        }

        /*
        * private List findTriggersToBeBlocked(Connection conn, SchedulingContext
        * ctxt, string groupName) throws JobPersistenceException {
        * 
        * try { List blockList = new LinkedList();
        * 
        * List affectingJobs =
        * getDelegate().SelectStatefulJobsOfTriggerGroup(conn, groupName);
        * 
        * Iterator itr = affectingJobs.iterator(); while(itr.hasNext()) { Key
        * jobKey = (Key) itr.next();
        * 
        * List lst = getDelegate().SelectFiredTriggerRecordsByJob(conn,
        * jobKey.getName(), jobKey.getGroup());
        * 
        * This logic is BROKEN...
        * 
        * if(lst.size() > 0) { FiredTriggerRecord rec =
        * (FiredTriggerRecord)lst.get(0); if(rec.isJobIsStateful()) // TODO: worry
        * about failed/recovering/volatile job states? blockList.add(
        * rec.getTriggerKey() ); } }
        * 
        * 
        * return blockList; } catch (SQLException e) { throw new
        * JobPersistenceException ("Couldn't determine states of resumed triggers
        * in group '" + groupName + "': " + e.getMessage(), e); } }
        */


        public virtual void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new ResumeTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class ResumeTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string triggerName;
            private readonly string groupName;

            public ResumeTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, string triggerName,
                                         string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.triggerName = triggerName;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.ResumeTrigger(conn, ctxt, triggerName, groupName);
            }
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="Trigger" /> with the
        /// given name.
        /// </summary>
        /// <remarks>
        /// If the <see cref="Trigger" /> missed one or more fire-times, then the
        /// <see cref="Trigger" />'s misfire instruction will be applied.
        /// </remarks>
        /// <seealso cref="SchedulingContext"/>
        public virtual void ResumeTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                          string triggerName, string groupName)
        {
            try
            {
                TriggerStatus status = Delegate.SelectTriggerStatus(conn, triggerName, groupName);

                if (status == null || !status.NextFireTimeUtc.HasValue || status.NextFireTimeUtc == DateTime.MinValue)
                {
                    return;
                }

                bool blocked = false;
                if (StatePausedBlocked.Equals(status.Status))
                {
                    blocked = true;
                }

                string newState = CheckBlockedState(conn, ctxt, status.JobKey.Name, status.JobKey.Group, StateWaiting);

                bool misfired = false;

                if ((status.NextFireTimeUtc.Value < SystemTime.UtcNow()))
                {
                    misfired = UpdateMisfiredTrigger(conn, ctxt, triggerName, groupName, newState, true);
                }

                if (!misfired)
                {
                    if (blocked)
                    {
                        Delegate.UpdateTriggerStateFromOtherState(conn, triggerName, groupName, newState,
                                                                  StatePausedBlocked);
                    }
                    else
                    {
                        Delegate.UpdateTriggerStateFromOtherState(conn, triggerName, groupName, newState, StatePaused);
                    }
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't resume trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
            }
        }


        /// <summary>
        /// Resume (un-pause) the <see cref="IJob" /> with the
        /// given name.
        /// </summary>
        /// <remarks>
        /// If any of the <see cref="IJob"/>'s <see cref="Trigger" /> s missed one
        /// or more fire-times, then the <see cref="Trigger" />'s misfire
        /// instruction will be applied.
        /// </remarks>
        /// <seealso cref="PauseJob(SchedulingContext, string, string)" />
        public virtual void ResumeJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new ResumeJobCallback(this, ctxt, jobName, groupName));
        }

        protected class ResumeJobCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string jobName;
            private readonly string groupName;

            public ResumeJobCallback(JobStoreSupport js, SchedulingContext ctxt, string jobName, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.jobName = jobName;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                Trigger[] triggers = js.GetTriggersForJob(conn, ctxt, jobName, groupName);
                for (int j = 0; j < triggers.Length; j++)
                {
                    js.ResumeTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
                }
            }
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="IJob" />s in
        /// the given group.
        /// </summary>
        /// <remarks>
        /// If any of the <see cref="IJob" /> s had <see cref="Trigger" /> s that
        /// missed one or more fire-times, then the <see cref="Trigger" />'s
        /// misfire instruction will be applied.
        /// </remarks>
        /// <seealso cref="PauseJobGroup(SchedulingContext, string)" />
        public virtual void ResumeJobGroup(SchedulingContext ctxt, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new ResumeJobGroupCallback(this, ctxt, groupName));
        }

        protected class ResumeJobGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string groupName;

            public ResumeJobGroupCallback(JobStoreSupport js, SchedulingContext ctxt, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                String[] jobNames = js.GetJobNames(conn, ctxt, groupName);

                for (int i = 0; i < jobNames.Length; i++)
                {
                    Trigger[] triggers = js.GetTriggersForJob(conn, ctxt, jobNames[i], groupName);
                    for (int j = 0; j < triggers.Length; j++)
                    {
                        js.ResumeTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
                    }
                }
            }
        }

        /// <summary>
        /// Pause all of the <see cref="Trigger" />s in the given group.
        /// </summary>
        /// <seealso cref="ResumeTriggerGroup(SchedulingContext, string)" />
        public virtual void PauseTriggerGroup(SchedulingContext ctxt, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new PauseTriggerGroupCallback(this, ctxt, groupName));
        }

        protected class PauseTriggerGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string groupName;


            public PauseTriggerGroupCallback(JobStoreSupport js, SchedulingContext ctxt, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.PauseTriggerGroup(conn, ctxt, groupName);
            }
        }

        /// <summary>
        /// Pause all of the <see cref="Trigger" />s in the given group.
        /// </summary>
        /// <seealso cref="SchedulingContext()" />
        public virtual void PauseTriggerGroup(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                              string groupName)
        {
            try
            {
                Delegate.UpdateTriggerGroupStateFromOtherStates(conn, groupName, StatePaused,
                                                                StateAcquired, StateWaiting,
                                                                StateWaiting);

                Delegate.UpdateTriggerGroupStateFromOtherState(conn, groupName, StatePausedBlocked,
                                                               StateBlocked);

                if (!Delegate.IsTriggerGroupPaused(conn, groupName))
                {
                    Delegate.InsertPausedTriggerGroup(conn, groupName);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't pause trigger group '" + groupName + "': " + e.Message, e);
            }
        }


        public ICollection<string> GetPausedTriggerGroups(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (ICollection<string>) ExecuteWithoutLock(new GetPausedTriggerGroupsCallback(this, ctxt));
        }

        protected class GetPausedTriggerGroupsCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;


            public GetPausedTriggerGroupsCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public Object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.GetPausedTriggerGroups(conn, ctxt);
            }
        }


        /// <summary> 
        /// Pause all of the <see cref="Trigger" />s in the
        /// given group.
        /// </summary>
        /// <seealso cref="SchedulingContext()" />
        public virtual ICollection<string> GetPausedTriggerGroups(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
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


        public virtual void ResumeTriggerGroup(SchedulingContext ctxt, string groupName)
        {
            ExecuteInLock(LockTriggerAccess, new ResumeTriggerGroupCallback(this, ctxt, groupName));
        }

        protected class ResumeTriggerGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly string groupName;


            public ResumeTriggerGroupCallback(JobStoreSupport js, SchedulingContext ctxt, string groupName)
                : base(js)
            {
                this.ctxt = ctxt;
                this.groupName = groupName;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.ResumeTriggerGroup(conn, ctxt, groupName);
            }
        }


        /// <summary>
        /// Resume (un-pause) all of the <see cref="Trigger" />s
        /// in the given group.
        /// <p>
        /// If any <see cref="Trigger" /> missed one or more fire-times, then the
        /// <see cref="Trigger" />'s misfire instruction will be applied.
        /// </p>
        /// </summary>
        /// <seealso cref="SchedulingContext()" />
        public virtual void ResumeTriggerGroup(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                               string groupName)
        {
            try
            {
                Delegate.DeletePausedTriggerGroup(conn, groupName);

                String[] trigNames = Delegate.SelectTriggersInGroup(conn, groupName);

                for (int i = 0; i < trigNames.Length; i++)
                {
                    ResumeTrigger(conn, ctxt, trigNames[i], groupName);
                }

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
                * List blockedTriggers = findTriggersToBeBlocked(conn, ctxt,
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
                * StateBlocked; UpdateMisfiredTrigger(conn, ctxt,
                * misfires[i].getName(), misfires[i].getGroup(), newState, true); } }
                */
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't pause trigger group '" + groupName + "': " + e.Message, e);
            }
        }

        public virtual void PauseAll(SchedulingContext ctxt)
        {
            ExecuteInLock(LockTriggerAccess, new PauseAllCallback(this, ctxt));
        }

        protected class PauseAllCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;


            public PauseAllCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.PauseAll(conn, ctxt);
            }
        }

        /// <summary>
        /// Pause all triggers - equivalent of calling <see cref="PauseTriggerGroup(SchedulingContext,string)" />
        /// on every group.
        /// <p>
        /// When <see cref="ResumeAll(SchedulingContext)" /> is called (to un-pause), trigger misfire
        /// instructions WILL be applied.
        /// </p>
        /// </summary>
        /// <seealso cref="ResumeAll(SchedulingContext)" />
        /// <seealso cref="String" />
        public virtual void PauseAll(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
        {
            String[] names = GetTriggerGroupNames(conn, ctxt);

            for (int i = 0; i < names.Length; i++)
            {
                PauseTriggerGroup(conn, ctxt, names[i]);
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
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroup(SchedulingContext,string)" />
        /// on every group.
        /// </summary>
        /// <remarks>
        /// If any <see cref="Trigger" /> missed one or more fire-times, then the
        /// <see cref="Trigger" />'s misfire instruction will be applied.
        /// </remarks>
        /// <seealso cref="PauseAll(SchedulingContext)" />
        public virtual void ResumeAll(SchedulingContext ctxt)
        {
            ExecuteInLock(LockTriggerAccess, new ResumeAllCallback(this, ctxt));
        }

        protected class ResumeAllCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;


            public ResumeAllCallback(JobStoreSupport js, SchedulingContext ctxt)
                : base(js)
            {
                this.ctxt = ctxt;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.ResumeAll(conn, ctxt);
            }
        }

        /// <summary>
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroup(SchedulingContext,string)" />
        /// on every group.
        /// <p>
        /// If any <see cref="Trigger" /> missed one or more fire-times, then the
        /// <see cref="Trigger" />'s misfire instruction will be applied.
        /// </p>
        /// </summary>
        /// <seealso cref="PauseAll(SchedulingContext)" />
        public virtual void ResumeAll(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
        {
            String[] names = GetTriggerGroupNames(conn, ctxt);

            for (int i = 0; i < names.Length; i++)
            {
                ResumeTriggerGroup(conn, ctxt, names[i]);
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
        /// <seealso cref="ReleaseAcquiredTrigger(SchedulingContext, Trigger)" />
        public virtual Trigger AcquireNextTrigger(SchedulingContext ctxt, DateTime noLaterThan)
        {
            if (AcquireTriggersWithinLock)
            {
                return
                    (Trigger)
                    ExecuteInNonManagedTXLock(LockTriggerAccess, new AcquireNextTriggerCallback(this, ctxt, noLaterThan));
            }
            else
            {
                // default behavior since Quartz 1.0.1 release
                return (Trigger)ExecuteInNonManagedTXLock(
                    null, /* passing null as lock name causes no lock to be made */
                    new AcquireNextTriggerCallback(this, ctxt, noLaterThan));
            }
        }

        protected class AcquireNextTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly DateTime noLaterThan;

            public AcquireNextTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, DateTime noLaterThan)
                : base(js)
            {
                this.ctxt = ctxt;
                this.noLaterThan = noLaterThan;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                return js.AcquireNextTrigger(conn, ctxt, noLaterThan);
            }
        }


        // TODO: this really ought to return something like a FiredTriggerBundle,
        // so that the fireInstanceId doesn't have to be on the trigger...

        protected virtual Trigger AcquireNextTrigger(ConnectionAndTransactionHolder conn,
                                                              SchedulingContext ctxt, DateTime noLaterThan)
        {
            do
            {
                try
                {
            	    Trigger nextTrigger = null;
                	
            	    IList<Key> keys = Delegate.SelectTriggerToAcquire(conn, noLaterThan, MisfireTime);

            	    // No trigger is ready to fire yet.
            	    if (keys == null || keys.Count == 0)
            	    {
            	        return null;
            	    }
                	
            	    foreach (Key triggerKey in keys) 
                    {
	                
                        int rowsUpdated = Delegate.UpdateTriggerStateFromOtherState(
                            conn,
                            triggerKey.Name, triggerKey.Group,
                            StateAcquired, StateWaiting);

                        // If our trigger was no longer in the expected state, try a new one.
                        if (rowsUpdated <= 0)
                        {
                            continue;
                        }

                        nextTrigger = RetrieveTrigger(conn, ctxt, triggerKey.Name, triggerKey.Group);

                        // If our trigger is no longer available, try a new one.
                        if (nextTrigger == null)
                        {
                            continue;
                        }
                        break;
                    }

                    // if we didn't end up with a trigger to fire from that first
                    // batch, try again for another batch
                    if (nextTrigger == null)
                    {
                        continue;
                    }
            	
                    nextTrigger.FireInstanceId = FiredTriggerRecordId;
                    Delegate.InsertFiredTrigger(conn, nextTrigger, StateAcquired, null);

                    return nextTrigger;
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException(
                        "Couldn't acquire next trigger: " + e.Message, e);
                }
            } while (true);
        }


        /// <summary>
        /// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
        /// fire the given <see cref="Trigger" />, that it had previously acquired
        /// (reserved).
        /// </summary>
        public void ReleaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger)
        {
            ExecuteInNonManagedTXLock(LockTriggerAccess, new ReleaseAcquiredTriggerCallback(this, ctxt, trigger));
        }

        protected class ReleaseAcquiredTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly Trigger trigger;

            public ReleaseAcquiredTriggerCallback(JobStoreSupport js, SchedulingContext ctxt, Trigger trigger)
                : base(js)
            {
                this.ctxt = ctxt;
                this.trigger = trigger;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.ReleaseAcquiredTrigger(conn, ctxt, trigger);
            }
        }

        protected virtual void ReleaseAcquiredTrigger(ConnectionAndTransactionHolder conn,
                                                               SchedulingContext ctxt, Trigger trigger)
        {
            try
            {
                Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Name, trigger.Group, StateWaiting,
                                                          StateAcquired);
                Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't release acquired trigger: " + e.Message, e);
            }
        }


        public virtual TriggerFiredBundle TriggerFired(SchedulingContext ctxt, Trigger trigger)
        {
            return
                (TriggerFiredBundle)
                ExecuteInNonManagedTXLock(LockTriggerAccess, new TriggerFiredCallback(this, ctxt, trigger));
        }

        protected class TriggerFiredCallback : CallbackSupport, ITransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly Trigger trigger;

            public TriggerFiredCallback(JobStoreSupport js, SchedulingContext ctxt, Trigger trigger)
                : base(js)
            {
                this.ctxt = ctxt;
                this.trigger = trigger;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                try
                {
                    return js.TriggerFired(conn, ctxt, trigger);
                }
                catch (JobPersistenceException jpe)
                {
                    // If job didn't exisit, we still want to commit our work and return null.
                    if (jpe.ErrorCode == SchedulerException.ErrorPersistenceJobDoesNotExist)
                    {
                        return null;
                    }
                    throw;
                }
            }
        }


        protected virtual TriggerFiredBundle TriggerFired(ConnectionAndTransactionHolder conn,
                                                                   SchedulingContext ctxt,
                                                                   Trigger trigger)
        {
            JobDetail job;
            ICalendar cal = null;

            // Make sure trigger wasn't deleted, paused, or completed...
            try
            {
                // if trigger was deleted, state will be StateDeleted
                String state = Delegate.SelectTriggerState(conn, trigger.Name, trigger.Group);
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
                job = RetrieveJob(conn, ctxt, trigger.JobName, trigger.JobGroup);
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
                    Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, StateError);
                }
                catch (Exception sqle)
                {
                    Log.Error("Unable to set trigger state to ERROR.", sqle);
                }
                throw;
            }

            if (trigger.CalendarName != null)
            {
                cal = RetrieveCalendar(conn, ctxt, trigger.CalendarName);
                if (cal == null)
                {
                    return null;
                }
            }

            try
            {
                Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId);
                Delegate.InsertFiredTrigger(conn, trigger, StateExecuting, job);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't insert fired trigger: " + e.Message, e);
            }

            DateTime? prevFireTime = trigger.GetPreviousFireTimeUtc();

            // call triggered - to update the trigger's next-fire-time state...
            trigger.Triggered(cal);

            String state2 = StateWaiting;
            bool force = true;

            if (job.Stateful)
            {
                state2 = StateBlocked;
                force = false;
                try
                {
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, StateBlocked,
                                                                     StateWaiting);
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, StateBlocked,
                                                                     StateAcquired);
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, StatePausedBlocked,
                                                                     StatePaused);
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

            StoreTrigger(conn, ctxt, trigger, job, true, state2, force, false);

            job.JobDataMap.ClearDirtyFlag();

            return new TriggerFiredBundle(
                job, 
                trigger, 
                cal, 
                trigger.Group.Equals(SchedulerConstants.DefaultRecoveryGroup),
                SystemTime.UtcNow(),
                trigger.GetPreviousFireTimeUtc(), 
                prevFireTime, 
                trigger.GetNextFireTimeUtc());
        }


        /// <summary>
        /// Inform the <see cref="IJobStore" /> that the scheduler has completed the
        /// firing of the given <see cref="Trigger" /> (and the execution its
        /// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
        /// in the given <see cref="JobDetail" /> should be updated if the <see cref="IJob" />
        /// is stateful.
        /// </summary>
        public virtual void TriggeredJobComplete(SchedulingContext ctxt, Trigger trigger, JobDetail jobDetail,
                                                 SchedulerInstruction triggerInstCode)
        {
            ExecuteInNonManagedTXLock(LockTriggerAccess,
                                      new TriggeredJobCompleteCallback(this, ctxt, trigger, triggerInstCode, jobDetail));
        }

        protected class TriggeredJobCompleteCallback : CallbackSupport, IVoidTransactionCallback
        {
            private readonly SchedulingContext ctxt;
            private readonly Trigger trigger;
            private readonly SchedulerInstruction triggerInstCode;
            private readonly JobDetail jobDetail;

            public TriggeredJobCompleteCallback(JobStoreSupport js, SchedulingContext ctxt, Trigger trigger,
                                                SchedulerInstruction triggerInstCode, JobDetail jobDetail)
                : base(js)
            {
                this.ctxt = ctxt;
                this.trigger = trigger;
                this.triggerInstCode = triggerInstCode;
                this.jobDetail = jobDetail;
            }

            public void Execute(ConnectionAndTransactionHolder conn)
            {
                js.TriggeredJobComplete(conn, ctxt, trigger, jobDetail, triggerInstCode);
            }
        }


        protected virtual void TriggeredJobComplete(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                                             Trigger trigger,
                                                             JobDetail jobDetail, SchedulerInstruction triggerInstCode)
        {
            try
            {
                if (triggerInstCode == SchedulerInstruction.DeleteTrigger)
                {
                    if (!trigger.GetNextFireTimeUtc().HasValue)
                    {
                        // double check for possible reschedule within job 
                        // execution, which would cancel the need to delete...
                        TriggerStatus stat = Delegate.SelectTriggerStatus(conn, trigger.Name, trigger.Group);
                        if (stat != null && !stat.NextFireTimeUtc.HasValue)
                        {
                            RemoveTrigger(conn, ctxt, trigger.Name, trigger.Group);
                        }
                    }
                    else
                    {
                        RemoveTrigger(conn, ctxt, trigger.Name, trigger.Group);
                        signaler.SignalSchedulingChange(null);
                    }
                }
                else if (triggerInstCode == SchedulerInstruction.SetTriggerComplete)
                {
                    Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, StateComplete);
                    signaler.SignalSchedulingChange(null);
                }
                else if (triggerInstCode == SchedulerInstruction.SetTriggerError)
                {
                    Log.Info("Trigger " + trigger.FullName + " set to ERROR state.");
                    Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, StateError);
                    signaler.SignalSchedulingChange(null);
                }
                else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersComplete)
                {
                    Delegate.UpdateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, StateComplete);
                    signaler.SignalSchedulingChange(null);
                }
                else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersError)
                {
                    Log.Info("All triggers of Job " + trigger.FullJobName + " set to ERROR state.");
                    Delegate.UpdateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, StateError);
                    signaler.SignalSchedulingChange(null);
                }

                if (jobDetail.Stateful)
                {
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
                                                                     StateWaiting, StateBlocked);

                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
                                                                     StatePaused,
                                                                     StatePausedBlocked);
                    signaler.SignalSchedulingChange(null);

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
                                       ?
                                   Delegate.CountMisfiredTriggersInStates(conn, StateMisfired, StateWaiting,
                                                                          MisfireTime)
                                       :
                                   Int32.MaxValue;

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
                    ReleaseLock(conn, LockTriggerAccess, transOwner);
                }
                finally
                {
                    CleanupConnection(conn);
                }
            }
        }


        protected virtual void SignalSchedulingChange(DateTime? candidateNewNextFireTimeUtc)
        {
            signaler.SignalSchedulingChange(candidateNewNextFireTimeUtc);
        }

        //---------------------------------------------------------------------------
        // Cluster management methods
        //---------------------------------------------------------------------------

        protected bool firstCheckIn = true;

        protected DateTime lastCheckin = SystemTime.UtcNow();

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
                    ReleaseLock(conn, LockTriggerAccess, transOwner);
                }
                finally
                {
                    try
                    {
                        ReleaseLock(conn, LockStateAccess, transStateOwner);
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
                    // TODO: revisit when handle self-failed-out implied (see TODO in clusterCheckIn() below)
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
        /// <param name="schedulerStateRecords">List of all current <see cref="SchedulerStateRecord" />s</param>
        private IList<SchedulerStateRecord> FindOrphanedFailedInstances(ConnectionAndTransactionHolder conn, IList<SchedulerStateRecord> schedulerStateRecords)
        {
            IList<SchedulerStateRecord> orphanedInstances = new List<SchedulerStateRecord>();

            ICollection<string> allFiredTriggerInstanceNames = Delegate.SelectFiredTriggerInstanceNames(conn);
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

        protected DateTime CalcFailedIfAfter(SchedulerStateRecord rec)
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

                        KeyHashSet triggerKeys = new KeyHashSet();

                        foreach (FiredTriggerRecord ftRec in firedTriggerRecs)
                        {
                            Key tKey = ftRec.TriggerKey;
                            Key jKey = ftRec.JobKey;

                            triggerKeys.Add(tKey);

                            // release blocked triggers..
                            if (ftRec.FireInstanceState.Equals(StateBlocked))
                            {
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group,
                                                                                 StateWaiting,
                                                                                 StateBlocked);
                            }
                            else if (ftRec.FireInstanceState.Equals(StatePausedBlocked))
                            {
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group,
                                                                                 StatePaused,
                                                                                 StatePausedBlocked);
                            }

                            // release acquired triggers..
                            if (ftRec.FireInstanceState.Equals(StateAcquired))
                            {
                                Delegate.UpdateTriggerStateFromOtherState(conn, tKey.Name, tKey.Group, StateWaiting,
                                                                          StateAcquired);
                                acquiredCount++;
                            }
                            else if (ftRec.JobRequestsRecovery)
                            {
                                // handle jobs marked for recovery that were not fully
                                // executed..
                                if (JobExists(conn, jKey.Name, jKey.Group))
                                {
                                    DateTime tempAux = new DateTime(ftRec.FireTimestamp);
                                    SimpleTrigger rcvryTrig =
                                        new SimpleTrigger(
                                            "recover_" + rec.SchedulerInstanceId + "_" + Convert.ToString(recoverIds++, CultureInfo.InvariantCulture),
                                            SchedulerConstants.DefaultRecoveryGroup, tempAux);
                                    rcvryTrig.Volatile = ftRec.TriggerIsVolatile;
                                    rcvryTrig.JobName = jKey.Name;
                                    rcvryTrig.JobGroup = jKey.Group;
                                    rcvryTrig.MisfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;
                                    rcvryTrig.Priority = ftRec.Priority;
                                    JobDataMap jd = Delegate.SelectTriggerJobDataMap(conn, tKey.Name, tKey.Group);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerName, tKey.Name);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerGroup, tKey.Group);
                                    jd.Put(SchedulerConstants.FailedJobOriginalTriggerFiretimeInMillisecoonds, Convert.ToString(ftRec.FireTimestamp, CultureInfo.InvariantCulture));
                                    rcvryTrig.JobDataMap = jd;

                                    rcvryTrig.ComputeFirstFireTimeUtc(null);
                                    StoreTrigger(conn, null, rcvryTrig, null, false, StateWaiting, false, true);
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
                            if (ftRec.JobIsStateful)
                            {
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group,
                                                                                 StateWaiting,
                                                                                 StateBlocked);
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group,
                                                                                 StatePaused,
                                                                                 StatePausedBlocked);
                            }
                        }

                        Delegate.DeleteFiredTriggers(conn, rec.SchedulerInstanceId);


                        // Check if any of the fired triggers we just deleted were the last fired trigger
                        // records of a COMPLETE trigger.
                        int completeCount = 0;
                        foreach (Key triggerKey in triggerKeys)
                        {
                            if (
                                Delegate.SelectTriggerState(conn, triggerKey.Name, triggerKey.Group).Equals(
                                    StateComplete))
                            {
                                IList<FiredTriggerRecord> firedTriggers =
                                    Delegate.SelectFiredTriggerRecords(conn, triggerKey.Name, triggerKey.Group);
                                if (firedTriggers.Count == 0)
                                {
                                    SchedulingContext schedulingContext = new SchedulingContext();
                                    schedulingContext.InstanceId = instanceId;

                                    if (RemoveTrigger(conn, schedulingContext, triggerKey.Name, triggerKey.Group))
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
                /* TODO if (conn is Proxy) {
                    Proxy connProxy = (Proxy)conn;
                
                    InvocationHandler invocationHandler = 
                        Proxy.getInvocationHandler(connProxy);
                    if (invocationHandler instanceof AttributeRestoringConnectionInvocationHandler) {
                        AttributeRestoringConnectionInvocationHandler connHandler =
                            (AttributeRestoringConnectionInvocationHandler)invocationHandler;
                        
                        connHandler.RestoreOriginalAtributes();
                        CloseConnection(connHandler.WrappedConnection);
                        return;
                    }
                }
                */
                // Wan't a Proxy, or was a Proxy, but wasn't ours.
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
            if (cth != null && cth.Transaction != null)
            {
                try
                {
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
            if (cth.Transaction != null)
            {
                try
                {
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
        /// Implement this interface to provide the code to execute within
        /// the a transaction template.  If no return value is required, execute
        /// should just return null.
        /// </summary>
        /// <seealso cref="JobStoreSupport.ExecuteInNonManagedTXLock(string, ITransactionCallback)" />
        /// <seealso cref="JobStoreSupport.ExecuteInLock(string, ITransactionCallback)" />
        /// <seealso cref="JobStoreSupport.ExecuteWithoutLock(ITransactionCallback)" />
        protected interface ITransactionCallback
        {
            object Execute(ConnectionAndTransactionHolder conn);
        }

        /// <summary>
        /// Implement this interface to provide the code to execute within
        /// the a transaction template that has no return value.
        /// </summary>
        /// <seealso cref="JobStoreSupport.ExecuteInNonManagedTXLock(string, ITransactionCallback)" />
        protected interface IVoidTransactionCallback
        {
            void Execute(ConnectionAndTransactionHolder conn);
        }

        /// <summary>
        /// Execute the given callback in a transaction. Depending on the JobStore, 
        /// the surrounding transaction may be assumed to be already present 
        /// (managed).  
        /// </summary>
        /// <remarks>
        /// This method just forwards to ExecuteInLock() with a null lockName.
        /// </remarks>
        /// <seealso cref="ExecuteInLock(string, ITransactionCallback)" />
        protected object ExecuteWithoutLock(ITransactionCallback txCallback)
        {
            return ExecuteInLock(null, txCallback);
        }

        /// <summary>
        /// Execute the given callback having aquired the given lock.  
        /// Depending on the JobStore, the surrounding transaction may be 
        /// assumed to be already present (managed).  This version is just a 
        /// handy wrapper around executeInLock that doesn't require a return
        /// value.
        /// </summary>
        /// <param name="lockName">
        /// The name of the lock to aquire, for example 
        /// "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
        /// lockCallback is still executed in a transaction. 
        /// </param>
        /// <seealso cref="ExecuteInLock(string, ITransactionCallback)" />
        protected void ExecuteInLock(string lockName, IVoidTransactionCallback txCallback)
        {
            ExecuteInLock(lockName, new ExecuteInLockCallback(this, txCallback));
        }

        protected class ExecuteInLockCallback : CallbackSupport, ITransactionCallback
        {
            private readonly IVoidTransactionCallback txCallback;

            public ExecuteInLockCallback(JobStoreSupport js, IVoidTransactionCallback txCallback)
                : base(js)
            {
                this.txCallback = txCallback;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                txCallback.Execute(conn);
                return null;
            }
        }

        /// <summary>
        /// Execute the given callback having aquired the given lock.  
        /// Depending on the JobStore, the surrounding transaction may be 
        /// assumed to be already present (managed).
        /// </summary> 
        /// <param name="lockName">
        /// The name of the lock to aquire, for example 
        /// "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
        /// lockCallback is still executed in a transaction. 
        /// </param>
        protected abstract object ExecuteInLock(string lockName, ITransactionCallback txCallback);

        /// <summary>
        /// Execute the given callback having optionally aquired the given lock.
        /// This uses the non-managed transaction connection.  This version is just a 
        /// handy wrapper around executeInNonManagedTXLock that doesn't require a return
        /// value.
        /// </summary>
        /// <param name="lockName">
        /// The name of the lock to aquire, for example 
        /// "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
        /// lockCallback is still executed in a non-managed transaction. 
        /// </param>
        /// <seealso cref="ExecuteInNonManagedTXLock(string, ITransactionCallback)" />
        protected void ExecuteInNonManagedTXLock(string lockName, IVoidTransactionCallback txCallback)
        {
            ExecuteInNonManagedTXLock(lockName, new ExecuteInNonManagedTXLockCallback(this, txCallback));
        }

        protected class ExecuteInNonManagedTXLockCallback : CallbackSupport, ITransactionCallback
        {
            private readonly IVoidTransactionCallback txCallback;

            public ExecuteInNonManagedTXLockCallback(JobStoreSupport js, IVoidTransactionCallback txCallback)
                : base(js)
            {
                this.txCallback = txCallback;
            }

            public object Execute(ConnectionAndTransactionHolder conn)
            {
                txCallback.Execute(conn);
                return null;
            }
        }

        /// <summary>
        /// Execute the given callback having optionally aquired the given lock.
        /// This uses the non-managed transaction connection.
        /// </summary>
        /// <param name="lockName">
        /// The name of the lock to aquire, for example 
        /// "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
        /// lockCallback is still executed in a non-managed transaction. 
        /// </param>
        protected object ExecuteInNonManagedTXLock(string lockName, ITransactionCallback txCallback)
        {
            bool transOwner = false;
            ConnectionAndTransactionHolder conn = null;
            try
            {
                if (lockName != null)
                {
                    // If we aren't using db locks, then delay getting DB connection 
                    // until after aquiring the lock since it isn't needed.
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

                object result = txCallback.Execute(conn);
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
                throw new JobPersistenceException("Unexpected runtime exception: " + e.Message, e);
            }
            finally
            {
                try
                {
                    ReleaseLock(conn, lockName, transOwner);
                }
                finally
                {
                    CleanupConnection(conn);
                }
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
            private bool shutdown;
            private int numFails;

            internal ClusterManager(JobStoreSupport jobStoreSupport)
            {
                this.jobStoreSupport = jobStoreSupport;
                Priority = ThreadPriority.AboveNormal;
                Name = "QuartzScheduler_" + jobStoreSupport.instanceName + "-" + jobStoreSupport.instanceId +
                       "_ClusterManager";
                IsBackground = jobStoreSupport.MakeThreadsDaemons;
            }

            public virtual void Initialize()
            {
                Manage();
                Start();
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
                    if (numFails % 4 == 0)
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
                            jobStoreSupport.SignalSchedulingChange(null);
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
                private bool shutdown;
                private int numFails;

                internal MisfireHandler(JobStoreSupport jobStoreSupport)
                {
                    this.jobStoreSupport = jobStoreSupport;
                    Name =
                        string.Format(CultureInfo.InvariantCulture, "QuartzScheduler_{0}-{1}_MisfireHandler", jobStoreSupport.instanceName,
                                      jobStoreSupport.instanceId);
                    IsBackground = jobStoreSupport.MakeThreadsDaemons;
                }

                public virtual void Initialize()
                {
                    //this.Manage();
                    Start();
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
                        if (numFails % 4 == 0)
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
                        DateTime sTime = SystemTime.UtcNow();

                        RecoverMisfiredJobsResult recoverMisfiredJobsResult = Manage();

                        if (recoverMisfiredJobsResult.ProcessedMisfiredTriggerCount > 0)
                        {
                            jobStoreSupport.SignalSchedulingChange(recoverMisfiredJobsResult.EarliestNewTime);
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
                public static readonly RecoverMisfiredJobsResult NoOp = new RecoverMisfiredJobsResult(false, 0, DateTime.MaxValue);

                private readonly bool _hasMoreMisfiredTriggers;
                private readonly int _processedMisfiredTriggerCount;
                private readonly DateTime _earliestNewTimeUtc;

                /// <summary>
                /// Initializes a new instance of the <see cref="RecoverMisfiredJobsResult"/> class.
                /// </summary>
                /// <param name="hasMoreMisfiredTriggers">if set to <c>true</c> [has more misfired triggers].</param>
                /// <param name="processedMisfiredTriggerCount">The processed misfired trigger count.</param>
                /// <param name="earliestNewTimeUtc"></param>
                public RecoverMisfiredJobsResult(bool hasMoreMisfiredTriggers, int processedMisfiredTriggerCount, DateTime earliestNewTimeUtc)
                {
                    _hasMoreMisfiredTriggers = hasMoreMisfiredTriggers;
                    _processedMisfiredTriggerCount = processedMisfiredTriggerCount;
                    _earliestNewTimeUtc = earliestNewTimeUtc;
                }

                /// <summary>
                /// Gets a value indicating whether this instance has more misfired triggers.
                /// </summary>
                /// <value>
                /// 	<c>true</c> if this instance has more misfired triggers; otherwise, <c>false</c>.
                /// </value>
                public bool HasMoreMisfiredTriggers
                {
                    get { return _hasMoreMisfiredTriggers; }
                }

                /// <summary>
                /// Gets the processed misfired trigger count.
                /// </summary>
                /// <value>The processed misfired trigger count.</value>
                public int ProcessedMisfiredTriggerCount
                {
                    get { return _processedMisfiredTriggerCount; }
                }

                public DateTime EarliestNewTime
                {
                    get { return _earliestNewTimeUtc; }
                }
            }
        }
    }
