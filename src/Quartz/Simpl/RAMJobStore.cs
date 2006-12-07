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
using System.Text;

using Common.Logging;

using Nullables;

using Quartz.Collection;
using Quartz.Core;
using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary>
	/// This class implements a <code>IJobStore</code> that
	/// utilizes RAM as its storage device.
	/// <p>
	/// As you should know, the ramification of this is that access is extrememly
	/// fast, but the data is completely volatile - therefore this <code>JobStore</code>
	/// should not be used if true persistence between program shutdowns is
	/// required.
	/// </p>
	/// </summary>
	/// <author>James House</author>
	/// <author>Sharada Jambula</author>
	/// <author>Marko Lahma (.NET)</author>
	public class RAMJobStore : IJobStore
	{
		private static ILog Log = LogManager.GetLogger(typeof (RAMJobStore));

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
					throw new ArgumentException("Misfirethreashold must be larger than 0");
				}
				misfireThreshold = value;
			}
		}

		protected internal virtual string FiredTriggerRecordId
		{
			get
			{
				lock (this)
				{
					return Convert.ToString(ftrCtr++);
				}
			}
		}

		protected internal Hashtable jobsByFQN = new Hashtable(1000);
		protected internal Hashtable triggersByFQN = new Hashtable(1000);
		protected internal Hashtable jobsByGroup = new Hashtable(25);
		protected internal Hashtable triggersByGroup = new Hashtable(25);
		protected internal TreeSet timeTriggers = new TreeSet(new TriggerComparator());
		protected internal Hashtable calendarsByName = new Hashtable(25);
		protected internal ArrayList triggers = new ArrayList(1000);
		protected internal object jobLock = new object();
		protected internal object triggerLock = new object();
		protected internal HashSet pausedTriggerGroups = new HashSet();
		protected internal HashSet blockedJobs = new HashSet();
		protected internal long misfireThreshold = 5000L;
		protected internal ISchedulerSignaler signaler;

		/// <summary>
		/// Called by the QuartzScheduler before the <code>JobStore</code> is
		/// used, in order to give the it a chance to initialize.
		/// </summary>
		public virtual void Initialize(IClassLoadHelper loadHelper, ISchedulerSignaler s)
		{
			signaler = s;
			Log.Info("RAMJobStore initialized.");
		}

		public virtual void SchedulerStarted()
		{
			// nothing to do
		}

		/// <summary>
		/// Called by the QuartzScheduler to inform the <code>JobStore</code> that
		/// it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </summary>
		public virtual void Shutdown()
		{
		}

		public virtual bool SupportsPersistence()
		{
			return false;
		}

		/// <summary> 
		/// Store the given <code>{@link org.quartz.JobDetail}</code> and <code>{@link org.quartz.Trigger}</code>.
		/// </summary>
		/// <param name="newJob">The <code>JobDetail</code> to be stored.</param>
		/// <param name="newTrigger">The <code>Trigger</code> to be stored.</param>
		public virtual void StoreJobAndTrigger(SchedulingContext ctxt, JobDetail newJob, Trigger newTrigger)
		{
			StoreJob(ctxt, newJob, false);
			StoreTrigger(ctxt, newTrigger, false);
		}

		/// <summary> 
		/// Store the given <code>IJob</code>.
		/// </summary>
		/// <param name="newJob">The <code>Job</code> to be stored.</param>
		/// <param name="replaceExisting">
		/// If <code>true</code>, any <code>Job</code> existing in the
		/// <code>JobStore</code> with the same name and group should be
		/// over-written.
		/// </param>
		public virtual void StoreJob(SchedulingContext ctxt, JobDetail newJob, bool replaceExisting)
		{
			JobWrapper jw = new JobWrapper(newJob);

			bool repl = false;

			if (jobsByFQN[jw.key] != null)
			{
				if (!replaceExisting)
				{
					throw new ObjectAlreadyExistsException(newJob);
				}
				repl = true;
			}

			lock (jobLock)
			{
				if (!repl)
				{
					// get job group
					Hashtable grpMap = (Hashtable) jobsByGroup[newJob.Group];
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
					orig.jobDetail = newJob;
				}
			}
		}

		/// <summary>
		/// Remove (delete) the <code>IJob</code> with the given
		/// name, and any <code>Trigger</code> s that reference
		/// it.
		/// </summary>
		/// <param name="jobName">The name of the <code>Job</code> to be removed.</param>
		/// <param name="groupName">The group name of the <code>Job</code> to be removed.</param>
		/// <returns> 
		/// <code>true</code> if a <code>Job</code> with the given name and
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
			lock (jobLock)
			{
				object tempObject;
				tempObject = jobsByFQN[key];
				jobsByFQN.Remove(key);
				found = (tempObject != null) | found;
				if (found)
				{
					Hashtable grpMap = (Hashtable) jobsByGroup[groupName];
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
		/// Store the given <code>Trigger</code>.
		/// </summary>
		/// <param name="newTrigger">The <code>Trigger</code> to be stored.</param>
		/// <param name="replaceExisting">
		/// If <code>true</code>, any <code>Trigger</code> existing in
		/// the <code>JobStore</code> with the same name and group should
		/// be over-written.
		/// </param>
		public virtual void StoreTrigger(SchedulingContext ctxt, Trigger newTrigger, bool replaceExisting)
		{
			TriggerWrapper tw = new TriggerWrapper(newTrigger);

			if (triggersByFQN[tw.key] != null)
			{
				if (!replaceExisting)
				{
					throw new ObjectAlreadyExistsException(newTrigger);
				}

				RemoveTrigger(ctxt, newTrigger.Name, newTrigger.Group);
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
				Hashtable grpMap = (Hashtable) triggersByGroup[newTrigger.Group];
				if (grpMap == null)
				{
					grpMap = new Hashtable(100);
					triggersByGroup[newTrigger.Group] = grpMap;
				}
				grpMap[newTrigger.Name] = tw;
				// add to triggers by FQN map
				triggersByFQN[tw.key] = tw;

				lock (pausedTriggerGroups)
				{
					if (pausedTriggerGroups.Contains(newTrigger.Group))
					{
						tw.state = TriggerWrapper.STATE_PAUSED;
						if (blockedJobs.Contains(tw.jobKey))
						{
							tw.state = TriggerWrapper.STATE_PAUSED_BLOCKED;
						}
					}
					else if (blockedJobs.Contains(tw.jobKey))
					{
						tw.state = TriggerWrapper.STATE_BLOCKED;
					}
					else
					{
						timeTriggers.Add(tw);
					}
				}
			}
		}

		/// <summary>
		/// Remove (delete) the <code>Trigger</code> with the
		/// given name.
		/// </summary>
		/// <param name="triggerName">The name of the <code>Trigger</code> to be removed.</param>
		/// <param name="groupName">The group name of the <code>Trigger</code> to be removed.</param>
		/// <returns>
		/// <code>true</code> if a <code>Trigger</code> with the given
		/// name and group was found and removed from the store.
		/// </returns>
		public virtual bool RemoveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			string key = TriggerWrapper.GetTriggerNameKey(triggerName, groupName);

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
					Hashtable grpMap = (Hashtable) triggersByGroup[groupName];
					if (grpMap != null)
					{
						grpMap.Remove(triggerName);
						if (grpMap.Count == 0)
						{
							triggersByGroup.Remove(groupName);
						}
					}
					// remove from triggers array
					for (int i = 0;i < triggers.Count; ++i)
					{
						tw = (TriggerWrapper) triggers[i];
						if (key.Equals(tw.key))
						{
							triggers.RemoveAt(i);
							break;
						}
					}
					timeTriggers.Remove(tw);

					JobWrapper jw = (JobWrapper) jobsByFQN[JobWrapper.GetJobNameKey(tw.trigger.JobName, tw.trigger.JobGroup)];
					Trigger[] trigs = GetTriggersForJob(ctxt, tw.trigger.JobName, tw.trigger.JobGroup);
					if ((trigs == null || trigs.Length == 0) && !jw.jobDetail.Durable)
					{
						RemoveJob(ctxt, tw.trigger.JobName, tw.trigger.JobGroup);
					}
				}
			}

			return found;
		}


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
					Hashtable grpMap = (Hashtable) triggersByGroup[groupName];
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
					catch (JobPersistenceException jpe)
					{
						StoreTrigger(ctxt, tw.Trigger, false); // put previous trigger back...
						throw jpe;
					}
				}
			}

			return found;
		}

		/// <summary>
		/// Retrieve the <code>JobDetail</code> for the given
		/// <code>Job</code>.
		/// </summary>
		/// <param name="jobName">The name of the <code>Job</code> to be retrieved.</param>
		/// <param name="groupName">The group name of the <code>Job</code> to be retrieved.</param>
		/// <returns>The desired <code>Job</code>, or null if there is no match.</returns>
		public virtual JobDetail RetrieveJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			JobWrapper jw = (JobWrapper) jobsByFQN[JobWrapper.GetJobNameKey(jobName, groupName)];
			if (jw != null)
			{
				return jw.jobDetail;
			}

			return null;
		}

		/// <summary>
		/// Retrieve the given <code>Trigger</code>.
		/// </summary>
		/// <param name="triggerName"> The name of the <code>Trigger</code> to be retrieved.</param>
		/// <param name="groupName">The group name of the <code>Trigger</code> to be retrieved.</param>
		/// <returns> The desired <code>Trigger</code>, or null if there is no match.</returns>
		public virtual Trigger RetrieveTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];
			if (tw != null)
			{
				return tw.Trigger;
			}

			return null;
		}

		/// <summary>
		/// Get the current state of the identified <code>Trigger</code>.
		/// </summary>
		/// <seealso cref="Trigger.STATE_NORMAL" />
		/// <seealso cref="Trigger.STATE_PAUSED" />
		/// <seealso cref="Trigger.STATE_COMPLETE" />
		/// <seealso cref="Trigger.STATE_ERROR" />
		/// <seealso cref="Trigger.STATE_BLOCKED" />
		/// <seealso cref="Trigger.STATE_NONE"/>
		public virtual int GetTriggerState(SchedulingContext ctxt, string triggerName, string groupName)
		{
			TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];
			if (tw == null)
			{
				return Trigger.STATE_NONE;
			}
			if (tw.state == TriggerWrapper.STATE_COMPLETE)
			{
				return Trigger.STATE_COMPLETE;
			}
			if (tw.state == TriggerWrapper.STATE_PAUSED)
			{
				return Trigger.STATE_PAUSED;
			}
			if (tw.state == TriggerWrapper.STATE_PAUSED_BLOCKED)
			{
				return Trigger.STATE_PAUSED;
			}
			if (tw.state == TriggerWrapper.STATE_BLOCKED)
			{
				return Trigger.STATE_BLOCKED;
			}
			if (tw.state == TriggerWrapper.STATE_ERROR)
			{
				return Trigger.STATE_ERROR;
			}
			return Trigger.STATE_NORMAL;
		}

		/// <summary>
		/// Store the given <code>ICalendar</code>.
		/// </summary>
		/// <param name="calendar">The <code>ICalendar</code> to be stored.</param>
		/// <param name="replaceExisting">
		/// If <code>true</code>, any <code>ICalendar</code> existing
		/// in the <code>JobStore</code> with the same name and group
		/// should be over-written.
		/// </param>
		/// <param name="updateTriggers">
		/// If <code>true</code>, any <code>Trigger</code>s existing
		/// in the <code>JobStore</code> that reference an existing 
		/// Calendar with the same name with have their next fire time
		/// re-computed with the new <code>Calendar</code>.
		/// </param>
		public virtual void StoreCalendar(SchedulingContext ctxt, string name, ICalendar calendar, bool replaceExisting,
		                                  bool updateTriggers)
		{
			object obj = calendarsByName[name];

			if (obj != null && replaceExisting == false)
			{
				throw new ObjectAlreadyExistsException("Calendar with name '" + name + "' already exists.");
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
		/// Remove (delete) the <code>ICalendar</code> with the
		/// given name.
		/// <p>
		/// If removal of the <code>ICalendar</code> would result in
		/// <code>Trigger</code>s pointing to non-existent calendars, then a
		/// <code>JobPersistenceException</code> will be thrown.</p>
		/// </summary>
		/// <param name="calName">The name of the <code>ICalendar</code> to be removed.</param>
		/// <returns>
		/// <code>true</code> if a <code>ICalendar</code> with the given name
		/// was found and removed from the store.
		/// </returns>
		public virtual bool RemoveCalendar(SchedulingContext ctxt, string calName)
		{
			int numRefs = 0;

			lock (triggerLock)
			{
				foreach (Trigger trigg in triggers)
				{
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
		/// Retrieve the given <code>Trigger</code>.
		/// </summary>
		/// <param name="calName">The name of the <code>Calendar</code> to be retrieved.</param>
		/// <returns> The desired <code>Calendar</code>, or null if there is no match. </returns>
		public virtual ICalendar RetrieveCalendar(SchedulingContext ctxt, string calName)
		{
			return (ICalendar) calendarsByName[calName];
		}

		/// <summary>
		/// Get the number of <code>JobDetail</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </summary>
		public virtual int GetNumberOfJobs(SchedulingContext ctxt)
		{
			return jobsByFQN.Count;
		}

		/// <summary>
		/// Get the number of <code>Trigger</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </summary>
		public virtual int GetNumberOfTriggers(SchedulingContext ctxt)
		{
			return triggers.Count;
		}

		/// <summary>
		/// Get the number of <code>ICalendar</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </summary>
		public virtual int GetNumberOfCalendars(SchedulingContext ctxt)
		{
			return calendarsByName.Count;
		}

		/// <summary>
		/// Get the names of all of the <code>IJob</code> s that
		/// have the given group name.
		/// </summary>
		public virtual string[] GetJobNames(SchedulingContext ctxt, string groupName)
		{
			string[] outList;
			Hashtable grpMap = (Hashtable) jobsByGroup[groupName];
			if (grpMap != null)
			{
				lock (jobLock)
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
		/// Get the names of all of the <code>ICalendar</code> s
		/// in the <code>JobStore</code>.
		/// <p>
		/// If there are no ICalendars in the given group name, the result should be
		/// a zero-length array (not <code>null</code>).
		/// </p>
		/// </summary>
		public virtual string[] GetCalendarNames(SchedulingContext ctxt)
		{
			ArrayList names = new ArrayList(calendarsByName.Keys);
			return (string[]) names.ToArray(typeof (string));
		}

		/// <summary>
		/// Get the names of all of the <code>Trigger</code> s
		/// that have the given group name.
		/// </summary>
		public virtual string[] GetTriggerNames(SchedulingContext ctxt, string groupName)
		{
			string[] outList;
			Hashtable grpMap = (Hashtable) triggersByGroup[groupName];
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
		/// Get the names of all of the <code>IJob</code>
		/// groups.
		/// </summary>
		public virtual string[] GetJobGroupNames(SchedulingContext ctxt)
		{
			string[] outList;

			lock (jobLock)
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
		/// Get the names of all of the <code>Trigger</code> groups.
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
		/// Pause the <code>Trigger</code> with the given name.
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
			if (tw.state == TriggerWrapper.STATE_COMPLETE)
			{
				return;
			}

			lock (triggerLock)
			{
				if (tw.state == TriggerWrapper.STATE_BLOCKED)
				{
					tw.state = TriggerWrapper.STATE_PAUSED_BLOCKED;
				}
				else
				{
					tw.state = TriggerWrapper.STATE_PAUSED;
				}
				timeTriggers.Remove(tw);
			}
		}

		/// <summary>
		/// Pause all of the <code>Trigger</code>s in the given group.
		/// <p>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new triggers that are added to the group while the group is
		/// paused.
		/// </p>
		/// </summary>
		public virtual void PauseTriggerGroup(SchedulingContext ctxt, string groupName)
		{
			lock (pausedTriggerGroups)
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
		/// Pause the <code>JobDetail</code> with the given
		/// name - by pausing all of its current <code>Trigger</code>s.
		/// </summary>
		public virtual void PauseJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			lock (pausedTriggerGroups)
			{
				Trigger[] t = GetTriggersForJob(ctxt, jobName, groupName);
				for (int j = 0; j < t.Length; j++)
				{
					PauseTrigger(ctxt, t[j].Name, t[j].Group);
				}
			}
		}

		/// <summary>
		/// Pause all of the <code>{@link org.quartz.JobDetail}s</code> in the
		/// given group - by pausing all of their <code>Trigger</code>s.
		/// <p>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new jobs that are added to the group while the group is
		/// paused.
		/// </p>
		/// </summary>
		public virtual void PauseJobGroup(SchedulingContext ctxt, string groupName)
		{
			lock (pausedTriggerGroups)
			{
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
		/// Resume (un-pause) the <code>Trigger</code> with the given
		/// name.
		/// <p>
		/// If the <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		public virtual void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName)
		{
			TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(triggerName, groupName)];

			Trigger trig = tw.Trigger;

			// does the trigger exist?
			if (tw == null || tw.trigger == null)
			{
				return;
			}
			// if the trigger is not paused resuming it does not make sense...
			if (tw.state != TriggerWrapper.STATE_PAUSED && tw.state != TriggerWrapper.STATE_PAUSED_BLOCKED)
			{
				return;
			}

			lock (triggerLock)
			{
				if (blockedJobs.Contains(JobWrapper.GetJobNameKey(trig.JobName, trig.JobGroup)))
				{
					tw.state = TriggerWrapper.STATE_BLOCKED;
				}
				else
				{
					tw.state = TriggerWrapper.STATE_WAITING;
				}

				ApplyMisfire(tw);

				if (tw.state == TriggerWrapper.STATE_WAITING)
				{
					timeTriggers.Add(tw);
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) all of the <code>Trigger</code>s in the
		/// given group.
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		public virtual void ResumeTriggerGroup(SchedulingContext ctxt, string groupName)
		{
			lock (pausedTriggerGroups)
			{
				string[] names = GetTriggerNames(ctxt, groupName);

				for (int i = 0; i < names.Length; i++)
				{
					ResumeTrigger(ctxt, names[i], groupName);
				}
				pausedTriggerGroups.Remove(groupName);
			}
		}

		/// <summary>
		/// Resume (un-pause) the <code>JobDetail</code> with
		/// the given name.
		/// <p>
		/// If any of the <code>Job</code>'s<code>Trigger</code> s missed one
		/// or more fire-times, then the <code>Trigger</code>'s misfire
		/// instruction will be applied.
		/// </p>
		/// </summary>
		public virtual void ResumeJob(SchedulingContext ctxt, string jobName, string groupName)
		{
			lock (pausedTriggerGroups)
			{
				Trigger[] t = GetTriggersForJob(ctxt, jobName, groupName);
				for (int j = 0; j < t.Length; j++)
				{
					ResumeTrigger(ctxt, t[j].Name, t[j].Group);
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) all of the <code>JobDetail</code>s
		/// in the given group.
		/// <p>
		/// If any of the <code>Job</code> s had <code>Trigger</code> s that
		/// missed one or more fire-times, then the <code>Trigger</code>'s
		/// misfire instruction will be applied.
		/// </p>
		/// </summary>
		public virtual void ResumeJobGroup(SchedulingContext ctxt, string groupName)
		{
			lock (pausedTriggerGroups)
			{
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
		/// Pause all triggers - equivalent of calling <code>PauseTriggerGroup(group)</code>
		/// on every group.
		/// <p>
		/// When <code>ResumeAll()</code> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="ResumeAll(SchedulingContext)" /> 
		public virtual void PauseAll(SchedulingContext ctxt)
		{
			lock (pausedTriggerGroups)
			{
				string[] names = GetTriggerGroupNames(ctxt);

				for (int i = 0; i < names.Length; i++)
				{
					PauseTriggerGroup(ctxt, names[i]);
				}
			}
		}

		/// <summary>
		/// Resume (un-pause) all triggers - equivalent of calling <code>resumeTriggerGroup(group)</code>
		/// on every group.
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="PauseAll(SchedulingContext)" />
		public virtual void ResumeAll(SchedulingContext ctxt)
		{
			lock (pausedTriggerGroups)
			{
				string[] names = GetTriggerGroupNames(ctxt);

				for (int i = 0; i < names.Length; i++)
				{
					ResumeTriggerGroup(ctxt, names[i]);
				}
			}
		}

		protected internal virtual bool ApplyMisfire(TriggerWrapper tw)
		{
			DateTime misfireTime = DateTime.Now;
			if (MisfireThreshold > 0)
			{
				misfireTime = misfireTime.AddMilliseconds(-1 *MisfireThreshold);
			}

			NullableDateTime tnft = tw.trigger.GetNextFireTime();
			if (tnft.Value > misfireTime)
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

			if (!tw.trigger.GetNextFireTime().HasValue)
			{
				tw.state = TriggerWrapper.STATE_COMPLETE;
				lock (triggerLock)
				{
					timeTriggers.Remove(tw);
				}
			}
			else if (tnft.Equals(tw.trigger.GetNextFireTime()))
			{
				return false;
			}

			return true;
		}

		private static long ftrCtr = DateTime.Now.Ticks;

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

					if (!tw.trigger.GetNextFireTime().HasValue)
					{
						timeTriggers.Remove(tw);
						tw = null;
						continue;
					}

					timeTriggers.Remove(tw);

					if (ApplyMisfire(tw))
					{
						if (tw.trigger.GetNextFireTime().HasValue)
						{
							timeTriggers.Add(tw);
						}
						tw = null;
						continue;
					}

					if (tw.trigger.GetNextFireTime().Value > noLaterThan)
					{
						timeTriggers.Add(tw);
						return null;
					}

					tw.state = TriggerWrapper.STATE_ACQUIRED;

					tw.trigger.FireInstanceId = FiredTriggerRecordId;
					Trigger trig = (Trigger) tw.trigger.Clone();
					return trig;
				}
			}

			return null;
		}

		/// <summary>
		/// Inform the <code>JobStore</code> that the scheduler no longer plans to
		/// fire the given <code>Trigger</code>, that it had previously acquired
		/// (reserved).
		/// </summary>
		public virtual void ReleaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger)
		{
			lock (triggerLock)
			{
				TriggerWrapper tw = (TriggerWrapper) triggersByFQN[TriggerWrapper.GetTriggerNameKey(trigger)];
				if (tw != null && tw.state == TriggerWrapper.STATE_ACQUIRED)
				{
					tw.state = TriggerWrapper.STATE_WAITING;
					timeTriggers.Add(tw);
				}
			}
		}

		/// <summary>
		/// Inform the <code>JobStore</code> that the scheduler is now firing the
		/// given <code>Trigger</code> (executing its associated <code>Job</code>),
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
				// was the trigger completed since being acquired?
				if (tw.state == TriggerWrapper.STATE_COMPLETE)
				{
					return null;
				}
				// was the trigger paused since being acquired?
				if (tw.state == TriggerWrapper.STATE_PAUSED)
				{
					return null;
				}
				// was the trigger blocked since being acquired?
				if (tw.state == TriggerWrapper.STATE_BLOCKED)
				{
					return null;
				}
				// was the trigger paused and blocked since being acquired?
				if (tw.state == TriggerWrapper.STATE_PAUSED_BLOCKED)
				{
					return null;
				}

				ICalendar cal = null;
				if (tw.trigger.CalendarName != null)
				{
					cal = RetrieveCalendar(ctxt, tw.trigger.CalendarName);
				}
				NullableDateTime prevFireTime = trigger.GetPreviousFireTime();
				// call triggered on our copy, and the scheduler's copy
				tw.trigger.Triggered(cal);
				trigger.Triggered(cal);
				//tw.state = TriggerWrapper.STATE_EXECUTING;
				tw.state = TriggerWrapper.STATE_WAITING;

				TriggerFiredBundle bndle =
					new TriggerFiredBundle(RetrieveJob(ctxt, trigger.JobName, trigger.JobGroup), trigger, cal, false, DateTime.Now,
					                       trigger.GetPreviousFireTime(), prevFireTime, trigger.GetNextFireTime());

				JobDetail job = bndle.JobDetail;

				if (job.Stateful)
				{
					ArrayList trigs = GetTriggerWrappersForJob(job.Name, job.Group);
					IEnumerator itr = trigs.GetEnumerator();
					while (itr.MoveNext())
					{
						TriggerWrapper ttw = (TriggerWrapper) itr.Current;
						if (ttw.state == TriggerWrapper.STATE_WAITING)
						{
							ttw.state = TriggerWrapper.STATE_BLOCKED;
						}
						if (ttw.state == TriggerWrapper.STATE_PAUSED)
						{
							ttw.state = TriggerWrapper.STATE_PAUSED_BLOCKED;
						}
						timeTriggers.Remove(ttw);
					}
					blockedJobs.Add(JobWrapper.GetJobNameKey(job));
				}
				else
				{
					NullableDateTime d = tw.trigger.GetNextFireTime();
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
		/// Inform the <code>JobStore</code> that the scheduler has completed the
		/// firing of the given <code>Trigger</code> (and the execution its
		/// associated <code>Job</code>), and that the <code>{@link org.quartz.JobDataMap}</code>
		/// in the given <code>JobDetail</code> should be updated if the <code>Job</code>
		/// is stateful.
		/// </summary>
		public virtual void TriggeredJobComplete(SchedulingContext ctxt, Trigger trigger, JobDetail jobDetail,
		                                         int triggerInstCode)
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
							newData.ClearDirtyFlag();
						}
						jd.JobDataMap = newData;
						blockedJobs.Remove(JobWrapper.GetJobNameKey(jd));
						ArrayList trigs = GetTriggerWrappersForJob(jd.Name, jd.Group);
						foreach (TriggerWrapper ttw in trigs)
						{
							if (ttw.state == TriggerWrapper.STATE_BLOCKED)
							{
								ttw.state = TriggerWrapper.STATE_WAITING;
								timeTriggers.Add(ttw);
							}
							if (ttw.state == TriggerWrapper.STATE_PAUSED_BLOCKED)
							{
								ttw.state = TriggerWrapper.STATE_PAUSED;
							}
						}
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
					if (triggerInstCode == Trigger.INSTRUCTION_DELETE_TRIGGER)
					{
						NullableDateTime d = trigger.GetNextFireTime();
						if (!d.HasValue)
						{
							// double check for possible reschedule within job 
							// execution, which would cancel the need to delete...
							d = tw.Trigger.GetNextFireTime();
							if (!d.HasValue)
							{
								RemoveTrigger(ctxt, trigger.Name, trigger.Group);
							}
						}
						else
						{
							RemoveTrigger(ctxt, trigger.Name, trigger.Group);
						}
					}
					else if (triggerInstCode == Trigger.INSTRUCTION_SET_TRIGGER_COMPLETE)
					{
						tw.state = TriggerWrapper.STATE_COMPLETE;
						timeTriggers.Remove(tw);
					}
					else if (triggerInstCode == Trigger.INSTRUCTION_SET_TRIGGER_ERROR)
					{
						Log.Info("Trigger " + trigger.FullName + " set to ERROR state.");
						tw.state = TriggerWrapper.STATE_ERROR;
					}
					else if (triggerInstCode == Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_ERROR)
					{
						Log.Info("All triggers of Job " + trigger.FullJobName + " set to ERROR state.");
						SetAllTriggersOfJobToState(trigger.JobName, trigger.JobGroup, TriggerWrapper.STATE_ERROR);
					}
					else if (triggerInstCode == Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_COMPLETE)
					{
						SetAllTriggersOfJobToState(trigger.JobName, trigger.JobGroup, TriggerWrapper.STATE_COMPLETE);
					}
				}
			}
		}

		protected internal virtual void SetAllTriggersOfJobToState(string jobName, string jobGroup, int state)
		{
			ArrayList tws = GetTriggerWrappersForJob(jobName, jobGroup);
			foreach (TriggerWrapper tw in tws)
			{
				tw.state = state;
				if (state != TriggerWrapper.STATE_WAITING)
				{
					timeTriggers.Remove(tw);
				}
			}
		}

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

			if (comp == 0)
			{
				return String.CompareOrdinal(trig1.trigger.FullName, trig2.trigger.FullName);
			}

			return comp;
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

	public class TriggerWrapper
	{
		public virtual Trigger Trigger
		{
			get { return trigger; }
		}

		public string key;

		public string jobKey;

		public Trigger trigger;

		public int state = STATE_WAITING;

		public const int STATE_WAITING = 0;

		public const int STATE_ACQUIRED = 1;

		public const int STATE_EXECUTING = 2;

		public const int STATE_COMPLETE = 3;

		public const int STATE_PAUSED = 4;

		public const int STATE_BLOCKED = 5;

		public const int STATE_PAUSED_BLOCKED = 6;

		public const int STATE_ERROR = 7;

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

		public override int GetHashCode()
		{
			return key.GetHashCode();
		}
	}
}