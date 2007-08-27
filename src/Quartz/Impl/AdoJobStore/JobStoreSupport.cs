/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Collections;
using System.Data;
using System.IO;
using System.Reflection;
using System.Threading;

using Common.Logging;

#if !NET_20
using Nullables;
#endif

using Quartz.Collection;
using Quartz.Core;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// 
    /// </summary>
    public class ConnectionAndTransactionHolder
    {
        private IDbConnection connection;
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
            set { connection = value; }
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
        /// <summary>
        /// Initializes a new instance of the <see cref="JobStoreSupport"/> class.
        /// </summary>
        public JobStoreSupport()
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

        /// <summary> <p>
        /// Set whether String-only properties will be handled in JobDataMaps.
        /// </p>
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

        /// <summary> 
        /// Get or set whether this instance is part of a cluster.
        /// </summary>
        public virtual bool Clustered
        {
            get { return clustered; }
            set { clustered = value; }
        }

        /// <summary>
        /// Get or set the frequency (in milliseconds) at which this instance "checks-in"
        /// with the other instances of the cluster. -- Affects the rate of
        /// detecting failed instances.
        /// </summary>
        public virtual long ClusterCheckinInterval
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
        public virtual long DbRetryInterval
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
        /// Defaults to <code>true</code>, which is safest - some db's (such as 
        /// MS SQLServer) seem to require this to avoid deadlocks under high load,
        /// while others seem to do fine without.  
        /// 
        /// <p>
        /// Setting this property to <code>false</code> will provide a 
        /// significant performance increase during the addition of new jobs 
        /// and triggers.
        /// </p>
        /// </summary>
        public virtual bool LockOnInsert
        {
            get { return lockOnInsert; }
            set { lockOnInsert = value; }
        }

        /// <summary> 
        /// The the number of milliseconds by which a trigger must have missed its
        /// next-fire-time, in order for it to be considered "misfired" and thus
        /// have its misfire instruction applied.
        /// </summary>
        public virtual long MisfireThreshold
        {
            get { return misfireThreshold; }
            set
            {
                if (value < 1)
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

        protected virtual IClassLoadHelper ClassLoadHelper
        {
            get { return classLoadHelper; }
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

        protected ConnectionAndTransactionHolder GetConnection()
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
                    "Failed to obtain DB connection from data source '" + DataSource + "': " + e, e,
                    JobPersistenceException.ERR_PERSISTENCE_CRITICAL_FAILURE);
            }
            if (conn == null)
            {
                throw new JobPersistenceException("Could not get connection from DataSource '" + DataSource + "'");
            }

            try
            {
                if (!DontSetAutoCommitFalse)
                {
                    // TODO SupportClass.TransactionManager.manager.SetAutoCommit(conn, false);
                }

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
                DateTime misfireTime = DateTime.Now;
                if (MisfireThreshold > 0)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold);
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
                if (null == driverDelegate)
                {
                    try
                    {
                        if (delegateTypeName != null)
                        {
                            delegateType = ClassLoadHelper.LoadType(delegateTypeName);
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

        /*
        * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        * 
        * Constants.
        * 
        * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        */

        protected static string LOCK_TRIGGER_ACCESS = "TRIGGER_ACCESS";
        protected static string LOCK_JOB_ACCESS = "JOB_ACCESS";
        protected static string LOCK_CALENDAR_ACCESS = "CALENDAR_ACCESS";
        protected static string LOCK_STATE_ACCESS = "STATE_ACCESS";
        protected static string LOCK_MISFIRE_ACCESS = "MISFIRE_ACCESS";

        /*
        * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        * 
        * Data members.
        * 
        * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        */

        protected string dataSource;
        protected string tablePrefix = DEFAULT_TABLE_PREFIX;
        protected bool useProperties = false;
        protected string instanceId;
        protected string instanceName;
        protected string delegateTypeName;
        protected Type delegateType;
        protected Hashtable calendarCache = new Hashtable();
        private IDriverDelegate driverDelegate;
        private long misfireThreshold = 60000L; // one minute
        private bool dontSetAutoCommitFalse = false;
        private bool clustered = false;
        private bool useDBLocks = false;

        private bool lockOnInsert = true;

        private ISemaphore lockHandler = null; // set in Initialize() method...

        private string selectWithLockSQL = null;

        private long clusterCheckinInterval = 7500L;

        private ClusterManager clusterManagementThread = null;

        private MisfireHandler misfireHandler = null;

        private IClassLoadHelper classLoadHelper;

        private ISchedulerSignaler signaler;

        protected int maxToRecoverAtATime = 20;

        private bool setTxIsolationLevelSequential = false;

        private long dbRetryInterval = 10000;

        private bool makeThreadsDaemons = false;

        private bool doubleCheckLockMisfireHandler = true;

        private readonly ILog log;

        /*
        * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        * 
        * Interface.
        * 
        * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        */

        /// <summary>
        /// Get whether String-only properties will be handled in JobDataMaps.
        /// </summary>
        public virtual bool CanUseProperties
        {
            get { return useProperties; }
        }

        /// <summary>
        /// Called by the QuartzScheduler before the <code>JobStore</code> is
        /// used, in order to give it a chance to Initialize.
        /// </summary>
        public virtual void Initialize(IClassLoadHelper loadHelper, ISchedulerSignaler s)
        {
            if (dataSource == null)
            {
                throw new SchedulerConfigException("DataSource name not set.");
            }

            classLoadHelper = loadHelper;
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
        /// Called by the QuartzScheduler to inform the <code>JobStore</code> that
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

        //---------------------------------------------------------------------------
        // helper methods for subclasses
        //---------------------------------------------------------------------------


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
            ExecuteInNonManagedTXLock(LOCK_TRIGGER_ACCESS, new CleanVolatileTriggerAndJobsCallback(this));
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
            ExecuteInNonManagedTXLock(LOCK_TRIGGER_ACCESS, new RecoverJobsCallback(this));
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
                    Delegate.UpdateTriggerStatesFromOtherStates(conn, STATE_WAITING, STATE_ACQUIRED,
                                                                STATE_BLOCKED);

                rows +=
                    Delegate.UpdateTriggerStatesFromOtherStates(conn, STATE_PAUSED,
                                                                STATE_PAUSED_BLOCKED,
                                                                STATE_PAUSED_BLOCKED);

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
                        recoveringJobTriggers[i].ComputeFirstFireTime(null);
                        StoreTrigger(conn, null, recoveringJobTriggers[i], null, false, STATE_WAITING, false, true);
                    }
                }
                Log.Info("Recovery complete.");

                // remove lingering 'complete' triggers...
                Key[] ct = Delegate.SelectTriggersInState(conn, STATE_COMPLETE);
                for (int i = 0; ct != null && i < ct.Length; i++)
                {
                    RemoveTrigger(conn, null, ct[i].Name, ct[i].Group);
                }
                if (ct != null)
                {
                    Log.Info(string.Format("Removed {0} 'complete' triggers.", ct.Length));
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

            IList misfiredTriggers = new ArrayList();

            // We must still look for the MISFIRED state in case triggers were left 
            // in this state when upgrading to this version that does not support it. 
            bool hasMoreMisfiredTriggers =
                Delegate.SelectMisfiredTriggersInStates(conn, STATE_MISFIRED, STATE_WAITING, MisfireTime,
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
                return RecoverMisfiredJobsResult.NO_OP;
            }

            foreach (Key triggerKey in misfiredTriggers)
            {
                Trigger trig = RetrieveTrigger(conn, triggerKey.Name, triggerKey.Group);

                if (trig == null)
                {
                    continue;
                }

                DoUpdateOfMisfiredTrigger(conn, null, trig, false, STATE_WAITING, recovering);

                signaler.NotifySchedulerListenersFinalized(trig);
            }

            return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count);
        }


        protected virtual bool UpdateMisfiredTrigger(ConnectionAndTransactionHolder conn,
                                                              SchedulingContext ctxt, string triggerName,
                                                              string groupName, string newStateIfNotComplete,
                                                              bool forceState)
        {
            try
            {
                Trigger trig = Delegate.SelectTrigger(conn, triggerName, groupName);

                DateTime misfireTime = DateTime.Now;
                if (MisfireThreshold > 0)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold);
                }

                if (trig.GetNextFireTime().Value > misfireTime)
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
                    "Couldn't update misfired trigger '" + groupName + "."
                    + triggerName + "': " + e.Message, e);
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

            if (!trig.GetNextFireTime().HasValue)
            {
                StoreTrigger(conn, ctxt, trig, null, true, STATE_COMPLETE, forceState, recovering);
            }
            else
            {
                StoreTrigger(conn, ctxt, trig, null, true, newStateIfNotComplete, forceState, false);
            }
        }

        /// <summary>
        /// Store the given <code>{@link org.quartz.JobDetail}</code> and <code>{@link org.quartz.Trigger}</code>.
        /// </summary>
        /// <param name="ctxt">SchedulingContext</param>
        /// <param name="newJob">Job to be stored.</param>
        /// <param name="newTrigger">Trigger to be stored.</param>
        public void StoreJobAndTrigger(SchedulingContext ctxt, JobDetail newJob, Trigger newTrigger)
        {
            ExecuteInLock((LockOnInsert) ? LOCK_TRIGGER_ACCESS : null,
                          new StoreJobAndTriggerCallback(this, newJob, newTrigger, ctxt));
        }

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
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
        /// <param name="groupName"></param>
        /// <returns></returns>
        public bool IsTriggerGroupPaused(SchedulingContext ctxt, string groupName)
        {
            throw new NotImplementedException();
        }

        protected class StoreJobAndTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private JobDetail newJob;
            private Trigger newTrigger;
            private SchedulingContext ctxt;

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
                    jpe.ErrorCode = SchedulerException.ERR_CLIENT_ERROR;
                    throw jpe;
                }

                js.StoreJob(conn, ctxt, newJob, false);
                js.StoreTrigger(conn, ctxt, newTrigger, newJob, false,
                                STATE_WAITING, false, false);
            }
        }


        /// <summary>
        /// Stores the given <see cref="JobDetail" />.
        /// </summary>
        /// <param name="ctxt"></param>
        /// <param name="newJob">The <see cref="JobDetail" /> to be stored.</param>
        /// <param name="replaceExisting">If <see langword="true" />, any <see cref="IJob" /> existing in the
        ///          <see cref="IJobStore" /> with the same name &amp; group should be
        ///         over-written.
        ///     </param>
        public void StoreJob(SchedulingContext ctxt, JobDetail newJob, bool replaceExisting)
        {
            ExecuteInLock(
                (LockOnInsert || replaceExisting) ? LOCK_TRIGGER_ACCESS : null,
                new StoreJobCallback(this, ctxt, newJob, replaceExisting));
        }

        protected class StoreJobCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private JobDetail newJob;
            private bool replaceExisting;

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

        /// <summary> <p>
        /// Check existence of a given job.
        /// </p>
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


        /**
         * <p>
         * Store the given <code>{@link org.quartz.Trigger}</code>.
         * </p>
         * 
         * @param newTrigger
         *          The <code>Trigger</code> to be stored.
         * @param replaceExisting
         *          If <code>true</code>, any <code>Trigger</code> existing in
         *          the <code>JobStore</code> with the same name &amp; group should
         *          be over-written.
         * @throws ObjectAlreadyExistsException
         *           if a <code>Trigger</code> with the same name/group already
         *           exists, and replaceExisting is set to false.
         */

        public void StoreTrigger(SchedulingContext ctxt, Trigger newTrigger, bool replaceExisting)
        {
            ExecuteInLock(
                (LockOnInsert || replaceExisting) ? LOCK_TRIGGER_ACCESS : null,
                new StoreTriggerCallback(this, ctxt, newTrigger, replaceExisting));
        }

        protected class StoreTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private Trigger newTrigger;
            private bool replaceExisting;

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
                js.StoreTrigger(conn, ctxt, newTrigger, null, replaceExisting, STATE_WAITING, false, false);
            }
        }

        /// <summary> <p>
        /// Insert or update a trigger.
        /// </p>
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
                        shouldBepaused = Delegate.IsTriggerGroupPaused(conn, ALL_GROUPS_PAUSED);

                        if (shouldBepaused)
                        {
                            Delegate.InsertPausedTriggerGroup(conn, newTrigger.Group);
                        }
                    }

                    if (shouldBepaused &&
                        (state.Equals(STATE_WAITING) || state.Equals(STATE_ACQUIRED)))
                    {
                        state = STATE_PAUSED;
                    }
                }

                if (job == null)
                {
                    job = Delegate.SelectJobDetail(conn, newTrigger.JobName, newTrigger.JobGroup, ClassLoadHelper);
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
                    if (newTrigger.GetType() == typeof(SimpleTrigger))
                    {
                        Delegate.UpdateSimpleTrigger(conn, (SimpleTrigger)newTrigger);
                    }
                    else if (newTrigger.GetType() == typeof(CronTrigger))
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
                    if (newTrigger.GetType() == typeof(SimpleTrigger))
                    {
                        Delegate.InsertSimpleTrigger(conn, (SimpleTrigger)newTrigger);
                    }
                    else if (newTrigger.GetType() == typeof(CronTrigger))
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
                throw new JobPersistenceException("Couldn't store trigger: " + e.Message, e);
            }
        }

        /// <summary> <p>
        /// Check existence of a given trigger.
        /// </p>
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


        /**
         * <p>
         * Remove (delete) the <code>{@link org.quartz.Job}</code> with the given
         * name, and any <code>{@link org.quartz.Trigger}</code> s that reference
         * it.
         * </p>
         * 
         * <p>
         * If removal of the <code>Job</code> results in an empty group, the
         * group should be removed from the <code>JobStore</code>'s list of
         * known group names.
         * </p>
         * 
         * @param jobName
         *          The name of the <code>Job</code> to be removed.
         * @param groupName
         *          The group name of the <code>Job</code> to be removed.
         * @return <code>true</code> if a <code>Job</code> with the given name &amp;
         *         group was found and removed from the store.
         */

        public bool RemoveJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            return (bool)ExecuteInLock(
                              LOCK_TRIGGER_ACCESS,
                              new RemoveJobCallback(this, ctxt, jobName, groupName));
        }

        protected class RemoveJobCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string jobName;
            private string groupName;


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


        /**
         * Delete a job and its listeners.
         * 
         * @see #removeJob(Connection, SchedulingContext, String, String, boolean)
         * @see #removeTrigger(Connection, SchedulingContext, String, String)
         */

        private bool DeleteJobAndChildren(ConnectionAndTransactionHolder conn, SchedulingContext ctxt, string jobName,
                                          string groupName)
        {
            Delegate.DeleteJobListeners(conn, jobName, groupName);

            return (Delegate.DeleteJobDetail(conn, jobName, groupName) > 0);
        }

        /**
         * Delete a trigger, its listeners, and its Simple/Cron/BLOB sub-table entry.
         * 
         * @see #removeJob(Connection, SchedulingContext, String, String, boolean)
         * @see #removeTrigger(Connection, SchedulingContext, String, String)
         * @see #replaceTrigger(Connection, SchedulingContext, String, String, Trigger)
         */

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

        /**
         * <p>
         * Retrieve the <code>{@link org.quartz.JobDetail}</code> for the given
         * <code>{@link org.quartz.Job}</code>.
         * </p>
         * 
         * @param jobName
         *          The name of the <code>Job</code> to be retrieved.
         * @param groupName
         *          The group name of the <code>Job</code> to be retrieved.
         * @return The desired <code>Job</code>, or null if there is no match.
         */

        public JobDetail RetrieveJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            // no locks necessary for read...
            return (JobDetail)ExecuteWithoutLock(new RetrieveJobCallback(this, ctxt, jobName, groupName));
        }

        protected class RetrieveJobCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string jobName;
            private string groupName;


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
                JobDetail job = Delegate.SelectJobDetail(conn, jobName, groupName, ClassLoadHelper);
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
                    SchedulerException.ERR_PERSISTENCE_JOB_DOES_NOT_EXIST);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't retrieve job: " + e.Message, e);
            }
        }


        /**
         * <p>
         * Remove (delete) the <code>{@link org.quartz.Trigger}</code> with the
         * given name.
         * </p>
         * 
         * <p>
         * If removal of the <code>Trigger</code> results in an empty group, the
         * group should be removed from the <code>JobStore</code>'s list of
         * known group names.
         * </p>
         * 
         * <p>
         * If removal of the <code>Trigger</code> results in an 'orphaned' <code>Job</code>
         * that is not 'durable', then the <code>Job</code> should be deleted
         * also.
         * </p>
         * 
         * @param triggerName
         *          The name of the <code>Trigger</code> to be removed.
         * @param groupName
         *          The group name of the <code>Trigger</code> to be removed.
         * @return <code>true</code> if a <code>Trigger</code> with the given
         *         name &amp; group was found and removed from the store.
         */

        public bool RemoveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            return (bool)ExecuteInLock(
                              LOCK_TRIGGER_ACCESS,
                              new RemoveTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class RemoveTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string triggerName;
            private string groupName;


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
                JobDetail job = Delegate.SelectJobForTrigger(conn, triggerName, groupName, ClassLoadHelper);

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

        /** 
         * @see org.quartz.spi.JobStore#replaceTrigger(org.quartz.core.SchedulingContext, java.lang.String, java.lang.String, org.quartz.Trigger)
         */

        public bool ReplaceTrigger(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger)
        {
            return
                (bool)
                ExecuteInLock(LOCK_TRIGGER_ACCESS,
                              new ReplaceTriggerCallback(this, ctxt, triggerName, groupName, newTrigger));
        }

        protected class ReplaceTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string triggerName;
            private string groupName;
            private Trigger newTrigger;


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
                JobDetail job = Delegate.SelectJobForTrigger(conn, triggerName, groupName, ClassLoadHelper);

                if (job == null)
                {
                    return false;
                }

                if (!newTrigger.JobName.Equals(job.Name) || !newTrigger.JobGroup.Equals(job.Group))
                {
                    throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                }

                bool removedTrigger = DeleteTriggerAndChildren(conn, triggerName, groupName);

                StoreTrigger(conn, ctxt, newTrigger, job, false, STATE_WAITING, false, false);

                return removedTrigger;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
            }
        }

        /**
         * <p>
         * Retrieve the given <code>{@link org.quartz.Trigger}</code>.
         * </p>
         * 
         * @param triggerName
         *          The name of the <code>Trigger</code> to be retrieved.
         * @param groupName
         *          The group name of the <code>Trigger</code> to be retrieved.
         * @return The desired <code>Trigger</code>, or null if there is no
         *         match.
         */

        public Trigger RetrieveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            return (Trigger)ExecuteWithoutLock( // no locks necessary for read...
                                 new RetrieveTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class RetrieveTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string triggerName;
            private string groupName;


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


        /**
         * <p>
         * Get the current state of the identified <code>{@link Trigger}</code>.
         * </p>
         * 
         * @see Trigger#STATE_NORMAL
         * @see Trigger#STATE_PAUSED
         * @see Trigger#STATE_COMPLETE
         * @see Trigger#STATE_ERROR
         * @see Trigger#STATE_NONE
         */

        public TriggerState GetTriggerState(SchedulingContext ctxt, string triggerName, string groupName)
        {
            // no locks necessary for read...
            return (TriggerState)ExecuteWithoutLock(new GetTriggerStateCallback(this, ctxt, triggerName, groupName));
        }

        protected class GetTriggerStateCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string triggerName;
            private string groupName;


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

                if (ts.Equals(STATE_DELETED))
                {
                    return TriggerState.None;
                }

                if (ts.Equals(STATE_COMPLETE))
                {
                    return TriggerState.Complete;
                }

                if (ts.Equals(STATE_PAUSED))
                {
                    return TriggerState.Paused;
                }

                if (ts.Equals(STATE_PAUSED_BLOCKED))
                {
                    return TriggerState.Blocked;
                }

                if (ts.Equals(STATE_ERROR))
                {
                    return TriggerState.Error;
                }

                if (ts.Equals(STATE_BLOCKED))
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

        /**
         * <p>
         * Store the given <code>{@link org.quartz.Calendar}</code>.
         * </p>
         * 
         * @param calName
         *          The name of the calendar.
         * @param calendar
         *          The <code>Calendar</code> to be stored.
         * @param replaceExisting
         *          If <code>true</code>, any <code>Calendar</code> existing
         *          in the <code>JobStore</code> with the same name &amp; group
         *          should be over-written.
         * @throws ObjectAlreadyExistsException
         *           if a <code>Calendar</code> with the same name already
         *           exists, and replaceExisting is set to false.
         */

        public void StoreCalendar(SchedulingContext ctxt, string calName, ICalendar calendar, bool replaceExisting,
                                  bool updateTriggers)
        {
            ExecuteInLock(
                (LockOnInsert || updateTriggers) ? LOCK_TRIGGER_ACCESS : null,
                new StoreCalendarCallback(this, ctxt, calName, calendar, replaceExisting, updateTriggers));
        }

        protected class StoreCalendarCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string calName;
            private ICalendar calendar;
            private bool replaceExisting;
            private bool updateTriggers;


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
                            StoreTrigger(conn, ctxt, trigs[i], null, true, STATE_WAITING, false, false);
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


        /**
         * <p>
         * Remove (delete) the <code>{@link org.quartz.Calendar}</code> with the
         * given name.
         * </p>
         * 
         * <p>
         * If removal of the <code>Calendar</code> would result in
         * <code>Trigger</code>s pointing to non-existent calendars, then a
         * <code>JobPersistenceException</code> will be thrown.</p>
         *       *
         * @param calName The name of the <code>Calendar</code> to be removed.
         * @return <code>true</code> if a <code>Calendar</code> with the given name
         * was found and removed from the store.
         */

        public bool RemoveCalendar(SchedulingContext ctxt, string calName)
        {
            return (bool)ExecuteInLock(LOCK_TRIGGER_ACCESS, new RemoveCalendarCallback(this, ctxt, calName));
        }

        protected class RemoveCalendarCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string calName;


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

        /**
         * <p>
         * Retrieve the given <code>{@link org.quartz.Trigger}</code>.
         * </p>
         * 
         * @param calName
         *          The name of the <code>Calendar</code> to be retrieved.
         * @return The desired <code>Calendar</code>, or null if there is no
         *         match.
         */

        public ICalendar RetrieveCalendar(SchedulingContext ctxt, string calName)
        {
            return (ICalendar)ExecuteWithoutLock( // no locks necessary for read...
                                   new RetrieveCalendarCallback(this, ctxt, calName));
        }

        protected class RetrieveCalendarCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string calName;


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
            ICalendar cal = Clustered ? null : (ICalendar)calendarCache[calName];
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


        /**
         * <p>
         * Get the number of <code>{@link org.quartz.Job}</code> s that are
         * stored in the <code>JobStore</code>.
         * </p>
         */

        public int GetNumberOfJobs(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (int)ExecuteWithoutLock(new GetNumberOfJobsCallback(this, ctxt));
        }

        protected class GetNumberOfJobsCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;

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

        /**
         * <p>
         * Get the number of <code>{@link org.quartz.Trigger}</code> s that are
         * stored in the <code>JobsStore</code>.
         * </p>
         */

        public int GetNumberOfTriggers(SchedulingContext ctxt)
        {
            return (int)ExecuteWithoutLock( // no locks necessary for read...
                             new GetNumberOfTriggersCallback(this, ctxt));
        }

        protected class GetNumberOfTriggersCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;


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

        /**
         * <p>
         * Get the number of <code>{@link org.quartz.Calendar}</code> s that are
         * stored in the <code>JobsStore</code>.
         * </p>
         */

        public int GetNumberOfCalendars(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (int)ExecuteWithoutLock(new GetNumberOfCalendarsCallback(this, ctxt));
        }

        protected class GetNumberOfCalendarsCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;

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

        /**
         * <p>
         * Get the names of all of the <code>{@link org.quartz.Job}</code> s that
         * have the given group name.
         * </p>
         * 
         * <p>
         * If there are no jobs in the given group name, the result should be a
         * zero-length array (not <code>null</code>).
         * </p>
         */

        public string[] GetJobNames(SchedulingContext ctxt, string groupName)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetJobNamesCallback(this, ctxt, groupName));
        }

        protected class GetJobNamesCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string groupName;


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


        /**
         * <p>
         * Get the names of all of the <code>{@link org.quartz.Trigger}</code> s
         * that have the given group name.
         * </p>
         * 
         * <p>
         * If there are no triggers in the given group name, the result should be a
         * zero-length array (not <code>null</code>).
         * </p>
         */

        public string[] GetTriggerNames(SchedulingContext ctxt, string groupName)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetTriggerNamesCallback(this, ctxt, groupName));
        }

        protected class GetTriggerNamesCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string groupName;


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


        /**
         * <p>
         * Get the names of all of the <code>{@link org.quartz.Job}</code>
         * groups.
         * </p>
         * 
         * <p>
         * If there are no known group names, the result should be a zero-length
         * array (not <code>null</code>).
         * </p>
         */

        public String[] GetJobGroupNames(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetJobGroupNamesCallback(this, ctxt));
        }

        protected class GetJobGroupNamesCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;


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

        /**
         * <p>
         * Get the names of all of the <code>{@link org.quartz.Trigger}</code>
         * groups.
         * </p>
         * 
         * <p>
         * If there are no known group names, the result should be a zero-length
         * array (not <code>null</code>).
         * </p>
         */

        public String[] GetTriggerGroupNames(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (String[])ExecuteWithoutLock(new GetTriggerGroupNamesCallback(this, ctxt));
        }

        protected class GetTriggerGroupNamesCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;


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


        /**
         * <p>
         * Get the names of all of the <code>{@link org.quartz.Calendar}</code> s
         * in the <code>JobStore</code>.
         * </p>
         * 
         * <p>
         * If there are no Calendars in the given group name, the result should be
         * a zero-length array (not <code>null</code>).
         * </p>
         */

        public string[] GetCalendarNames(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (string[])ExecuteWithoutLock(new GetCalendarNamesCallback(this, ctxt));
        }

        protected class GetCalendarNamesCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;


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


        /**
         * <p>
         * Get all of the Triggers that are associated to the given Job.
         * </p>
         * 
         * <p>
         * If there are no matches, a zero-length array should be returned.
         * </p>
         */

        public Trigger[] GetTriggersForJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            // no locks necessary for read...
            return (Trigger[])ExecuteWithoutLock(new GetTriggersForJobCallback(this, ctxt, jobName, groupName));
        }

        protected class GetTriggersForJobCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private string jobName;
            private string groupName;


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


        /**
         * <p>
         * Pause the <code>{@link org.quartz.Trigger}</code> with the given name.
         * </p>
          * 
         * @see #resumeTrigger(SchedulingContext, String, String)
         */

        public void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new PauseTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class PauseTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string triggerName;
            private string groupName;


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
        /// Pause the <code>Trigger</code> with the given name.
        /// </summary>
        /// <seealso cref="SchedulingContext()" />
        public virtual void PauseTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt, string triggerName,
                                         string groupName)
        {
            try
            {
                String oldState = Delegate.SelectTriggerState(conn, triggerName, groupName);

                if (oldState.Equals(STATE_WAITING) || oldState.Equals(STATE_ACQUIRED))
                {
                    Delegate.UpdateTriggerState(conn, triggerName, groupName, STATE_PAUSED);
                }
                else if (oldState.Equals(STATE_BLOCKED))
                {
                    Delegate.UpdateTriggerState(conn, triggerName, groupName, STATE_PAUSED_BLOCKED);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't pause trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
            }
        }


        /**
         * <p>
         * Pause the <code>{@link org.quartz.Job}</code> with the given name - by
         * pausing all of its current <code>Trigger</code>s.
         * </p>
         * 
         * @see #resumeJob(SchedulingContext, String, String)
         */

        public virtual void PauseJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new PauseJobCallback(this, ctxt, jobName, groupName));
        }

        protected class PauseJobCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string jobName;
            private string groupName;


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

        /**
         * <p>
         * Pause all of the <code>{@link org.quartz.Job}s</code> in the given
         * group - by pausing all of their <code>Trigger</code>s.
         * </p>
         * 
         * @see #resumeJobGroup(SchedulingContext, String)
         */

        public virtual void PauseJobGroup(SchedulingContext ctxt, string groupName)
        {
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new PauseJobGroupCallback(this, ctxt, groupName));
        }

        protected class PauseJobGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string groupName;


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

        /**
         * Determines if a Trigger for the given job should be blocked.  
         * State can only transition to STATE_PAUSED_BLOCKED/STATE_BLOCKED from 
         * STATE_PAUSED/STATE_WAITING respectively.
         * 
         * @return STATE_PAUSED_BLOCKED, STATE_BLOCKED, or the currentState. 
         */

        protected virtual string CheckBlockedState(
            ConnectionAndTransactionHolder conn, SchedulingContext ctxt, string jobName,
            string jobGroupName, string currentState)
        {
            // State can only transition to BLOCKED from PAUSED or WAITING.
            if ((currentState.Equals(STATE_WAITING) == false) &&
                (currentState.Equals(STATE_PAUSED) == false))
            {
                return currentState;
            }

            try
            {
                IList lst = Delegate.SelectFiredTriggerRecordsByJob(conn,
                                                                    jobName, jobGroupName);

                if (lst.Count > 0)
                {
                    FiredTriggerRecord rec = (FiredTriggerRecord)lst[0];
                    if (rec.JobIsStateful)
                    {
                        // TODO: worry about
                        // failed/recovering/volatile job
                        // states?
                        return (STATE_PAUSED.Equals(currentState)) ? STATE_PAUSED_BLOCKED : STATE_BLOCKED;
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
                String newState = STATE_WAITING;

                IList lst = Delegate.SelectFiredTriggerRecordsByJob(conn, jobName, groupName);

                if (lst.Count > 0)
                {
                    FiredTriggerRecord rec = (FiredTriggerRecord)lst[0];
                    if (rec.JobIsStateful)
                    {
                        // TODO: worry about
                        // failed/recovering/volatile job
                        // states?
                        newState = STATE_BLOCKED;
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
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new ResumeTriggerCallback(this, ctxt, triggerName, groupName));
        }

        protected class ResumeTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string triggerName;
            private string groupName;


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
        /// If the <code>Trigger</code> missed one or more fire-times, then the
        /// <code>Trigger</code>'s misfire instruction will be applied.
        /// </remarks>
        /// <seealso cref="SchedulingContext"/>
        public virtual void ResumeTrigger(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                          string triggerName, string groupName)
        {
            try
            {
                TriggerStatus status = Delegate.SelectTriggerStatus(conn, triggerName, groupName);

                if (status == null || !status.NextFireTime.HasValue || status.NextFireTime == DateTime.MinValue)
                {
                    return;
                }

                bool blocked = false;
                if (STATE_PAUSED_BLOCKED.Equals(status.Status))
                {
                    blocked = true;
                }

                string newState = CheckBlockedState(conn, ctxt, status.JobKey.Name, status.JobKey.Group, STATE_WAITING);

                bool misfired = false;

                if ((status.NextFireTime.Value < DateTime.Now))
                {
                    misfired = UpdateMisfiredTrigger(conn, ctxt, triggerName, groupName, newState, true);
                }

                if (!misfired)
                {
                    if (blocked)
                    {
                        Delegate.UpdateTriggerStateFromOtherState(conn, triggerName, groupName, newState,
                                                                  STATE_PAUSED_BLOCKED);
                    }
                    else
                    {
                        Delegate.UpdateTriggerStateFromOtherState(conn, triggerName, groupName, newState, STATE_PAUSED);
                    }
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    "Couldn't resume trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
            }
        }


        /**
         * <p>
         * Resume (un-pause) the <code>{@link org.quartz.Job}</code> with the
         * given name.
         * </p>
         * 
         * <p>
         * If any of the <code>Job</code>'s<code>Trigger</code> s missed one
         * or more fire-times, then the <code>Trigger</code>'s misfire
         * instruction will be applied.
         * </p>
         * 
         * @see #pauseJob(SchedulingContext, String, String)
         */

        public virtual void ResumeJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new ResumeJobCallback(this, ctxt, jobName, groupName));
        }

        protected class ResumeJobCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string jobName;
            private string groupName;


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

        /**
         * <p>
         * Resume (un-pause) all of the <code>{@link org.quartz.Job}s</code> in
         * the given group.
         * </p>
         * 
         * <p>
         * If any of the <code>Job</code> s had <code>Trigger</code> s that
         * missed one or more fire-times, then the <code>Trigger</code>'s
         * misfire instruction will be applied.
         * </p>
         * 
         * @see #pauseJobGroup(SchedulingContext, String)
         */

        public virtual void ResumeJobGroup(SchedulingContext ctxt, string groupName)
        {
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new ResumeJobGroupCallback(this, ctxt, groupName));
        }

        protected class ResumeJobGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string groupName;


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

        /**
         * <p>
         * Pause all of the <code>{@link org.quartz.Trigger}s</code> in the
         * given group.
         * </p>
         * 
         * @see #resumeTriggerGroup(SchedulingContext, String)
         */

        public virtual void PauseTriggerGroup(SchedulingContext ctxt, string groupName)
        {
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new PauseTriggerGroupCallback(this, ctxt, groupName));
        }


        protected class PauseTriggerGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string groupName;


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
        /// Pause all of the <code>Trigger</code>s in the given group.
        /// </summary>
        /// <seealso cref="SchedulingContext()" />
        public virtual void PauseTriggerGroup(ConnectionAndTransactionHolder conn, SchedulingContext ctxt,
                                              string groupName)
        {
            try
            {
                Delegate.UpdateTriggerGroupStateFromOtherStates(conn, groupName, STATE_PAUSED,
                                                                STATE_ACQUIRED, STATE_WAITING,
                                                                STATE_WAITING);

                Delegate.UpdateTriggerGroupStateFromOtherState(conn, groupName, STATE_PAUSED_BLOCKED,
                                                               STATE_BLOCKED);

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


        public ISet GetPausedTriggerGroups(SchedulingContext ctxt)
        {
            // no locks necessary for read...
            return (ISet)ExecuteWithoutLock(new GetPausedTriggerGroupsCallback(this, ctxt));
        }

        protected class GetPausedTriggerGroupsCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;


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
        /// Pause all of the <code>Trigger</code>s in the
        /// given group.
        /// </summary>
        /// <seealso cref="SchedulingContext()" />
        public virtual ISet GetPausedTriggerGroups(ConnectionAndTransactionHolder conn, SchedulingContext ctxt)
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
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new ResumeTriggerGroupCallback(this, ctxt, groupName));
        }

        protected class ResumeTriggerGroupCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private string groupName;


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
        /// If any <code>Trigger</code> missed one or more fire-times, then the
        /// <code>Trigger</code>'s misfire instruction will be applied.
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
                * groupName, STATE_WAITING, STATE_PAUSED);
                * 
                * if(res > 0) {
                * 
                * long misfireTime = System.currentTimeMillis();
                * if(getMisfireThreshold() > 0) misfireTime -=
                * getMisfireThreshold();
                * 
                * Key[] misfires =
                * getDelegate().SelectMisfiredTriggersInGroupInState(conn,
                * groupName, STATE_WAITING, misfireTime);
                * 
                * List blockedTriggers = findTriggersToBeBlocked(conn, ctxt,
                * groupName);
                * 
                * Iterator itr = blockedTriggers.iterator(); while(itr.hasNext()) {
                * Key key = (Key)itr.next();
                * getDelegate().UpdateTriggerState(conn, key.getName(),
                * key.getGroup(), STATE_BLOCKED); }
                * 
                * for(int i=0; i < misfires.length; i++) {               String
                * newState = STATE_WAITING;
                * if(blockedTriggers.contains(misfires[i])) newState =
                * STATE_BLOCKED; UpdateMisfiredTrigger(conn, ctxt,
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
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new PauseAllCallback(this, ctxt));
        }

        protected class PauseAllCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;


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
        /// Pause all triggers - equivalent of calling <code>PauseTriggerGroup(group)</code>
        /// on every group.
        /// <p>
        /// When <code>ResumeAll()</code> is called (to un-pause), trigger misfire
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
                if (!Delegate.IsTriggerGroupPaused(conn, ALL_GROUPS_PAUSED))
                {
                    Delegate.InsertPausedTriggerGroup(conn, ALL_GROUPS_PAUSED);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't pause all trigger groups: " + e.Message, e);
            }
        }


        /**
         * <p>
         * Resume (un-pause) all triggers - equivalent of calling <code>resumeTriggerGroup(group)</code>
         * on every group.
         * </p>
         * 
         * <p>
         * If any <code>Trigger</code> missed one or more fire-times, then the
         * <code>Trigger</code>'s misfire instruction will be applied.
         * </p>
         * 
         * @see #pauseAll(SchedulingContext)
         */

        public virtual void ResumeAll(SchedulingContext ctxt)
        {
            ExecuteInLock(LOCK_TRIGGER_ACCESS, new ResumeAllCallback(this, ctxt));
        }

        protected class ResumeAllCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;


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
        /// Resume (un-pause) all triggers - equivalent of calling <code>ResumeTriggerGroup(group)</code>
        /// on every group.
        /// <p>
        /// If any <code>Trigger</code> missed one or more fire-times, then the
        /// <code>Trigger</code>'s misfire instruction will be applied.
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
                Delegate.DeletePausedTriggerGroup(conn, ALL_GROUPS_PAUSED);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't resume all trigger groups: " + e.Message, e);
            }
        }

        private static long ftrCtr = DateTime.Now.Ticks;


        /**
         * <p>
         * Get a handle to the next N triggers to be fired, and mark them as 'reserved'
         * by the calling scheduler.
         * </p>
         * 
         * @see #releaseAcquiredTrigger(SchedulingContext, Trigger)
         */

        public virtual Trigger AcquireNextTrigger(SchedulingContext ctxt, DateTime noLaterThan)
        {
            return
                (Trigger)
                ExecuteInNonManagedTXLock(LOCK_TRIGGER_ACCESS, new AcquireNextTriggerCallback(this, ctxt, noLaterThan));
        }

        protected class AcquireNextTriggerCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private DateTime noLaterThan;


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
                    Key triggerKey = Delegate.SelectTriggerToAcquire(conn, noLaterThan, MisfireTime);

                    // No trigger is ready to fire yet.
                    if (triggerKey == null)
                    {
                        return null;
                    }

                    int rowsUpdated = Delegate.UpdateTriggerStateFromOtherState(
                        conn,
                        triggerKey.Name, triggerKey.Group,
                        STATE_ACQUIRED, STATE_WAITING);

                    // If our trigger was no longer in the expected state, try a new one.
                    if (rowsUpdated <= 0)
                    {
                        continue;
                    }

                    Trigger nextTrigger = RetrieveTrigger(conn, ctxt, triggerKey.Name, triggerKey.Group);

                    // If our trigger is no longer available, try a new one.
                    if (nextTrigger == null)
                    {
                        continue;
                    }

                    nextTrigger.FireInstanceId = FiredTriggerRecordId;
                    Delegate.InsertFiredTrigger(conn, nextTrigger, STATE_ACQUIRED, null);

                    return nextTrigger;
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException(
                        "Couldn't acquire next trigger: " + e.Message, e);
                }
            } while (true);
        }


        /**
         * <p>
         * Inform the <code>JobStore</code> that the scheduler no longer plans to
         * fire the given <code>Trigger</code>, that it had previously acquired
         * (reserved).
         * </p>
         */

        public void ReleaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger)
        {
            ExecuteInNonManagedTXLock(LOCK_TRIGGER_ACCESS, new ReleaseAcquiredTriggerCallback(this, ctxt, trigger));
        }

        protected class ReleaseAcquiredTriggerCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private Trigger trigger;


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
                Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Name, trigger.Group, STATE_WAITING,
                                                          STATE_ACQUIRED);
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
                ExecuteInNonManagedTXLock(LOCK_TRIGGER_ACCESS, new TriggerFiredCallback(this, ctxt, trigger));
        }

        protected class TriggerFiredCallback : CallbackSupport, ITransactionCallback
        {
            private SchedulingContext ctxt;
            private Trigger trigger;


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
                    if (jpe.ErrorCode == SchedulerException.ERR_PERSISTENCE_JOB_DOES_NOT_EXIST)
                    {
                        return null;
                    }
                    else
                    {
                        throw jpe;
                    }
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
                // if trigger was deleted, state will be STATE_DELETED
                String state = Delegate.SelectTriggerState(conn, trigger.Name, trigger.Group);
                if (!state.Equals(STATE_ACQUIRED))
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
                    Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, STATE_ERROR);
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
                Delegate.InsertFiredTrigger(conn, trigger, STATE_EXECUTING, job);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't insert fired trigger: " + e.Message, e);
            }

