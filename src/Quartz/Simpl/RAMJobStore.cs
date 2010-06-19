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
using System.Globalization;
using System.Text;
using System.Threading;

using Common.Logging;

using Quartz.Collection;
using Quartz.Core;
using Quartz.Spi;

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
		private readonly IDictionary<string, JobWrapper> jobsByFQN = new Dictionary<string, JobWrapper>(1000);
		private readonly IDictionary<string, TriggerWrapper> triggersByFQN = new Dictionary<string, TriggerWrapper>(1000);
		private readonly IDictionary<string, IDictionary<string, JobWrapper>> jobsByGroup = new Dictionary<string, IDictionary<string, JobWrapper>>(25);
		private readonly IDictionary<string, IDictionary<string, TriggerWrapper>> triggersByGroup = new Dictionary<string, IDictionary<string, TriggerWrapper>>(25);
		private readonly TreeSet<TriggerWrapper> timeTriggers = new TreeSet<TriggerWrapper>(new TriggerComparator());
		private readonly IDictionary<string, ICalendar> calendarsByName = new Dictionary<string, ICalendar>(5);
		private readonly List<TriggerWrapper> triggers = new List<TriggerWrapper>(1000);
		private readonly object lockObject = new object();
        private readonly Collection.HashSet<string> pausedTriggerGroups = new Collection.HashSet<string>();
        private readonly Collection.HashSet<string> pausedJobGroups = new Collection.HashSet<string>();
        private readonly Collection.HashSet<string> blockedJobs = new Collection.HashSet<string>();
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

        private static long ftrCtr = SystemTime.UtcNow().Ticks;

        /// <summary>
	    /// Gets the fired trigger record id.
	    /// </summary>
	    /// <returns>The fired trigger record id.</returns>
	    protected virtual string GetFiredTriggerRecordId()
	    {
	        long value = Interlocked.Increment(ref ftrCtr);
	        return Convert.ToString(value, CultureInfo.InvariantCulture);
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


            lock (lockObject) {
                
                if (jobsByFQN.ContainsKey(jw.key)) {
                    if (!replaceExisting) {
                        throw new ObjectAlreadyExistsException(newJob);
                    }
                    repl = true;
                }

                if (!repl)
				{
					// get job group
					IDictionary<string, JobWrapper> grpMap;
					if (!jobsByGroup.TryGetValue(newJob.Group, out grpMap))
					{
						grpMap = new Dictionary<string, JobWrapper>(100);
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
					JobWrapper orig = jobsByFQN[jw.key];
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

            lock (lockObject)
			{
                IList<Trigger> triggersForJob = GetTriggersForJob(ctxt, jobName, groupName);
                foreach (Trigger trigger in triggersForJob)
                {
                    RemoveTrigger(ctxt, trigger.Name, trigger.Group);
                    found = true;
                }
                
                JobWrapper tempObject;
				if (jobsByFQN.TryGetValue(key, out tempObject))
				{
				    jobsByFQN.Remove(key);
				}
				found = (tempObject != null) | found;
				if (found)
				{
				    IDictionary<string, JobWrapper> grpMap;
				    jobsByGroup.TryGetValue(groupName, out grpMap);
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

            lock (lockObject)
            {

	            TriggerWrapper wrapper;
                if (triggersByFQN.TryGetValue(tw.key, out wrapper))
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

				// add to triggers array
				triggers.Add(tw);

				// add to triggers by group
				IDictionary<string, TriggerWrapper> grpMap;
			    triggersByGroup.TryGetValue(newTrigger.Group, out grpMap);

				if (grpMap == null)
				{
					grpMap = new Dictionary<string, TriggerWrapper>(100);
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
		    bool locked = !Monitor.TryEnter(lockObject);
		    if (!locked)
                Monitor.Exit(lockObject);
*/          
		    bool found;
			lock (lockObject)
			{
				// remove from triggers by FQN map
				TriggerWrapper tempObject;
				if (triggersByFQN.TryGetValue(key, out tempObject))
				{
				    triggersByFQN.Remove(key);
				}
                found = (tempObject == null) ? false : true;
                if (found)
                {
                    TriggerWrapper tw = null;
                    // remove from triggers by group
                    IDictionary<string, TriggerWrapper> grpMap = triggersByGroup[groupName];
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
                        tw = triggers[i];
                        if (key.Equals(tw.key))
                        {
                            triggers.RemoveAt(i);
                            break;
                        }
                    }
                    timeTriggers.Remove(tw);

                    JobWrapper jw = jobsByFQN[JobWrapper.GetJobNameKey(tw.trigger.JobName, tw.trigger.JobGroup)];
                    IList<Trigger> triggersForJob = GetTriggersForJob(ctxt, tw.trigger.JobName, tw.trigger.JobGroup);
                    if ((triggersForJob == null || triggersForJob.Count == 0) && !jw.jobDetail.Durable && deleteOrphanedJob)
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

			lock (lockObject)
			{
				// remove from triggers by FQN map
                TriggerWrapper tw;
                if (triggersByFQN.TryGetValue(key, out tw))
				{
				    triggersByFQN.Remove(key);
				}
				found = tw != null;

				if (found)
				{
					if (!tw.trigger.JobName.Equals(newTrigger.JobName) || !tw.trigger.JobGroup.Equals(newTrigger.JobGroup))
					{
						throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
					}

					tw = null;
					// remove from triggers by group
					IDictionary<string, TriggerWrapper> grpMap;
				    triggersByGroup.TryGetValue(groupName, out grpMap);

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
						tw = triggers[i];
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
						StoreTrigger(ctxt, tw.trigger, false); // put previous trigger back...
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
            lock (lockObject)
            {
                JobWrapper jw;
			    jobsByFQN.TryGetValue(JobWrapper.GetJobNameKey(jobName, groupName), out jw);
                return (jw != null) ? (JobDetail) jw.jobDetail.Clone() : null;
            }
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
            lock (lockObject)
            {
                TriggerWrapper tw;
                triggersByFQN.TryGetValue(TriggerWrapper.GetTriggerNameKey(triggerName, groupName), out tw);
                return (tw != null) ? (Trigger)tw.trigger.Clone() : null;
            }
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
            lock (lockObject)
            {
                TriggerWrapper tw;
                triggersByFQN.TryGetValue(TriggerWrapper.GetTriggerNameKey(triggerName, groupName), out tw);

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
            calendar = (ICalendar) calendar.Clone();

            lock (lockObject)
            {
                ICalendar obj;
		        calendarsByName.TryGetValue(name, out obj);

			    if (obj != null && replaceExisting == false)
			    {
				    throw new ObjectAlreadyExistsException(string.Format(CultureInfo.InvariantCulture, "Calendar with name '{0}' already exists.", name));
			    }
		        if (obj != null)
		        {
		            calendarsByName.Remove(name);
		        }

		        calendarsByName[name] = calendar;

			    if (obj != null && updateTriggers)
			    {
					List<TriggerWrapper> trigs = GetTriggerWrappersForCalendar(name);
					for (int i = 0; i < trigs.Count; ++i)
					{
						TriggerWrapper tw = trigs[i];
						Trigger trig = tw.trigger;
                        bool removed = timeTriggers.Remove(tw);

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

			lock (lockObject)
			{
				foreach (TriggerWrapper triggerWrapper in triggers)
				{
                    Trigger trigg = triggerWrapper.trigger;
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

			return calendarsByName.Remove(calName);
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
            lock (lockObject)
            {
                ICalendar calendar;
                calendarsByName.TryGetValue(calName, out calendar);
                if (calendar != null)
                {
                    return (ICalendar) calendar.Clone();
                }
                return null;
            }
		}

	    /// <summary>
		/// Get the number of <see cref="JobDetail" /> s that are
		/// stored in the <see cref="IJobStore" />.
		/// </summary>
		public virtual int GetNumberOfJobs(SchedulingContext ctxt)
		{
            lock (lockObject)
            {
                return jobsByFQN.Count;
            }
		}

		/// <summary>
		/// Get the number of <see cref="Trigger" /> s that are
		/// stored in the <see cref="IJobStore" />.
		/// </summary>
		public virtual int GetNumberOfTriggers(SchedulingContext ctxt)
		{
            lock (lockObject)
            {
                return triggers.Count;
            }
		}

		/// <summary>
		/// Get the number of <see cref="ICalendar" /> s that are
		/// stored in the <see cref="IJobStore" />.
		/// </summary>
		public virtual int GetNumberOfCalendars(SchedulingContext ctxt)
		{
            lock (lockObject)
            {
                return calendarsByName.Count;
            }
		}

		/// <summary>
		/// Get the names of all of the <see cref="IJob" /> s that
		/// have the given group name.
		/// </summary>
		public virtual IList<string> GetJobNames(SchedulingContext ctxt, string groupName)
		{
			string[] outList;
			lock (lockObject)
			{
                IDictionary<string, JobWrapper> grpMap = jobsByGroup[groupName];
			    if (grpMap != null)
			    {
				    outList = new string[grpMap.Count];
					int outListPos = 0;
				    foreach (KeyValuePair<string, JobWrapper> pair in grpMap)
				    {
						if (pair.Value != null)
						{
							outList[outListPos++] = pair.Value.jobDetail.Name;
						}
					}
				}
                else
                {
                    outList = new string[0];
                }
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
		public virtual IList<string> GetCalendarNames(SchedulingContext ctxt)
		{
            lock (lockObject)
            {
                return new List<string>(calendarsByName.Keys).ToArray();
            }
		}

		/// <summary>
		/// Get the names of all of the <see cref="Trigger" /> s
		/// that have the given group name.
		/// </summary>
		public virtual IList<string> GetTriggerNames(SchedulingContext ctxt, string groupName)
		{
			string[] outList;
            lock (lockObject)
	        {
                IDictionary<string, TriggerWrapper> grpMap = triggersByGroup[groupName];
			    if (grpMap != null)
			    {

					    outList = new string[grpMap.Count];
					    int outListPos = 0;
				        foreach (KeyValuePair<string, TriggerWrapper> pair in grpMap)
				        {
						    if (pair.Value != null)
						    {
							    outList[outListPos++] = pair.Value.trigger.Name;
						    }
					    }
				    }
			    else
			    {
				    outList = new string[0];
			    }
            }

			return outList;
		}

		/// <summary>
		/// Get the names of all of the <see cref="IJob" />
		/// groups.
		/// </summary>
		public virtual IList<string> GetJobGroupNames(SchedulingContext ctxt)
		{
            lock (lockObject)
			{
			    return  new List<string>(jobsByGroup.Keys).ToArray();
			}
		}

		/// <summary>
		/// Get the names of all of the <see cref="Trigger" /> groups.
		/// </summary>
		public virtual IList<string> GetTriggerGroupNames(SchedulingContext ctxt)
		{
            lock (lockObject)
            {
                return new List<string>(triggersByGroup.Keys).ToArray();
            }
		}

		/// <summary>
		/// Get all of the Triggers that are associated to the given Job.
		/// <p>
		/// If there are no matches, a zero-length array should be returned.
		/// </p>
		/// </summary>
		public virtual IList<Trigger> GetTriggersForJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			var trigList = new List<Trigger>();

			string jobKey = JobWrapper.GetJobNameKey(jobName, groupName);
			lock (lockObject)
			{
				for (int i = 0; i < triggers.Count; i++)
				{
					TriggerWrapper tw = triggers[i];
					if (tw.jobKey.Equals(jobKey))
					{
						trigList.Add((Trigger) tw.trigger.Clone());
					}
				}
			}

			return trigList.ToArray();
		}

		/// <summary>
		/// Gets the trigger wrappers for job.
		/// </summary>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="groupName">Name of the group.</param>
		/// <returns></returns>
		protected virtual List<TriggerWrapper> GetTriggerWrappersForJob(string jobName, string groupName)
		{
			var trigList = new List<TriggerWrapper>();

			string jobKey = JobWrapper.GetJobNameKey(jobName, groupName);
			lock (lockObject)
			{
				for (int i = 0; i < triggers.Count; i++)
				{
					TriggerWrapper tw = triggers[i];
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
		protected virtual List<TriggerWrapper> GetTriggerWrappersForCalendar(string calName)
		{
			var trigList = new List<TriggerWrapper>();

			lock (lockObject)
			{
				for (int i = 0; i < triggers.Count; i++)
				{
					TriggerWrapper tw = triggers[i];
					string tcalName = tw.trigger.CalendarName;
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
            lock (lockObject)
            {
                TriggerWrapper tw = triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];

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
            lock (lockObject)
			{
				if (pausedTriggerGroups.Contains(groupName))
				{
					return;
				}
				pausedTriggerGroups.Add(groupName);
				IList<string> triggerNames = GetTriggerNames(ctxt, groupName);

				foreach (string triggerName in triggerNames)
				{
				    PauseTrigger(ctxt, triggerName, groupName);
				}
			}
		}

		/// <summary> 
		/// Pause the <see cref="JobDetail" /> with the given
		/// name - by pausing all of its current <see cref="Trigger" />s.
		/// </summary>
		public virtual void PauseJob(SchedulingContext ctxt, string jobName, string groupName)
		{
            lock (lockObject)
            {
                IList<Trigger> triggersForJob = GetTriggersForJob(ctxt, jobName, groupName);
                foreach (Trigger trigger in triggersForJob)
                {
                    PauseTrigger(ctxt, trigger.Name, trigger.Group);
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
            lock (lockObject)
			{
                if (!pausedJobGroups.Contains(groupName))
                {
                    pausedJobGroups.Add(groupName);
                }
				IList<string> jobNames = GetJobNames(ctxt, groupName);

				foreach (string jobName in jobNames)
				{
				    IList<Trigger> triggersForJob = GetTriggersForJob(ctxt, jobName, groupName);
				    foreach (Trigger trigger in triggersForJob)
				    {
				        PauseTrigger(ctxt, trigger.Name, trigger.Group);
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
            lock (lockObject)
            {
                TriggerWrapper tw = triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];

                // does the trigger exist?
                if (tw == null || tw.trigger == null)
                {
                    return;
                }

			    Trigger trig = tw.trigger;


			    // if the trigger is not paused resuming it does not make sense...
                if (tw.state != InternalTriggerState.Paused && 
                    tw.state != InternalTriggerState.PausedAndBlocked)
			    {
				    return;
			    }

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
            lock (lockObject)
			{
				IList<string> triggerNames = GetTriggerNames(ctxt, groupName);
                
				foreach (string triggerName in triggerNames)
				{
				    string key = TriggerWrapper.GetTriggerNameKey(triggerName, groupName);
				    if ((triggersByFQN[key] != null))
				    {
				        string jobGroup = triggersByFQN[key].trigger.JobGroup;
				        if (pausedJobGroups.Contains(jobGroup))
				        {
				            continue;
				        }
				    }
				    ResumeTrigger(ctxt, triggerName, groupName);
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
            lock (lockObject)
            {
                IList<Trigger> triggersForJob = GetTriggersForJob(ctxt, jobName, groupName);
                foreach (Trigger trigger in triggersForJob)
                {
                    ResumeTrigger(ctxt, trigger.Name, trigger.Group);
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
            lock (lockObject)
			{
			    if (pausedJobGroups.Contains(groupName))
			    {
			        pausedJobGroups.Remove(groupName);
			    }
				IList<string> jobNames = GetJobNames(ctxt, groupName);

				foreach (string jobName in jobNames)
				{
				    IList<Trigger> triggersForJob = GetTriggersForJob(ctxt, jobName, groupName);
				    foreach (Trigger trigger in triggersForJob)
				    {
				        ResumeTrigger(ctxt, trigger.Name, trigger.Group);
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
            lock (lockObject)
            {
                IList<string> triggerGroupNames = GetTriggerGroupNames(ctxt);

                foreach (string groupName in triggerGroupNames)
                {
                    PauseTriggerGroup(ctxt, groupName);
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
            lock (lockObject)
			{
			    pausedJobGroups.Clear();
				IList<string> triggerGroupNames = GetTriggerGroupNames(ctxt);

				foreach (string groupName in triggerGroupNames)
				{
				    ResumeTriggerGroup(ctxt, groupName);
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
			DateTime misfireTime = SystemTime.UtcNow();
			if (MisfireThreshold > TimeSpan.Zero)
			{
				misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
			}

            DateTime? tnft = tw.trigger.GetNextFireTimeUtc();
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
				lock (lockObject)
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

		/// <summary>
		/// Get a handle to the next trigger to be fired, and mark it as 'reserved'
		/// by the calling scheduler.
		/// </summary>
		/// <seealso cref="Trigger" />
        public virtual IList<Trigger> AcquireNextTriggers(SchedulingContext ctxt, DateTime noLaterThan, int maxCount, TimeSpan timeWindow)
		{
			lock (lockObject)
			{
                List<Trigger> result = new List<Trigger>();

                while (true)
                {
                    TriggerWrapper tw;

                    tw = timeTriggers.First();
                    if (tw == null) return result;
                    if (!timeTriggers.Remove(tw))
                    {
                        return result;
                    }

                    if (tw.trigger.GetNextFireTimeUtc() == null)
                    {
                        continue;
                    }

                    if (ApplyMisfire(tw))
                    {
                        if (tw.trigger.GetNextFireTimeUtc() != null)
                        {
                            timeTriggers.Add(tw);
                        }
                        continue;
                    }

                    if (tw.trigger.GetNextFireTimeUtc() > noLaterThan + timeWindow)
                    {
                        timeTriggers.Add(tw);
                        return result;
                    }

                    tw.state = InternalTriggerState.Acquired;

                    tw.trigger.FireInstanceId = GetFiredTriggerRecordId();
                    Trigger trig = (Trigger)tw.trigger.Clone();
                    result.Add(trig);

                    if (result.Count == maxCount)
                    {
                        return result;
                    }
                }
            }
		}

		/// <summary>
		/// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
		/// fire the given <see cref="Trigger" />, that it had previously acquired
		/// (reserved).
		/// </summary>
		public virtual void ReleaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger)
		{
			lock (lockObject)
			{
				TriggerWrapper tw = triggersByFQN[TriggerWrapper.GetTriggerNameKey(trigger)];
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
		public virtual IList<TriggerFiredResult> TriggersFired(SchedulingContext ctxt, IList<Trigger> triggers)
		{
		    lock (lockObject)
		    {
		        List<TriggerFiredResult> results = new List<TriggerFiredResult>();

		        foreach (Trigger trigger in triggers)
		        {
		            TriggerWrapper tw = triggersByFQN[TriggerWrapper.GetTriggerNameKey(trigger)];
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
		                    return null;
		            }
		            DateTime? prevFireTime = trigger.GetPreviousFireTimeUtc();
		            // in case trigger was replaced between acquiring and firering
		            timeTriggers.Remove(tw);
		            // call triggered on our copy, and the scheduler's copy
		            tw.trigger.Triggered(cal);
		            trigger.Triggered(cal);
		            //tw.state = TriggerWrapper.STATE_EXECUTING;
                    tw.state = InternalTriggerState.Waiting;

		            TriggerFiredBundle bndle = new TriggerFiredBundle(RetrieveJob(ctxt,
		                                                                          trigger.JobName, trigger.JobGroup), trigger,
		                                                              cal,
		                                                              false, SystemTime.UtcNow(),
		                                                              trigger.GetPreviousFireTimeUtc(), prevFireTime,
		                                                              trigger.GetNextFireTimeUtc());

		            JobDetail job = bndle.JobDetail;

		            if (job.Stateful)
		            {
		                List<TriggerWrapper> trigs = GetTriggerWrappersForJob(job.Name, job.Group);
		                foreach (TriggerWrapper ttw in trigs)
		                {
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
		            else if (tw.trigger.GetNextFireTimeUtc() != null)
		            {
		                lock (lockObject)
		                {
		                    timeTriggers.Add(tw);
		                }
		            }

		            results.Add(new TriggerFiredResult(bndle));
		        }
		        return results;
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
			lock (lockObject)
			{
				string jobKey = JobWrapper.GetJobNameKey(jobDetail.Name, jobDetail.Group);
				JobWrapper jw = jobsByFQN[jobKey];
				TriggerWrapper tw = triggersByFQN[TriggerWrapper.GetTriggerNameKey(trigger)];

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
						List<TriggerWrapper> trigs = GetTriggerWrappersForJob(jd.Name, jd.Group);
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
                        DateTime? d = trigger.GetNextFireTimeUtc();
                        if (!d.HasValue)
						{
							// double check for possible reschedule within job 
							// execution, which would cancel the need to delete...
							d = tw.trigger.GetNextFireTimeUtc();
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
	    /// Inform the <see cref="IJobStore" /> of the Scheduler instance's Id, 
	    /// prior to initialize being invoked.
	    /// </summary>
	    public virtual string InstanceId
	    {
	        set {  }
	    }

	    /// <summary>
	    /// Inform the <see cref="IJobStore" /> of the Scheduler instance's name, 
	    /// prior to initialize being invoked.
	    /// </summary>
        public virtual string InstanceName
	    {
	        set {  }
	    }

        public long EstimatedTimeToReleaseAndAcquireTrigger
        {
            get { return 5; }
        }

        public bool Clustered
        {
            get {return false; }
        }

	    /// <summary>
		/// Sets the state of all triggers of job to specified state.
		/// </summary>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="state">The internal state to set.</param>
		protected internal virtual void SetAllTriggersOfJobToState(string jobName, string jobGroup, InternalTriggerState state)
		{
			List<TriggerWrapper> tws = GetTriggerWrappersForJob(jobName, jobGroup);
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

			lock (lockObject)
			{
                foreach (string s in triggersByFQN.Keys)
				{
					tw = triggersByFQN[s];
					str.Append(tw.trigger.Name);
					str.Append("/");
				}
			}
			str.Append(" | ");

			lock (lockObject)
			{
				IEnumerator<TriggerWrapper> itr = timeTriggers.GetEnumerator();
				while (itr.MoveNext())
				{
					tw = itr.Current;
					str.Append(tw.trigger.Name);
					str.Append("->");
				}
			}

			return str.ToString();
		}

		/// <seealso cref="IJobStore.GetPausedTriggerGroups(SchedulingContext)" />
        public virtual Collection.ISet<string> GetPausedTriggerGroups(SchedulingContext ctxt)
		{
            Collection.HashSet<string> data = new Collection.HashSet<string>(pausedTriggerGroups);
			return data;
		}
	}

	/// <summary>
	/// Comparer for triggers.
	/// </summary>
	internal class TriggerComparator : IComparer<TriggerWrapper>
	{
		public virtual int Compare(TriggerWrapper trig1, TriggerWrapper trig2)
		{
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
	public class TriggerWrapper : IEquatable<TriggerWrapper>
	{
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

        public bool Equals(TriggerWrapper other)
        {
            return other != null && other.key.Equals(key);
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
    	    return Equals(obj as TriggerWrapper);
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
