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
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1262_3"'
//UPGRADE_TODO: The type 'org.apache.commons.logging.LogFactory' could not be found. If it was not included in the conversion, there may be compiler issues. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1262_3"'
using System;
using System.Collections;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Reflection;
using System.Threading;
using log4net;
using Quartz;
using Quartz.core;
using Quartz.impl.jdbcjobstore;
using Quartz.spi;
using Quartz.utils;

namespace org.quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// Contains base functionality for JDBC-based JobStore implementations.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
	/// </author>
	/// <author>  James House
	/// </author>
	public abstract class JobStoreSupport : IJobStore, Constants
	{
		public JobStoreSupport()
		{
			InitBlock();
		}

		private void InitBlock()
		{
			delegateClass = typeof (StdJDBCDelegate);
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
		public virtual String DataSource
		{
			get { return dsName; }

			set { dsName = value; }
		}

		//UPGRADE_NOTE: Respective javadoc comments were merged.  It should be changed in order to comply with .NET documentation conventions. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1199_3"'
		/// <summary> <p>
		/// Get the prefix that should be pre-pended to all table names.
		/// </p>
		/// </summary>
		/// <summary> <p>
		/// Set the prefix that should be pre-pended to all table names.
		/// </p>
		/// </summary>
		public virtual String TablePrefix
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
		public virtual String UseProperties
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
		public virtual String InstanceId
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
		public virtual String InstanceName
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
		public virtual String DriverDelegateClass
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
		public virtual String SelectWithLockSQL
		{
			get { return selectWithLockSQL; }

			set { selectWithLockSQL = value; }
		}

		protected internal virtual ClassLoadHelper ClassLoadHelper
		{
			get { return classLoadHelper; }
		}

		internal virtual ILog Log
		{
			//---------------------------------------------------------------------------
			// interface methods
			//---------------------------------------------------------------------------


			get { return LogFactory.Log; }
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual OleDbConnection Connection
		{
			get
			{
				try
				{
					//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
					IDbConnection conn = DBConnectionManager.Instance.getConnection(DataSource);

					if (conn == null)
					{
						//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
						throw new SQLException("Could not get connection from DataSource '" + DataSource + "'");
					}

					try
					{
						if (!DontSetAutoCommitFalse)
						{
							SupportClass.TransactionManager.manager.SetAutoCommit(conn, false);
						}

						if (TxIsolationLevelSerializable)
						{
							//UPGRADE_TODO: The equivalent in .NET for field 'java.sql.Connection.TRANSACTION_SERIALIZABLE' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
							SupportClass.TransactionManager.manager.SetTransactionIsolation(conn, (int) IsolationLevel.Serializable);
						}
					}
					catch (OleDbException ingore)
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
		protected internal virtual String FiredTriggerRecordId
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
		protected internal virtual DriverDelegate Delegate
		{
			get
			{
				if (null == delegate_Renamed)
				{
					try
					{
						if (delegateClassName != null)
						{
							delegateClass = ClassLoadHelper.loadClass(delegateClassName);
						}

						ConstructorInfo ctor = null;
						Object[] ctorParams = null;
						if (canUseProperties())
						{
							Type[] ctorParamTypes = new Type[] {typeof (Log), typeof (String), typeof (String), typeof (Boolean)};
							ctor = delegateClass.GetConstructor(ctorParamTypes);
							ctorParams = new Object[] {Log, tablePrefix, instanceId, canUseProperties()};
						}
						else
						{
							Type[] ctorParamTypes = new Type[] {typeof (Log), typeof (String), typeof (String)};
							ctor = delegateClass.GetConstructor(ctorParamTypes);
							ctorParams = new Object[] {Log, tablePrefix, instanceId};
						}

						delegate_Renamed = (DriverDelegate) ctor.Invoke(ctorParams);
					}
					catch (MethodAccessException e)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new NoSuchDelegateException("Couldn't find delegate constructor: " + e.Message);
					}
						//UPGRADE_NOTE: Exception 'java.lang.InstantiationException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
					catch (Exception e)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new NoSuchDelegateException("Couldn't create delegate: " + e.Message);
					}
					catch (UnauthorizedAccessException e)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new NoSuchDelegateException("Couldn't create delegate: " + e.Message);
					}
					catch (TargetInvocationException e)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new NoSuchDelegateException("Couldn't create delegate: " + e.Message);
					}
						//UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
					catch (Exception e)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						throw new NoSuchDelegateException("Couldn't load delegate class: " + e.Message);
					}
				}

				return delegate_Renamed;
			}
		}

		protected internal virtual Semaphore LockHandler
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

		protected internal static String LOCK_TRIGGER_ACCESS = "TRIGGER_ACCESS";

		protected internal static String LOCK_JOB_ACCESS = "JOB_ACCESS";

		protected internal static String LOCK_CALENDAR_ACCESS = "CALENDAR_ACCESS";

		protected internal static String LOCK_STATE_ACCESS = "STATE_ACCESS";

		protected internal static String LOCK_MISFIRE_ACCESS = "MISFIRE_ACCESS";

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		protected internal String dsName;

		protected internal String tablePrefix = Constants_Fields.DEFAULT_TABLE_PREFIX;

		protected internal bool useProperties = false;

		protected internal String instanceId;

		protected internal String instanceName;

		protected internal String delegateClassName;
		//UPGRADE_NOTE: The initialization of  'delegateClass' was moved to method 'InitBlock'. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1005_3"'
		protected internal Type delegateClass;

		//UPGRADE_TODO: Class 'java.util.HashMap' was converted to 'System.Collections.Hashtable' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashMap_3"'
		protected internal Hashtable calendarCache = new Hashtable();

		private DriverDelegate delegate_Renamed;

		private long misfireThreshold = 60000L; // one minute

		private bool dontSetAutoCommitFalse = false;

		private bool isClustered_Renamed_Field = false;

		private bool useDBLocks = false;

		private bool lockOnInsert = true;

		private Semaphore lockHandler = null; // set in initialize() method...

		private String selectWithLockSQL = null;

		private long clusterCheckinInterval = 7500L;

		private ClusterManager clusterManagementThread = null;

		private MisfireHandler misfireHandler = null;

		private ClassLoadHelper classLoadHelper;

		private SchedulerSignaler signaler;

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
		/// used, in order to give the it a chance to initialize.
		/// </p>
		/// </summary>
		public virtual void initialize(ClassLoadHelper loadHelper, SchedulerSignaler signaler)
		{
			if (dsName == null)
			{
				throw new SchedulerConfigException("DataSource name not set.");
			}

			classLoadHelper = loadHelper;
			this.signaler = signaler;

			if (!UseDBLocks && !Clustered)
			{
				Log.info("Using thread monitor-based data access locking (synchronization).");
				lockHandler = new SimpleSemaphore();
			}
			else
			{
				Log.info("Using db table-based data access locking (synchronization).");
				lockHandler = new StdRowLockSemaphore(TablePrefix, SelectWithLockSQL);
			}

			if (!Clustered)
			{
				try
				{
					cleanVolatileTriggerAndJobs();
				}
				catch (SchedulerException se)
				{
					throw new SchedulerConfigException("Failure occured during job recovery.", se);
				}
			}
		}

		/// <seealso cref="org.quartz.spi.JobStore#schedulerStarted()">
		/// </seealso>
		public virtual void schedulerStarted()
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
					recoverJobs();
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
		public virtual void shutdown()
		{
			if (clusterManagementThread != null)
			{
				clusterManagementThread.shutdown();
			}

			if (misfireHandler != null)
			{
				misfireHandler.shutdown();
			}

			try
			{
				DBConnectionManager.Instance.shutdown(DataSource);
			}
			catch (OleDbException sqle)
			{
				Log.warn("Database connection shutdown unsuccessful.", sqle);
			}
		}

		public virtual bool supportsPersistence()
		{
			return true;
		}

		//---------------------------------------------------------------------------
		// helper methods for subclasses
		//---------------------------------------------------------------------------

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void releaseLock(OleDbConnection conn, String lockName, bool doIt)
		{
			if (doIt && conn != null)
			{
				try
				{
					LockHandler.releaseLock(conn, lockName);
				}
				catch (LockException le)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					Log.error("Error returning lock: " + le.Message, le);
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
		protected internal abstract void cleanVolatileTriggerAndJobs();

		/// <summary> <p>
		/// Removes all volatile data.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobPersistenceException </throws>
		/// <summary>           if jobs could not be recovered
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void cleanVolatileTriggerAndJobs(OleDbConnection conn)
		{
			try
			{
				// find volatile jobs & triggers...
				Key[] volatileTriggers = Delegate.selectVolatileTriggers(conn);
				Key[] volatileJobs = Delegate.selectVolatileJobs(conn);

				for (int i = 0; i < volatileTriggers.Length; i++)
				{
					removeTrigger(conn, null, volatileTriggers[i].Name, volatileTriggers[i].Group);
				}
				Log.info("Removed " + volatileTriggers.Length + " Volatile Trigger(s).");

				for (int i = 0; i < volatileJobs.Length; i++)
				{
					removeJob(conn, null, volatileJobs[i].Name, volatileJobs[i].Group, true);
				}
				Log.info("Removed " + volatileJobs.Length + " Volatile Job(s).");

				// clean up any fired trigger entries
				Delegate.deleteVolatileFiredTriggers(conn);
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
		protected internal abstract void recoverJobs();

		/// <summary> <p>
		/// Will recover any failed or misfired jobs and clean up the data store as
		/// appropriate.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobPersistenceException </throws>
		/// <summary>           if jobs could not be recovered
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void recoverJobs(OleDbConnection conn)
		{
			try
			{
				// update inconsistent job states
				int rows =
					Delegate.updateTriggerStatesFromOtherStates(conn, Constants_Fields.STATE_WAITING, Constants_Fields.STATE_ACQUIRED,
					                                            Constants_Fields.STATE_BLOCKED);

				rows +=
					Delegate.updateTriggerStatesFromOtherStates(conn, Constants_Fields.STATE_PAUSED,
					                                            Constants_Fields.STATE_PAUSED_BLOCKED,
					                                            Constants_Fields.STATE_PAUSED_BLOCKED);

				Log.info("Freed " + rows + " triggers from 'acquired' / 'blocked' state.");

				// clean up misfired jobs
				Delegate.updateTriggerStateFromOtherStatesBeforeTime(conn, Constants_Fields.STATE_MISFIRED,
				                                                     Constants_Fields.STATE_WAITING, Constants_Fields.STATE_WAITING,
				                                                     MisfireTime); // only waiting
				recoverMisfiredJobs(conn, true);

				// recover jobs marked for recovery that were not fully executed
				Trigger[] recoveringJobTriggers = Delegate.selectTriggersForRecoveringJobs(conn);
				Log.info("Recovering " + recoveringJobTriggers.Length +
				         " jobs that were in-progress at the time of the last shut-down.");

				for (int i = 0; i < recoveringJobTriggers.Length; ++i)
				{
					if (jobExists(conn, recoveringJobTriggers[i].JobName, recoveringJobTriggers[i].JobGroup))
					{
						recoveringJobTriggers[i].computeFirstFireTime(null);
						storeTrigger(conn, null, recoveringJobTriggers[i], null, false, Constants_Fields.STATE_WAITING, false, true);
					}
				}
				Log.info("Recovery complete.");

				// remove lingering 'complete' triggers...
				Key[] ct = Delegate.selectTriggersInState(conn, Constants_Fields.STATE_COMPLETE);
				for (int i = 0; ct != null && i < ct.Length; i++)
				{
					removeTrigger(conn, null, ct[i].Name, ct[i].Group);
				}
				Log.info("Removed " + ct.Length + " 'complete' triggers.");

				// clean up any fired trigger entries
				int n = Delegate.deleteFiredTriggers(conn);
				Log.info("Removed " + n + " stale fired job entries.");
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't recover jobs: " + e.Message, e);
			}
		}

		private int lastRecoverCount = 0;

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool recoverMisfiredJobs(OleDbConnection conn, bool recovering)
		{
			Key[] misfiredTriggers = Delegate.selectTriggersInState(conn, Constants_Fields.STATE_MISFIRED);

			if (misfiredTriggers.Length > 0 && misfiredTriggers.Length > MaxMisfiresToHandleAtATime)
			{
				Log.info("Handling " + MaxMisfiresToHandleAtATime + " of " + misfiredTriggers.Length +
				         " triggers that missed their scheduled fire-time.");
			}
			else if (misfiredTriggers.Length > 0)
			{
				Log.info("Handling " + misfiredTriggers.Length + " triggers that missed their scheduled fire-time.");
			}
			else
			{
				Log.debug("Found 0 triggers that missed their scheduled fire-time.");
			}

			lastRecoverCount = misfiredTriggers.Length;

			for (int i = 0; i < misfiredTriggers.Length && i < MaxMisfiresToHandleAtATime; i++)
			{
				Trigger trig = Delegate.selectTrigger(conn, misfiredTriggers[i].Name, misfiredTriggers[i].Group);

				if (trig == null)
				{
					continue;
				}

				Calendar cal = null;
				if (trig.CalendarName != null)
				{
					cal = retrieveCalendar(conn, null, trig.CalendarName);
				}

				String[] listeners = Delegate.selectTriggerListeners(conn, trig.Name, trig.Group);
				for (int l = 0; l < listeners.Length; ++l)
				{
					trig.addTriggerListener(listeners[l]);
				}

				signaler.notifyTriggerListenersMisfired(trig);

				trig.updateAfterMisfire(cal);

				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trig.getNextFireTime() == null)
				{
					storeTrigger(conn, null, trig, null, true, Constants_Fields.STATE_COMPLETE, false, recovering);
				}
				else
				{
					storeTrigger(conn, null, trig, null, true, Constants_Fields.STATE_WAITING, false, recovering);
				}
			}

			if (misfiredTriggers.Length > MaxMisfiresToHandleAtATime)
			{
				return true;
			}

			return false;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool updateMisfiredTrigger(OleDbConnection conn, SchedulingContext ctxt, String triggerName,
		                                                      String groupName, String newStateIfNotComplete, bool forceState)
		{
			try
			{
				Trigger trig = Delegate.selectTrigger(conn, triggerName, groupName);

				long misfireTime = (DateTime.Now.Ticks - 621355968000000000)/10000;
				if (MisfireThreshold > 0)
				{
					misfireTime -= MisfireThreshold;
				}

				//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
				if (trig.getNextFireTime().Ticks > misfireTime)
				{
					return false;
				}

				Calendar cal = null;
				if (trig.CalendarName != null)
				{
					cal = retrieveCalendar(conn, ctxt, trig.CalendarName);
				}

				signaler.notifyTriggerListenersMisfired(trig);

				trig.updateAfterMisfire(cal);

				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trig.getNextFireTime() == null)
				{
					storeTrigger(conn, ctxt, trig, null, true, Constants_Fields.STATE_COMPLETE, forceState, false);
				}
				else
				{
					storeTrigger(conn, ctxt, trig, null, true, newStateIfNotComplete, forceState, false);
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void storeJob(OleDbConnection conn, SchedulingContext ctxt, JobDetail newJob,
		                                         bool replaceExisting)
		{
			if (newJob.Volatile && Clustered)
			{
				Log.info("note: volatile jobs are effectively non-volatile in a clustered environment.");
			}

			bool existingJob = jobExists(conn, newJob.Name, newJob.Group);
			try
			{
				if (existingJob)
				{
					if (!replaceExisting)
					{
						throw new ObjectAlreadyExistsException(newJob);
					}
					Delegate.updateJobDetail(conn, newJob);
				}
				else
				{
					Delegate.insertJobDetail(conn, newJob);
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool jobExists(OleDbConnection conn, String jobName, String groupName)
		{
			try
			{
				return Delegate.jobExists(conn, jobName, groupName);
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void storeTrigger(OleDbConnection conn, SchedulingContext ctxt, Trigger newTrigger,
		                                             JobDetail job, bool replaceExisting, String state, bool forceState,
		                                             bool recovering)
		{
			if (newTrigger.Volatile && Clustered)
			{
				Log.info("note: volatile triggers are effectively non-volatile in a clustered environment.");
			}

			bool existingTrigger = triggerExists(conn, newTrigger.Name, newTrigger.Group);

			try
			{
				bool shouldBepaused = false;

				if (!forceState)
				{
					shouldBepaused = Delegate.isTriggerGroupPaused(conn, newTrigger.Group);

					if (!shouldBepaused)
					{
						shouldBepaused = Delegate.isTriggerGroupPaused(conn, Constants_Fields.ALL_GROUPS_PAUSED);

						if (shouldBepaused)
						{
							Delegate.insertPausedTriggerGroup(conn, newTrigger.Group);
						}
					}

					if (shouldBepaused &&
					    (state.Equals(Constants_Fields.STATE_WAITING) || state.Equals(Constants_Fields.STATE_ACQUIRED)))
					{
						state = Constants_Fields.STATE_PAUSED;
					}
				}

				if (job == null)
				{
					job = Delegate.selectJobDetail(conn, newTrigger.JobName, newTrigger.JobGroup, ClassLoadHelper);
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
					String bstate = getNewStatusForTrigger(conn, ctxt, job.Name, job.Group);
					if (Constants_Fields.STATE_BLOCKED.Equals(bstate) && Constants_Fields.STATE_WAITING.Equals(state))
					{
						state = Constants_Fields.STATE_BLOCKED;
					}
					if (Constants_Fields.STATE_BLOCKED.Equals(bstate) && Constants_Fields.STATE_PAUSED.Equals(state))
					{
						state = Constants_Fields.STATE_PAUSED_BLOCKED;
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
						Delegate.updateSimpleTrigger(conn, (SimpleTrigger) newTrigger);
					}
					else if (newTrigger is CronTrigger)
					{
						Delegate.updateCronTrigger(conn, (CronTrigger) newTrigger);
					}
					else
					{
						Delegate.updateBlobTrigger(conn, newTrigger);
					}
					Delegate.updateTrigger(conn, newTrigger, state, job);
				}
				else
				{
					Delegate.insertTrigger(conn, newTrigger, state, job);
					if (newTrigger is SimpleTrigger)
					{
						Delegate.insertSimpleTrigger(conn, (SimpleTrigger) newTrigger);
					}
					else if (newTrigger is CronTrigger)
					{
						Delegate.insertCronTrigger(conn, (CronTrigger) newTrigger);
					}
					else
					{
						Delegate.insertBlobTrigger(conn, newTrigger);
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool triggerExists(OleDbConnection conn, String triggerName, String groupName)
		{
			try
			{
				return Delegate.triggerExists(conn, triggerName, groupName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't determine trigger existence (" + groupName + "." + triggerName + "): " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool removeJob(OleDbConnection conn, SchedulingContext ctxt, String jobName,
		                                          String groupName, bool activeDeleteSafe)
		{
			try
			{
				Key[] jobTriggers = Delegate.selectTriggerNamesForJob(conn, jobName, groupName);
				for (int i = 0; i < jobTriggers.Length; ++i)
				{
					Delegate.deleteSimpleTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.deleteCronTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.deleteBlobTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.deleteTriggerListeners(conn, jobTriggers[i].Name, jobTriggers[i].Group);
					Delegate.deleteTrigger(conn, jobTriggers[i].Name, jobTriggers[i].Group);
				}

				Delegate.deleteJobListeners(conn, jobName, groupName);

				if (Delegate.deleteJobDetail(conn, jobName, groupName) > 0)
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual JobDetail retrieveJob(OleDbConnection conn, SchedulingContext ctxt, String jobName,
		                                                 String groupName)
		{
			try
			{
				JobDetail job = Delegate.selectJobDetail(conn, jobName, groupName, ClassLoadHelper);
				String[] listeners = Delegate.selectJobListeners(conn, jobName, groupName);
				for (int i = 0; i < listeners.Length; ++i)
				{
					job.addJobListener(listeners[i]);
				}

				return job;
			}
				//UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't retrieve job because a required class was not found: " + e.Message, e,
				                                  SchedulerException.ERR_PERSISTENCE_JOB_DOES_NOT_EXIST);
			}
			catch (IOException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't retrieve job because the BLOB couldn't be deserialized: " + e.Message, e,
				                                  SchedulerException.ERR_PERSISTENCE_JOB_DOES_NOT_EXIST);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't retrieve job: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool removeTrigger(OleDbConnection conn, SchedulingContext ctxt, String triggerName,
		                                              String groupName)
		{
			bool removedTrigger = false;
			try
			{
				// this must be called before we delete the trigger, obviously
				JobDetail job = Delegate.selectJobForTrigger(conn, triggerName, groupName, ClassLoadHelper);

				Delegate.deleteSimpleTrigger(conn, triggerName, groupName);
				Delegate.deleteCronTrigger(conn, triggerName, groupName);
				Delegate.deleteBlobTrigger(conn, triggerName, groupName);
				Delegate.deleteTriggerListeners(conn, triggerName, groupName);
				removedTrigger = (Delegate.deleteTrigger(conn, triggerName, groupName) > 0);

				if (null != job && !job.Durable)
				{
					int numTriggers = Delegate.selectNumTriggersForJob(conn, job.Name, job.Group);
					if (numTriggers == 0)
					{
						removeJob(conn, ctxt, job.Name, job.Group, true);
					}
				}
			}
				//UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
			}

			return removedTrigger;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool replaceTrigger(OleDbConnection conn, SchedulingContext ctxt, String triggerName,
		                                               String groupName, Trigger newTrigger)
		{
			bool removedTrigger = false;
			try
			{
				// this must be called before we delete the trigger, obviously
				JobDetail job = Delegate.selectJobForTrigger(conn, triggerName, groupName, ClassLoadHelper);

				if (job == null)
				{
					return false;
				}

				if (!newTrigger.JobName.Equals(job.Name) || !newTrigger.JobGroup.Equals(job.Group))
				{
					throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
				}

				Delegate.deleteSimpleTrigger(conn, triggerName, groupName);
				Delegate.deleteCronTrigger(conn, triggerName, groupName);
				Delegate.deleteBlobTrigger(conn, triggerName, groupName);
				Delegate.deleteTriggerListeners(conn, triggerName, groupName);
				removedTrigger = (Delegate.deleteTrigger(conn, triggerName, groupName) > 0);

				storeTrigger(conn, ctxt, newTrigger, job, false, Constants_Fields.STATE_WAITING, false, false);
			}
				//UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't remove trigger: " + e.Message, e);
			}

			return removedTrigger;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual Trigger retrieveTrigger(OleDbConnection conn, SchedulingContext ctxt, String triggerName,
		                                                   String groupName)
		{
			try
			{
				Trigger trigger = Delegate.selectTrigger(conn, triggerName, groupName);
				if (trigger == null)
				{
					return null;
				}
				String[] listeners = Delegate.selectTriggerListeners(conn, triggerName, groupName);
				for (int i = 0; i < listeners.Length; ++i)
				{
					trigger.addTriggerListener(listeners[i]);
				}

				return trigger;
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't retrieve trigger: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int getTriggerState(OleDbConnection conn, SchedulingContext ctxt, String triggerName, String groupName)
		{
			try
			{
				String ts = Delegate.selectTriggerState(conn, triggerName, groupName);

				if (ts == null)
				{
					return Trigger.STATE_NONE;
				}

				if (ts.Equals(Constants_Fields.STATE_DELETED))
				{
					return Trigger.STATE_NONE;
				}

				if (ts.Equals(Constants_Fields.STATE_COMPLETE))
				{
					return Trigger.STATE_COMPLETE;
				}

				if (ts.Equals(Constants_Fields.STATE_PAUSED))
				{
					return Trigger.STATE_PAUSED;
				}

				if (ts.Equals(Constants_Fields.STATE_PAUSED_BLOCKED))
				{
					return Trigger.STATE_PAUSED;
				}

				if (ts.Equals(Constants_Fields.STATE_ERROR))
				{
					return Trigger.STATE_ERROR;
				}

				if (ts.Equals(Constants_Fields.STATE_BLOCKED))
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void storeCalendar(OleDbConnection conn, SchedulingContext ctxt, String calName,
		                                              Calendar calendar, bool replaceExisting, bool updateTriggers)
		{
			try
			{
				bool existingCal = calendarExists(conn, calName);
				if (existingCal && !replaceExisting)
				{
					throw new ObjectAlreadyExistsException("Calendar with name '" + calName + "' already exists.");
				}

				if (existingCal)
				{
					if (Delegate.updateCalendar(conn, calName, calendar) < 1)
					{
						throw new JobPersistenceException("Couldn't store calendar.  Update failed.");
					}

					if (updateTriggers)
					{
						Trigger[] trigs = Delegate.selectTriggersForCalendar(conn, calName);

						for (int i = 0; i < trigs.Length; i++)
						{
							trigs[i].updateWithNewCalendar(calendar, MisfireThreshold);
							storeTrigger(conn, ctxt, trigs[i], null, true, Constants_Fields.STATE_WAITING, false, false);
						}
					}
				}
				else
				{
					if (Delegate.insertCalendar(conn, calName, calendar) < 1)
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
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't store calendar: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool calendarExists(OleDbConnection conn, String calName)
		{
			try
			{
				return Delegate.calendarExists(conn, calName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't determine calendar existence (" + calName + "): " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual bool removeCalendar(OleDbConnection conn, SchedulingContext ctxt, String calName)
		{
			try
			{
				if (Delegate.calendarIsReferenced(conn, calName))
				{
					throw new JobPersistenceException("Calender cannot be removed if it referenced by a trigger!");
				}

				calendarCache.Remove(calName);

				return (Delegate.deleteCalendar(conn, calName) > 0);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't remove calendar: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual Calendar retrieveCalendar(OleDbConnection conn, SchedulingContext ctxt, String calName)
		{
			// all calendars are persistent, but we lazy-cache them during run
			// time...
			//UPGRADE_TODO: Method 'java.util.HashMap.get' was converted to 'System.Collections.Hashtable.Item' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashMapget_javalangObject_3"'
			Calendar cal = (Calendar) calendarCache[calName];
			if (cal != null)
			{
				return cal;
			}

			try
			{
				cal = Delegate.selectCalendar(conn, calName);
				calendarCache[calName] = cal; // lazy-cache...
				return cal;
			}
				//UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't retrieve calendar because a required class was not found: " + e.Message, e);
			}
			catch (IOException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException(
					"Couldn't retrieve calendar because the BLOB couldn't be deserialized: " + e.Message, e);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't retrieve calendar: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual int getNumberOfJobs(OleDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.selectNumJobs(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain number of jobs: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual int getNumberOfTriggers(OleDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.selectNumTriggers(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain number of triggers: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual int getNumberOfCalendars(OleDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.selectNumCalendars(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain number of calendars: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual String[] getJobNames(OleDbConnection conn, SchedulingContext ctxt, String groupName)
		{
			String[] jobNames = null;

			try
			{
				jobNames = Delegate.selectJobsInGroup(conn, groupName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain job names: " + e.Message, e);
			}

			return jobNames;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual String[] getTriggerNames(OleDbConnection conn, SchedulingContext ctxt, String groupName)
		{
			String[] trigNames = null;

			try
			{
				trigNames = Delegate.selectTriggersInGroup(conn, groupName);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain trigger names: " + e.Message, e);
			}

			return trigNames;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual String[] getJobGroupNames(OleDbConnection conn, SchedulingContext ctxt)
		{
			String[] groupNames = null;

			try
			{
				groupNames = Delegate.selectJobGroups(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain job groups: " + e.Message, e);
			}

			return groupNames;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual String[] getTriggerGroupNames(OleDbConnection conn, SchedulingContext ctxt)
		{
			String[] groupNames = null;

			try
			{
				groupNames = Delegate.selectTriggerGroups(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
			}

			return groupNames;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual String[] getCalendarNames(OleDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.selectCalendars(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain trigger groups: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual Trigger[] getTriggersForJob(OleDbConnection conn, SchedulingContext ctxt, String jobName,
		                                                       String groupName)
		{
			Trigger[] array = null;

			try
			{
				array = Delegate.selectTriggersForJob(conn, jobName, groupName);
			}
			catch (Exception e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't obtain triggers for job: " + e.Message, e);
			}

			return array;
		}

		/// <summary> <p>
		/// Pause the <code>{@link org.quartz.Trigger}</code> with the given name.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="SchedulingContext, String, String)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void pauseTrigger(OleDbConnection conn, SchedulingContext ctxt, String triggerName, String groupName)
		{
			try
			{
				String oldState = Delegate.selectTriggerState(conn, triggerName, groupName);

				if (oldState.Equals(Constants_Fields.STATE_WAITING) || oldState.Equals(Constants_Fields.STATE_ACQUIRED))
				{
					Delegate.updateTriggerState(conn, triggerName, groupName, Constants_Fields.STATE_PAUSED);
				}
				else if (oldState.Equals(Constants_Fields.STATE_BLOCKED))
				{
					Delegate.updateTriggerState(conn, triggerName, groupName, Constants_Fields.STATE_PAUSED_BLOCKED);
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual String getStatusForResumedTrigger(OleDbConnection conn, SchedulingContext ctxt,
		                                                             TriggerStatus status)
		{
			try
			{
				String newState = Constants_Fields.STATE_WAITING;

				IList lst = Delegate.selectFiredTriggerRecordsByJob(conn, status.JobKey.Name, status.JobKey.Group);

				if (lst.Count > 0)
				{
					FiredTriggerRecord rec = (FiredTriggerRecord) lst[0];
					if (rec.JobIsStateful)
					{
						// TODO: worry about
						// failed/recovering/volatile job
						// states?
						newState = Constants_Fields.STATE_BLOCKED;
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual String getNewStatusForTrigger(OleDbConnection conn, SchedulingContext ctxt, String jobName,
		                                                         String groupName)
		{
			try
			{
				String newState = Constants_Fields.STATE_WAITING;

				IList lst = Delegate.selectFiredTriggerRecordsByJob(conn, jobName, groupName);

				if (lst.Count > 0)
				{
					FiredTriggerRecord rec = (FiredTriggerRecord) lst[0];
					if (rec.JobIsStateful)
					{
						// TODO: worry about
						// failed/recovering/volatile job
						// states?
						newState = Constants_Fields.STATE_BLOCKED;
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
		* ctxt, String groupName) throws JobPersistenceException {
		* 
		* try { List blockList = new LinkedList();
		* 
		* List affectingJobs =
		* getDelegate().selectStatefulJobsOfTriggerGroup(conn, groupName);
		* 
		* Iterator itr = affectingJobs.iterator(); while(itr.hasNext()) { Key
		* jobKey = (Key) itr.next();
		* 
		* List lst = getDelegate().selectFiredTriggerRecordsByJob(conn,
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

		/// <summary> <p>
		/// Resume (un-pause) the <code>{@link org.quartz.Trigger}</code> with the
		/// given name.
		/// </p>
		/// 
		/// <p>
		/// If the <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="SchedulingContext, String, String)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void resumeTrigger(OleDbConnection conn, SchedulingContext ctxt, String triggerName, String groupName)
		{
			try
			{
				TriggerStatus status = Delegate.selectTriggerStatus(conn, triggerName, groupName);

				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (status == null || status.NextFireTime == null)
				{
					return;
				}

				bool blocked = false;
				if (Constants_Fields.STATE_PAUSED_BLOCKED.Equals(status.Status))
				{
					blocked = true;
				}

				String newState = getStatusForResumedTrigger(conn, ctxt, status);

				bool misfired = false;

				if ((status.NextFireTime < DateTime.Now))
				{
					misfired = updateMisfiredTrigger(conn, ctxt, triggerName, groupName, newState, true);
				}

				if (!misfired)
				{
					if (blocked)
					{
						Delegate.updateTriggerStateFromOtherState(conn, triggerName, groupName, newState,
						                                          Constants_Fields.STATE_PAUSED_BLOCKED);
					}
					else
					{
						Delegate.updateTriggerStateFromOtherState(conn, triggerName, groupName, newState, Constants_Fields.STATE_PAUSED);
					}
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't resume trigger '" + groupName + "." + triggerName + "': " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Pause all of the <code>{@link org.quartz.Trigger}s</code> in the
		/// given group.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="SchedulingContext, String)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void pauseTriggerGroup(OleDbConnection conn, SchedulingContext ctxt, String groupName)
		{
			try
			{
				Delegate.updateTriggerGroupStateFromOtherStates(conn, groupName, Constants_Fields.STATE_PAUSED,
				                                                Constants_Fields.STATE_ACQUIRED, Constants_Fields.STATE_WAITING,
				                                                Constants_Fields.STATE_WAITING);

				Delegate.updateTriggerGroupStateFromOtherState(conn, groupName, Constants_Fields.STATE_PAUSED_BLOCKED,
				                                               Constants_Fields.STATE_BLOCKED);

				if (!Delegate.isTriggerGroupPaused(conn, groupName))
				{
					Delegate.insertPausedTriggerGroup(conn, groupName);
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause trigger group '" + groupName + "': " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Pause all of the <code>{@link org.quartz.Trigger}s</code> in the
		/// given group.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="SchedulingContext, String)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual SupportClass.SetSupport getPausedTriggerGroups(OleDbConnection conn, SchedulingContext ctxt)
		{
			try
			{
				return Delegate.selectPausedTriggerGroups(conn);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't determine paused trigger groups: " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Resume (un-pause) all of the <code>{@link org.quartz.Trigger}s</code>
		/// in the given group.
		/// </p>
		/// 
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="SchedulingContext, String)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void resumeTriggerGroup(OleDbConnection conn, SchedulingContext ctxt, String groupName)
		{
			try
			{
				Delegate.deletePausedTriggerGroup(conn, groupName);

				String[] trigNames = Delegate.selectTriggersInGroup(conn, groupName);

				for (int i = 0; i < trigNames.Length; i++)
				{
					resumeTrigger(conn, ctxt, trigNames[i], groupName);
				}

				// TODO: find an efficient way to resume triggers (better than the
				// above)... logic below is broken because of
				// findTriggersToBeBlocked()
				/*
				* int res =
				* getDelegate().updateTriggerGroupStateFromOtherState(conn,
				* groupName, STATE_WAITING, STATE_PAUSED);
				* 
				* if(res > 0) {
				* 
				* long misfireTime = System.currentTimeMillis();
				* if(getMisfireThreshold() > 0) misfireTime -=
				* getMisfireThreshold();
				* 
				* Key[] misfires =
				* getDelegate().selectMisfiredTriggersInGroupInState(conn,
				* groupName, STATE_WAITING, misfireTime);
				* 
				* List blockedTriggers = findTriggersToBeBlocked(conn, ctxt,
				* groupName);
				* 
				* Iterator itr = blockedTriggers.iterator(); while(itr.hasNext()) {
				* Key key = (Key)itr.next();
				* getDelegate().updateTriggerState(conn, key.getName(),
				* key.getGroup(), STATE_BLOCKED); }
				* 
				* for(int i=0; i < misfires.length; i++) {               String
				* newState = STATE_WAITING;
				* if(blockedTriggers.contains(misfires[i])) newState =
				* STATE_BLOCKED; updateMisfiredTrigger(conn, ctxt,
				* misfires[i].getName(), misfires[i].getGroup(), newState, true); } }
				*/
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause trigger group '" + groupName + "': " + e.Message, e);
			}
		}

		/// <summary> <p>
		/// Pause all triggers - equivalent of calling <code>pauseTriggerGroup(group)</code>
		/// on every group.
		/// </p>
		/// 
		/// <p>
		/// When <code>resumeAll()</code> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="#resumeAll(SchedulingContext)">
		/// </seealso>
		/// <seealso cref="String)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void pauseAll(OleDbConnection conn, SchedulingContext ctxt)
		{
			String[] names = getTriggerGroupNames(conn, ctxt);

			for (int i = 0; i < names.Length; i++)
			{
				pauseTriggerGroup(conn, ctxt, names[i]);
			}

			try
			{
				if (!Delegate.isTriggerGroupPaused(conn, Constants_Fields.ALL_GROUPS_PAUSED))
				{
					Delegate.insertPausedTriggerGroup(conn, Constants_Fields.ALL_GROUPS_PAUSED);
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't pause all trigger groups: " + e.Message, e);
			}
		}

		/// <summary> protected
		/// <p>
		/// Resume (un-pause) all triggers - equivalent of calling <code>resumeTriggerGroup(group)</code>
		/// on every group.
		/// </p>
		/// 
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="#pauseAll(SchedulingContext)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void resumeAll(OleDbConnection conn, SchedulingContext ctxt)
		{
			String[] names = getTriggerGroupNames(conn, ctxt);

			for (int i = 0; i < names.Length; i++)
			{
				resumeTriggerGroup(conn, ctxt, names[i]);
			}

			try
			{
				Delegate.deletePausedTriggerGroup(conn, Constants_Fields.ALL_GROUPS_PAUSED);
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual Trigger acquireNextTrigger(OleDbConnection conn, SchedulingContext ctxt, long noLaterThan)
		{
			Trigger nextTrigger = null;

			bool acquiredOne = false;

			do
			{
				try
				{
					Delegate.updateTriggerStateFromOtherStatesBeforeTime(conn, Constants_Fields.STATE_MISFIRED,
					                                                     Constants_Fields.STATE_WAITING, Constants_Fields.STATE_WAITING,
					                                                     MisfireTime); // only waiting

					long nextFireTime = Delegate.selectNextFireTime(conn);

					if (nextFireTime == 0 || nextFireTime > noLaterThan)
					{
						return null;
					}

					Key triggerKey = null;
					do
					{
						triggerKey = Delegate.selectTriggerForFireTime(conn, nextFireTime);
						if (null != triggerKey)
						{
							int res =
								Delegate.updateTriggerStateFromOtherState(conn, triggerKey.Name, triggerKey.Group,
								                                          Constants_Fields.STATE_ACQUIRED, Constants_Fields.STATE_WAITING);

							if (res <= 0)
							{
								continue;
							}

							nextTrigger = retrieveTrigger(conn, ctxt, triggerKey.Name, triggerKey.Group);

							if (nextTrigger == null)
							{
								continue;
							}

							nextTrigger.FireInstanceId = FiredTriggerRecordId;
							Delegate.insertFiredTrigger(conn, nextTrigger, Constants_Fields.STATE_ACQUIRED, null);

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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void releaseAcquiredTrigger(OleDbConnection conn, SchedulingContext ctxt, Trigger trigger)
		{
			try
			{
				Delegate.updateTriggerStateFromOtherState(conn, trigger.Name, trigger.Group, Constants_Fields.STATE_WAITING,
				                                          Constants_Fields.STATE_ACQUIRED);
				Delegate.deleteFiredTrigger(conn, trigger.FireInstanceId);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't release acquired trigger: " + e.Message, e);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual TriggerFiredBundle triggerFired(OleDbConnection conn, SchedulingContext ctxt,
		                                                           Trigger trigger)
		{
			JobDetail job = null;
			Calendar cal = null;

			// Make sure trigger wasn't deleted, paused, or completed...
			try
			{
				// if trigger was deleted, state will be STATE_DELETED
				String state = Delegate.selectTriggerState(conn, trigger.Name, trigger.Group);
				if (!state.Equals(Constants_Fields.STATE_ACQUIRED))
				{
					return null;
				}
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't select trigger state: " + e.Message, e);
			}

			try
			{
				job = retrieveJob(conn, ctxt, trigger.JobName, trigger.JobGroup);
				if (job == null)
				{
					return null;
				}
			}
			catch (JobPersistenceException jpe)
			{
				try
				{
					Log.error("Error retrieving job, setting trigger state to ERROR.", jpe);
					Delegate.updateTriggerState(conn, trigger.Name, trigger.Group, Constants_Fields.STATE_ERROR);
				}
				catch (OleDbException sqle)
				{
					Log.error("Unable to set trigger state to ERROR.", sqle);
				}
				throw jpe;
			}

			if (trigger.CalendarName != null)
			{
				cal = retrieveCalendar(conn, ctxt, trigger.CalendarName);
				if (cal == null)
				{
					return null;
				}
			}

			try
			{
				Delegate.deleteFiredTrigger(conn, trigger.FireInstanceId);
				Delegate.insertFiredTrigger(conn, trigger, Constants_Fields.STATE_EXECUTING, job);
			}
			catch (OleDbException e)
			{
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Couldn't insert fired trigger: " + e.Message, e);
			}

			DateTime prevFireTime = trigger.getPreviousFireTime();

			// call triggered - to update the trigger's next-fire-time state...
			trigger.triggered(cal);

			String state2 = Constants_Fields.STATE_WAITING;
			bool force = true;

			if (job.Stateful)
			{
				state2 = Constants_Fields.STATE_BLOCKED;
				force = false;
				try
				{
					Delegate.updateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, Constants_Fields.STATE_BLOCKED,
					                                                 Constants_Fields.STATE_WAITING);
					Delegate.updateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, Constants_Fields.STATE_BLOCKED,
					                                                 Constants_Fields.STATE_ACQUIRED);
					Delegate.updateTriggerStatesForJobFromOtherState(conn, job.Name, job.Group, Constants_Fields.STATE_PAUSED_BLOCKED,
					                                                 Constants_Fields.STATE_PAUSED);
				}
				catch (OleDbException e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't update states of blocked triggers: " + e.Message, e);
				}
			}

			//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
			if (trigger.getNextFireTime() == null)
			{
				state2 = Constants_Fields.STATE_COMPLETE;
				force = true;
			}

			storeTrigger(conn, ctxt, trigger, job, true, state2, force, false);

			job.JobDataMap.clearDirtyFlag();

			DateTime tempAux = DateTime.Now;
			//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
			DateTime tempAux2 = trigger.getPreviousFireTime();
			DateTime tempAux3 = trigger.getNextFireTime();
			return
				new TriggerFiredBundle(job, trigger, cal, trigger.Group.Equals(Scheduler_Fields.DEFAULT_RECOVERY_GROUP), ref tempAux,
				                       ref tempAux2, ref prevFireTime, ref tempAux3);
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void triggeredJobComplete(OleDbConnection conn, SchedulingContext ctxt, Trigger trigger,
		                                                     JobDetail jobDetail, int triggerInstCode)
		{
			try
			{
				if (triggerInstCode == Trigger.INSTRUCTION_DELETE_TRIGGER)
				{
					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					if (trigger.getNextFireTime() == null)
					{
						// double check for possible reschedule within job 
						// execution, which would cancel the need to delete...
						TriggerStatus stat = Delegate.selectTriggerStatus(conn, trigger.Name, trigger.Group);
						//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
						if (stat != null && stat.NextFireTime == null)
						{
							removeTrigger(conn, ctxt, trigger.Name, trigger.Group);
						}
					}
					else
					{
						removeTrigger(conn, ctxt, trigger.Name, trigger.Group);
					}
				}
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_TRIGGER_COMPLETE)
				{
					Delegate.updateTriggerState(conn, trigger.Name, trigger.Group, Constants_Fields.STATE_COMPLETE);
				}
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_TRIGGER_ERROR)
				{
					Log.info("Trigger " + trigger.FullName + " set to ERROR state.");
					Delegate.updateTriggerState(conn, trigger.Name, trigger.Group, Constants_Fields.STATE_ERROR);
				}
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_COMPLETE)
				{
					Delegate.updateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, Constants_Fields.STATE_COMPLETE);
				}
				else if (triggerInstCode == Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_ERROR)
				{
					Log.info("All triggers of Job " + trigger.FullJobName + " set to ERROR state.");
					Delegate.updateTriggerStatesForJob(conn, trigger.JobName, trigger.JobGroup, Constants_Fields.STATE_ERROR);
				}

				if (jobDetail.Stateful)
				{
					Delegate.updateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
					                                                 Constants_Fields.STATE_WAITING, Constants_Fields.STATE_BLOCKED);

					Delegate.updateTriggerStatesForJobFromOtherState(conn, jobDetail.Name, jobDetail.Group,
					                                                 Constants_Fields.STATE_PAUSED,
					                                                 Constants_Fields.STATE_PAUSED_BLOCKED);

					try
					{
						if (jobDetail.JobDataMap.Dirty)
						{
							Delegate.updateJobData(conn, jobDetail);
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
				Delegate.deleteFiredTrigger(conn, trigger.FireInstanceId);
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

		protected internal abstract bool doRecoverMisfires();

		protected internal virtual void signalSchedulingChange()
		{
			signaler.signalSchedulingChange();
		}

		//---------------------------------------------------------------------------
		// Cluster management methods
		//---------------------------------------------------------------------------

		protected internal abstract bool doCheckin();

		protected internal bool firstCheckIn = true;

		protected internal long lastCheckin = (DateTime.Now.Ticks - 621355968000000000)/10000;

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual IList clusterCheckIn(OleDbConnection conn)
		{
			IList states = null;
			//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
			IList failedInstances = new ArrayList();
			SchedulerStateRecord myLastState = null;
			bool selfFailed = false;

			long timeNow = (DateTime.Now.Ticks - 621355968000000000)/10000;

			try
			{
				states = Delegate.selectSchedulerStateRecords(conn, null);

				IEnumerator itr = states.GetEnumerator();
				//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
				while (itr.MoveNext())
				{
					//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
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
					Delegate.deleteSchedulerState(conn, InstanceId);
					Delegate.insertSchedulerState(conn, InstanceId, lastCheckin, ClusterCheckinInterval, null);
					firstCheckIn = false;
				}
				else
				{
					Delegate.updateSchedulerState(conn, InstanceId, lastCheckin);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void clusterRecover(OleDbConnection conn, IList failedInstances)
		{
			if (failedInstances.Count > 0)
			{
				long recoverIds = (DateTime.Now.Ticks - 621355968000000000)/10000;

				logWarnIfNonZero(failedInstances.Count,
				                 "ClusterManager: detected " + failedInstances.Count + " failed or restarted instances.");
				try
				{
					IEnumerator itr = failedInstances.GetEnumerator();
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					while (itr.MoveNext())
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						SchedulerStateRecord rec = (SchedulerStateRecord) itr.Current;

						Log.info("ClusterManager: Scanning for instance \"" + rec.SchedulerInstanceId + "\"'s failed in-progress jobs.");

						IList firedTriggerRecs = Delegate.selectInstancesFiredTriggerRecords(conn, rec.SchedulerInstanceId);

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
							if (ftRec.FireInstanceState.Equals(Constants_Fields.STATE_BLOCKED))
							{
								Delegate.updateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, Constants_Fields.STATE_WAITING,
								                                                 Constants_Fields.STATE_BLOCKED);
							}
							if (ftRec.FireInstanceState.Equals(Constants_Fields.STATE_PAUSED_BLOCKED))
							{
								Delegate.updateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, Constants_Fields.STATE_PAUSED,
								                                                 Constants_Fields.STATE_PAUSED_BLOCKED);
							}

							// release acquired triggers..
							if (ftRec.FireInstanceState.Equals(Constants_Fields.STATE_ACQUIRED))
							{
								Delegate.updateTriggerStateFromOtherState(conn, tKey.Name, tKey.Group, Constants_Fields.STATE_WAITING,
								                                          Constants_Fields.STATE_ACQUIRED);
								acquiredCount++;
							}
								// handle jobs marked for recovery that were not fully
								// executed..
							else if (ftRec.JobRequestsRecovery)
							{
								if (jobExists(conn, jKey.Name, jKey.Group))
								{
									//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
									DateTime tempAux = new DateTime(ftRec.FireTimestamp);
									//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
									SimpleTrigger rcvryTrig =
										new SimpleTrigger("recover_" + rec.SchedulerInstanceId + "_" + Convert.ToString(recoverIds++),
										                  Scheduler_Fields.DEFAULT_RECOVERY_GROUP, ref tempAux);
									rcvryTrig.JobName = jKey.Name;
									rcvryTrig.JobGroup = jKey.Group;
									rcvryTrig.MisfireInstruction = SimpleTrigger.MISFIRE_INSTRUCTION_FIRE_NOW;
									JobDataMap jd = Delegate.selectTriggerJobDataMap(conn, tKey.Name, tKey.Group);
									jd.put("QRTZ_FAILED_JOB_ORIG_TRIGGER_NAME", tKey.Name);
									jd.put("QRTZ_FAILED_JOB_ORIG_TRIGGER_GROUP", tKey.Group);
									jd.put("QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_IN_MILLISECONDS_AS_STRING", Convert.ToString(ftRec.FireTimestamp));
									rcvryTrig.JobDataMap = jd;

									rcvryTrig.computeFirstFireTime(null);
									storeTrigger(conn, null, rcvryTrig, null, false, Constants_Fields.STATE_WAITING, false, true);
									recoveredCount++;
								}
								else
								{
									Log.warn("ClusterManager: failed job '" + jKey + "' no longer exists, cannot schedule recovery.");
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
								Delegate.updateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, Constants_Fields.STATE_WAITING,
								                                                 Constants_Fields.STATE_BLOCKED);
								Delegate.updateTriggerStatesForJobFromOtherState(conn, jKey.Name, jKey.Group, Constants_Fields.STATE_PAUSED,
								                                                 Constants_Fields.STATE_PAUSED_BLOCKED);
							}
						}

						Delegate.deleteFiredTriggers(conn, rec.SchedulerInstanceId);

						logWarnIfNonZero(acquiredCount, "ClusterManager: ......Freed " + acquiredCount + " acquired trigger(s).");
						logWarnIfNonZero(recoveredCount,
						                 "ClusterManager: ......Scheduled " + recoveredCount + " recoverable job(s) for recovery.");
						logWarnIfNonZero(otherCount, "ClusterManager: ......Cleaned-up " + otherCount + " other failed job(s).");

						Delegate.deleteSchedulerState(conn, rec.SchedulerInstanceId);

						// update record to show that recovery was handled
						String recoverer = InstanceId;
						long checkInTS = rec.CheckinTimestamp;
						if (rec.SchedulerInstanceId.Equals(InstanceId))
						{
							recoverer = null;
							checkInTS = (DateTime.Now.Ticks - 621355968000000000)/10000;
						}

						Delegate.insertSchedulerState(conn, rec.SchedulerInstanceId, checkInTS, rec.CheckinInterval, recoverer);
					}
				}
				catch (Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Failure recovering jobs: " + e.Message, e);
				}
			}
		}

		protected internal virtual void logWarnIfNonZero(int val, String warning)
		{
			if (val > 0)
			{
				Log.info(warning);
			}
			else
			{
				Log.debug(warning);
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void closeConnection(OleDbConnection conn)
		{
			if (conn != null)
			{
				try
				{
					SupportClass.TransactionManager.manager.Close(conn);
				}
				catch (OleDbException e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't close jdbc connection. " + e.Message, e);
				}
			}
		}

		/// <summary> Rollback the supplied connection
		/// 
		/// </summary>
		/// <param name="conn">(Optional)
		/// </param>
		/// <throws>  JobPersistenceException thrown if a SQLException occurs when the </throws>
		/// <summary> connection is rolled back
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void rollbackConnection(OleDbConnection conn)
		{
			if (conn != null)
			{
				try
				{
					SupportClass.TransactionManager.manager.RollBack(conn);
				}
				catch (OleDbException e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException("Couldn't rollback jdbc connection. " + e.Message, e);
				}
			}
		}

		/// <summary> Commit the supplied connection
		/// 
		/// </summary>
		/// <param name="conn">(Optional)
		/// </param>
		/// <throws>  JobPersistenceException thrown if a SQLException occurs when the </throws>
		/// <summary> connection is committed
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual void commitConnection(OleDbConnection conn)
		{
			if (conn != null)
			{
				try
				{
					SupportClass.TransactionManager.manager.Commit(conn);
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
		internal class ClusterManager : SupportClass.ThreadClass
		{
			private void InitBlock(JobStoreSupport enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
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
					res = js.doCheckin();

					numFails = 0;
					Enclosing_Instance.Log.debug("ClusterManager: Check-in complete.");
				}
				catch (Exception e)
				{
					if (numFails%4 == 0)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						Enclosing_Instance.Log.error("ClusterManager: Error managing cluster: " + e.Message, e);
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
							Thread.Sleep(new TimeSpan((Int64) 10000*timeToSleep));
						}
						catch (Exception ignore)
						{
						}
					}

					if (!shutdown_Renamed_Field && manage())
					{
						Enclosing_Instance.signalSchedulingChange();
					}
				} //while !shutdown
			}
		}

		//UPGRADE_NOTE: Field 'EnclosingInstance' was added to class 'MisfireHandler' to access its enclosing instance. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1019_3"'
		/////////////////////////////////////////////////////////////////////////////
		//
		// MisfireHandler Thread
		//
		/////////////////////////////////////////////////////////////////////////////
		internal class MisfireHandler : SupportClass.ThreadClass
		{
			private void InitBlock(JobStoreSupport enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}

			private JobStoreSupport enclosingInstance;

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
				//this.manage();
				Start();
			}

			public virtual void shutdown()
			{
				shutdown_Renamed_Field = true;
				Interrupt();
			}

			private bool manage()
			{
				try
				{
					Enclosing_Instance.Log.debug("MisfireHandler: scanning for misfires...");

					bool res = js.doRecoverMisfires();
					numFails = 0;
					return res;
				}
				catch (Exception e)
				{
					if (numFails%4 == 0)
					{
						//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						Enclosing_Instance.Log.error("MisfireHandler: Error handling misfires: " + e.Message, e);
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

					bool moreToDo = manage();

					if (Enclosing_Instance.lastRecoverCount > 0)
					{
						Enclosing_Instance.signalSchedulingChange();
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
								//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javalangThreadsleep_long_3"'
								Thread.Sleep(new TimeSpan((Int64) 10000*timeToSleep));
							}
							catch (Exception ignore)
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
						catch (Exception ignore)
						{
						}
					}
				} //while !shutdown
			}
		}

		public abstract int getNumberOfTriggers(SchedulingContext param1);
		public abstract int getTriggerState(SchedulingContext param1, String param2, String param3);
		public abstract bool removeTrigger(SchedulingContext param1, String param2, String param3);
		public abstract void storeJobAndTrigger(SchedulingContext param1, JobDetail param2, Trigger param3);
		public abstract String[] getCalendarNames(SchedulingContext param1);
		public abstract int getNumberOfCalendars(SchedulingContext param1);
		public abstract void resumeJobGroup(SchedulingContext param1, String param2);
		public abstract void storeJob(SchedulingContext param1, JobDetail param2, bool param3);
		public abstract String[] getJobNames(SchedulingContext param1, String param2);
		public abstract TriggerFiredBundle triggerFired(SchedulingContext param1, Trigger param2);
		public abstract void triggeredJobComplete(SchedulingContext param1, Trigger param2, JobDetail param3, int param4);
		public abstract String[] getTriggerGroupNames(SchedulingContext param1);
		public abstract void pauseTrigger(SchedulingContext param1, String param2, String param3);
		public abstract void resumeAll(SchedulingContext param1);
		public abstract void storeTrigger(SchedulingContext param1, Trigger param2, bool param3);
		public abstract String[] getJobGroupNames(SchedulingContext param1);
		public abstract String[] getTriggerNames(SchedulingContext param1, String param2);
		public abstract void pauseAll(SchedulingContext param1);
		public abstract void pauseJobGroup(SchedulingContext param1, String param2);
		public abstract void pauseTriggerGroup(SchedulingContext param1, String param2);
		public abstract bool replaceTrigger(SchedulingContext param1, String param2, String param3, Trigger param4);
		public abstract void resumeJob(SchedulingContext param1, String param2, String param3);
		public abstract int getNumberOfJobs(SchedulingContext param1);
		public abstract void pauseJob(SchedulingContext param1, String param2, String param3);
		public abstract void releaseAcquiredTrigger(SchedulingContext param1, Trigger param2);
		public abstract JobDetail retrieveJob(SchedulingContext param1, String param2, String param3);
		public abstract bool removeJob(SchedulingContext param1, String param2, String param3);
		public abstract void resumeTrigger(SchedulingContext param1, String param2, String param3);
		public abstract Trigger acquireNextTrigger(SchedulingContext param1, long param2);
		public abstract Trigger[] getTriggersForJob(SchedulingContext param1, String param2, String param3);
		public abstract bool removeCalendar(SchedulingContext param1, String param2);
		public abstract Calendar retrieveCalendar(SchedulingContext param1, String param2);
		public abstract Trigger retrieveTrigger(SchedulingContext param1, String param2, String param3);
		public abstract void storeCalendar(SchedulingContext param1, String param2, Calendar param3, bool param4, bool param5);
		public abstract SupportClass.SetSupport getPausedTriggerGroups(SchedulingContext param1);
		public abstract void resumeTriggerGroup(SchedulingContext param1, String param2);
	}

	// EOF
}