#if !NET_20
            NullableDateTime prevFireTime = trigger.GetPreviousFireTime();
#else
            DateTime? prevFireTime = trigger.GetPreviousFireTime();
#endif

            // call triggered - to update the trigger's next-fire-time state...
            trigger.Triggered(cal);

            String state2 = STATE_WAITING;
            bool force = true;

            if (job.Stateful)
            {
                state2 = STATE_BLOCKED;
                force = false;
                try
                {
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, STATE_BLOCKED,
                                                                     STATE_WAITING);
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, STATE_BLOCKED,
                                                                     STATE_ACQUIRED);
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, STATE_PAUSED_BLOCKED,
                                                                     STATE_PAUSED);
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException("Couldn't update states of blocked triggers: " + e.Message, e);
                }
            }

            if (!trigger.GetNextFireTime().HasValue)
            {
                state2 = STATE_COMPLETE;
                force = true;
            }

            StoreTrigger(conn, ctxt, trigger, job, true, state2, force, false);

            job.JobDataMap.ClearDirtyFlag();

            return new TriggerFiredBundle(
                job, 
                trigger, 
                cal, 
                trigger.Group.Equals(SchedulerConstants.DEFAULT_RECOVERY_GROUP),
                DateTime.Now,
                trigger.GetPreviousFireTime(), 
                prevFireTime, 
                trigger.GetNextFireTime());
        }


        /**
         * <p>
         * Inform the <code>JobStore</code> that the scheduler has completed the
         * firing of the given <code>Trigger</code> (and the execution its
         * associated <code>Job</code>), and that the <code>{@link org.quartz.JobDataMap}</code>
         * in the given <code>JobDetail</code> should be updated if the <code>Job</code>
         * is stateful.
         * </p>
         */

        public virtual void TriggeredJobComplete(SchedulingContext ctxt, Trigger trigger, JobDetail jobDetail,
                                                 SchedulerInstruction triggerInstCode)
        {
            ExecuteInNonManagedTXLock(LOCK_TRIGGER_ACCESS,
                                      new TriggeredJobCompleteCallback(this, ctxt, trigger, triggerInstCode, jobDetail));
        }

        protected class TriggeredJobCompleteCallback : CallbackSupport, IVoidTransactionCallback
        {
            private SchedulingContext ctxt;
            private Trigger trigger;
            private SchedulerInstruction triggerInstCode;
            private JobDetail jobDetail;


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
                    if (!trigger.GetNextFireTime().HasValue)
                    {
                        // double check for possible reschedule within job 
                        // execution, which would cancel the need to delete...
                        TriggerStatus stat = Delegate.SelectTriggerStatus(conn, trigger.Name, trigger.Group);
                        if (stat != null && !stat.NextFireTime.HasValue)
                        {
                            RemoveTrigger(conn, ctxt, trigger.Name, trigger.Group);
                        }
                    }
                    else
                    {
                        RemoveTrigger(conn, ctxt, trigger.Name, trigger.Group);
                    }
                }
                else if (triggerInstCode == SchedulerInstruction.SetTriggerComplete)
                {
                    Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, STATE_COMPLETE);
                }
                else if (triggerInstCode == SchedulerInstruction.SetTriggerError)
                {
                    Log.Info("Trigger " + trigger.FullName + " set to ERROR state.");
                    Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, STATE_ERROR);
                }
                else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersComplete)
                {
                    Delegate.UpdateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, STATE_COMPLETE);
                }
                else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersError)
                {
                    Log.Info("All triggers of Job " + trigger.FullJobName + " set to ERROR state.");
                    Delegate.UpdateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, STATE_ERROR);
                }

                if (jobDetail.Stateful)
                {
                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
                                                                     STATE_WAITING, STATE_BLOCKED);

                    Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
                                                                     STATE_PAUSED,
                                                                     STATE_PAUSED_BLOCKED);

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
                RecoverMisfiredJobsResult result = RecoverMisfiredJobsResult.NO_OP;

                // Before we make the potentially expensive call to acquire the 
                // trigger lock, peek ahead to see if it is likely we would find
                // misfired triggers requiring recovery.
                int misfireCount = (DoubleCheckLockMisfireHandler)
                                       ?
                                   Delegate.CountMisfiredTriggersInStates(conn, STATE_MISFIRED, STATE_WAITING,
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
                    transOwner = LockHandler.ObtainLock(DbProvider.Metadata, conn, LOCK_TRIGGER_ACCESS);

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
                    ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
                }
                finally
                {
                    CleanupConnection(conn);
                }
            }
        }


        protected virtual void SignalSchedulingChange()
        {
            signaler.SignalSchedulingChange();
        }

        //---------------------------------------------------------------------------
        // Cluster management methods
        //---------------------------------------------------------------------------

        protected bool firstCheckIn = true;

        protected DateTime lastCheckin = DateTime.Now;

        protected virtual bool DoCheckin()
        {
            bool transOwner = false;
            bool transStateOwner = false;
            bool recovered = false;

            ConnectionAndTransactionHolder conn = GetNonManagedTXConnection();
            try
            {
                // Other than the first time, always checkin first to make sure there is 
                // work to be done before we aquire the lock (since that is expensive, 
                // and is almost never necessary).  This must be done in a separate
                // transaction to prevent a deadlock under recovery conditions.
                IList failedRecords = null;
                if (firstCheckIn == false)
                {
                    bool succeeded = false;
                    try
                    {
                        failedRecords = ClusterCheckIn(conn);
                        CommitConnection(conn, true);
                        succeeded = true;
                    }
                    catch (JobPersistenceException)
                    {
                        RollbackConnection(conn);
                        throw;
                    }
                    finally
                    {
                        // Only cleanup the connection if we failed and are bailing
                        // as we will otherwise continue to use it.
                        if (succeeded == false)
                        {
                            CleanupConnection(conn);
                        }
                    }
                }

                if (firstCheckIn || (failedRecords.Count > 0))
                {
                    LockHandler.ObtainLock(DbProvider.Metadata, conn, LOCK_STATE_ACCESS);
                    transStateOwner = true;

                    // Now that we own the lock, make sure we still have work to do. 
                    // The first time through, we also need to make sure we update/create our state record
                    failedRecords = (firstCheckIn) ? ClusterCheckIn(conn) : FindFailedInstances(conn);

                    if (failedRecords.Count > 0)
                    {
                        LockHandler.ObtainLock(DbProvider.Metadata, conn, LOCK_TRIGGER_ACCESS);
                        //getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);
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
                    ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
                }
                finally
                {
                    try
                    {
                        ReleaseLock(conn, LOCK_STATE_ACCESS, transStateOwner);
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

        /**
             * Get a list of all scheduler instances in the cluster that may have failed.
             * This includes this scheduler if it is checking in for the first time.
             */

        protected virtual IList FindFailedInstances(ConnectionAndTransactionHolder conn)
        {
            try
            {
                ArrayList failedInstances = new ArrayList();
                bool foundThisScheduler = false;

                IList states = Delegate.SelectSchedulerStateRecords(conn, null);

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
                        if (CalcFailedIfAfter(rec) < DateTime.Now)
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
                lastCheckin = DateTime.Now;
                throw new JobPersistenceException("Failure identifying failed instances when checking-in: "
                                                  + e.Message, e);
            }
        }

        /**
             * Create dummy <code>SchedulerStateRecord</code> objects for fired triggers
             * that have no scheduler state record.  Checkin timestamp and interval are
             * left as zero on these dummy <code>SchedulerStateRecord</code> objects.
             * 
             * @param schedulerStateRecords List of all current <code>SchedulerStateRecords</code>
             */

        private IList FindOrphanedFailedInstances(ConnectionAndTransactionHolder conn, IList schedulerStateRecords)
        {
            IList orphanedInstances = new ArrayList();

            ISet allFiredTriggerInstanceNames = Delegate.SelectFiredTriggerInstanceNames(conn);
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
            return rec.CheckinTimestamp.AddMilliseconds(Math.Max(rec.CheckinInterval, (long)(DateTime.Now - lastCheckin).TotalMilliseconds) + 7500L);
        }

        protected virtual IList ClusterCheckIn(ConnectionAndTransactionHolder conn)
        {
            IList failedInstances = FindFailedInstances(conn);
            try
            {
                // TODO: handle self-failed-out


                // check in...
                lastCheckin = DateTime.Now;
                if (Delegate.UpdateSchedulerState(conn, InstanceId, lastCheckin) == 0)
                {
                    Delegate.InsertSchedulerState(conn, InstanceId, lastCheckin, ClusterCheckinInterval);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Failure updating scheduler state when checking-in: "
                                                  + e.Message, e);
            }

            return failedInstances;
        }


        protected virtual void ClusterRecover(ConnectionAndTransactionHolder conn, IList failedInstances)
        {
            if (failedInstances.Count > 0)
            {
                long recoverIds = DateTime.Now.Ticks;

                LogWarnIfNonZero(failedInstances.Count,
                                 "ClusterManager: detected " + failedInstances.Count + " failed or restarted instances.");
                try
                {
                    foreach (SchedulerStateRecord rec in failedInstances)
                    {
                        Log.Info("ClusterManager: Scanning for instance \"" + rec.SchedulerInstanceId +
                                 "\"'s failed in-progress jobs.");

                        IList firedTriggerRecs =
                            Delegate.SelectInstancesFiredTriggerRecords(conn, rec.SchedulerInstanceId);

                        int acquiredCount = 0;
                        int recoveredCount = 0;
                        int otherCount = 0;

                        ISet triggerKeys = new HashSet();

                        foreach (FiredTriggerRecord ftRec in firedTriggerRecs)
                        {
                            Key tKey = ftRec.TriggerKey;
                            Key jKey = ftRec.JobKey;

                            triggerKeys.Add(tKey);

                            // release blocked triggers..
                            if (ftRec.FireInstanceState.Equals(STATE_BLOCKED))
                            {
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group,
                                                                                 STATE_WAITING,
                                                                                 STATE_BLOCKED);
                            }
                            else if (ftRec.FireInstanceState.Equals(STATE_PAUSED_BLOCKED))
                            {
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group,
                                                                                 STATE_PAUSED,
                                                                                 STATE_PAUSED_BLOCKED);
                            }

                            // release acquired triggers..
                            if (ftRec.FireInstanceState.Equals(STATE_ACQUIRED))
                            {
                                Delegate.UpdateTriggerStateFromOtherState(conn, tKey.Name, tKey.Group, STATE_WAITING,
                                                                          STATE_ACQUIRED);
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
                                            "recover_" + rec.SchedulerInstanceId + "_" + Convert.ToString(recoverIds++),
                                            SchedulerConstants.DEFAULT_RECOVERY_GROUP, tempAux);
                                    rcvryTrig.JobName = jKey.Name;
                                    rcvryTrig.JobGroup = jKey.Group;
                                    rcvryTrig.MisfireInstruction = MisfirePolicy.SimpleTrigger.FireNow;
                                    rcvryTrig.Priority = ftRec.Priority;
                                    JobDataMap jd = Delegate.SelectTriggerJobDataMap(conn, tKey.Name, tKey.Group);
                                    jd.Put(SchedulerConstants.FAILED_JOB_ORIGINAL_TRIGGER_NAME, tKey.Name);
                                    jd.Put(SchedulerConstants.FAILED_JOB_ORIGINAL_TRIGGER_GROUP, tKey.Group);
                                    jd.Put(SchedulerConstants.FAILED_JOB_ORIGINAL_TRIGGER_FIRETIME_IN_MILLISECONDS, Convert.ToString(ftRec.FireTimestamp));
                                    rcvryTrig.JobDataMap = jd;

                                    rcvryTrig.ComputeFirstFireTime(null);
                                    StoreTrigger(conn, null, rcvryTrig, null, false, STATE_WAITING, false, true);
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
                                                                                 STATE_WAITING,
                                                                                 STATE_BLOCKED);
                                Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group,
                                                                                 STATE_PAUSED,
                                                                                 STATE_PAUSED_BLOCKED);
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
                                    STATE_COMPLETE))
                            {
                                IList firedTriggers =
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

        /**
         * <p>
         * Cleanup the given database connection.  This means restoring
         * any modified auto commit or transaction isolation connection
         * attributes, and then closing the underlying connection.
         * </p>
         * 
         * <p>
         * This is separate from closeConnection() because the Spring 
         * integration relies on being able to overload closeConnection() and
         * expects the same connection back that it originally returned
         * from the datasource. 
         * </p>
         * 
         * @see #closeConnection(Connection)
         */

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


        /**
         * Implement this interface to provide the code to execute within
         * the a transaction template.  If no return value is required, execute
         * should just return null.
         * 
         * @see JobStoreSupport#executeInNonManagedTXLock(String, TransactionCallback)
         * @see JobStoreSupport#executeInLock(String, TransactionCallback)
         * @see JobStoreSupport#executeWithoutLock(TransactionCallback)
         */

        protected interface ITransactionCallback
        {
            object Execute(ConnectionAndTransactionHolder conn);
        }

        /**
         * Implement this interface to provide the code to execute within
         * the a transaction template that has no return value.
         * 
         * @see JobStoreSupport#executeInNonManagedTXLock(String, TransactionCallback)
         */

        protected interface IVoidTransactionCallback
        {
            void Execute(ConnectionAndTransactionHolder conn);
        }

        /**
         * Execute the given callback in a transaction. Depending on the JobStore, 
         * the surrounding transaction may be assumed to be already present 
         * (managed).  
         * 
         * <p>
         * This method just forwards to executeInLock() with a null lockName.
         * </p>
         * 
         * @see #executeInLock(String, TransactionCallback)
         */

        protected object ExecuteWithoutLock(ITransactionCallback txCallback)
        {
            return ExecuteInLock(null, txCallback);
        }

        /**
         * Execute the given callback having aquired the given lock.  
         * Depending on the JobStore, the surrounding transaction may be 
         * assumed to be already present (managed).  This version is just a 
         * handy wrapper around executeInLock that doesn't require a return
         * value.
         * 
         * @param lockName The name of the lock to aquire, for example 
         * "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
         * lockCallback is still executed in a transaction. 
         * 
         * @see #executeInLock(String, TransactionCallback)
         */

        protected void ExecuteInLock(string lockName, IVoidTransactionCallback txCallback)
        {
            ExecuteInLock(lockName, new ExecuteInLockCallback(this, txCallback));
        }

        protected class ExecuteInLockCallback : CallbackSupport, ITransactionCallback
        {
            private IVoidTransactionCallback txCallback;

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

        /**
         * Execute the given callback having aquired the given lock.  
         * Depending on the JobStore, the surrounding transaction may be 
         * assumed to be already present (managed).
         * 
         * @param lockName The name of the lock to aquire, for example 
         * "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
         * lockCallback is still executed in a transaction. 
         */

        protected abstract object ExecuteInLock(string lockName, ITransactionCallback txCallback);

        /**
         * Execute the given callback having optionally aquired the given lock.
         * This uses the non-managed transaction connection.  This version is just a 
         * handy wrapper around executeInNonManagedTXLock that doesn't require a return
         * value.
         * 
         * @param lockName The name of the lock to aquire, for example 
         * "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
         * lockCallback is still executed in a non-managed transaction. 
         * 
         * @see #executeInNonManagedTXLock(String, TransactionCallback)
         */

        protected void ExecuteInNonManagedTXLock(string lockName, IVoidTransactionCallback txCallback)
        {
            ExecuteInNonManagedTXLock(lockName, new ExecuteInNonManagedTXLockCallback(this, txCallback));
        }

        protected class ExecuteInNonManagedTXLockCallback : CallbackSupport, ITransactionCallback
        {
            private IVoidTransactionCallback txCallback;


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

        /**
         * Execute the given callback having optionally aquired the given lock.
         * This uses the non-managed transaction connection.
         * 
         * @param lockName The name of the lock to aquire, for example 
         * "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
         * lockCallback is still executed in a non-managed transaction. 
         */

        protected Object ExecuteInNonManagedTXLock(string lockName, ITransactionCallback txCallback)
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
            private JobStoreSupport jobStoreSupport;
            private bool shutdown = false;
            private int numFails = 0;

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
                        long timeToSleep = jobStoreSupport.ClusterCheckinInterval;
                        long transpiredTime = (long)(DateTime.Now - jobStoreSupport.lastCheckin).TotalMilliseconds;
                        timeToSleep = timeToSleep - transpiredTime;
                        if (timeToSleep <= 0)
                        {
                            timeToSleep = 100L;
                        }

                        if (numFails > 0)
                        {
                            timeToSleep = Math.Max(jobStoreSupport.DbRetryInterval, timeToSleep);
                        }

                        try
                        {
                            Thread.Sleep((int)timeToSleep);
                        }
                        catch (ThreadInterruptedException)
                        {
                        }


                        if (!shutdown && Manage())
                        {
                            jobStoreSupport.SignalSchedulingChange();
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
                private JobStoreSupport jobStoreSupport;
                private bool shutdown = false;
                private int numFails = 0;

                internal MisfireHandler(JobStoreSupport jobStoreSupport)
                {
                    this.jobStoreSupport = jobStoreSupport;
                    Name =
                        string.Format("QuartzScheduler_{0}-{1}_MisfireHandler", jobStoreSupport.instanceName,
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
                    return RecoverMisfiredJobsResult.NO_OP;
                }

                public override void Run()
                {
                    while (!shutdown)
                    {
                        DateTime sTime = DateTime.Now;

                        RecoverMisfiredJobsResult recoverMisfiredJobsResult = Manage();

                        if (recoverMisfiredJobsResult.ProcessedMisfiredTriggerCount > 0)
                        {
                            jobStoreSupport.SignalSchedulingChange();
                        }

                        if (!shutdown)
                        {
                            long timeToSleep = 50L; // At least a short pause to help balance threads
                            if (!recoverMisfiredJobsResult.HasMoreMisfiredTriggers)
                            {
                                timeToSleep = jobStoreSupport.MisfireThreshold -
                                              (long)(DateTime.Now - sTime).TotalMilliseconds;
                                if (timeToSleep <= 0)
                                {
                                    timeToSleep = 50L;
                                }

                                if (numFails > 0)
                                {
                                    timeToSleep = Math.Max(jobStoreSupport.DbRetryInterval, timeToSleep);
                                }
                            }

                            try
                            {
                                Thread.Sleep((int)timeToSleep);
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
                public static readonly RecoverMisfiredJobsResult NO_OP = new RecoverMisfiredJobsResult(false, 0);

                private bool _hasMoreMisfiredTriggers;
                private int _processedMisfiredTriggerCount;

                /// <summary>
                /// Initializes a new instance of the <see cref="RecoverMisfiredJobsResult"/> class.
                /// </summary>
                /// <param name="hasMoreMisfiredTriggers">if set to <c>true</c> [has more misfired triggers].</param>
                /// <param name="processedMisfiredTriggerCount">The processed misfired trigger count.</param>
                public RecoverMisfiredJobsResult(bool hasMoreMisfiredTriggers, int processedMisfiredTriggerCount)
                {
                    _hasMoreMisfiredTriggers = hasMoreMisfiredTriggers;
                    _processedMisfiredTriggerCount = processedMisfiredTriggerCount;
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
            }
        }
    }
