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
using System.Data.OleDb;
using System.IO;
using System.Reflection;
using System.Threading;
using Common.Logging;

using Nullables;

using Quartz;
using Quartz.Collection;
using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary> <p>
	/// Contains base functionality for JDBC-based JobStore implementations.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
	/// </author>
	/// <author>James House</author>
	public abstract class JobStoreSupport : AdoConstants, IJobStore
	{
		public ILog Log = LogManager.GetLogger(typeof(JobStoreSupport));
		
		public JobStoreSupport()
		{
			InitBlock();
		}

		private void InitBlock()
		{
			delegateClass = typeof (StdAdoDelegate);
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get the name of the <code>DataSource</code> that should be used for
		/// performing database functions.
		/// </p>
		/// </summary>
		/// <summary> <p>
		/// Set the name of the <code>DataSource</code> that should be used for
		/// performing database functions.
		/// </p>
		/// </summary>
		public virtual string DataSource
		{
			get { return dsName; }

			set { dsName = value; }
		}

		/// <summary> 
		/// Get or sets the prefix that should be pre-pended to all table names.
		/// </summary>
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

				//UPGRADE_NOTE: Exceptions thrown by the equivalent in .NET of method 'java.lang.Boolean.valueOf' may be different. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1099_3"'
				useProperties = Boolean.Parse(value);
			}
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get the instance Id of the Scheduler (must be unique within a cluster).
		/// </p>
		/// </summary>
		/// <summary> <p>
		/// Set the instance Id of the Scheduler (must be unique within a cluster).
		/// </p>
		/// </summary>
		public virtual string InstanceId
		{
			get { return instanceId; }

			set { instanceId = value; }
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get the instance Id of the Scheduler (must be unique within a cluster).
		/// </p>
		/// </summary>
		/// <summary> <p>
		/// Set the instance Id of the Scheduler (must be unique within a cluster).
		/// </p>
		/// </summary>
		public virtual string InstanceName
		{
			get { return instanceName; }

			set { instanceName = value; }
		}

		/// <summary> <p>
		/// Set whether this instance is part of a cluster.
		/// </p>
		/// </summary>
		public virtual bool IsClustered
		{
			set { isClustered_Renamed_Field = value; }
		}

		/// <summary> <p>
		/// Get whether this instance is part of a cluster.
		/// </p>
		/// </summary>
		public virtual bool Clustered
		{
			get { return isClustered_Renamed_Field; }
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get the frequency (in milliseconds) at which this instance "checks-in"
		/// with the other instances of the cluster. -- Affects the rate of
		/// detecting failed instances.
		/// </p>
		/// </summary>
		/// <summary> <p>
		/// Set the frequency (in milliseconds) at which this instance "checks-in"
		/// with the other instances of the cluster. -- Affects the rate of
		/// detecting failed instances.
		/// </p>
		/// </summary>
		public virtual long ClusterCheckinInterval
		{
			get { return clusterCheckinInterval; }

			set { clusterCheckinInterval = value; }
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get the maximum number of misfired triggers that the misfire handling
		/// thread will try to recover at one time (within one transaction).  The
		/// default is 20.
		/// </p>
		/// </summary>
		/// <summary> <p>
		/// Set the maximum number of misfired triggers that the misfire handling
		/// thread will try to recover at one time (within one transaction).  The
		/// default is 20.
		/// </p>
		/// </summary>
		public virtual int MaxMisfiresToHandleAtATime
		{
			get { return maxToRecoverAtATime; }

			set { maxToRecoverAtATime = value; }
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <returns> Returns the dbRetryInterval.
		/// </returns>
		/// <param name="dbRetryInterval">The dbRetryInterval to set.
		/// </param>
		public virtual long DbRetryInterval
		{
			get { return dbRetryInterval; }

			set { dbRetryInterval = value; }
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get whether this instance should use database-based thread
		/// synchronization.
		/// </p>
		/// </summary>
		/// <summary> <p>
		/// Set whether this instance should use database-based thread
		/// synchronization.
		/// </p>
		/// </summary>
		public virtual bool UseDBLocks
		{
			get { return useDBLocks; }

			set { useDBLocks = value; }
		}

		/// <summary> Whether or not to obtain locks when inserting new jobs/triggers.  
		/// Defaults to <code>true</code>, which is safest - some db's (such as 
		/// MS SQLServer) seem to require this to avoid deadlocks under high load,
		/// while others seem to do fine without.  
		/// 
		/// <p>Setting this property to <code>false</code> will provide a 
		/// significant performance increase during the addition of new jobs 
		/// and triggers.</p>
		/// 
		/// </summary>
		/// <param name="">lockOnInsert
		/// </param>
		public virtual bool LockOnInsert
		{
			get { return lockOnInsert; }

			set { lockOnInsert = value; }
		}

		/// <summary> The the number of milliseconds by which a trigger must have missed its
		/// next-fire-time, in order for it to be considered "misfired" and thus
		/// have its misfire instruction applied.
		/// 
		/// </summary>
		/// <param name="">misfireThreshold
		/// </param>
		public virtual long MisfireThreshold
		{
			get { return misfireThreshold; }

			set
			{
				if (value < 1)
				{
					throw new ArgumentException("Misfirethreashold must be larger than 0");
				}
				misfireThreshold = value;
			}
		}

		/// <summary> Don't call set autocommit(false) on connections obtained from the
		/// DataSource. This can be helpfull in a few situations, such as if you
		/// have a driver that complains if it is called when it is already off.
		/// 
		/// </summary>
		/// <param name="">b
		/// </param>
		public virtual bool DontSetAutoCommitFalse
		{
			get { return dontSetAutoCommitFalse; }

			set { dontSetAutoCommitFalse = value; }
		}

		/// <summary> Set the transaction isolation level of DB connections to sequential.
		/// 
		/// </summary>
		/// <param name="">b
		/// </param>
		public virtual bool TxIsolationLevelSerializable
		{
			get { return setTxIsolationLevelSequential; }

			set { setTxIsolationLevelSequential = value; }
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get the JDBC driver delegate class name.
		/// </p>
		/// 
		/// </summary>
		/// <returns> the delegate class name
		/// </returns>
		/// <summary> <p>
		/// Set the JDBC driver delegate class.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">delegateClassName
		/// the delegate class name
		/// </param>
		public virtual string DriverDelegateClass
		{
			get { return delegateClassName; }

			set { delegateClassName = value; }
		}

		/// <summary> <p>
		/// set the SQL statement to use to select and lock a row in the "locks"
		/// table.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="StdRowLockSemaphore">
		/// </seealso>
		public virtual string SelectWithLockSQL
		{
			get { return selectWithLockSQL; }

			set { selectWithLockSQL = value; }
		}

		protected internal virtual IClassLoadHelper ClassLoadHelper
		{
			get { return classLoadHelper; }
		}

		
		protected internal virtual IDbConnection Connection
		{
			get
			{
				try
				{
					
					IDbConnection conn = DBConnectionManager.Instance.GetConnection(DataSource);

					if (conn == null)
					{
						//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
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
							//UPGRADE_TODO: The equivalent in .NET for field 'java.sql.Connection.TRANSACTION_SERIALIZABLE' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
							// TODO SupportClass.TransactionManager.manager.SetTransactionIsolation(conn, (int) IsolationLevel.Serializable);
						}
					}
					catch (OleDbException)
					{
					}

					return conn;
				}
				catch (OleDbException sqle)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException(
						"Failed to obtain DB connection from data source '" + DataSource + "': " + sqle.ToString(), sqle);
				}
				catch (Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException(
						"Failed to obtain DB connection from data source '" + DataSource + "': " + e.ToString(), e,
						JobPersistenceException.ERR_PERSISTENCE_CRITICAL_FAILURE);
				}
			}
		}

		protected internal virtual long MisfireTime
		{
			get
			{
				long misfireTime = (DateTime.Now.Ticks - 621355968000000000)/10000;
				if (MisfireThreshold > 0)
				{
					misfireTime -= MisfireThreshold;
				}

				return misfireTime;
			}
		}

		//UPGRADE_NOTE: Synchronized keyword was removed from method 'getFiredTriggerRecordId'. Lock expression was added. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1027_3"'
		protected internal virtual string FiredTriggerRecordId
		{
			get
			{
				lock (this)
				{
					return InstanceId + ftrCtr++;
				}
			}
		}

		/// <summary> <P>
		/// Get the driver delegate for DB operations.
		/// </p>
		/// </summary>
		protected internal virtual IDriverDelegate Delegate
		{
			get
			{
				if (null == delegate_Renamed)
				{
					try
					{
						if (delegateClassName != null)
						{
							delegateClass = ClassLoadHelper.LoadClass(delegateClassName);
						}

						ConstructorInfo ctor = null;
						Object[] ctorParams = null;
						if (canUseProperties())
						{
							Type[] ctorParamTypes = new Type[] {typeof (ILog), typeof (String), typeof (String), typeof (Boolean)};
							ctor = delegateClass.GetConstructor(ctorParamTypes);
							ctorParams = new Object[] {Log, tablePrefix, instanceId, canUseProperties()};
						}
						else
						{
							Type[] ctorParamTypes = new Type[] {typeof (ILog), typeof (String), typeof (String)};
							ctor = delegateClass.GetConstructor(ctorParamTypes);
							ctorParams = new Object[] {Log, tablePrefix, instanceId};
						}

						delegate_Renamed = (IDriverDelegate) ctor.Invoke(ctorParams);
					}
					catch (Exception e)
					{
						throw new NoSuchDelegateException("Couldn't load delegate class: " + e.Message, e);
					}
				}

				return delegate_Renamed;
			}
		}

		protected internal virtual ISemaphore LockHandler
		{
			get { return lockHandler; }
		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constants.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		protected internal static string LOCK_TRIGGER_ACCESS = "TRIGGER_ACCESS";

		protected internal static string LOCK_JOB_ACCESS = "JOB_ACCESS";

		protected internal static string LOCK_CALENDAR_ACCESS = "CALENDAR_ACCESS";

		protected internal static string LOCK_STATE_ACCESS = "STATE_ACCESS";

		protected internal static string LOCK_MISFIRE_ACCESS = "MISFIRE_ACCESS";

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		protected internal string dsName;

		protected internal string tablePrefix = AdoConstants.DEFAULT_TABLE_PREFIX;

		protected internal bool useProperties = false;

		protected internal string instanceId;

		protected internal string instanceName;

		protected internal string delegateClassName;
		//UPGRADE_NOTE: The initialization of  'delegateClass' was moved to method 'InitBlock'. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1005_3"'
		protected internal Type delegateClass;

		//UPGRADE_TODO: Class 'java.util.HashMap' was converted to 'System.Collections.Hashtable' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashMap_3"'
		protected internal Hashtable calendarCache = new Hashtable();

		private IDriverDelegate delegate_Renamed;

		private long misfireThreshold = 60000L; // one minute

		private bool dontSetAutoCommitFalse = false;

		private bool isClustered_Renamed_Field = false;

		private bool useDBLocks = false;

		private bool lockOnInsert = true;

		private ISemaphore lockHandler = null; // set in Initialize() method...

		private string selectWithLockSQL = null;

		private long clusterCheckinInterval = 7500L;

		private ClusterManager clusterManagementThread = null;

		private MisfireHandler misfireHandler = null;

		private IClassLoadHelper classLoadHelper;

		private ISchedulerSignaler signaler;

		protected internal int maxToRecoverAtATime = 20;

		private bool setTxIsolationLevelSequential = false;

		private long dbRetryInterval = 10000;

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary> <p>
		/// Get whether String-only properties will be handled in JobDataMaps.
		/// </p>
		/// </summary>
		public virtual bool canUseProperties()
		{
			return useProperties;
		}

		/// <summary> <p>
		/// Called by the QuartzScheduler before the <code>JobStore</code> is
		/// used, in order to give the it a chance to Initialize.
		/// </p>
		/// </summary>
		public virtual void Initialize(IClassLoadHelper loadHelper, ISchedulerSignaler s)
		{
			if (dsName == null)
			{
				throw new SchedulerConfigException("DataSource name not set.");
			}

			classLoadHelper = loadHelper;
			signaler = s;

			if (!UseDBLocks && !Clustered)
			{
				Log.Info("Using thread monitor-based data access locking (synchronization).");
				lockHandler = new SimpleSemaphore();
			}
			else
			{
				Log.Info("Using db table-based data access locking (synchronization).");
				lockHandler = new StdRowLockSemaphore(TablePrefix, SelectWithLockSQL);
			}

			if (!Clustered)
			{
				try
				{
					CleanVolatileTriggerAndJobs();
				}
				catch (SchedulerException se)
				{
					throw new SchedulerConfigException("Failure occured during job recovery.", se);
				}
			}
		}

		/// <seealso cref="JobStore.SchedulerStarted()" />
		public virtual void SchedulerStarted()
		{
			if (Clustered)
			{
				clusterManagementThread = new ClusterManager(this, this);
				clusterManagementThread.initialize();
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

			misfireHandler = new MisfireHandler(this, this);
			misfireHandler.initialize();
		}

		/// <summary> <p>
		/// Called by the QuartzScheduler to inform the <code>JobStore</code> that
		/// it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </p>
		/// </summary>
		public virtual void Shutdown()
		{
			if (clusterManagementThread != null)
			{
				clusterManagementThread.shutdown();
			}

			if (misfireHandler != null)
			{
				misfireHandler.Shutdown();
			}

			try
			{
				DBConnectionManager.Instance.Shutdown(DataSource);
			}
			catch (OleDbException sqle)
			{
				Log.Warn("Database connection Shutdown unsuccessful.", sqle);
			}
		}

		public virtual bool SupportsPersistence()
		{
			return true;
		}

		//---------------------------------------------------------------------------
		// helper methods for subclasses
		//---------------------------------------------------------------------------

		
		protected internal virtual void ReleaseLock(IDbConnection conn, string lockName, bool doIt)
		{
			if (doIt && conn != null)
			{
				try
				{
					LockHandler.ReleaseLock(conn, lockName);
				}
				catch (LockException le)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					Log.Error("Error returning lock: " + le.Message, le);
				}
			}
		}

		/// <summary> <p>
		/// Removes all volatile data
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobPersistenceException </throws>
		/// <summary>           if jobs could not be recovered
		/// </summary>
		protected internal abstract void CleanVolatileTriggerAndJobs();

		/// <summary> <p>
		/// Removes all volatile data.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobPersistenceException </throws>
		/// <summary>           if jobs could not be recovered
		/// </summary>
		
		protected internal virtual void CleanVolatileTriggerAndJobs(IDbConnection conn)
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
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't clean volatile data: " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Will recover any failed or misfired jobs and clean up the data store as
		/// appropriate.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobPersistenceException </throws>
		/// <summary>           if jobs could not be recovered
		/// </summary>
		protected internal abstract void RecoverJobs();

		/// <summary> <p>
		/// Will recover any failed or misfired jobs and clean up the data store as
		/// appropriate.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobPersistenceException </throws>
		/// <summary>           if jobs could not be recovered
		/// </summary>
		
		protected internal virtual void RecoverJobs(IDbConnection conn)
		{
			try
			{
				// update inconsistent job states
				int rows =
					Delegate.UpdateTriggerStatesFromOtherStates(conn, AdoConstants.STATE_WAITING, AdoConstants.STATE_ACQUIRED,
					                                            AdoConstants.STATE_BLOCKED);

				rows +=
					Delegate.UpdateTriggerStatesFromOtherStates(conn, AdoConstants.STATE_PAUSED,
					                                            AdoConstants.STATE_PAUSED_BLOCKED,
					                                            AdoConstants.STATE_PAUSED_BLOCKED);

				Log.Info("Freed " + rows + " triggers from 'acquired' / 'blocked' state.");

				// clean up misfired jobs
				Delegate.UpdateTriggerStateFromOtherStatesBeforeTime(conn, AdoConstants.STATE_MISFIRED,
				                                                     AdoConstants.STATE_WAITING, AdoConstants.STATE_WAITING,
				                                                     MisfireTime); // only waiting
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
						StoreTrigger(conn, null, recoveringJobTriggers[i], null, false, AdoConstants.STATE_WAITING, false, true);
					}
				}
				Log.Info("Recovery complete.");

				// remove lingering 'complete' triggers...
				Key[] ct = Delegate.SelectTriggersInState(conn, AdoConstants.STATE_COMPLETE);
				for (int i = 0; ct != null && i < ct.Length; i++)
				{
					RemoveTrigger(conn, null, ct[i].Name, ct[i].Group);
				}
				Log.Info("Removed " + ct.Length + " 'complete' triggers.");

				// clean up any fired trigger entries
				int n = Delegate.DeleteFiredTriggers(conn);
				Log.Info("Removed " + n + " stale fired job entries.");
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't recover jobs: " + e.Message, e);
			}
		}

		private int lastRecoverCount = 0;

		
		protected internal virtual bool RecoverMisfiredJobs(IDbConnection conn, bool recovering)
		{
			Key[] misfiredTriggers = Delegate.SelectTriggersInState(conn, AdoConstants.STATE_MISFIRED);

			if (misfiredTriggers.Length > 0 && misfiredTriggers.Length > MaxMisfiresToHandleAtATime)
			{
				Log.Info("Handling " + MaxMisfiresToHandleAtATime + " of " + misfiredTriggers.Length +
				         " triggers that missed their scheduled fire-time.");
			}
			else if (misfiredTriggers.Length > 0)
			{
				Log.Info("Handling " + misfiredTriggers.Length + " triggers that missed their scheduled fire-time.");
			}
			else
			{
				Log.Debug("Found 0 triggers that missed their scheduled fire-time.");
			}

			lastRecoverCount = misfiredTriggers.Length;

			for (int i = 0; i < misfiredTriggers.Length && i < MaxMisfiresToHandleAtATime; i++)
			{
				Trigger trig = Delegate.SelectTrigger(conn, misfiredTriggers[i].Name, misfiredTriggers[i].Group);

				if (trig == null)
				{
					continue;
				}

				ICalendar cal = null;
				if (trig.CalendarName != null)
				{
					cal = RetrieveCalendar(conn, null, trig.CalendarName);
				}

				String[] listeners = Delegate.SelectTriggerListeners(conn, trig.Name, trig.Group);
				for (int l = 0; l < listeners.Length; ++l)
				{
					trig.AddTriggerListener(listeners[l]);
				}

				signaler.NotifyTriggerListenersMisfired(trig);

				trig.UpdateAfterMisfire(cal);

				if (!trig.GetNextFireTime().HasValue)
				{
					StoreTrigger(conn, null, trig, null, true, AdoConstants.STATE_COMPLETE, false, recovering);
				}
				else
				{
					StoreTrigger(conn, null, trig, null, true, AdoConstants.STATE_WAITING, false, recovering);
				}
			}

			if (misfiredTriggers.Length > MaxMisfiresToHandleAtATime)
			{
				return true;
			}

			return false;
		}

		
		protected internal virtual bool UpdateMisfiredTrigger(IDbConnection conn, SchedulingContext ctxt, string triggerName,
		                                                      string groupName, string newStateIfNotComplete, bool forceState)
		{
			try
			{
				Trigger trig = Delegate.SelectTrigger(conn, triggerName, groupName);

				// TODO
				long misfireTime = (DateTime.Now.Ticks - 621355968000000000)/10000;
				if (MisfireThreshold > 0)
				{
					misfireTime -= MisfireThreshold;
				}

				if (trig.GetNextFireTime().Value.Ticks > misfireTime)
				{
					return false;
				}

				ICalendar cal = null;
				if (trig.CalendarName != null)
				{
					cal = RetrieveCalendar(conn, ctxt, trig.CalendarName);
				}

				signaler.NotifyTriggerListenersMisfired(trig);

				trig.UpdateAfterMisfire(cal);

				if (!trig.GetNextFireTime().HasValue)
				{
					StoreTrigger(conn, ctxt, trig, null, true, AdoConstants.STATE_COMPLETE, forceState, false);
				}
				else
				{
					StoreTrigger(conn, ctxt, trig, null, true, newStateIfNotComplete, forceState, false);
				}

				return true;
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't update misfired trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Insert or update a job.
		/// </p>
		/// </summary>
		
		protected internal virtual void StoreJob(IDbConnection conn, SchedulingContext ctxt, JobDetail newJob,
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
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't store job: " + e.Message, e);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't store job: " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Check existence of a given job.
		/// </p>
		/// </summary>
		
		protected internal virtual bool JobExists(IDbConnection conn, string jobName, string groupName)
		{
			try
			{
				return Delegate.JobExists(conn, jobName, groupName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't determine job existence (" + groupName + "." + jobName + "): " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Insert or update a trigger.
		/// </p>
		/// </summary>
		
		protected internal virtual void StoreTrigger(IDbConnection conn, SchedulingContext ctxt, Trigger newTrigger,
		                                             JobDetail job, bool replaceExisting, string state, bool forceState,
		                                             bool recovering)
		{
			if (newTrigger.Volatile && Clustered)
			{
				Log.Info("note: volatile triggers are effectively non-volatile in a clustered environment.");
			}

			bool existingTrigger = TriggerExists(conn, newTrigger.Name, newTrigger.Group);

			try
			{
				bool shouldBepaused = false;

				if (!forceState)
				{
					shouldBepaused = Delegate.IsTriggerGroupPaused(conn, newTrigger.Group);

					if (!shouldBepaused)
					{
						shouldBepaused = Delegate.IsTriggerGroupPaused(conn, AdoConstants.ALL_GROUPS_PAUSED);

						if (shouldBepaused)
						{
							Delegate.InsertPausedTriggerGroup(conn, newTrigger.Group);
						}
					}

					if (shouldBepaused &&
					    (state.Equals(AdoConstants.STATE_WAITING) || state.Equals(AdoConstants.STATE_ACQUIRED)))
					{
						state = AdoConstants.STATE_PAUSED;
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
					String bstate = GetNewStatusForTrigger(conn, ctxt, job.Name, job.Group);
					if (AdoConstants.STATE_BLOCKED.Equals(bstate) && AdoConstants.STATE_WAITING.Equals(state))
					{
						state = AdoConstants.STATE_BLOCKED;
					}
					if (AdoConstants.STATE_BLOCKED.Equals(bstate) && AdoConstants.STATE_PAUSED.Equals(state))
					{
						state = AdoConstants.STATE_PAUSED_BLOCKED;
					}
				}
				if (existingTrigger)
				{
					if (!replaceExisting)
					{
						throw new ObjectAlreadyExistsException(newTrigger);
					}
					if (newTrigger is SimpleTrigger)
					{
						Delegate.UpdateSimpleTrigger(conn, (SimpleTrigger) newTrigger);
					}
					else if (newTrigger is CronTrigger)
					{
						Delegate.UpdateCronTrigger(conn, (CronTrigger) newTrigger);
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
					if (newTrigger is SimpleTrigger)
					{
						Delegate.InsertSimpleTrigger(conn, (SimpleTrigger) newTrigger);
					}
					else if (newTrigger is CronTrigger)
					{
						Delegate.InsertCronTrigger(conn, (CronTrigger) newTrigger);
					}
					else
					{
						Delegate.InsertBlobTrigger(conn, newTrigger);
					}
				}
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't store trigger: " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Check existence of a given trigger.
		/// </p>
		/// </summary>
		
		protected internal virtual bool TriggerExists(IDbConnection conn, string triggerName, string groupName)
		{
			try
			{
				return Delegate.TriggerExists(conn, triggerName, groupName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't determine trigger existence (" + groupName + "." + triggerName + "): " + e.Message, e);
			}
		}

		
		protected internal virtual bool RemoveJob(IDbConnection conn, SchedulingContext ctxt, string jobName,
		                                          string groupName, bool activeDeleteSafe)
		{
			try
			{
				Key[] jobTriggers = Delegate.SelectTriggerNamesForJob(conn, jobName, groupName);
				for (int i = 0; i < jobTriggers.Length; ++i)
				{
					Delegate.DeleteSimpleTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.DeleteCronTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.DeleteBlobTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.DeleteTriggerListeners(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.DeleteTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
				}

				Delegate.DeleteJobListeners(conn, jobName, groupName);

				if (Delegate.DeleteJobDetail(conn, jobName, groupName) > 0)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't remove job: " + e.Message, e);
			}
		}

		
		protected internal virtual JobDetail RetrieveJob(IDbConnection conn, SchedulingContext ctxt, string jobName,
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
				throw new JobPersistenceException("Couldn't retrieve job because the BLOB couldn't be deserialized: " + e.Message, e,
				                                  SchedulerException.ERR_PERSISTENCE_JOB_DOES_NOT_EXIST);
			}
			catch (Exception e)
			{
				throw new JobPersistenceException("Couldn't retrieve job: " + e.Message, e);
			}	
		}

		
		protected internal virtual bool RemoveTrigger(IDbConnection conn, SchedulingContext ctxt, string triggerName,
		                                              string groupName)
		{
			bool removedTrigger = false;
			try
			{
				// this must be called before we delete the trigger, obviously
				JobDetail job = Delegate.SelectJobForTrigger(conn, triggerName, groupName, ClassLoadHelper);

				Delegate.DeleteSimpleTrigger(conn, triggerName, groupName);
				Delegate.DeleteCronTrigger(conn, triggerName, groupName);
				Delegate.DeleteBlobTrigger(conn, triggerName, groupName);
				Delegate.DeleteTriggerListeners(conn, triggerName, groupName);
				removedTrigger = (Delegate.DeleteTrigger(conn, triggerName, groupName) > 0);

				if (null != job && !job.Durable)
				{
					int numTriggers = Delegate.SelectNumTriggersForJob(conn, job.Name, job.Group);
					if (numTriggers == 0)
					{
						RemoveJob(conn, ctxt, job.Name, job.Group, true);
					}
				}
			}
			catch (Exception e)
			{
				throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
			}

			return removedTrigger;
		}

		
		protected internal virtual bool ReplaceTrigger(IDbConnection conn, SchedulingContext ctxt, string triggerName,
		                                               string groupName, Trigger newTrigger)
		{
			bool removedTrigger = false;
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

				Delegate.DeleteSimpleTrigger(conn, triggerName, groupName);
				Delegate.DeleteCronTrigger(conn, triggerName, groupName);
				Delegate.DeleteBlobTrigger(conn, triggerName, groupName);
				Delegate.DeleteTriggerListeners(conn, triggerName, groupName);
				removedTrigger = (Delegate.DeleteTrigger(conn, triggerName, groupName) > 0);

				StoreTrigger(conn, ctxt, newTrigger, job, false, AdoConstants.STATE_WAITING, false, false);
			}
			catch (Exception e)
			{
				throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
			}
			return removedTrigger;
		}

		
		protected internal virtual Trigger RetrieveTrigger(IDbConnection conn, SchedulingContext ctxt, string triggerName,
		                                                   string groupName)
		{
			try
			{
				Trigger trigger = Delegate.SelectTrigger(conn, triggerName, groupName);
				if (trigger == null)
				{
					return null;
				}
				String[] listeners = Delegate.SelectTriggerListeners(conn, triggerName, groupName);
				for (int i = 0; i < listeners.Length; ++i)
				{
					trigger.AddTriggerListener(listeners[i]);
				}

				return trigger;
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't retrieve trigger: " + e.Message, e);
			}
		}

		
		public virtual int GetTriggerState(IDbConnection conn, SchedulingContext ctxt, string triggerName, string groupName)
		{
			try
			{
				String ts = Delegate.SelectTriggerState(conn, triggerName, groupName);

				if (ts == null)
				{
					return Trigger.STATE_NONE;
				}

				if (ts.Equals(AdoConstants.STATE_DELETED))
				{
					return Trigger.STATE_NONE;
				}

				if (ts.Equals(AdoConstants.STATE_COMPLETE))
				{
					return Trigger.STATE_COMPLETE;
				}

				if (ts.Equals(AdoConstants.STATE_PAUSED))
				{
					return Trigger.STATE_PAUSED;
				}

				if (ts.Equals(AdoConstants.STATE_PAUSED_BLOCKED))
				{
					return Trigger.STATE_PAUSED;
				}

				if (ts.Equals(AdoConstants.STATE_ERROR))
				{
					return Trigger.STATE_ERROR;
				}

				if (ts.Equals(AdoConstants.STATE_BLOCKED))
				{
					return Trigger.STATE_BLOCKED;
				}

				return Trigger.STATE_NORMAL;
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't determine state of trigger (" + groupName + "." + triggerName + "): " + e.Message, e);
			}
		}

		
		protected internal virtual void StoreCalendar(IDbConnection conn, SchedulingContext ctxt, string calName,
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
							StoreTrigger(conn, ctxt, trigs[i], null, true, AdoConstants.STATE_WAITING, false, false);
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

				calendarCache[calName] = calendar; // lazy-cache
			}
			catch (IOException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't store calendar because the BLOB couldn't be serialized: " + e.Message, e);
			}
				//UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't store calendar: " + e.Message, e);
			}
		}

		
		protected internal virtual bool CalendarExists(IDbConnection conn, string calName)
		{
			try
			{
				return Delegate.CalendarExists(conn, calName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't determine calendar existence (" + calName + "): " + e.Message, e);
			}
		}

		
		protected internal virtual bool RemoveCalendar(IDbConnection conn, SchedulingContext ctxt, string calName)
		{
			try
			{
				if (Delegate.CalendarIsReferenced(conn, calName))
				{
					throw new JobPersistenceException("Calender cannot be removed if it referenced by a trigger!");
				}

				calendarCache.Remove(calName);

				return (Delegate.DeleteCalendar(conn, calName) > 0);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't remove calendar: " + e.Message, e);
			}
		}

		
		protected internal virtual ICalendar RetrieveCalendar(IDbConnection conn, SchedulingContext ctxt, string calName)
		{
			// all calendars are persistent, but we lazy-cache them during run
			// time...
			//UPGRADE_TODO: Method 'java.util.HashMap.get' was converted to 'System.Collections.Hashtable.Item' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashMapget_javalangObject_3"'
			ICalendar cal = (ICalendar) calendarCache[calName];
			if (cal != null)
			{
				return cal;
			}

			try
			{
				cal = Delegate.SelectCalendar(conn, calName);
				calendarCache[calName] = cal; // lazy-cache...
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

		
		protected internal virtual int GetNumberOfJobs(IDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.SelectNumJobs(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain number of jobs: " + e.Message, e);
			}
		}

		
		protected internal virtual int GetNumberOfTriggers(IDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.SelectNumTriggers(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain number of triggers: " + e.Message, e);
			}
		}

		
		protected internal virtual int GetNumberOfCalendars(IDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.SelectNumCalendars(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain number of calendars: " + e.Message, e);
			}
		}

		
		protected internal virtual String[] GetJobNames(IDbConnection conn, SchedulingContext ctxt, string groupName)
		{
			String[] jobNames = null;

			try
			{
				jobNames = Delegate.SelectJobsInGroup(conn, groupName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain job names: " + e.Message, e);
			}

			return jobNames;
		}

		
		protected internal virtual String[] GetTriggerNames(IDbConnection conn, SchedulingContext ctxt, string groupName)
		{
			String[] trigNames = null;

			try
			{
				trigNames = Delegate.SelectTriggersInGroup(conn, groupName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain trigger names: " + e.Message, e);
			}

			return trigNames;
		}

		
		protected internal virtual String[] GetJobGroupNames(IDbConnection conn, SchedulingContext ctxt)
		{
			String[] groupNames = null;

			try
			{
				groupNames = Delegate.SelectJobGroups(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain job groups: " + e.Message, e);
			}

			return groupNames;
		}

		
		protected internal virtual String[] GetTriggerGroupNames(IDbConnection conn, SchedulingContext ctxt)
		{
			String[] groupNames = null;

			try
			{
				groupNames = Delegate.SelectTriggerGroups(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
			}

			return groupNames;
		}

		
		protected internal virtual String[] GetCalendarNames(IDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.SelectCalendars(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
			}
		}

		
		protected internal virtual Trigger[] GetTriggersForJob(IDbConnection conn, SchedulingContext ctxt, string jobName,
		                                                       string groupName)
		{
			Trigger[] array = null;

			try
			{
				array = Delegate.SelectTriggersForJob(conn, jobName, groupName);
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain triggers for job: " + e.Message, e);
			}

			return array;
		}

		/// <summary>
		/// Pause the <code>Trigger</code> with the given name.
		/// </summary>
		/// <seealso cref="SchedulingContext(String, String)" />
		public virtual void PauseTrigger(IDbConnection conn, SchedulingContext ctxt, string triggerName, string groupName)
		{
			try
			{
				String oldState = Delegate.SelectTriggerState(conn, triggerName, groupName);

				if (oldState.Equals(AdoConstants.STATE_WAITING) || oldState.Equals(AdoConstants.STATE_ACQUIRED))
				{
					Delegate.UpdateTriggerState(conn, triggerName, groupName, AdoConstants.STATE_PAUSED);
				}
				else if (oldState.Equals(AdoConstants.STATE_BLOCKED))
				{
					Delegate.UpdateTriggerState(conn, triggerName, groupName, AdoConstants.STATE_PAUSED_BLOCKED);
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
			}
		}

		
		protected internal virtual string GetStatusForResumedTrigger(IDbConnection conn, SchedulingContext ctxt,
		                                                             TriggerStatus status)
		{
			try
			{
				String newState = AdoConstants.STATE_WAITING;

				IList lst = Delegate.SelectFiredTriggerRecordsByJob(conn, status.JobKey.Name, status.JobKey.Group);

				if (lst.Count > 0)
				{
					FiredTriggerRecord rec = (FiredTriggerRecord) lst[0];
					if (rec.JobIsStateful)
					{
						// TODO: worry about
						// failed/recovering/volatile job
						// states?
						newState = AdoConstants.STATE_BLOCKED;
					}
				}

				return newState;
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't determine new state in order to resume trigger '" + status.Key.Group + "." + status.Key.Name + "': " +
					e.Message, e);
			}
		}

		
		protected internal virtual string GetNewStatusForTrigger(IDbConnection conn, SchedulingContext ctxt, string jobName,
		                                                         string groupName)
		{
			try
			{
				String newState = AdoConstants.STATE_WAITING;

				IList lst = Delegate.SelectFiredTriggerRecordsByJob(conn, jobName, groupName);

				if (lst.Count > 0)
				{
					FiredTriggerRecord rec = (FiredTriggerRecord) lst[0];
					if (rec.JobIsStateful)
					{
						// TODO: worry about
						// failed/recovering/volatile job
						// states?
						newState = AdoConstants.STATE_BLOCKED;
					}
				}

				return newState;
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
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

		/// <summary>
		/// Resume (un-pause) the <code>{@link org.quartz.Trigger}</code> with the
		/// given name.
		/// <p>
		/// If the <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="SchedulingContext(String, String)"/>
		public virtual void ResumeTrigger(IDbConnection conn, SchedulingContext ctxt, string triggerName, string groupName)
		{
			try
			{
				TriggerStatus status = Delegate.SelectTriggerStatus(conn, triggerName, groupName);

				if (status == null || !status.NextFireTime.HasValue || status.NextFireTime == DateTime.MinValue)
				{
					return;
				}

				bool blocked = false;
				if (AdoConstants.STATE_PAUSED_BLOCKED.Equals(status.Status))
				{
					blocked = true;
				}

				String newState = GetStatusForResumedTrigger(conn, ctxt, status);

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
						                                          AdoConstants.STATE_PAUSED_BLOCKED);
					}
					else
					{
						Delegate.UpdateTriggerStateFromOtherState(conn, triggerName, groupName, newState, AdoConstants.STATE_PAUSED);
					}
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't resume trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
			}
		}

		/// <summary>
		/// Pause all of the <code>Trigger</code>s in the given group.
		/// </summary>
		/// <seealso cref="SchedulingContext(String)" />
		public virtual void PauseTriggerGroup(IDbConnection conn, SchedulingContext ctxt, string groupName)
		{
			try
			{
				Delegate.UpdateTriggerGroupStateFromOtherStates(conn, groupName, AdoConstants.STATE_PAUSED,
				                                                AdoConstants.STATE_ACQUIRED, AdoConstants.STATE_WAITING,
				                                                AdoConstants.STATE_WAITING);

				Delegate.UpdateTriggerGroupStateFromOtherState(conn, groupName, AdoConstants.STATE_PAUSED_BLOCKED,
				                                               AdoConstants.STATE_BLOCKED);

				if (!Delegate.IsTriggerGroupPaused(conn, groupName))
				{
					Delegate.InsertPausedTriggerGroup(conn, groupName);
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause trigger group '" + groupName + "': " + e.Message, e);
			}
		}

		/// <summary> 
		/// Pause all of the <code>Trigger</code>s in the
		/// given group.
		/// </summary>
		/// <seealso cref="SchedulingContext(string)" />
		public virtual ISet GetPausedTriggerGroups(IDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.SelectPausedTriggerGroups(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't determine paused trigger groups: " + e.Message, e);
			}
		}

		/// <summary>
		/// Resume (un-pause) all of the <code>{@link org.quartz.Trigger}s</code>
		/// in the given group.
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="SchedulingContext(string)" />
		public virtual void ResumeTriggerGroup(IDbConnection conn, SchedulingContext ctxt, string groupName)
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
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause trigger group '" + groupName + "': " + e.Message, e);
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
		public virtual void PauseAll(IDbConnection conn, SchedulingContext ctxt)
		{
			String[] names = GetTriggerGroupNames(conn, ctxt);

			for (int i = 0; i < names.Length; i++)
			{
				PauseTriggerGroup(conn, ctxt, names[i]);
			}

			try
			{
				if (!Delegate.IsTriggerGroupPaused(conn, AdoConstants.ALL_GROUPS_PAUSED))
				{
					Delegate.InsertPausedTriggerGroup(conn, AdoConstants.ALL_GROUPS_PAUSED);
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause all trigger groups: " + e.Message, e);
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
		public virtual void ResumeAll(IDbConnection conn, SchedulingContext ctxt)
		{
			String[] names = GetTriggerGroupNames(conn, ctxt);

			for (int i = 0; i < names.Length; i++)
			{
				ResumeTriggerGroup(conn, ctxt, names[i]);
			}

			try
			{
				Delegate.DeletePausedTriggerGroup(conn, AdoConstants.ALL_GROUPS_PAUSED);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't resume all trigger groups: " + e.Message, e);
			}
		}

		private static long ftrCtr = (DateTime.Now.Ticks - 621355968000000000)/10000;

		// TODO: this really ought to return something like a FiredTriggerBundle,
		// so that the fireInstanceId doesn't have to be on the trigger...
		
		protected internal virtual Trigger AcquireNextTrigger(IDbConnection conn, SchedulingContext ctxt, DateTime noLaterThan)
		{
			Trigger nextTrigger = null;

			bool acquiredOne = false;

			do
			{
				try
				{
					Delegate.UpdateTriggerStateFromOtherStatesBeforeTime(conn, AdoConstants.STATE_MISFIRED,
					                                                     AdoConstants.STATE_WAITING, AdoConstants.STATE_WAITING,
					                                                     MisfireTime); // only waiting

					DateTime nextFireTime = Delegate.SelectNextFireTime(conn);

					if (nextFireTime == DateTime.MinValue || nextFireTime > noLaterThan)
					{
						return null;
					}

					Key triggerKey = null;
					do
					{
						triggerKey = Delegate.SelectTriggerForFireTime(conn, nextFireTime);
						if (null != triggerKey)
						{
							int res =
								Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey.Name, triggerKey.Group,
								                                          AdoConstants.STATE_ACQUIRED, AdoConstants.STATE_WAITING);

							if (res <= 0)
							{
								continue;
							}

							nextTrigger = RetrieveTrigger(conn, ctxt, triggerKey.Name, triggerKey.Group);

							if (nextTrigger == null)
							{
								continue;
							}

							nextTrigger.FireInstanceId = FiredTriggerRecordId;
							Delegate.InsertFiredTrigger(conn, nextTrigger, AdoConstants.STATE_ACQUIRED, null);

							acquiredOne = true;
						}
					} while (triggerKey != null && !acquiredOne);
				}
				catch (Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't acquire next trigger: " + e.Message, e);
				}
			} while (!acquiredOne);

			return nextTrigger;
		}

		
		protected internal virtual void ReleaseAcquiredTrigger(IDbConnection conn, SchedulingContext ctxt, Trigger trigger)
		{
			try
			{
				Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Name, trigger.Group, AdoConstants.STATE_WAITING,
				                                          AdoConstants.STATE_ACQUIRED);
				Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't release acquired trigger: " + e.Message, e);
			}
		}

		
		protected internal virtual TriggerFiredBundle TriggerFired(IDbConnection conn, SchedulingContext ctxt,
		                                                           Trigger trigger)
		{
			JobDetail job = null;
			ICalendar cal = null;

			// Make sure trigger wasn't deleted, paused, or completed...
			try
			{
				// if trigger was deleted, state will be STATE_DELETED
				String state = Delegate.SelectTriggerState(conn, trigger.Name, trigger.Group);
				if (!state.Equals(AdoConstants.STATE_ACQUIRED))
				{
					return null;
				}
			}
			catch (OleDbException e)
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
					Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, AdoConstants.STATE_ERROR);
				}
				catch (OleDbException sqle)
				{
					Log.Error("Unable to set trigger state to ERROR.", sqle);
				}
				throw jpe;
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
				Delegate.InsertFiredTrigger(conn, trigger, AdoConstants.STATE_EXECUTING, job);
			}
			catch (OleDbException e)
			{
				throw new JobPersistenceException("Couldn't insert fired trigger: " + e.Message, e);
			}

			NullableDateTime prevFireTime = trigger.GetPreviousFireTime();

			// call triggered - to update the trigger's next-fire-time state...
			trigger.Triggered(cal);

			String state2 = AdoConstants.STATE_WAITING;
			bool force = true;

			if (job.Stateful)
			{
				state2 = AdoConstants.STATE_BLOCKED;
				force = false;
				try
				{
					Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, AdoConstants.STATE_BLOCKED,
					                                                 AdoConstants.STATE_WAITING);
					Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, AdoConstants.STATE_BLOCKED,
					                                                 AdoConstants.STATE_ACQUIRED);
					Delegate.UpdateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, AdoConstants.STATE_PAUSED_BLOCKED,
					                                                 AdoConstants.STATE_PAUSED);
				}
				catch (OleDbException e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't update states of blocked triggers: " + e.Message, e);
				}
			}

			if (!trigger.GetNextFireTime().HasValue)
			{
				state2 = AdoConstants.STATE_COMPLETE;
				force = true;
			}

			StoreTrigger(conn, ctxt, trigger, job, true, state2, force, false);

			job.JobDataMap.ClearDirtyFlag();

			DateTime tempAux = DateTime.Now;

			NullableDateTime tempAux2 = trigger.GetPreviousFireTime();
			NullableDateTime tempAux3 = trigger.GetNextFireTime();
			return
				new TriggerFiredBundle(job, trigger, cal, trigger.Group.Equals(Scheduler_Fields.DEFAULT_RECOVERY_GROUP), tempAux,
				                       tempAux2, prevFireTime, tempAux3);
		}

		
		protected internal virtual void TriggeredJobComplete(IDbConnection conn, SchedulingContext ctxt, Trigger trigger,
		                                                     JobDetail jobDetail, int triggerInstCode)
		{
			try
			{
				if (triggerInstCode == Trigger.INSTRUCTION_DELETE_TRIGGER)
				{
					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					if (!trigger.GetNextFireTime().HasValue)
					{
						// double check for possible reschedule within job 
						// execution, which would cancel the need to delete...
						TriggerStatus stat = Delegate.SelectTriggerStatus(conn, trigger.Name, trigger.Group);
						//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
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
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_TRIGGER_COMPLETE)
				{
					Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, AdoConstants.STATE_COMPLETE);
				}
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_TRIGGER_ERROR)
				{
					Log.Info("Trigger " + trigger.FullName + " set to ERROR state.");
					Delegate.UpdateTriggerState(conn, trigger.Name, trigger.Group, AdoConstants.STATE_ERROR);
				}
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_COMPLETE)
				{
					Delegate.UpdateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, AdoConstants.STATE_COMPLETE);
				}
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_ERROR)
				{
					Log.Info("All triggers of Job " + trigger.FullJobName + " set to ERROR state.");
					Delegate.UpdateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, AdoConstants.STATE_ERROR);
				}

				if (jobDetail.Stateful)
				{
					Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
					                                                 AdoConstants.STATE_WAITING, AdoConstants.STATE_BLOCKED);

					Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
					                                                 AdoConstants.STATE_PAUSED,
					                                                 AdoConstants.STATE_PAUSED_BLOCKED);

					try
					{
						if (jobDetail.JobDataMap.Dirty)
						{
							Delegate.UpdateJobData(conn, jobDetail);
						}
					}
					catch (IOException e)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new JobPersistenceException("Couldn't serialize job data: " + e.Message, e);
					}
					catch (OleDbException e)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new JobPersistenceException("Couldn't update job data: " + e.Message, e);
					}
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't update trigger state(s): " + e.Message, e);
			}

			try
			{
				Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't delete fired trigger: " + e.Message, e);
			}
		}

		//---------------------------------------------------------------------------
		// Management methods
		//---------------------------------------------------------------------------

		protected internal abstract bool DoRecoverMisfires();

		protected internal virtual void SignalSchedulingChange()
		{
			signaler.SignalSchedulingChange();
		}

		//---------------------------------------------------------------------------
		// Cluster management methods
		//---------------------------------------------------------------------------

		protected internal abstract bool DoCheckin();

		protected internal bool firstCheckIn = true;

		protected internal long lastCheckin = (DateTime.Now.Ticks - 621355968000000000)/10000;

		
		protected internal virtual IList ClusterCheckIn(IDbConnection conn)
		{
			IList states = null;
			IList failedInstances = new ArrayList();
			SchedulerStateRecord myLastState = null;
			//bool selfFailed = false;

			// TODO
			long timeNow = (DateTime.Now.Ticks - 621355968000000000)/10000;

			try
			{
				states = Delegate.SelectSchedulerStateRecords(conn, null);

				IEnumerator itr = states.GetEnumerator();

				while (itr.MoveNext())
				{
					SchedulerStateRecord rec = (SchedulerStateRecord) itr.Current;

					// find own record...
					if (rec.SchedulerInstanceId.Equals(InstanceId))
					{
						myLastState = rec;

						// TODO: revisit when handle self-failed-out impled (see TODO below)
						//                    if (rec.getRecoverer() != null && !firstCheckIn) {
						//                        selfFailed = true;
						//                    }
						//                    if (rec.getRecoverer() == null && firstCheckIn) {
						//                        failedInstances.add(rec);
						//                    }
						if (rec.Recoverer == null)
						{
							failedInstances.Add(rec);
						}
					}
					else
					{
						// find failed instances...
						long failedIfAfter = rec.CheckinTimestamp +
						                     Math.Max(rec.CheckinInterval, ((DateTime.Now.Ticks - 621355968000000000)/10000 - lastCheckin)) +
						                     7500L;

						if (failedIfAfter < timeNow && rec.Recoverer == null)
						{
							failedInstances.Add(rec);
						}
					}
				}

				// TODO: handle self-failed-out

				// check in...
				lastCheckin = (DateTime.Now.Ticks - 621355968000000000)/10000;
				if (firstCheckIn)
				{
					Delegate.DeleteSchedulerState(conn, InstanceId);
					Delegate.InsertSchedulerState(conn, InstanceId, lastCheckin, ClusterCheckinInterval, null);
					firstCheckIn = false;
				}
				else
				{
					Delegate.UpdateSchedulerState(conn, InstanceId, lastCheckin);
				}
			}
			catch (Exception e)
			{
				lastCheckin = (DateTime.Now.Ticks - 621355968000000000)/10000;
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Failure checking-in: " + e.Message, e);
			}

			return failedInstances;
		}

		
		protected internal virtual void ClusterRecover(IDbConnection conn, IList failedInstances)
		{
			if (failedInstances.Count > 0)
			{
				long recoverIds = (DateTime.Now.Ticks - 621355968000000000)/10000;

				LogWarnIfNonZero(failedInstances.Count,
				                 "ClusterManager: detected " + failedInstances.Count + " failed or restarted instances.");
				try
				{
					IEnumerator itr = failedInstances.GetEnumerator();
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					while (itr.MoveNext())
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						SchedulerStateRecord rec = (SchedulerStateRecord) itr.Current;

						Log.Info("ClusterManager: Scanning for instance \"" + rec.SchedulerInstanceId + "\"'s failed in-progress jobs.");

						IList firedTriggerRecs = Delegate.SelectInstancesFiredTriggerRecords(conn, rec.SchedulerInstanceId);

						int acquiredCount = 0;
						int recoveredCount = 0;
						int otherCount = 0;

						IEnumerator ftItr = firedTriggerRecs.GetEnumerator();
						//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
						while (ftItr.MoveNext())
						{
							//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
							FiredTriggerRecord ftRec = (FiredTriggerRecord) ftItr.Current;

							Key tKey = ftRec.TriggerKey;
							Key jKey = ftRec.JobKey;

							// release blocked triggers..
							if (ftRec.FireInstanceState.Equals(AdoConstants.STATE_BLOCKED))
							{
								Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, AdoConstants.STATE_WAITING,
								                                                 AdoConstants.STATE_BLOCKED);
							}
							if (ftRec.FireInstanceState.Equals(AdoConstants.STATE_PAUSED_BLOCKED))
							{
								Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, AdoConstants.STATE_PAUSED,
								                                                 AdoConstants.STATE_PAUSED_BLOCKED);
							}

							// release acquired triggers..
							if (ftRec.FireInstanceState.Equals(AdoConstants.STATE_ACQUIRED))
							{
								Delegate.UpdateTriggerStateFromOtherState(conn, tKey.Name, tKey.Group, AdoConstants.STATE_WAITING,
								                                          AdoConstants.STATE_ACQUIRED);
								acquiredCount++;
							}
								// handle jobs marked for recovery that were not fully
								// executed..
							else if (ftRec.JobRequestsRecovery)
							{
								if (JobExists(conn, jKey.Name, jKey.Group))
								{
									//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
									DateTime tempAux = new DateTime(ftRec.FireTimestamp);
									//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
									SimpleTrigger rcvryTrig =
										new SimpleTrigger("recover_" + rec.SchedulerInstanceId + "_" + Convert.ToString(recoverIds++),
										                  Scheduler_Fields.DEFAULT_RECOVERY_GROUP, tempAux);
									rcvryTrig.JobName = jKey.Name;
									rcvryTrig.JobGroup = jKey.Group;
									rcvryTrig.MisfireInstruction = SimpleTrigger.MISFIRE_INSTRUCTION_FIRE_NOW;
									JobDataMap jd = Delegate.SelectTriggerJobDataMap(conn, tKey.Name, tKey.Group);
									jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_NAME", tKey.Name);
									jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_GROUP", tKey.Group);
									jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_IN_MILLISECONDS_AS_STRING", Convert.ToString(ftRec.FireTimestamp));
									rcvryTrig.JobDataMap = jd;

									rcvryTrig.ComputeFirstFireTime(null);
									StoreTrigger(conn, null, rcvryTrig, null, false, AdoConstants.STATE_WAITING, false, true);
									recoveredCount++;
								}
								else
								{
									Log.Warn("ClusterManager: failed job '" + jKey + "' no longer exists, cannot schedule recovery.");
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
								Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, AdoConstants.STATE_WAITING,
								                                                 AdoConstants.STATE_BLOCKED);
								Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, AdoConstants.STATE_PAUSED,
								                                                 AdoConstants.STATE_PAUSED_BLOCKED);
							}
						}

						Delegate.DeleteFiredTriggers(conn, rec.SchedulerInstanceId);

						LogWarnIfNonZero(acquiredCount, "ClusterManager: ......Freed " + acquiredCount + " acquired trigger(s).");
						LogWarnIfNonZero(recoveredCount,
						                 "ClusterManager: ......Scheduled " + recoveredCount + " recoverable job(s) for recovery.");
						LogWarnIfNonZero(otherCount, "ClusterManager: ......Cleaned-up " + otherCount + " other failed job(s).");

						Delegate.DeleteSchedulerState(conn, rec.SchedulerInstanceId);

						// update record to show that recovery was handled
						String recoverer = InstanceId;
						long checkInTS = rec.CheckinTimestamp;
						if (rec.SchedulerInstanceId.Equals(InstanceId))
						{
							recoverer = null;
							checkInTS = (DateTime.Now.Ticks - 621355968000000000)/10000;
						}

						Delegate.InsertSchedulerState(conn, rec.SchedulerInstanceId, checkInTS, rec.CheckinInterval, recoverer);
					}
				}
				catch (Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Failure recovering jobs: " + e.Message, e);
				}
			}
		}

		protected internal virtual void LogWarnIfNonZero(int val, string warning)
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

		/// <summary> Closes the supplied connection
		/// 
		/// </summary>
		/// <param name="conn">(Optional)
		/// </param>
		/// <throws>  JobPersistenceException thrown if a SQLException occurs when the </throws>
		/// <summary> connection is closed
		/// </summary>
		
		protected internal virtual void CloseConnection(IDbConnection conn)
		{
			if (conn != null)
			{
				try
				{
					conn.Close();
				}
				catch (Exception ex)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't close jdbc connection. " + ex.Message, ex);
				}
			}
		}

		/// <summary>
		/// Rollback the supplied connection.
		/// </summary>
		/// <param name="conn">(Optional)
		/// </param>
		/// <throws>  JobPersistenceException thrown if a SQLException occurs when the </throws>
		/// <summary> connection is rolled back
		/// </summary>
		
		protected internal virtual void RollbackConnection(IDbConnection conn)
		{
			if (conn != null)
			{
				try
				{
					// TODO SupportClass.TransactionManager.manager.RollBack(conn);
				}
				catch (OleDbException e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't rollback jdbc connection. " + e.Message, e);
				}
			}
		}

		/// <summary> 
		/// Commit the supplied connection.
		/// </summary>
		/// <param name="conn"></param>
		/// <throws>JobPersistenceException thrown if a SQLException occurs when the </throws>
		protected internal virtual void CommitConnection(IDbConnection conn)
		{
			if (conn != null)
			{
				try
				{
					// TODO SupportClass.TransactionManager.manager.Commit(conn);
				}
				catch (OleDbException e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't commit jdbc connection. " + e.Message, e);
				}
			}
		}

		//UPGRADE_NOTE: Field 'EnclosingInstance' was added to class 'ClusterManager' to access its enclosing instance. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1019_3"'
		/////////////////////////////////////////////////////////////////////////////
		//
		// ClusterManager Thread
		//
		/////////////////////////////////////////////////////////////////////////////
		internal class ClusterManager : SupportClass.QuartzThread
		{
			private void InitBlock(JobStoreSupport enclosingInstanceParam)
			{
				enclosingInstance = enclosingInstanceParam;
			}

			private JobStoreSupport enclosingInstance;

			public JobStoreSupport Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			private bool shutdown_Renamed_Field = false;

			private JobStoreSupport js;

			private int numFails = 0;

			internal ClusterManager(JobStoreSupport enclosingInstance, JobStoreSupport js)
			{
				InitBlock(enclosingInstance);
				this.js = js;

				//UPGRADE_NOTE: The Name property of a Thread in C# is write-once. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1140_3"'
				Name = "QuartzScheduler_" + Enclosing_Instance.instanceName + "-" + Enclosing_Instance.instanceId +
				       "_ClusterManager";
			}

			public virtual void initialize()
			{
				manage();
				Start();
			}

			public virtual void shutdown()
			{
				shutdown_Renamed_Field = true;
				Interrupt();
			}

			private bool manage()
			{
				bool res = false;
				try
				{
					res = js.DoCheckin();

					numFails = 0;
					Enclosing_Instance.Log.Debug("ClusterManager: Check-in complete.");
				}
				catch (Exception e)
				{
					if (numFails%4 == 0)
					{
						Enclosing_Instance.Log.Error("ClusterManager: Error managing cluster: " + e.Message, e);
					}
					numFails++;
				}
				return res;
			}

			public override void Run()
			{
				while (!shutdown_Renamed_Field)
				{
					if (!shutdown_Renamed_Field)
					{
						long timeToSleep = Enclosing_Instance.ClusterCheckinInterval;
						long transpiredTime = ((DateTime.Now.Ticks - 621355968000000000)/10000 - Enclosing_Instance.lastCheckin);
						timeToSleep = timeToSleep - transpiredTime;
						if (timeToSleep <= 0)
						{
							timeToSleep = 100L;
						}

						if (numFails > 0)
						{
							timeToSleep = Math.Max(Enclosing_Instance.DbRetryInterval, timeToSleep);
						}

						try
						{
							//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javalangThreadsleep_long_3"'
							Thread.Sleep(new TimeSpan(10000*timeToSleep));
						}
						catch (Exception)
						{
						}
					}

					if (!shutdown_Renamed_Field && manage())
					{
						Enclosing_Instance.SignalSchedulingChange();
					}
				} //while !Shutdown
			}
		}

		//UPGRADE_NOTE: Field 'EnclosingInstance' was added to class 'MisfireHandler' to access its enclosing instance. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1019_3"'
		/////////////////////////////////////////////////////////////////////////////
		//
		// MisfireHandler Thread
		//
		/////////////////////////////////////////////////////////////////////////////
		internal class MisfireHandler : SupportClass.QuartzThread
		{
			private JobStoreSupport enclosingInstance;
			
			private void InitBlock(JobStoreSupport instance)
			{
				enclosingInstance = instance;
			}

			public JobStoreSupport Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			private bool shutdown_Renamed_Field = false;

			private JobStoreSupport js;

			private int numFails = 0;


			internal MisfireHandler(JobStoreSupport enclosingInstance, JobStoreSupport js)
			{
				InitBlock(enclosingInstance);
				this.js = js;
				//UPGRADE_NOTE: The Name property of a Thread in C# is write-once. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1140_3"'
				Name = "QuartzScheduler_" + Enclosing_Instance.instanceName + "-" + Enclosing_Instance.instanceId +
				       "_MisfireHandler";
			}

			public virtual void initialize()
			{
				//this.Manage();
				Start();
			}

			public virtual void Shutdown()
			{
				shutdown_Renamed_Field = true;
				Interrupt();
			}

			private bool Manage()
			{
				try
				{
					Enclosing_Instance.Log.Debug("MisfireHandler: scanning for misfires...");

					bool res = js.DoRecoverMisfires();
					numFails = 0;
					return res;
				}
				catch (Exception e)
				{
					if (numFails%4 == 0)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						Enclosing_Instance.Log.Error("MisfireHandler: Error handling misfires: " + e.Message, e);
					}
					numFails++;
				}
				return false;
			}

			public override void Run()
			{
				while (!shutdown_Renamed_Field)
				{
					long sTime = (DateTime.Now.Ticks - 621355968000000000)/10000;

					bool moreToDo = Manage();

					if (Enclosing_Instance.lastRecoverCount > 0)
					{
						Enclosing_Instance.SignalSchedulingChange();
					}

					long spanTime = (DateTime.Now.Ticks - 621355968000000000)/10000 - sTime;

					if (!shutdown_Renamed_Field && !moreToDo)
					{
						long timeToSleep = Enclosing_Instance.MisfireThreshold - spanTime;
						if (timeToSleep <= 0)
						{
							timeToSleep = 50L;
						}

						if (numFails > 0)
						{
							timeToSleep = Math.Max(Enclosing_Instance.DbRetryInterval, timeToSleep);
						}

						if (timeToSleep > 0)
						{
							try
							{
								// TODO
								//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javalangThreadsleep_long_3"'
								Thread.Sleep(new TimeSpan(10000*timeToSleep));
							}
							catch (Exception)
							{
							}
						}
					}
					else if (moreToDo)
					{
						// short pause to help balance threads...
						try
						{
							//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javalangThreadsleep_long_3"'
							Thread.Sleep(new TimeSpan((Int64) 10000*50));
						}
						catch (Exception)
						{
						}
					}
				} //while !Shutdown
			}
		}

		public abstract int GetNumberOfTriggers(SchedulingContext param1);
		public abstract int GetTriggerState(SchedulingContext param1, string param2, string param3);
		public abstract bool RemoveTrigger(SchedulingContext param1, string param2, string param3);
		public abstract void StoreJobAndTrigger(SchedulingContext param1, JobDetail param2, Trigger param3);
		public abstract String[] GetCalendarNames(SchedulingContext param1);
		public abstract int GetNumberOfCalendars(SchedulingContext param1);
		public abstract void ResumeJobGroup(SchedulingContext param1, string param2);
		public abstract void StoreJob(SchedulingContext param1, JobDetail param2, bool param3);
		public abstract String[] GetJobNames(SchedulingContext param1, string param2);
		public abstract TriggerFiredBundle TriggerFired(SchedulingContext param1, Trigger param2);
		public abstract void TriggeredJobComplete(SchedulingContext param1, Trigger param2, JobDetail param3, int param4);
		public abstract String[] GetTriggerGroupNames(SchedulingContext param1);
		public abstract void PauseTrigger(SchedulingContext param1, string param2, string param3);
		public abstract void ResumeAll(SchedulingContext param1);
		public abstract void StoreTrigger(SchedulingContext param1, Trigger param2, bool param3);
		public abstract String[] GetJobGroupNames(SchedulingContext param1);
		public abstract String[] GetTriggerNames(SchedulingContext param1, string param2);
		public abstract void PauseAll(SchedulingContext param1);
		public abstract void PauseJobGroup(SchedulingContext param1, string param2);
		public abstract void PauseTriggerGroup(SchedulingContext param1, string param2);
		public abstract bool ReplaceTrigger(SchedulingContext param1, string param2, string param3, Trigger param4);
		public abstract void ResumeJob(SchedulingContext param1, string param2, string param3);
		public abstract int GetNumberOfJobs(SchedulingContext param1);
		public abstract void PauseJob(SchedulingContext param1, string param2, string param3);
		public abstract void ReleaseAcquiredTrigger(SchedulingContext param1, Trigger param2);
		public abstract JobDetail RetrieveJob(SchedulingContext param1, string param2, string param3);
		public abstract bool RemoveJob(SchedulingContext param1, string param2, string param3);
		public abstract void ResumeTrigger(SchedulingContext param1, string param2, string param3);
		public abstract Trigger AcquireNextTrigger(SchedulingContext param1, DateTime param2);
		public abstract Trigger[] GetTriggersForJob(SchedulingContext param1, string param2, string param3);
		public abstract bool RemoveCalendar(SchedulingContext param1, string param2);
		public abstract ICalendar RetrieveCalendar(SchedulingContext param1, string param2);
		public abstract Trigger RetrieveTrigger(SchedulingContext param1, string param2, string param3);
		public abstract void StoreCalendar(SchedulingContext param1, string param2, ICalendar param3, bool param4, bool param5);
		public abstract ISet GetPausedTriggerGroups(SchedulingContext param1);
		public abstract void ResumeTriggerGroup(SchedulingContext param1, string param2);
	}

	// EOF
}