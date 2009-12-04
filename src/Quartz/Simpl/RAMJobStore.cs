/* 
* Copyright 2004-2009 James House 
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
using System.Globalization;
using System.Text;

using Common.Logging;

using Quartz.Collection;
using Quartz.Core;
using Quartz.Spi;
#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

namespace Quartz.Simpl
{
	/// <summary>
	/// This class implements a <see cref="IJobStore" /> that
	/// utilizes RAM as its storage device.
	/// <p>
	/// As you should know, the ramification of this is that access is extrememly
	/// fast, but the data is completely volatile - therefore this <see cref="IJobStore" />
	/// should not be used if true persistence between program shutdowns is
	/// required.
	/// </p>
	/// </summary>
	/// <author>James House</author>
	/// <author>Sharada Jambula</author>
	/// <author>Marko Lahma (.NET)</author>
	public class RAMJobStore : IJobStore
	{
		private readonly IDictionary jobsByFQN = new Hashtable(1000);
		private readonly IDictionary triggersByFQN = new Hashtable(1000);
		private readonly IDictionary jobsByGroup = new Hashtable(25);
		private readonly IDictionary triggersByGroup = new Hashtable(25);
		private readonly TreeSet timeTriggers = new TreeSet(new TriggerComparator());
		private readonly IDictionary calendarsByName = new Hashtable(25);
		private readonly ArrayList triggers = new ArrayList(1000);
		private readonly object triggerLock = new object();
		private readonly HashSet pausedTriggerGroups = new HashSet();
        private readonly HashSet pausedJobGroups = new HashSet();
        private readonly HashSet blockedJobs = new HashSet();
		private TimeSpan misfireThreshold = TimeSpan.FromSeconds(5);
		private ISchedulerSignaler signaler;
		
		private readonly ILog log;


        /// <summary>
        /// Initializes a new instance of the <see cref="RAMJobStore"/> class.
        /// </summary>
	    public RAMJobStore()
	    {
	        log = LogManager.GetLogger(GetType());
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
					throw new ArgumentException("Misfirethreashold must be larger than 0");
				}
				misfireThreshold = value;
			}
		}

		/// <summary>
		/// Gets the fired trigger record id.
		/// </summary>
		/// <value>The fired trigger record id.</value>
		protected internal virtual string FiredTriggerRecordId
		{
			get
			{
				lock (this)
				{
                    return Convert.ToString(ftrCtr++, CultureInfo.InvariantCulture);
				}
			}
		}

		/// <summary>
		/// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
		/// used, in order to give the it a chance to Initialize.
		/// </summary>
		public virtual void Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler s)
		{
			signaler = s;
			Log.Info("RAMJobStore initialized.");
		}

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
        /// the scheduler has started.
        /// </summary>
		public virtual void SchedulerStarted()
		{
			// nothing to do
		}

		/// <summary>
		/// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
		/// it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </summary>
		public virtual void Shutdown()
		{
		}

		/// <summary>
		/// Returns whether this instance supports persistence.
		/// </summary>
		/// <value></value>
		/// <returns></returns>
	    public virtual bool SupportsPersistence
	    {
	        get { return false; }
	    }

	    protected ILog Log
	    {
	        get { return log; }
	    }

	    /// <summary>
		/// Store the given <see cref="JobDetail" /> and <see cref="Trigger" />.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="newJob">The <see cref="JobDetail" /> to be stored.</param>
		/// <param name="newTrigger">The <see cref="Trigger" /> to be stored.</param>
		public virtual void StoreJobAndTrigger(SchedulingContext ctxt, JobDetail newJob, Trigger newTrigger)
		{
			StoreJob(ctxt, newJob, false);
			StoreTrigger(ctxt, newTrigger, false);
		}

	    /// <summary>
	    /// Returns true if the given job group is paused.
	    /// </summary>
	    /// <param name="ctxt"></param>
	    /// <param name="groupName">Job group name</param>
	    /// <returns></returns>
	    public virtual bool IsJobGroupPaused(SchedulingContext ctxt, string groupName)
	    {
            return pausedJobGroups.Contains(groupName);
	    }

	    /// <summary>
	    /// returns true if the given TriggerGroup is paused.
	    /// </summary>
	    /// <param name="ctxt"></param>
	    /// <param name="groupName"></param>
	    /// <returns></returns>
	    public virtual bool IsTriggerGroupPaused(SchedulingContext ctxt, string groupName)
	    {
	       return pausedTriggerGroups.Contains(groupName);
	    }

	    /// <summary>
		/// Store the given <see cref="IJob" />.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="newJob">The <see cref="IJob" /> to be stored.</param>
		/// <param name="replaceExisting">If <see langword="true" />, any <see cref="IJob" /> existing in the
		/// <see cref="IJobStore" /> with the same name and group should be
		/// over-written.</param>
		public virtual void StoreJob(SchedulingContext ctxt, JobDetail newJob, bool replaceExisting)
		{
            JobWrapper jw = new JobWrapper((JobDetail)newJob.Clone());

			bool repl = false;

			if (jobsByFQN[jw.key] != null)
			{
				if (!replaceExisting)
				{
					throw new ObjectAlreadyExistsException(newJob);
				}
				repl = true;
			}

			lock (triggerLock)
			{
				if (!repl)
				{
					// get job group
					IDictionary grpMap = (Hashtable) jobsByGroup[newJob.Group];
					if (grpMap == null)
					{
						grpMap = new Hashtable(100);
						jobsByGroup[newJob.Group] = grpMap;
					}
					// add to jobs by group
					grpMap[newJob.Name] = jw;
					// add to jobs by FQN map
					jobsByFQN[jw.key] = jw;
				}
				else
				{
					// update job detail
					JobWrapper orig = (JobWrapper) jobsByFQN[jw.key];
                    orig.jobDetail = jw.jobDetail;
				}
			}
		}

		/// <summary>
		/// Remove (delete) the <see cref="IJob" /> with the given
		/// name, and any <see cref="Trigger" /> s that reference
		/// it.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="jobName">The name of the <see cref="IJob" /> to be removed.</param>
		/// <param name="groupName">The group name of the <see cref="IJob" /> to be removed.</param>
		/// <returns>
		/// 	<see langword="true" /> if a <see cref="IJob" /> with the given name and
		/// group was found and removed from the store.
		/// </returns>
		public virtual bool RemoveJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			string key = JobWrapper.GetJobNameKey(jobName, groupName);

			bool found = false;

			Trigger[] trigger = GetTriggersForJob(ctxt, jobName, groupName);
			for (int i = 0; i < trigger.Length; i++)
			{
				Trigger trig = trigger[i];
				RemoveTrigger(ctxt, trig.Name, trig.Group);
				found = true;
			}
            lock (triggerLock)
			{
				object tempObject;
				tempObject = jobsByFQN[key];
				jobsByFQN.Remove(key);
				found = (tempObject != null) | found;
				if (found)
				{
					IDictionary grpMap = (Hashtable) jobsByGroup[groupName];
					if (grpMap != null)
					{
						grpMap.Remove(jobName);
						if (grpMap.Count == 0)
						{
							jobsByGroup.Remove(groupName);
						}
					}
				}
			}

			return found;
		}

        /// <summary>
        /// Remove (delete) the <see cref="Trigger" /> with the
        /// given name.
        /// </summary>
        /// <param name="ctxt">The scheduling context.</param>
        /// <param name="triggerName">The name of the <see cref="Trigger" /> to be removed.</param>
        /// <param name="groupName">The group name of the <see cref="Trigger" /> to be removed.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="Trigger" /> with the given
        /// name and group was found and removed from the store.
        /// </returns>
	    public virtual bool RemoveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
	    {
	        return RemoveTrigger(ctxt, triggerName, groupName, true);
	    }

	    /// <summary>
		/// Store the given <see cref="Trigger" />.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="newTrigger">The <see cref="Trigger" /> to be stored.</param>
		/// <param name="replaceExisting">If <see langword="true" />, any <see cref="Trigger" /> existing in
		/// the <see cref="IJobStore" /> with the same name and group should
		/// be over-written.</param>
		public virtual void StoreTrigger(SchedulingContext ctxt, Trigger newTrigger, bool replaceExisting)
		{
            TriggerWrapper tw = new TriggerWrapper((Trigger)newTrigger.Clone());

			if (triggersByFQN[tw.key] != null)
			{
				if (!replaceExisting)
				{
					throw new ObjectAlreadyExistsException(newTrigger);
				}

                // don't delete orphaned job, this trigger has the job anyways
				RemoveTrigger(ctxt, newTrigger.Name, newTrigger.Group, false);
			}

			if (RetrieveJob(ctxt, newTrigger.JobName, newTrigger.JobGroup) == null)
			{
				throw new JobPersistenceException("The job (" + newTrigger.FullJobName +
				                                  ") referenced by the trigger does not exist.");
			}

			lock (triggerLock)
			{
				// add to triggers array
				triggers.Add(tw);

				// add to triggers by group
				IDictionary grpMap = (Hashtable) triggersByGroup[newTrigger.Group];
				if (grpMap == null)
				{
					grpMap = new Hashtable(100);
					triggersByGroup[newTrigger.Group] = grpMap;
				}
				grpMap[newTrigger.Name] = tw;
				// add to triggers by FQN map
				triggersByFQN[tw.key] = tw;

                if (pausedTriggerGroups.Contains(newTrigger.Group) || pausedJobGroups.Contains(newTrigger.JobGroup))
                {
                    tw.state = InternalTriggerState.Paused;
                    if (blockedJobs.Contains(tw.jobKey))
                    {
                        tw.state = InternalTriggerState.PausedAndBlocked;
                    }
                }
                else if (blockedJobs.Contains(tw.jobKey))
                {
                    tw.state = InternalTriggerState.Blocked;
                }
                else
                {
                    timeTriggers.Add(tw);
                }
			}
		}

		/// <summary>
		/// Remove (delete) the <see cref="Trigger" /> with the
		/// given name.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="triggerName">The name of the <see cref="Trigger" /> to be removed.</param>
		/// <param name="groupName">The group name of the <see cref="Trigger" /> to be removed.</param>
		/// <returns>
		/// 	<see langword="true" /> if a <see cref="Trigger" /> with the given
		/// name and group was found and removed from the store.
		/// </returns>
		/// <param name="deleteOrphanedJob">Whether to delete orpahaned job details from scheduler if job becomes orphaned from removing the trigger.</param>
		public virtual bool RemoveTrigger(SchedulingContext ctxt, string triggerName, string groupName, bool deleteOrphanedJob)
		{
			string key = TriggerWrapper.GetTriggerNameKey(triggerName, groupName);
            log.Debug(string.Format(CultureInfo.InvariantCulture, "RemoveTrigger {0},{1}",triggerName,groupName));
/*
            //trying to find out if any concurrent thread
		    //may want to modify (maybe remove) this trigger
		    //if so, we shouldn't throw an exception when
		    //trigger is not found
		    //( the concurrent thread may want to acquire lock
            //  between Monitor.TryEnter and lock, but let's hope
            //  this is unlikely)
		    //Unfortunately, this can lead to not-throwing exceptions
		    //when there is no trigger to remove but 
		    //a different trigger is being deleted at the same time
		    bool locked = !Monitor.TryEnter(triggerLock);
		    if (!locked)
                Monitor.Exit(triggerLock);
*/          
		    bool found;
			lock (triggerLock)
			{
				// remove from triggers by FQN map
				object tempObject;
				tempObject = triggersByFQN[key];
				triggersByFQN.Remove(key);
                found = (tempObject == null) ? false : true;
                if (found)
                {
                    TriggerWrapper tw = null;
                    // remove from triggers by group
                    IDictionary grpMap = (Hashtable)triggersByGroup[groupName];
                    if (grpMap != null)
                    {
                        grpMap.Remove(triggerName);
                        if (grpMap.Count == 0)
                        {
                            triggersByGroup.Remove(groupName);
                        }
                    }
                    // remove from triggers array
                    for (int i = 0; i < triggers.Count; ++i)
                    {
                        tw = (TriggerWrapper)triggers[i];
                        if (key.Equals(tw.key))
                        {
                            triggers.RemoveAt(i);
                            break;
                        }
                    }
                    timeTriggers.Remove(tw);

                    JobWrapper jw = (JobWrapper)jobsByFQN[JobWrapper.GetJobNameKey(tw.trigger.JobName, tw.trigger.JobGroup)];
                    Trigger[] trigs = GetTriggersForJob(ctxt, tw.trigger.JobName, tw.trigger.JobGroup);
                    if ((trigs == null || trigs.Length == 0) && !jw.jobDetail.Durable && deleteOrphanedJob)
                    {
                        RemoveJob(ctxt, tw.trigger.JobName, tw.trigger.JobGroup);
                    }
                }
/*                else
                {
                    if (!locked)
                            throw new Quartz.SchedulerException("trigger to delete not found");
                }
 */
 
			}

			return found;
		}


		/// <summary>
		/// Replaces the trigger.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="triggerName">Name of the trigger.</param>
		/// <param name="groupName">Name of the group.</param>
		/// <param name="newTrigger">The new trigger.</param>
		/// <returns></returns>
		public virtual bool ReplaceTrigger(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger)
		{
			string key = TriggerWrapper.GetTriggerNameKey(triggerName, groupName);

			bool found;

			lock (triggerLock)
			{
				// remove from triggers by FQN map
				object tempObject;
				tempObject = triggersByFQN[key];
				triggersByFQN.Remove(key);
				TriggerWrapper tw = (TriggerWrapper) tempObject;
				found = tw != null;

				if (found)
				{
					if (!tw.Trigger.JobName.Equals(newTrigger.JobName) || !tw.Trigger.JobGroup.Equals(newTrigger.JobGroup))
					{
						throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
					}

					tw = null;
					// remove from triggers by group
					IDictionary grpMap = (Hashtable) triggersByGroup[groupName];
					if (grpMap != null)
					{
						grpMap.Remove(triggerName);
						if (grpMap.Count == 0)
						{
							triggersByGroup.Remove(groupName);
						}
					}
					// remove from triggers array
					for ( int i = 0; i < triggers.Count; ++i)
					{
						tw = (TriggerWrapper) triggers[i];
						if (key.Equals(tw.key))
						{
							triggers.RemoveAt(i);
							break;
						}
					}
					timeTriggers.Remove(tw);

					try
					{
						StoreTrigger(ctxt, newTrigger, false);
					}
					catch (JobPersistenceException)
					{
						StoreTrigger(ctxt, tw.Trigger, false); // put previous trigger back...
						throw;
					}
				}
			}

			return found;
		}

		/// <summary>
		/// Retrieve the <see cref="JobDetail" /> for the given
		/// <see cref="IJob" />.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="jobName">The name of the <see cref="IJob" /> to be retrieved.</param>
		/// <param name="groupName">The group name of the <see cref="IJob" /> to be retrieved.</param>
		/// <returns>
		/// The desired <see cref="IJob" />, or null if there is no match.
		/// </returns>
		public virtual JobDetail RetrieveJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			JobWrapper jw = (JobWrapper) jobsByFQN[JobWrapper.GetJobNameKey(jobName, groupName)];
            return (jw != null) ? (JobDetail) jw.jobDetail.Clone() : null;
		}

		/// <summary>
		/// Retrieve the given <see cref="Trigger" />.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="triggerName">The name of the <see cref="Trigger" /> to be retrieved.</param>
		/// <param name="groupName">The group name of the <see cref="Trigger" /> to be retrieved.</param>
		/// <returns>
		/// The desired <see cref="Trigger" />, or null if there is no match.
		/// </returns>
		public virtual Trigger RetrieveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];
            return (tw != null) ? (Trigger)tw.Trigger.Clone() : null;
		}

		/// <summary>
		/// Get the current state of the identified <see cref="Trigger" />.
		/// </summary>
        /// <seealso cref="TriggerState.Normal" />
        /// <seealso cref="TriggerState.Paused" />
        /// <seealso cref="TriggerState.Complete" />
        /// <seealso cref="TriggerState.Error" />
        /// <seealso cref="TriggerState.Blocked" />
        /// <seealso cref="TriggerState.None"/>
		public virtual TriggerState GetTriggerState(SchedulingContext ctxt, string triggerName, string groupName)
		{
			TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];
			if (tw == null)
			{
                return TriggerState.None;
			}
            if (tw.state == InternalTriggerState.Complete)
			{
				return TriggerState.Complete;
			}
            if (tw.state == InternalTriggerState.Paused)
			{
				return TriggerState.Paused;
			}
            if (tw.state == InternalTriggerState.PausedAndBlocked)
			{
				return TriggerState.Paused;
			}
			if (tw.state == InternalTriggerState.Blocked)
			{
				return TriggerState.Blocked;
			}
			if (tw.state == InternalTriggerState.Error)
			{
				return TriggerState.Error;
			}
			return TriggerState.Normal;
		}

		/// <summary>
		/// Store the given <see cref="ICalendar" />.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="name">The name.</param>
		/// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
		/// <param name="replaceExisting">If <see langword="true" />, any <see cref="ICalendar" /> existing
		/// in the <see cref="IJobStore" /> with the same name and group
		/// should be over-written.</param>
		/// <param name="updateTriggers">If <see langword="true" />, any <see cref="Trigger" />s existing
		/// in the <see cref="IJobStore" /> that reference an existing
		/// Calendar with the same name with have their next fire time
		/// re-computed with the new <see cref="ICalendar" />.</param>
		public virtual void StoreCalendar(SchedulingContext ctxt, string name, ICalendar calendar, bool replaceExisting,
		                                  bool updateTriggers)
		{
			object obj = calendarsByName[name];

			if (obj != null && replaceExisting == false)
			{
				throw new ObjectAlreadyExistsException(string.Format(CultureInfo.InvariantCulture, "Calendar with name '{0}' already exists.", name));
			}
			else if (obj != null)
			{
				calendarsByName.Remove(name);
			}

			calendarsByName[name] = calendar;

			if (obj != null && updateTriggers)
			{
				lock (triggerLock)
				{
					ArrayList trigs = GetTriggerWrappersForCalendar(name);
					for (int i = 0; i < trigs.Count; ++i)
					{
						TriggerWrapper tw = (TriggerWrapper) trigs[i];
						Trigger trig = tw.Trigger;
						Boolean tempBoolean;
						tempBoolean = timeTriggers.Contains(tw);
						timeTriggers.Remove(tw);
						bool removed = tempBoolean;

						trig.UpdateWithNewCalendar(calendar, MisfireThreshold);

						if (removed)
						{
							timeTriggers.Add(tw);
						}
					}
				}
			}
		}

		/// <summary>
		/// Remove (delete) the <see cref="ICalendar" /> with the
		/// given name.
		/// <p>
		/// If removal of the <see cref="ICalendar" /> would result in
		/// <see cref="Trigger" />s pointing to non-existent calendars, then a
		/// <see cref="JobPersistenceException" /> will be thrown.</p>
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="calName">The name of the <see cref="ICalendar" /> to be removed.</param>
		/// <returns>
		/// 	<see langword="true" /> if a <see cref="ICalendar" /> with the given name
		/// was found and removed from the store.
		/// </returns>
		public virtual bool RemoveCalendar(SchedulingContext ctxt, string calName)
		{
			int numRefs = 0;

			lock (triggerLock)
			{
				foreach (TriggerWrapper triggerWrapper in triggers)
				{
                    Trigger trigg = triggerWrapper.Trigger;
					if (trigg.CalendarName != null && trigg.CalendarName.Equals(calName))
					{
						numRefs++;
					}
				}
			}

			if (numRefs > 0)
			{
				throw new JobPersistenceException("Calender cannot be removed if it referenced by a Trigger!");
			}

			object tempObject;
			tempObject = calendarsByName[calName];
			calendarsByName.Remove(calName);
			return (tempObject != null);
		}

		/// <summary>
		/// Retrieve the given <see cref="Trigger" />.
		/// </summary>
		/// <param name="ctxt">The scheduling context.</param>
		/// <param name="calName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
		/// <returns>
		/// The desired <see cref="ICalendar" />, or null if there is no match.
		/// </returns>
		public virtual ICalendar RetrieveCalendar(SchedulingContext ctxt, string calName)
		{
			return (ICalendar) calendarsByName[calName];
		}

		/// <summary>
		/// Get the number of <see cref="JobDetail" /> s that are
		/// stored in the <see cref="IJobStore" />.
		/// </summary>
		public virtual int GetNumberOfJobs(SchedulingContext ctxt)
		{
			return jobsByFQN.Count;
		}

		/// <summary>
		/// Get the number of <see cref="Trigger" /> s that are
		/// stored in the <see cref="IJobStore" />.
		/// </summary>
		public virtual int GetNumberOfTriggers(SchedulingContext ctxt)
		{
			return triggers.Count;
		}

		/// <summary>
		/// Get the number of <see cref="ICalendar" /> s that are
		/// stored in the <see cref="IJobStore" />.
		/// </summary>
		public virtual int GetNumberOfCalendars(SchedulingContext ctxt)
		{
			return calendarsByName.Count;
		}

		/// <summary>
		/// Get the names of all of the <see cref="IJob" /> s that
		/// have the given group name.
		/// </summary>
		public virtual string[] GetJobNames(SchedulingContext ctxt, string groupName)
		{
			string[] outList;
			IDictionary grpMap = (Hashtable) jobsByGroup[groupName];
			if (grpMap != null)
			{
				lock (triggerLock)
				{
					outList = new string[grpMap.Count];
					int outListPos = 0;
					IEnumerator keys = new HashSet(grpMap.Keys).GetEnumerator();
					while (keys.MoveNext())
					{
						string key = (string) keys.Current;
						JobWrapper jw = (JobWrapper) grpMap[key];
						if (jw != null)
						{
							outList[outListPos++] = jw.jobDetail.Name;
						}
					}
				}
			}
			else
			{
				outList = new string[0];
			}

			return outList;
		}

		/// <summary>
		/// Get the names of all of the <see cref="ICalendar" /> s
		/// in the <see cref="IJobStore" />.
		/// <p>
		/// If there are no ICalendars in the given group name, the result should be
		/// a zero-length array (not <see langword="null" />).
		/// </p>
		/// </summary>
		public virtual string[] GetCalendarNames(SchedulingContext ctxt)
		{
			ArrayList names = new ArrayList(calendarsByName.Keys);
			return (string[]) names.ToArray(typeof (string));
		}

		/// <summary>
		/// Get the names of all of the <see cref="Trigger" /> s
		/// that have the given group name.
		/// </summary>
		public virtual string[] GetTriggerNames(SchedulingContext ctxt, string groupName)
		{
			string[] outList;
			IDictionary grpMap = (Hashtable) triggersByGroup[groupName];
			if (grpMap != null)
			{
				lock (triggerLock)
				{
					outList = new string[grpMap.Count];
					int outListPos = 0;
					IEnumerator keys = new HashSet(grpMap.Keys).GetEnumerator();
					while (keys.MoveNext())
					{
						string key = (string) keys.Current;
						TriggerWrapper tw = (TriggerWrapper) grpMap[key];
						if (tw != null)
						{
							outList[outListPos++] = tw.trigger.Name;
						}
					}
				}
			}
			else
			{
				outList = new string[0];
			}

			return outList;
		}

		/// <summary>
		/// Get the names of all of the <see cref="IJob" />
		/// groups.
		/// </summary>
		public virtual string[] GetJobGroupNames(SchedulingContext ctxt)
		{
			string[] outList;

            lock (triggerLock)
			{
				outList = new string[jobsByGroup.Count];
				int outListPos = 0;
				IEnumerator keys = new HashSet(jobsByGroup.Keys).GetEnumerator();
				while (keys.MoveNext())
				{
					outList[outListPos++] = ((String) keys.Current);
				}
			}

			return outList;
		}

		/// <summary>
		/// Get the names of all of the <see cref="Trigger" /> groups.
		/// </summary>
		public virtual string[] GetTriggerGroupNames(SchedulingContext ctxt)
		{
			string[] outList;

			lock (triggerLock)
			{
				outList = new string[triggersByGroup.Count];
				int outListPos = 0;
				IEnumerator keys = new HashSet(triggersByGroup.Keys).GetEnumerator();
				while (keys.MoveNext())
				{
					outList[outListPos++] = ((string) keys.Current);
				}
			}

			return outList;
		}

		/// <summary>
		/// Get all of the Triggers that are associated to the given Job.
		/// <p>
		/// If there are no matches, a zero-length array should be returned.
		/// </p>
		/// </summary>
		public virtual Trigger[] GetTriggersForJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			ArrayList trigList = new ArrayList();

			string jobKey = JobWrapper.GetJobNameKey(jobName, groupName);
			lock (triggerLock)
			{
				for (int i = 0; i < triggers.Count; i++)
				{
					TriggerWrapper tw = (TriggerWrapper) triggers[i];
					if (tw.jobKey.Equals(jobKey))
					{
						trigList.Add(tw.trigger.Clone());
					}
				}
			}

			return (Trigger[]) trigList.ToArray(typeof (Trigger));
		}

		/// <summary>
		/// Gets the trigger wrappers for job.
		/// </summary>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="groupName">Name of the group.</param>
		/// <returns></returns>
		protected virtual ArrayList GetTriggerWrappersForJob(string jobName, string groupName)
		{
			ArrayList trigList = new ArrayList();

			string jobKey = JobWrapper.GetJobNameKey(jobName, groupName);
			lock (triggerLock)
			{
				for (int i = 0; i < triggers.Count; i++)
				{
					TriggerWrapper tw = (TriggerWrapper) triggers[i];
					if (tw.jobKey.Equals(jobKey))
					{
						trigList.Add(tw);
					}
				}
			}

			return trigList;
		}

		/// <summary>
		/// Gets the trigger wrappers for calendar.
		/// </summary>
		/// <param name="calName">Name of the cal.</param>
		/// <returns></returns>
		protected internal virtual ArrayList GetTriggerWrappersForCalendar(String calName)
		{
			ArrayList trigList = new ArrayList();

			lock (triggerLock)
			{
				for (int i = 0; i < triggers.Count; i++)
				{
					TriggerWrapper tw = (TriggerWrapper) triggers[i];
					string tcalName = tw.Trigger.CalendarName;
					if (tcalName != null && tcalName.Equals(calName))
					{
						trigList.Add(tw);
					}
				}
			}

			return trigList;
		}

		/// <summary> 
		/// Pause the <see cref="Trigger" /> with the given name.
		/// </summary>
		public virtual void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];

			// does the trigger exist?
			if (tw == null || tw.trigger == null)
			{
				return;
			}
			// if the trigger is "complete" pausing it does not make sense...
            if (tw.state == InternalTriggerState.Complete)
			{
				return;
			}

			lock (triggerLock)
			{
                if (tw.state == InternalTriggerState.Blocked)
				{
                    tw.state = InternalTriggerState.PausedAndBlocked;
				}
				else
				{
                    tw.state = InternalTriggerState.Paused;
				}
				timeTriggers.Remove(tw);
			}
		}

		/// <summary>
		/// Pause all of the <see cref="Trigger" />s in the given group.
		/// <p>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new triggers that are added to the group while the group is
		/// paused.
		/// </p>
		/// </summary>
		public virtual void PauseTriggerGroup(SchedulingContext ctxt, string groupName)
		{
            lock (triggerLock)
			{
				if (pausedTriggerGroups.Contains(groupName))
				{
					return;
				}
				pausedTriggerGroups.Add(groupName);
				string[] names = GetTriggerNames(ctxt, groupName);

				for (int i = 0; i < names.Length; i++)
				{
					PauseTrigger(ctxt, names[i], groupName);
				}
			}
		}

		/// <summary> 
		/// Pause the <see cref="JobDetail" /> with the given
		/// name - by pausing all of its current <see cref="Trigger" />s.
		/// </summary>
		public virtual void PauseJob(SchedulingContext ctxt, string jobName, string groupName)
		{
            lock (triggerLock)
			{
				Trigger[] t = GetTriggersForJob(ctxt, jobName, groupName);
				for (int j = 0; j < t.Length; j++)
				{
					PauseTrigger(ctxt, t[j].Name, t[j].Group);
				}
			}
		}

		/// <summary>
		/// Pause all of the <see cref="JobDetail" />s in the
		/// given group - by pausing all of their <see cref="Trigger" />s.
		/// <p>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new jobs that are added to the group while the group is
		/// paused.
		/// </p>
		/// </summary>
		public virtual void PauseJobGroup(SchedulingContext ctxt, string groupName)
		{
            lock (triggerLock)
			{
                if (!pausedJobGroups.Contains(groupName))
                {
                    pausedJobGroups.Add(groupName);
                }
				string[] jobNames = GetJobNames(ctxt, groupName);

				for (int i = 0; i < jobNames.Length; i++)
				{
					Trigger[] t = GetTriggersForJob(ctxt, jobNames[i], groupName);
					for (int j = 0; j < t.Length; j++)
					{
						PauseTrigger(ctxt, t[j].Name, t[j].Group);
					}
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) the <see cref="Trigger" /> with the given name.
		/// </summary>
		/// <remarks>
		/// If the <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
		/// </remarks>
		public virtual void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];

            // does the trigger exist?
            if (tw == null || tw.trigger == null)
            {
                return;
            }

			Trigger trig = tw.Trigger;


			// if the trigger is not paused resuming it does not make sense...
            if (tw.state != InternalTriggerState.Paused && 
                tw.state != InternalTriggerState.PausedAndBlocked)
			{
				return;
			}

			lock (triggerLock)
			{
				if (blockedJobs.Contains(JobWrapper.GetJobNameKey(trig.JobName, trig.JobGroup)))
				{
					tw.state = InternalTriggerState.Blocked;
				}
				else
				{
                    tw.state = InternalTriggerState.Waiting;
				}

				ApplyMisfire(tw);

                if (tw.state == InternalTriggerState.Waiting)
				{
					timeTriggers.Add(tw);
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) all of the <see cref="Trigger" />s in the
		/// given group.
		/// <p>
		/// If any <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		public virtual void ResumeTriggerGroup(SchedulingContext ctxt, string groupName)
		{
            lock (triggerLock)
			{
				string[] names = GetTriggerNames(ctxt, groupName);
                
				for (int i = 0; i < names.Length; i++)
				{
                    string key = TriggerWrapper.GetTriggerNameKey(names[i], groupName);
				    if ((triggersByFQN[key] != null))
				    {
                        string jobGroup = ((TriggerWrapper) triggersByFQN[key]).Trigger.JobGroup;
				        if (pausedJobGroups.Contains(jobGroup))
				        {
				            continue;
				        }
				    }
				    ResumeTrigger(ctxt, names[i], groupName);
				}
				pausedTriggerGroups.Remove(groupName);
			}
		}

		/// <summary>
		/// Resume (un-pause) the <see cref="JobDetail" /> with
		/// the given name.
		/// <p>
		/// If any of the <see cref="IJob" />'s<see cref="Trigger" /> s missed one
		/// or more fire-times, then the <see cref="Trigger" />'s misfire
		/// instruction will be applied.
		/// </p>
		/// </summary>
		public virtual void ResumeJob(SchedulingContext ctxt, string jobName, string groupName)
		{
            lock (triggerLock)
			{
				Trigger[] t = GetTriggersForJob(ctxt, jobName, groupName);
				for (int j = 0; j < t.Length; j++)
				{
					ResumeTrigger(ctxt, t[j].Name, t[j].Group);
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) all of the <see cref="JobDetail" />s
		/// in the given group.
		/// <p>
		/// If any of the <see cref="IJob" /> s had <see cref="Trigger" /> s that
		/// missed one or more fire-times, then the <see cref="Trigger" />'s
		/// misfire instruction will be applied.
		/// </p>
		/// </summary>
		public virtual void ResumeJobGroup(SchedulingContext ctxt, string groupName)
		{
            lock (triggerLock)
			{
			    if (pausedJobGroups.Contains(groupName))
			    {
			        pausedJobGroups.Remove(groupName);
			    }
				string[] jobNames = GetJobNames(ctxt, groupName);

				for (int i = 0; i < jobNames.Length; i++)
				{
					Trigger[] r = GetTriggersForJob(ctxt, jobNames[i], groupName);
					for (int j = 0; j < r.Length; j++)
					{
						ResumeTrigger(ctxt, r[j].Name, r[j].Group);
					}
				}
			}
		}

		/// <summary>
		/// Pause all triggers - equivalent of calling <see cref="PauseTriggerGroup(SchedulingContext, string)" />
		/// on every group.
		/// <p>
		/// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="ResumeAll(SchedulingContext)" /> 
		public virtual void PauseAll(SchedulingContext ctxt)
		{
            lock (triggerLock)
			{
				string[] names = GetTriggerGroupNames(ctxt);

				for (int i = 0; i < names.Length; i++)
				{
					PauseTriggerGroup(ctxt, names[i]);
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroup(SchedulingContext, string)" />
        /// on every trigger group and setting all job groups unpaused />.
		/// <p>
		/// If any <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="PauseAll(SchedulingContext)" />
		public virtual void ResumeAll(SchedulingContext ctxt)
		{
            lock (triggerLock)
			{
			    pausedJobGroups.Clear();
				string[] names = GetTriggerGroupNames(ctxt);

				for (int i = 0; i < names.Length; i++)
				{
					ResumeTriggerGroup(ctxt, names[i]);
				}
			}
		}

		/// <summary>
		/// Applies the misfire.
		/// </summary>
		/// <param name="tw">The trigger wrapper.</param>
		/// <returns></returns>
		protected internal virtual bool ApplyMisfire(TriggerWrapper tw)
		{
			DateTime misfireTime = DateTime.UtcNow;
			if (MisfireThreshold > TimeSpan.Zero)
			{
				misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
			}

            NullableDateTime tnft = tw.trigger.GetNextFireTimeUtc();
            if (!tnft.HasValue || tnft.Value > misfireTime)
			{
				return false;
			}

			ICalendar cal = null;
			if (tw.trigger.CalendarName != null)
			{
				cal = RetrieveCalendar(null, tw.trigger.CalendarName);
			}

			signaler.NotifyTriggerListenersMisfired(tw.trigger);

			tw.trigger.UpdateAfterMisfire(cal);

			if (!tw.trigger.GetNextFireTimeUtc().HasValue)
			{
                tw.state = InternalTriggerState.Complete;
                signaler.NotifySchedulerListenersFinalized(tw.trigger);
				lock (triggerLock)
				{
					timeTriggers.Remove(tw);
				}
			}
			else if (tnft.Equals(tw.trigger.GetNextFireTimeUtc()))
			{
				return false;
			}

			return true;
		}

		private static long ftrCtr = DateTime.UtcNow.Ticks;

		/// <summary>
		/// Get a handle to the next trigger to be fired, and mark it as 'reserved'
		/// by the calling scheduler.
		/// </summary>
		/// <seealso cref="Trigger" />
		public virtual Trigger AcquireNextTrigger(SchedulingContext ctxt, DateTime noLaterThan)
		{
			TriggerWrapper tw = null;

			lock (triggerLock)
			{
				while (tw == null)
				{
					if (timeTriggers.Count > 0)
					{
						tw = (TriggerWrapper) timeTriggers[0];
					}

					if (tw == null)
					{
						return null;
					}

					if (!tw.trigger.GetNextFireTimeUtc().HasValue)
					{
						timeTriggers.Remove(tw);
						tw = null;
						continue;
					}

					timeTriggers.Remove(tw);

					if (ApplyMisfire(tw))
					{
						if (tw.trigger.GetNextFireTimeUtc().HasValue)
						{
							timeTriggers.Add(tw);
						}
						tw = null;
						continue;
					}

					if (tw.trigger.GetNextFireTimeUtc().Value > noLaterThan)
					{
						timeTriggers.Add(tw);
						return null;
					}

                    tw.state = InternalTriggerState.Acquired;

					tw.trigger.FireInstanceId = FiredTriggerRecordId;
					Trigger trig = (Trigger) tw.trigger.Clone();
					return trig;
				}
			}

			return null;
		}

		/// <summary>
		/// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
		/// fire the given <see cref="Trigger" />, that it had previously acquired
		/// (reserved).
		/// </summary>
		public virtual void ReleaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger)
		{
			lock (triggerLock)
			{
				TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(trigger)];
                if (tw != null && tw.state == InternalTriggerState.Acquired)
				{
                    tw.state = InternalTriggerState.Waiting;
					timeTriggers.Add(tw);
				}
			}
		}

		/// <summary>
		/// Inform the <see cref="IJobStore" /> that the scheduler is now firing the
		/// given <see cref="Trigger" /> (executing its associated <see cref="IJob" />),
		/// that it had previously acquired (reserved).
		/// </summary>
		public virtual TriggerFiredBundle TriggerFired(SchedulingContext ctxt, Trigger trigger)
		{
			lock (triggerLock)
			{
				TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(trigger)];
				// was the trigger deleted since being acquired?
				if (tw == null || tw.trigger == null)
				{
					return null;
				}

                // was the trigger completed, paused, blocked, etc. since being acquired?
                if (tw.state != InternalTriggerState.Acquired)
                {
					return null;
				}

				ICalendar cal = null;
				if (tw.trigger.CalendarName != null)
				{
					cal = RetrieveCalendar(ctxt, tw.trigger.CalendarName);

                    if (cal == null)
                    {
                        return null;
                    }
				}

                NullableDateTime prevFireTime = trigger.GetPreviousFireTimeUtc();
                
                // in case trigger was replaced between acquiring and firering
                timeTriggers.Remove(tw);   

                // call triggered on our copy, and the scheduler's copy
				tw.trigger.Triggered(cal);
				trigger.Triggered(cal);
				
                //tw.state = TriggerWrapper.StateExecuting;
                tw.state = InternalTriggerState.Waiting;

				TriggerFiredBundle bndle =
					new TriggerFiredBundle(RetrieveJob(ctxt, trigger.JobName, trigger.JobGroup), trigger, cal, false, DateTime.UtcNow,
					                       trigger.GetPreviousFireTimeUtc(), prevFireTime, trigger.GetNextFireTimeUtc());

				JobDetail job = bndle.JobDetail;

				if (job.Stateful)
				{
					ArrayList trigs = GetTriggerWrappersForJob(job.Name, job.Group);
					IEnumerator itr = trigs.GetEnumerator();
					while (itr.MoveNext())
					{
						TriggerWrapper ttw = (TriggerWrapper) itr.Current;
                        if (ttw.state == InternalTriggerState.Waiting)
						{
                            ttw.state = InternalTriggerState.Blocked;
						}
                        if (ttw.state == InternalTriggerState.Paused)
						{
                            ttw.state = InternalTriggerState.PausedAndBlocked;
						}
						timeTriggers.Remove(ttw);
					}
					blockedJobs.Add(JobWrapper.GetJobNameKey(job));
				}
				else
				{
                    NullableDateTime d = tw.trigger.GetNextFireTimeUtc();
                    if (d.HasValue)
					{
						lock (triggerLock)
						{
							timeTriggers.Add(tw);
						}
					}
				}

				return bndle;
			}
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
			lock (triggerLock)
			{
				string jobKey = JobWrapper.GetJobNameKey(jobDetail.Name, jobDetail.Group);
				JobWrapper jw = (JobWrapper) jobsByFQN[jobKey];
				TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(trigger)];

				// It's possible that the job is null if:
				//   1- it was deleted during execution
				//   2- RAMJobStore is being used only for volatile jobs / triggers
				//      from the JDBC job store
				if (jw != null)
				{
					JobDetail jd = jw.jobDetail;

					if (jobDetail.Stateful)
					{
						JobDataMap newData = jobDetail.JobDataMap;
						if (newData != null)
						{
                            newData = (JobDataMap)newData.Clone();
                            newData.ClearDirtyFlag();
						}
						jd.JobDataMap = newData;
						blockedJobs.Remove(JobWrapper.GetJobNameKey(jd));
						ArrayList trigs = GetTriggerWrappersForJob(jd.Name, jd.Group);
						foreach (TriggerWrapper ttw in trigs)
						{
                            if (ttw.state == InternalTriggerState.Blocked)
							{
                                ttw.state = InternalTriggerState.Waiting;
								timeTriggers.Add(ttw);
							}
                            if (ttw.state == InternalTriggerState.PausedAndBlocked)
							{
                                ttw.state = InternalTriggerState.Paused;
							}
						}

                        signaler.SignalSchedulingChange(null);
					}
				}
				else
				{
					// even if it was deleted, there may be cleanup to do
					blockedJobs.Remove(JobWrapper.GetJobNameKey(jobDetail));
				}

				// check for trigger deleted during execution...
				if (tw != null)
				{
					if (triggerInstCode == SchedulerInstruction.DeleteTrigger)
					{
					    log.Debug("Deleting trigger");
                        NullableDateTime d = trigger.GetNextFireTimeUtc();
                        if (!d.HasValue)
						{
							// double check for possible reschedule within job 
							// execution, which would cancel the need to delete...
							d = tw.Trigger.GetNextFireTimeUtc();
							if (!d.HasValue)
							{
								RemoveTrigger(ctxt, trigger.Name, trigger.Group);
							}
						    else
							{
							    log.Debug("Deleting cancelled - trigger still active");
							}
						}
						else
						{
							RemoveTrigger(ctxt, trigger.Name, trigger.Group);
                            signaler.SignalSchedulingChange(null);
						}
					}
					else if (triggerInstCode == SchedulerInstruction.SetTriggerComplete)
					{
                        tw.state = InternalTriggerState.Complete;
						timeTriggers.Remove(tw);
                        signaler.SignalSchedulingChange(null);
					}
                    else if (triggerInstCode == SchedulerInstruction.SetTriggerError)
					{
						Log.Info(string.Format(CultureInfo.InvariantCulture, "Trigger {0} set to ERROR state.", trigger.FullName));
                        tw.state = InternalTriggerState.Error;
                        signaler.SignalSchedulingChange(null);
					}
                    else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersError)
					{
						Log.Info(string.Format(CultureInfo.InvariantCulture, "All triggers of Job {0} set to ERROR state.", trigger.FullJobName));
                        SetAllTriggersOfJobToState(trigger.JobName, trigger.JobGroup, InternalTriggerState.Error);
                        signaler.SignalSchedulingChange(null);
					}
					else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersComplete)
					{
						SetAllTriggersOfJobToState(trigger.JobName, trigger.JobGroup, InternalTriggerState.Complete);
                        signaler.SignalSchedulingChange(null);
					}
				}
			}
		}

		/// <summary>
		/// Sets the state of all triggers of job to specified state.
		/// </summary>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="state">The internal state to set.</param>
		protected internal virtual void SetAllTriggersOfJobToState(string jobName, string jobGroup, InternalTriggerState state)
		{
			ArrayList tws = GetTriggerWrappersForJob(jobName, jobGroup);
			foreach (TriggerWrapper tw in tws)
			{
				tw.state = state;
                if (state != InternalTriggerState.Waiting)
				{
					timeTriggers.Remove(tw);
				}
			}
		}

		/// <summary>
		/// Peeks the triggers.
		/// </summary>
		/// <returns></returns>
		protected internal virtual string PeekTriggers()
		{
			StringBuilder str = new StringBuilder();
			TriggerWrapper tw;

			lock (triggerLock)
			{
				IEnumerator itr = new HashSet(triggersByFQN.Keys).GetEnumerator();
				while (itr.MoveNext())
				{
					tw = (TriggerWrapper) triggersByFQN[itr.Current];
					str.Append(tw.trigger.Name);
					str.Append("/");
				}
			}
			str.Append(" | ");

			lock (triggerLock)
			{
				IEnumerator itr = timeTriggers.GetEnumerator();
				while (itr.MoveNext())
				{
					tw = (TriggerWrapper) itr.Current;
					str.Append(tw.trigger.Name);
					str.Append("->");
				}
			}

			return str.ToString();
		}

		/// <seealso cref="IJobStore.GetPausedTriggerGroups(SchedulingContext)" />
		public virtual ISet GetPausedTriggerGroups(SchedulingContext ctxt)
		{
			HashSet data = new HashSet();
			data.AddAll(pausedTriggerGroups);
			return data;
		}
	}

	/// <summary>
	/// Comparer for triggers.
	/// </summary>
	internal class TriggerComparator : IComparer
	{
		public virtual int Compare(object obj1, object obj2)
		{
			TriggerWrapper trig1 = (TriggerWrapper) obj1;
			TriggerWrapper trig2 = (TriggerWrapper) obj2;

            int comp = trig1.trigger.CompareTo(trig2.trigger);
            if (comp != 0)
            {
                return comp;
            }

            comp = trig2.trigger.Priority - trig1.trigger.Priority;
            if (comp != 0)
            {
                return comp;
            }

            return trig1.trigger.FullName.CompareTo(trig2.trigger.FullName);
		}


	    public override bool Equals(object obj)
	    {
	        return (obj is TriggerComparator);
	    }


	    public override int GetHashCode()
	    {
	        return base.GetHashCode();
	    }
	}

	internal class JobWrapper
	{
		public string key;

		public JobDetail jobDetail;

		internal JobWrapper(JobDetail jobDetail)
		{
			this.jobDetail = jobDetail;
			key = GetJobNameKey(jobDetail);
		}

		internal JobWrapper(JobDetail jobDetail, string key)
		{
			this.jobDetail = jobDetail;
			this.key = key;
		}

		internal static string GetJobNameKey(JobDetail jobDetail)
		{
			return jobDetail.Group + "_$x$x$_" + jobDetail.Name;
		}

		internal static string GetJobNameKey(string jobName, string groupName)
		{
			return groupName + "_$x$x$_" + jobName;
		}

		public override bool Equals(object obj)
		{
			if (obj is JobWrapper)
			{
				JobWrapper jw = (JobWrapper) obj;
				if (jw.key.Equals(key))
				{
					return true;
				}
			}

			return false;
		}

		public override int GetHashCode()
		{
			return key.GetHashCode();
		}
	}

    /// <summary>
    /// Possible internal trigger states 
    /// in RAMJobStore
    /// </summary>
    public enum InternalTriggerState
    {
        /// <summary>
        /// Waiting 
        /// </summary>
        Waiting,
        /// <summary>
        /// Acquired
        /// </summary>
        Acquired,
        /// <summary>
        /// Executing
        /// </summary>
        Executing,
        /// <summary>
        /// Complete
        /// </summary>
        Complete,
        /// <summary>
        /// Paused
        /// </summary>
        Paused,
        /// <summary>
        /// Blocked
        /// </summary>
        Blocked,
        /// <summary>
        /// Paused and Blocked
        /// </summary>
        PausedAndBlocked,
        /// <summary>
        /// Error
        /// </summary>
        Error
    }

    /// <summary>
    /// Helper wrapper class
    /// </summary>
	public class TriggerWrapper
	{
        /// <summary>
        /// Gets the trigger
        /// </summary>
        /// <value>The trigger</value>
		public virtual Trigger Trigger
		{
			get { return trigger; }
		}

		/// <summary>
		/// The key used
		/// </summary>
		public string key;

		/// <summary>
		/// Job's key
		/// </summary>
		public string jobKey;

		/// <summary>
		/// The trigger
		/// </summary>
		public Trigger trigger;

		/// <summary>
		/// Current state
		/// </summary>
        public InternalTriggerState state = InternalTriggerState.Waiting;

		
		
		
		internal TriggerWrapper(Trigger trigger)
		{
			this.trigger = trigger;
			key = GetTriggerNameKey(trigger);
			jobKey = JobWrapper.GetJobNameKey(trigger.JobName, trigger.JobGroup);
		}

		internal TriggerWrapper(Trigger trigger, string key)
		{
			this.trigger = trigger;
			this.key = key;
			jobKey = JobWrapper.GetJobNameKey(trigger.JobName, trigger.JobGroup);
		}

		internal static string GetTriggerNameKey(Trigger trigger)
		{
			return trigger.Group + "_$x$x$_" + trigger.Name;
		}

		internal static string GetTriggerNameKey(string triggerName, string groupName)
		{
			return groupName + "_$x$x$_" + triggerName;
		}

        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>.
        /// </summary>
        /// <param name="obj">The <see cref="T:System.Object"></see> to compare with the current <see cref="T:System.Object"></see>.</param>
        /// <returns>
        /// true if the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>; otherwise, false.
        /// </returns>
		public override bool Equals(object obj)
		{
			if (obj is TriggerWrapper)
			{
				TriggerWrapper tw = (TriggerWrapper) obj;
				if (tw.key.Equals(key))
				{
					return true;
				}
			}

			return false;
		}

        /// <summary>
        /// Serves as a hash function for a particular type. <see cref="M:System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"></see>.
        /// </returns>
		public override int GetHashCode()
		{
			return key.GetHashCode();
		}
	}
}
