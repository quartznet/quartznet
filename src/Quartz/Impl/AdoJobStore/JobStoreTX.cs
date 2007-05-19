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

using Quartz;
using Quartz.Collection;
using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary>
	/// <code>JobStoreTX</code> is meant to be used in a standalone environment.
	/// Both commit and rollback will be handled by this class.
	/// </summary>
	/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
	/// <author>James House</author>
	public class JobStoreTX : JobStoreSupport
	{
		public override void Initialize(IClassLoadHelper loadHelper, ISchedulerSignaler signaler)
		{
			base.Initialize(loadHelper, signaler);
			Log.Info("JobStoreTX initialized.");
		}

		/// <summary>
		/// Recover any failed or misfired jobs and clean up the data store as
		/// appropriate.
		/// </summary>
		/// <throws>JobPersistenceException </throws>
		protected internal override void RecoverJobs()
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				RecoverJobs(conn);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		protected internal override void CleanVolatileTriggerAndJobs()
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				CleanVolatileTriggerAndJobs(conn);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
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
		public override void StoreJobAndTrigger(SchedulingContext ctxt, JobDetail newJob, Trigger newTrigger)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				if (LockOnInsert)
				{
					LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
					transOwner = true;
					//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);
				}

				if (newJob.Volatile && !newTrigger.Volatile)
				{
					JobPersistenceException jpe =
						new JobPersistenceException("Cannot associate non-volatile " + "trigger with a volatile job!");
					jpe.ErrorCode = SchedulerException.ERR_CLIENT_ERROR;
					throw jpe;
				}

				StoreJob(conn, ctxt, newJob, false);
				StoreTrigger(conn, ctxt, newTrigger, newJob, false, AdoConstants.STATE_WAITING, false, false);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override void StoreJob(SchedulingContext ctxt, JobDetail newJob, bool replaceExisting)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				if (LockOnInsert || replaceExisting)
				{
					LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
					transOwner = true;
					//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);
				}

				StoreJob(conn, ctxt, newJob, replaceExisting);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override bool RemoveJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				bool removed = RemoveJob(conn, ctxt, jobName, groupName, true);
				CommitConnection(conn);
				return removed;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override JobDetail RetrieveJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				JobDetail job = RetrieveJob(conn, ctxt, jobName, groupName);
				CommitConnection(conn);
				return job;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override void StoreTrigger(SchedulingContext ctxt, Trigger newTrigger, bool replaceExisting)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				if (LockOnInsert || replaceExisting)
				{
					LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
					transOwner = true;
					//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);
				}

				StoreTrigger(conn, ctxt, newTrigger, null, replaceExisting, AdoConstants.STATE_WAITING, false, false);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override bool RemoveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				bool removed = RemoveTrigger(conn, ctxt, triggerName, groupName);
				CommitConnection(conn);
				return removed;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		public override bool ReplaceTrigger(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				bool removed = ReplaceTrigger(conn, ctxt, triggerName, groupName, newTrigger);
				CommitConnection(conn);
				return removed;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override Trigger RetrieveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				Trigger trigger = RetrieveTrigger(conn, ctxt, triggerName, groupName);
				CommitConnection(conn);
				return trigger;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override void StoreCalendar(SchedulingContext ctxt, string calName, ICalendar calendar, bool replaceExisting,
		                                   bool updateTriggers)
		{
			
			IDbConnection conn = Connection;
			bool lockOwner = false;
			try
			{
				if (LockOnInsert || updateTriggers)
				{
					LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
					lockOwner = true;
				}

				StoreCalendar(conn, ctxt, calName, calendar, replaceExisting, updateTriggers);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, lockOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override bool RemoveCalendar(SchedulingContext ctxt, string calName)
		{
			IDbConnection conn = Connection;
			bool lockOwner = false;
			try 
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				lockOwner = true;

				bool removed = RemoveCalendar(conn, ctxt, calName);
				CommitConnection(conn);
				return removed;
			} 
			catch (JobPersistenceException e) 
			{
				RollbackConnection(conn);
				throw e;
			} 
			finally 
			{
				try 
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, lockOwner);
				} 
				finally 
				{
					CloseConnection(conn);
				}
			}
		}

		/// <summary>
		/// Retrieve the given <code>Trigger</code>.
		/// </summary>
		/// <param name="calName">The name of the <code>Calendar</code> to be retrieved.</param>
		/// <returns> The desired <code>Calendar</code>, or null if there is no match.</returns>
		public override ICalendar RetrieveCalendar(SchedulingContext ctxt, string calName)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				ICalendar cal = RetrieveCalendar(conn, ctxt, calName);
				CommitConnection(conn);
				return cal;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override int GetNumberOfJobs(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				int numJobs = GetNumberOfJobs(conn, ctxt);
				CommitConnection(conn);
				return numJobs;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Trigger}</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </p>
		/// </summary>
		public override int GetNumberOfTriggers(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				int numTriggers = GetNumberOfTriggers(conn, ctxt);
				CommitConnection(conn);
				return numTriggers;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
			}
		}

		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Calendar}</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </p>
		/// </summary>
		public override int GetNumberOfCalendars(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				int numCals = GetNumberOfCalendars(conn, ctxt);
				CommitConnection(conn);
				return numCals;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
			}
		}


		public override ISet GetPausedTriggerGroups(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				ISet groups = GetPausedTriggerGroups(conn, ctxt);
				CommitConnection(conn);
				return groups;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override string[] GetJobNames(SchedulingContext ctxt, string groupName)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				string[] jobNames = GetJobNames(conn, ctxt, groupName);
				CommitConnection(conn);
				return jobNames;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override string[] GetTriggerNames(SchedulingContext ctxt, string groupName)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				string[] triggerNames = GetTriggerNames(conn, ctxt, groupName);
				CommitConnection(conn);
				return triggerNames;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override string[] GetJobGroupNames(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				string[] groupNames = GetJobGroupNames(conn, ctxt);
				CommitConnection(conn);
				return groupNames;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override string[] GetTriggerGroupNames(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				string[] triggerGroups = GetTriggerGroupNames(conn, ctxt);
				CommitConnection(conn);
				return triggerGroups;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override string[] GetCalendarNames(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				string[] calNames = GetCalendarNames(conn, ctxt);
				CommitConnection(conn);
				return calNames;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
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
		public override Trigger[] GetTriggersForJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return GetTriggersForJob(conn, ctxt, jobName, groupName);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
			}
		}

		/// <summary>
		/// Get the current state of the identified <code>{@link Trigger}</code>.
		/// </summary>
		/// <seealso cref="Trigger.STATE_NORMAL">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_PAUSED">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_COMPLETE">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_ERROR">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_NONE">
		/// </seealso>
		public override int GetTriggerState(SchedulingContext ctxt, string triggerName, string groupName)
		{
			
			IDbConnection conn = Connection;
			try
			{
				// no locks necessary for read...
				return GetTriggerState(conn, ctxt, triggerName, groupName);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				CloseConnection(conn);
			}
		}

		//---------------------------------------------------------------------------
		// trigger state manipulation methods
		//---------------------------------------------------------------------------

		/// <summary>
		/// Pause the <code>Trigger</code> with the given name.
		/// </summary>
		public override void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				PauseTrigger(conn, ctxt, triggerName, groupName);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		/// <summary> 
		/// Pause all of the <code>Trigger</code>s in the given group.
		/// </summary>
		public override void PauseTriggerGroup(SchedulingContext ctxt, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				PauseTriggerGroup(conn, ctxt, groupName);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override void PauseJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				Trigger[] triggers = GetTriggersForJob(conn, ctxt, jobName, groupName);
				for (int j = 0; j < triggers.Length; j++)
				{
					PauseTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
				}

				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override void PauseJobGroup(SchedulingContext ctxt, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				string[] jobNames = GetJobNames(conn, ctxt, groupName);

				for (int i = 0; i < jobNames.Length; i++)
				{
					Trigger[] triggers = GetTriggersForJob(conn, ctxt, jobNames[i], groupName);
					for (int j = 0; j < triggers.Length; j++)
					{
						PauseTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
					}
				}

				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) the <code>Trigger</code> with the
		/// given name.
		/// <p>
		/// If the <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		public override void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				ResumeTrigger(conn, ctxt, triggerName, groupName);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override void ResumeTriggerGroup(SchedulingContext ctxt, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				ResumeTriggerGroup(conn, ctxt, groupName);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override void ResumeJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				Trigger[] triggers = GetTriggersForJob(conn, ctxt, jobName, groupName);
				for (int j = 0; j < triggers.Length; j++)
				{
					ResumeTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
				}

				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		/// <summary> 
		/// Resume (un-pause) all of the <code>Job</code>s in
		/// the given group.
		/// <p>
		/// If any of the <code>Job</code> s had <code>Trigger</code> s that
		/// missed one or more fire-times, then the <code>Trigger</code>'s
		/// misfire instruction will be applied.
		/// </p>
		/// </summary>
		public override void ResumeJobGroup(SchedulingContext ctxt, string groupName)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				string[] jobNames = GetJobNames(conn, ctxt, groupName);

				for (int i = 0; i < jobNames.Length; i++)
				{
					Trigger[] triggers = GetTriggersForJob(conn, ctxt, jobNames[i], groupName);
					for (int j = 0; j < triggers.Length; j++)
					{
						ResumeTrigger(conn, ctxt, triggers[j].Name, triggers[j].Group);
					}
				}
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		/// <summary> <p>
		/// Pause all triggers - equivalent of calling <code>PauseTriggerGroup(group)</code>
		/// on every group.
		/// </p>
		/// 
		/// <p>
		/// When <code>ResumeAll()</code> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="#ResumeAll(SchedulingContext)">
		/// </seealso>
		/// <seealso cref="string)">
		/// </seealso>
		public override void PauseAll(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				PauseAll(conn, ctxt);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override void ResumeAll(SchedulingContext ctxt)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				ResumeAll(conn, ctxt);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
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
		public override Trigger AcquireNextTrigger(SchedulingContext ctxt, DateTime noLaterThan)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				Trigger trigger = AcquireNextTrigger(conn, ctxt, noLaterThan);
				CommitConnection(conn);
				return trigger;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler no longer plans to
		/// fire the given <code>Trigger</code>, that it had previously acquired
		/// (reserved).
		/// </p>
		/// </summary>
		public override void ReleaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				ReleaseAcquiredTrigger(conn, ctxt, trigger);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
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
		public override TriggerFiredBundle TriggerFired(SchedulingContext ctxt, Trigger trigger)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				TriggerFiredBundle tfb = null;
				JobPersistenceException err = null;
				try
				{
					tfb = TriggerFired(conn, ctxt, trigger);
				}
				catch (JobPersistenceException jpe)
				{
					if (jpe.ErrorCode != SchedulerException.ERR_PERSISTENCE_JOB_DOES_NOT_EXIST)
					{
						throw jpe;
					}
					err = jpe;
				}

				CommitConnection(conn);
				if (err != null)
				{
					throw err;
				}
				return tfb;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
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
		public override void TriggeredJobComplete(SchedulingContext ctxt, Trigger trigger, JobDetail jobDetail,
		                                          int triggerInstCode)
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				TriggeredJobComplete(conn, ctxt, trigger, jobDetail, triggerInstCode);
				CommitConnection(conn);
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		protected internal override bool DoRecoverMisfires()
		{
			
			IDbConnection conn = Connection;
			bool transOwner = false;
			bool moreToDo = false;
			try
			{
				LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
				transOwner = true;
				//getLockHandler().ObtainLock(conn, LOCK_JOB_ACCESS);

				try
				{
					moreToDo = RecoverMisfiredJobs(conn, false);
				}
				catch (Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new JobPersistenceException(e.Message, e);
				}

				CommitConnection(conn);

				return moreToDo;
			}
			catch (JobPersistenceException e)
			{
				RollbackConnection(conn);
				throw e;
			}
			finally
			{
				try
				{
					ReleaseLock(conn, LOCK_TRIGGER_ACCESS, transOwner);
				}
				finally
				{
					CloseConnection(conn);
				}
			}
		}

		protected internal override bool DoCheckin()
		{
			
			IDbConnection conn = Connection;

			bool transOwner = false;
			bool transStateOwner = false;
			bool recovered = false;

			try 
			{
				// Other than the first time, always checkin first to make sure there is 
				// work to be done before we aquire / the lock (since that is expensive, 
				// and is almost never necessary)
				IList failedRecords = (firstCheckIn) ? null : ClusterCheckIn(conn);
            
				if (firstCheckIn || (failedRecords.Count > 0)) 
				{
					LockHandler.ObtainLock(conn, LOCK_STATE_ACCESS);
					transStateOwner = true;
    
					// Now that we own the lock, make sure we still have work to do. 
					// The first time through, we also need to make sure we update/create our state record
					failedRecords = (firstCheckIn) ? ClusterCheckIn(conn) : findFailedInstances(conn);
    
					if (failedRecords.Count > 0) 
					{
						LockHandler.ObtainLock(conn, LOCK_TRIGGER_ACCESS);
						//getLockHandler().obtainLock(conn, LOCK_JOB_ACCESS);
						transOwner = true;
    
						ClusterRecover(conn, failedRecords);
						recovered = true;
					}
				}
				CommitConnection(conn);
			} 
			catch (JobPersistenceException e) 
			{
				RollbackConnection(conn);
				throw e;
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
						CloseConnection(conn);
					}
				}
			}

			firstCheckIn = false;

			return recovered;
		}
	}

	// EOF
}