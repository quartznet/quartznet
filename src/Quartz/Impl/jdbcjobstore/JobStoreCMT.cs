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
using Quartz.core;

namespace org.quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// <code>JobStoreCMT</code> is meant to be used in an application-server
	/// environment that provides container-managed-transactions. No commit /
	/// rollback will be1 handled by this class.
	/// </p>
	/// 
	/// <p>
	/// If you need commit / rollback, use <code>{@link
	/// org.quartz.impl.jdbcjobstore.JobStoreTX}</code>
	/// instead.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
	/// </author>
	/// <author>  James House
	/// </author>
	/// <author>  Srinivas Venkatarangaiah
	/// 
	/// </author>
	public class JobStoreCMT : JobStoreSupport
	{
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
		public virtual string NonManagedTXDataSource
		{
			get { return nonManagedTxDsName; }

			set { nonManagedTxDsName = value; }
		}

		/// <summary> Don't call set autocommit(false) on connections obtained from the
		/// DataSource. This can be helpfull in a few situations, such as if you
		/// have a driver that complains if it is called when it is already off.
		/// 
		/// </summary>
		/// <param name="">b
		/// </param>
		public virtual bool DontSetNonManagedTXConnectionAutoCommitFalse
		{
			get { return dontSetNonManagedTXConnectionAutoCommitFalse; }

			set { dontSetNonManagedTXConnectionAutoCommitFalse = value; }
		}

		/// <summary> Set the transaction isolation level of DB connections to sequential.
		/// 
		/// </summary>
		/// <param name="">b
		/// </param>
		public virtual bool TxIsolationLevelReadCommitted
		{
			get { return setTxIsolationLevelReadCommitted_Renamed_Field; }

			set { setTxIsolationLevelReadCommitted_Renamed_Field = value; }
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		protected internal virtual OleDbConnection NonManagedTXConnection
		{
			get
			{
				try
				{
					//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
					OleDbConnection conn = DBConnectionManager.Instance.getConnection(NonManagedTXDataSource);

					if (conn == null)
					{
						//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
						throw new SQLException("Could not get connection from DataSource '" + NonManagedTXDataSource + "'");
					}

					if (!DontSetNonManagedTXConnectionAutoCommitFalse)
					{
						SupportClass.TransactionManager.manager.SetAutoCommit(conn, false);
					}

					if (TxIsolationLevelReadCommitted)
					{
						//UPGRADE_TODO: The equivalent in .NET for field 'java.sql.Connection.TRANSACTION_READ_UNCOMMITTED' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
						SupportClass.TransactionManager.manager.SetTransactionIsolation(conn, (int) IsolationLevel.ReadUncommitted);
					}

					return conn;
				}
				catch (OleDbException sqle)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException(
						"Failed to obtain DB connection from data source '" + NonManagedTXDataSource + "': " + sqle.ToString(), sqle);
				}
				catch (Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException(
						"Failed to obtain DB connection from data source '" + NonManagedTXDataSource + "': " + e.ToString(), e,
						JobPersistenceException.ERR_PERSISTENCE_CRITICAL_FAILURE);
				}
			}
		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		protected internal string nonManagedTxDsName;

		// Great name huh?
		protected internal bool dontSetNonManagedTXConnectionAutoCommitFalse = false;


		protected internal bool setTxIsolationLevelReadCommitted_Renamed_Field = false;

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/


		public override void initialize(ClassLoadHelper loadHelper, SchedulerSignaler signaler)
		{
			if (nonManagedTxDsName == null)
			{
				throw new SchedulerConfigException("Non-ManagedTX DataSource name not set!");
			}

			UseDBLocks = true; // *must* use DB locks with CMT...

			base.initialize(loadHelper, signaler);

			Log.info("JobStoreCMT initialized.");
		}

		public override void shutdown()
		{
			base.shutdown();

			try
			{
				DBConnectionManager.Instance.shutdown(NonManagedTXDataSource);
			}
			catch (OleDbException sqle)
			{
				Log.warn("Database connection shutdown unsuccessful.", sqle);
			}
		}


		//---------------------------------------------------------------------------
		// JobStoreSupport methods
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Recover any failed or misfired jobs and clean up the data store as
		/// appropriate.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobPersistenceException </throws>
		/// <summary>           if jobs could not be recovered
		/// </summary>
		protected internal override void recoverJobs()
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;

			bool transOwner = false;

			try
			{
				conn = NonManagedTXConnection;

				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				recoverJobs(conn);

				SupportClass.TransactionManager.manager.Commit(conn);
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Error recovering jobs: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
					closeConnection(conn);
				}
			}
		}

		protected internal override void cleanVolatileTriggerAndJobs()
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;

			bool transOwner = false;

			try
			{
				conn = NonManagedTXConnection;

				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				cleanVolatileTriggerAndJobs(conn);

				SupportClass.TransactionManager.manager.Commit(conn);
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Error cleaning volatile data: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

					closeConnection(conn);
				}
			}
		}

		//---------------------------------------------------------------------------
		// job / trigger storage methods
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.JobDetail}</code> and <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">newJob
		/// The <code>JobDetail</code> to be stored.
		/// </param>
		/// <param name="">newTrigger
		/// The <code>Trigger</code> to be stored.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Job</code> with the same name/group already
		/// exists.
		/// </summary>
		public override void storeJobAndTrigger(SchedulingContext ctxt, JobDetail newJob, Trigger newTrigger)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				if (LockOnInsert)
				{
					LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
					transOwner = true;
					//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);
				}

				if (newJob.Volatile && !newTrigger.Volatile)
				{
					JobPersistenceException jpe =
						new JobPersistenceException("Cannot associate non-volatile " + "trigger with a volatile job!");
					jpe.ErrorCode = SchedulerException.ERR_CLIENT_ERROR;
					throw jpe;
				}

				storeJob(conn, ctxt, newJob, false);
				storeTrigger(conn, ctxt, newTrigger, newJob, false, Constants_Fields.STATE_WAITING, false, false);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.JobDetail}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">newJob
		/// The <code>JobDetail</code> to be stored.
		/// </param>
		/// <param name="">replaceExisting
		/// If <code>true</code>, any <code>Job</code> existing in the
		/// <code>JobStore</code> with the same name & group should be
		/// over-written.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Job</code> with the same name/group already
		/// exists, and replaceExisting is set to false.
		/// </summary>
		public override void storeJob(SchedulingContext ctxt, JobDetail newJob, bool replaceExisting)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				if (LockOnInsert || replaceExisting)
				{
					LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
					transOwner = true;
					//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);
				}

				storeJob(conn, ctxt, newJob, replaceExisting);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Remove (delete) the <code>{@link org.quartz.Job}</code> with the given
		/// name, and any <code>{@link org.quartz.Trigger}</code> s that reference
		/// it.
		/// </p>
		/// 
		/// <p>
		/// If removal of the <code>Job</code> results in an empty group, the
		/// group should be removed from the <code>JobStore</code>'s list of
		/// known group names.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">jobName
		/// The name of the <code>Job</code> to be removed.
		/// </param>
		/// <param name="">groupName
		/// The group name of the <code>Job</code> to be removed.
		/// </param>
		/// <returns> <code>true</code> if a <code>Job</code> with the given name &
		/// group was found and removed from the store.
		/// </returns>
		public override bool removeJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				return removeJob(conn, ctxt, jobName, groupName, true);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Retrieve the <code>{@link org.quartz.JobDetail}</code> for the given
		/// <code>{@link org.quartz.Job}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">jobName
		/// The name of the <code>Job</code> to be retrieved.
		/// </param>
		/// <param name="">groupName
		/// The group name of the <code>Job</code> to be retrieved.
		/// </param>
		/// <returns> The desired <code>Job</code>, or null if there is no match.
		/// </returns>
		public override JobDetail retrieveJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return retrieveJob(conn, ctxt, jobName, groupName);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">newTrigger
		/// The <code>Trigger</code> to be stored.
		/// </param>
		/// <param name="">replaceExisting
		/// If <code>true</code>, any <code>Trigger</code> existing in
		/// the <code>JobStore</code> with the same name & group should
		/// be over-written.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Trigger</code> with the same name/group already
		/// exists, and replaceExisting is set to false.
		/// </summary>
		public override void storeTrigger(SchedulingContext ctxt, Trigger newTrigger, bool replaceExisting)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				if (LockOnInsert || replaceExisting)
				{
					LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
					transOwner = true;
				}

				storeTrigger(conn, ctxt, newTrigger, null, replaceExisting, Constants_Fields.STATE_WAITING, false, false);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Remove (delete) the <code>{@link org.quartz.Trigger}</code> with the
		/// given name.
		/// </p>
		/// 
		/// <p>
		/// If removal of the <code>Trigger</code> results in an empty group, the
		/// group should be removed from the <code>JobStore</code>'s list of
		/// known group names.
		/// </p>
		/// 
		/// <p>
		/// If removal of the <code>Trigger</code> results in an 'orphaned' <code>Job</code>
		/// that is not 'durable', then the <code>Job</code> should be deleted
		/// also.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">triggerName
		/// The name of the <code>Trigger</code> to be removed.
		/// </param>
		/// <param name="">groupName
		/// The group name of the <code>Trigger</code> to be removed.
		/// </param>
		/// <returns> <code>true</code> if a <code>Trigger</code> with the given
		/// name & group was found and removed from the store.
		/// </returns>
		public override bool removeTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;

				return removeTrigger(conn, ctxt, triggerName, groupName);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}


		/// <seealso cref="java.lang.String, java.lang.String, org.quartz.Trigger)">
		/// </seealso>
		public override bool replaceTrigger(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;

				return replaceTrigger(conn, ctxt, triggerName, groupName, newTrigger);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Retrieve the given <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">triggerName
		/// The name of the <code>Trigger</code> to be retrieved.
		/// </param>
		/// <param name="">groupName
		/// The group name of the <code>Trigger</code> to be retrieved.
		/// </param>
		/// <returns> The desired <code>Trigger</code>, or null if there is no
		/// match.
		/// </returns>
		public override Trigger retrieveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return retrieveTrigger(conn, ctxt, triggerName, groupName);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.Calendar}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">calName
		/// The name of the calendar.
		/// </param>
		/// <param name="">calendar
		/// The <code>Calendar</code> to be stored.
		/// </param>
		/// <param name="">replaceExisting
		/// If <code>true</code>, any <code>Calendar</code> existing
		/// in the <code>JobStore</code> with the same name & group
		/// should be over-written.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Calendar</code> with the same name already
		/// exists, and replaceExisting is set to false.
		/// </summary>
		public override void storeCalendar(SchedulingContext ctxt, string calName, Calendar calendar, bool replaceExisting,
		                                   bool updateTriggers)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool lockOwner = false;
			try
			{
				if (LockOnInsert || updateTriggers)
				{
					LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
					lockOwner = true;
				}

				storeCalendar(conn, ctxt, calName, calendar, replaceExisting, updateTriggers);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, lockOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Remove (delete) the <code>{@link org.quartz.Calendar}</code> with the
		/// given name.
		/// </p>
		/// 
		/// <p>
		/// If removal of the <code>Calendar</code> would result in
		/// <code.Trigger</code>s pointing to non-existent calendars, then a
		/// <code>JobPersistenceException</code> will be thrown.</p>
		/// *
		/// </summary>
		/// <param name="calName">The name of the <code>Calendar</code> to be removed.
		/// </param>
		/// <returns> <code>true</code> if a <code>Calendar</code> with the given name
		/// was found and removed from the store.
		/// </returns>
		public override bool removeCalendar(SchedulingContext ctxt, string calName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				LockHandler.obtainLock(conn, LOCK_CALENDAR_ACCESS);

				return removeCalendar(conn, ctxt, calName);
			}
			finally
			{
				releaseLock(conn, LOCK_CALENDAR_ACCESS, true);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Retrieve the given <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">calName
		/// The name of the <code>Calendar</code> to be retrieved.
		/// </param>
		/// <returns> The desired <code>Calendar</code>, or null if there is no
		/// match.
		/// </returns>
		public override Calendar retrieveCalendar(SchedulingContext ctxt, string calName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return retrieveCalendar(conn, ctxt, calName);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		//---------------------------------------------------------------------------
		// informational methods
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Job}</code> s that are
		/// stored in the <code>JobStore</code>.
		/// </p>
		/// </summary>
		public override int getNumberOfJobs(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getNumberOfJobs(conn, ctxt);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Trigger}</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </p>
		/// </summary>
		public override int getNumberOfTriggers(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getNumberOfTriggers(conn, ctxt);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Calendar}</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </p>
		/// </summary>
		public override int getNumberOfCalendars(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getNumberOfCalendars(conn, ctxt);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		public override SupportClass.SetSupport getPausedTriggerGroups(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				SupportClass.SetSupport groups = getPausedTriggerGroups(conn, ctxt);
				return groups;
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Job}</code> s that
		/// have the given group name.
		/// </p>
		/// 
		/// <p>
		/// If there are no jobs in the given group name, the result should be a
		/// zero-length array (not <code>null</code>).
		/// </p>
		/// </summary>
		public override string[] getJobNames(SchedulingContext ctxt, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getJobNames(conn, ctxt, groupName);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Trigger}</code> s
		/// that have the given group name.
		/// </p>
		/// 
		/// <p>
		/// If there are no triggers in the given group name, the result should be a
		/// zero-length array (not <code>null</code>).
		/// </p>
		/// </summary>
		public override string[] getTriggerNames(SchedulingContext ctxt, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getTriggerNames(conn, ctxt, groupName);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Job}</code>
		/// groups.
		/// </p>
		/// 
		/// <p>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <code>null</code>).
		/// </p>
		/// </summary>
		public override string[] getJobGroupNames(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getJobGroupNames(conn, ctxt);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Trigger}</code>
		/// groups.
		/// </p>
		/// 
		/// <p>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <code>null</code>).
		/// </p>
		/// </summary>
		public override string[] getTriggerGroupNames(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getTriggerGroupNames(conn, ctxt);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Calendar}</code> s
		/// in the <code>JobStore</code>.
		/// </p>
		/// 
		/// <p>
		/// If there are no Calendars in the given group name, the result should be
		/// a zero-length array (not <code>null</code>).
		/// </p>
		/// </summary>
		public override string[] getCalendarNames(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getCalendarNames(conn, ctxt);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get all of the Triggers that are associated to the given Job.
		/// </p>
		/// 
		/// <p>
		/// If there are no matches, a zero-length array should be returned.
		/// </p>
		/// </summary>
		public override Trigger[] getTriggersForJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getTriggersForJob(conn, ctxt, jobName, groupName);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the current state of the identified <code>{@link Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="Trigger#STATE_NORMAL">
		/// </seealso>
		/// <seealso cref="Trigger#STATE_PAUSED">
		/// </seealso>
		/// <seealso cref="Trigger#STATE_COMPLETE">
		/// </seealso>
		/// <seealso cref="Trigger#STATE_ERROR">
		/// </seealso>
		/// <seealso cref="Trigger#STATE_NONE">
		/// </seealso>
		public override int getTriggerState(SchedulingContext ctxt, string triggerName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return getTriggerState(conn, ctxt, triggerName, groupName);
			}
			finally
			{
				closeConnection(conn);
			}
		}

		//---------------------------------------------------------------------------
		// trigger state manipulation methods
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Pause the <code>{@link org.quartz.Trigger}</code> with the given name.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string, String)">
		/// </seealso>
		public override void pauseTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				pauseTrigger(conn, ctxt, triggerName, groupName);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Pause all of the <code>{@link org.quartz.Trigger}s</code> in the
		/// given group.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string)">
		/// </seealso>
		public override void pauseTriggerGroup(SchedulingContext ctxt, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				pauseTriggerGroup(conn, ctxt, groupName);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Pause the <code>{@link org.quartz.Job}</code> with the given name - by
		/// pausing all of its current <code>Trigger</code>s.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string, String)">
		/// </seealso>
		public override void pauseJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				Trigger[] triggers = getTriggersForJob(conn, ctxt, jobName, groupName);
				for (int j = 0; j < triggers.Length; j++)
				{
					pauseTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
				}
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Pause all of the <code>{@link org.quartz.Job}s</code> in the given
		/// group - by pausing all of their <code>Trigger</code>s.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string)">
		/// </seealso>
		public override void pauseJobGroup(SchedulingContext ctxt, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				string[] jobNames = getJobNames(conn, ctxt, groupName);

				for (int i = 0; i < jobNames.Length; i++)
				{
					Trigger[] triggers = getTriggersForJob(conn, ctxt, jobNames[i], groupName);
					for (int j = 0; j < triggers.Length; j++)
					{
						pauseTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
					}
				}
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

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
		/// <seealso cref="string, String)">
		/// </seealso>
		public override void resumeTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				resumeTrigger(conn, ctxt, triggerName, groupName);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
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
		/// <seealso cref="string)">
		/// </seealso>
		public override void resumeTriggerGroup(SchedulingContext ctxt, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				resumeTriggerGroup(conn, ctxt, groupName);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Resume (un-pause) the <code>{@link org.quartz.Job}</code> with the
		/// given name.
		/// </p>
		/// 
		/// <p>
		/// If any of the <code>Job</code>'s<code>Trigger</code> s missed one
		/// or more fire-times, then the <code>Trigger</code>'s misfire
		/// instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string, String)">
		/// </seealso>
		public override void resumeJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				Trigger[] triggers = getTriggersForJob(conn, ctxt, jobName, groupName);
				for (int j = 0; j < triggers.Length; j++)
				{
					resumeTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
				}
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
		/// Resume (un-pause) all of the <code>{@link org.quartz.Job}s</code> in
		/// the given group.
		/// </p>
		/// 
		/// <p>
		/// If any of the <code>Job</code> s had <code>Trigger</code> s that
		/// missed one or more fire-times, then the <code>Trigger</code>'s
		/// misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string)">
		/// </seealso>
		public override void resumeJobGroup(SchedulingContext ctxt, string groupName)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				string[] jobNames = getJobNames(conn, ctxt, groupName);

				for (int i = 0; i < jobNames.Length; i++)
				{
					Trigger[] triggers = getTriggersForJob(conn, ctxt, jobNames[i], groupName);
					for (int j = 0; j < triggers.Length; j++)
					{
						resumeTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
					}
				}
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
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
		/// <seealso cref="string)">
		/// </seealso>
		public override void pauseAll(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				pauseAll(conn, ctxt);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		/// <summary> <p>
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
		public override void resumeAll(SchedulingContext ctxt)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				resumeAll(conn, ctxt);
			}
			finally
			{
				releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

				closeConnection(conn);
			}
		}

		//---------------------------------------------------------------------------
		// trigger firing methods
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Get a handle to the next trigger to be fired, and mark it as 'reserved'
		/// by the calling scheduler.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="Trigger)">
		/// </seealso>
		public override Trigger acquireNextTrigger(SchedulingContext ctxt, long noLaterThan)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;
			bool transOwner = false;

			try
			{
				conn = NonManagedTXConnection;

				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				Trigger trigger = acquireNextTrigger(conn, ctxt, noLaterThan);

				SupportClass.TransactionManager.manager.Commit(conn);
				return trigger;
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Error acquiring next firable trigger: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

					closeConnection(conn);
				}
			}
		}

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler no longer plans to
		/// fire the given <code>Trigger</code>, that it had previously acquired
		/// (reserved).
		/// </p>
		/// </summary>
		public override void releaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;
			bool transOwner = false;

			try
			{
				conn = NonManagedTXConnection;

				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				releaseAcquiredTrigger(conn, ctxt, trigger);
				SupportClass.TransactionManager.manager.Commit(conn);
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("Error releasing acquired trigger: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

					closeConnection(conn);
				}
			}
		}

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler is now firing the
		/// given <code>Trigger</code> (executing its associated <code>Job</code>),
		/// that it had previously acquired (reserved).
		/// </p>
		/// 
		/// </summary>
		/// <returns> null if the trigger or it's job or calendar no longer exist, or
		/// if the trigger was not successfully put into the 'executing'
		/// state.
		/// </returns>
		public override TriggerFiredBundle triggerFired(SchedulingContext ctxt, Trigger trigger)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;
			bool transOwner = false;

			try
			{
				conn = NonManagedTXConnection;

				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				TriggerFiredBundle tfb = null;
				JobPersistenceException err = null;
				try
				{
					tfb = triggerFired(conn, ctxt, trigger);
				}
				catch (JobPersistenceException jpe)
				{
					if (jpe.ErrorCode != SchedulerException.ERR_PERSISTENCE_JOB_DOES_NOT_EXIST)
					{
						throw jpe;
					}
					err = jpe;
				}

				if (err != null)
				{
					throw err;
				}

				SupportClass.TransactionManager.manager.Commit(conn);
				return tfb;
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("TX failure: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

					closeConnection(conn);
				}
			}
		}

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler has completed the
		/// firing of the given <code>Trigger</code> (and the execution its
		/// associated <code>Job</code>), and that the <code>{@link org.quartz.JobDataMap}</code>
		/// in the given <code>JobDetail</code> should be updated if the <code>Job</code>
		/// is stateful.
		/// </p>
		/// </summary>
		public override void triggeredJobComplete(SchedulingContext ctxt, Trigger trigger, JobDetail jobDetail,
		                                          int triggerInstCode)
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;
			bool transOwner = false;

			try
			{
				conn = NonManagedTXConnection;
				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);

				triggeredJobComplete(conn, ctxt, trigger, jobDetail, triggerInstCode);

				SupportClass.TransactionManager.manager.Commit(conn);
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("TX failure: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

					closeConnection(conn);
				}
			}
		}

		protected internal override bool doRecoverMisfires()
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;
			bool transOwner = false;
			bool moreToDo = false;

			try
			{
				conn = NonManagedTXConnection;

				LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;

				try
				{
					moreToDo = recoverMisfiredJobs(conn, false);
				}
				catch (Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException(e.Message, e);
				}

				SupportClass.TransactionManager.manager.Commit(conn);

				return moreToDo;
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("TX failure: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);

					closeConnection(conn);
				}
			}
		}

		protected internal override bool doCheckin()
		{
			//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
			OleDbConnection conn = null;

			bool transOwner = false;
			bool transStateOwner = false;
			bool recovered = false;

			try
			{
				conn = NonManagedTXConnection;

				LockHandler.obtainLock(conn, LOCK_STATE_ACCESS);
				transStateOwner = true;

				IList failedRecords = clusterCheckIn(conn);

				if (failedRecords.Count > 0)
				{
					LockHandler.obtainLock(conn, LOCK_TRIGGER_ACCESS);
					//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);
					transOwner = true;

					clusterRecover(conn, failedRecords);
					recovered = true;
				}

				SupportClass.TransactionManager.manager.Commit(conn);
			}
			catch (JobPersistenceException e)
			{
				rollbackConnection(conn);
				throw e;
			}
			catch (Exception e)
			{
				rollbackConnection(conn);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				throw new JobPersistenceException("TX failure: " + e.Message, e);
			}
			finally
			{
				if (conn != null)
				{
					releaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
					releaseLock(conn, LOCK_STATE_ACCESS, transStateOwner);

					closeConnection(conn);
				}
			}

			firstCheckIn = false;

			return recovered;
		}

		//---------------------------------------------------------------------------
		// private helpers
		//---------------------------------------------------------------------------
	}

	// EOF
}