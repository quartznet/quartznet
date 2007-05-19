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

using Nullables;

namespace Quartz
{
	/// <summary>
	/// The base abstract class to be extended by all <code>Trigger</code>s.
	/// <p>
	/// <code>Triggers</code> s have a name and group associated with them, which
	/// should uniquely identify them within a single <code>Scheduler</code>.
	/// </p>
	/// 
	/// <p>
	/// <code>Trigger</code>s are the 'mechanism' by which <code>Job</code> s
	/// are scheduled. Many <code>Trigger</code> s can point to the same <code>Job</code>,
	/// but a single <code>Trigger</code> can only point to one <code>Job</code>.
	/// </p>
	/// 
	/// <p>
	/// Triggers can 'send' parameters/data to <code>Job</code>s by placing contents
	/// into the <code>JobDataMap</code> on the <code>Trigger</code>.
	/// </p>
	/// </summary>
	/// <seealso cref="SimpleTrigger" />
    /// <seealso cref="CronTrigger" />
    /// <seealso cref="NthIncludedDayTrigger" />
    /// <seealso cref="TriggerUtils" />
    /// <seealso cref="JobDataMap" />
    /// <seealso cref="JobExecutionContext" />
	/// <author>James House</author>
	/// <author>Sharada Jambula</author>
	[Serializable]
	public abstract class Trigger : ICloneable, IComparable
	{
        /// <summary>
        /// Instructs the <code>Scheduler</code> that the <code>Trigger</code>
        /// has no further instructions.
        /// </summary>
        public const int INSTRUCTION_NOOP = 0;

        /// <summary>
        /// Instructs the <code>{@link Scheduler}</code> that the <code>{@link Trigger}</code>
        /// wants the <code>{@link org.quartz.JobDetail}</code> to re-Execute
        /// immediately. If not in a 'RECOVERING' or 'FAILED_OVER' situation, the
        /// execution context will be re-used (giving the <code>Job</code> the
        /// abilitiy to 'see' anything placed in the context by its last execution).
        /// </summary>
        public const int INSTRUCTION_RE_EXECUTE_JOB = 1;

        /// <summary>
        /// Instructs the <code>{@link Scheduler}</code> that the <code>{@link Trigger}</code>
        /// should be put in the <code>COMPLETE</code> state.
        /// </summary>
        public const int INSTRUCTION_SET_TRIGGER_COMPLETE = 2;

        /// <summary>
        /// Instructs the <code>{@link Scheduler}</code> that the <code>{@link Trigger}</code>
        /// wants itself deleted.
        /// </summary>
        public const int INSTRUCTION_DELETE_TRIGGER = 3;

        /// <summary>
        /// Instructs the <code>{@link Scheduler}</code> that all <code>Trigger</code>
        /// s referencing the same <code>{@link org.quartz.JobDetail}</code> as
        /// this one should be put in the <code>COMPLETE</code> state.
        /// </summary>
        public const int INSTRUCTION_SET_ALL_JOB_TRIGGERS_COMPLETE = 4;

        /// <summary>
        /// Instructs the <code>{@link Scheduler}</code> that all <code>Trigger</code>
        /// s referencing the same <code>{@link org.quartz.JobDetail}</code> as
        /// this one should be put in the <code>ERROR</code> state.
        /// </summary>
        public const int INSTRUCTION_SET_TRIGGER_ERROR = 5;

        /// <summary>
        /// Instructs the <code>{@link Scheduler}</code> that the <code>Trigger</code>
        /// should be put in the <code>ERROR</code> state.
        /// </summary>
        public const int INSTRUCTION_SET_ALL_JOB_TRIGGERS_ERROR = 6;

        /// <summary>
        /// Instructs the <code>{@link Scheduler}</code> that upon a mis-fire
        /// situation, the <code>updateAfterMisfire()</code> method will be called
        /// on the <code>Trigger</code> to determine the mis-fire instruction.
        /// <p>
        /// In order to see if this instruction fits your needs, you should look at
        /// the documentation for the <code>getSmartMisfirePolicy()</code> method
        /// on the particular <code>Trigger</code> implementation you are using.
        /// </p>
        /// </summary>
        public const int MISFIRE_INSTRUCTION_SMART_POLICY = 0;

        /// <summary>
        /// Indicates that the <code>Trigger</code> is in the "normal" state.
        /// </summary>
        public const int STATE_NORMAL = 0;

        /// <summary>
        /// Indicates that the <code>Trigger</code> is in the "paused" state.
        /// </summary>
        public const int STATE_PAUSED = 1;

        /// <summary>
        /// Indicates that the <code>Trigger</code> is in the "complete" state.
        /// <p>
        /// "Complete" indicates that the trigger has not remaining fire-times in
        /// its schedule.
        /// </p>
        /// </summary>
        public const int STATE_COMPLETE = 2;

        /// <summary>
        /// Indicates that the <code>Trigger</code> is in the "error" state.
        /// <p>
        /// A <code>Trigger</code> arrives at the error state when the scheduler
        /// attempts to fire it, but cannot due to an error creating and executing
        /// its related job. Often this is due to the <code>Job</code>'s
        /// class not existing in the classpath.
        /// </p>
        /// 
        /// <p>
        /// When the trigger is in the error state, the scheduler will make no
        /// attempts to fire it.
        /// </p>
        /// </summary>
        public const int STATE_ERROR = 3;


        /// <summary>
        /// Indicates that the <code>Trigger</code> is in the "blocked" state.
        /// <p>
        /// A <code>Trigger</code> arrives at the blocked state when the job that
        /// it is associated with is a <code>StatefulJob</code> and it is 
        /// currently executing.
        /// </p>
        /// </summary>
        /// <seealso cref="IStatefulJob" />
        public const int STATE_BLOCKED = 4;

        /// <summary>
        /// Indicates that the <code>Trigger</code> does not exist.
        /// </summary>
        public const int STATE_NONE = -1;


        private string name;
        private string group = Scheduler_Fields.DEFAULT_GROUP;
        private string jobName;
        private string jobGroup = Scheduler_Fields.DEFAULT_GROUP;
        private string description;
        private JobDataMap jobDataMap;
        private bool volatility = false;
        private string calendarName = null;
        private string fireInstanceId = null;
        private int misfireInstruction = MISFIRE_INSTRUCTION_SMART_POLICY;
        private ArrayList triggerListeners = new ArrayList();
        private NullableDateTime endTime;
        private DateTime startTime;

		/// <summary>
		/// Get or sets the name of this <code>Trigger</code>.
		/// </summary>
		/// <exception cref="ArgumentException">If name is null or empty.</exception>
		public string Name
		{
			get { return name; }

			set
			{
				if (value == null || value.Trim().Length == 0)
				{
					throw new ArgumentException("Trigger name cannot be null or empty.");
				}

				name = value;
			}
		}

		/// <summary>
		/// Get the group of this <code>Trigger</code>. If <code>null</code>, Scheduler.DEFAULT_GROUP will be used.
		/// </summary>
		/// <exception cref="ArgumentException"> 
		/// if group is an empty string.
		/// </exception>
		public string Group
		{
			get { return group; }

			set
			{
				if (value != null && value.Trim().Length == 0)
				{
					throw new ArgumentException("Group name cannot be an empty string.");
				}

				if (value == null)
				{
					value = Scheduler_Fields.DEFAULT_GROUP;
				}

				group = value;
			}
		}

		/// <summary>
		/// Get or set the name of the associated <code>{@link org.quartz.JobDetail}</code>.
		/// </summary> 
		/// <exception cref="ArgumentException"> 
		/// if jobName is null or empty.
		/// </exception>
		public string JobName
		{
			get { return jobName; }

			set
			{
				if (value == null || value.Trim().Length == 0)
				{
					throw new ArgumentException("Job name cannot be null or empty.");
				}

				jobName = value;
			}
		}

		/// <summary>
		/// Gets or sets the name of the associated <code>{@link org.quartz.JobDetail}</code>'s
		/// group. If set with <code>null</code>, Scheduler.DEFAULT_GROUP will be used.
		/// </summary>
		/// <exception cref="ArgumentException"> ArgumentException
		/// if group is an empty string.
		/// </exception>
		public string JobGroup
		{
			get { return jobGroup; }

			set
			{
				if (value != null && value.Trim().Length == 0)
				{
					throw new ArgumentException("Group name cannot be null or empty.");
				}

				if (value == null)
				{
					value = Scheduler_Fields.DEFAULT_GROUP;
				}

				jobGroup = value;
			}
		}

		/// <summary>
		/// Returns the 'full name' of the <code>Trigger</code> in the format
		/// "group.name".
		/// </summary>
		public virtual string FullName
		{
			get { return group + "." + name; }
		}

		/// <summary>
		/// Returns the 'full name' of the <code>Job</code> that the <code>Trigger</code>
		/// points to, in the format "group.name".
		/// </summary>
		public virtual string FullJobName
		{
			get { return jobGroup + "." + jobName; }
		}

		/// <summary>
		/// Get or set the description given to the <code>Trigger</code> instance by
		/// its creator (if any).
		/// </summary>
		public virtual string Description
		{
			get { return description; }
			set { description = value; }
		}

		/// <summary>
		/// Set whether or not the <code>Trigger</code> should be persisted in the
		/// <code>IJobStore</code> for re-use after program  restarts.
		/// </summary>
		public virtual bool Volatility
		{
			set { volatility = value; }
		}

		/// <summary>
		/// Get or set  the <code>ICalendar</code> with the given name with
		/// this Trigger. Use <code>null</code> when setting to dis-associate a Calendar.
		/// </summary>
		public virtual string CalendarName
		{
			get { return calendarName; }
			set { calendarName = value; }
		}

		/// <summary>
		/// Get or set the <code>JobDataMap</code> that is associated with the 
		/// <code>Trigger</code>.
		/// <p>
		/// Changes made to this map during job execution are not re-persisted, and
		/// in fact typically result in an <code>IllegalStateException</code>.
		/// </p>
		/// </summary>
		public virtual JobDataMap JobDataMap
		{
			get
			{
				if (jobDataMap == null)
				{
					jobDataMap = new JobDataMap();
				}
				return jobDataMap;
			}

			set { jobDataMap = value; }
		}

		/// <summary>
		/// Whether or not the <code>Trigger</code> should be persisted in the
		/// <code>IJobStore</code> for re-use after program  restarts.
		/// <p>
		/// If not explicitly set, the default value is <code>false</code>.
		/// </p>
		/// </summary>
		public virtual bool Volatile
		{
			get { return volatility; }
		}

		/// <summary>
		/// Returns an array of <code>string</code> s containing the names of all
		/// <code>TriggerListener</code>s assigned to the <code>Trigger</code>,
		/// in the order in which they should be notified.
		/// </summary>
		public virtual string[] TriggerListenerNames
		{
			get { return (string[]) triggerListeners.ToArray(typeof (string)); }
		}
		
		/// <summary>
		/// Returns the last time at which the <code>Trigger</code> will fire, if
		/// the Trigger will repeat indefinitely, null will be returned.
		/// <p>
		/// Note that the return time *may* be in the past.
		/// </p>
		/// </summary>
		public abstract NullableDateTime FinalFireTime { get; }

		/// <summary>
		/// Get or set the instruction the <code>Scheduler</code> should be given for
		/// handling misfire situations for this <code>Trigger</code>- the
		/// concrete <code>Trigger</code> type that you are using will have
		/// defined a set of additional <code>MISFIRE_INSTRUCTION_XXX</code>
		/// constants that may be passed to this method.
		/// <p>
		/// If not explicitly set, the default value is <code>MISFIRE_INSTRUCTION_SMART_POLICY</code>.
		/// </p>
		/// </summary>
		/// <seealso cref="MISFIRE_INSTRUCTION_SMART_POLICY" />
		/// <seealso cref="UpdateAfterMisfire" />
		/// <seealso cref="SimpleTrigger" />
		/// <seealso cref="CronTrigger" />
		public virtual int MisfireInstruction
		{
			get { return misfireInstruction; }

			set
			{
				if (!ValidateMisfireInstruction(value))
				{
					throw new ArgumentException("The misfire instruction code is invalid for this type of trigger.");
				}
				misfireInstruction = value;
			}
		}

		/// <summary> 
		/// This method should not be used by the Quartz client.
		/// <p>
		/// Usable by <code>JobStore</code>
		/// implementations, in order to facilitate 'recognizing' instances of fired
		/// <code>Trigger</code> s as their jobs complete execution.
		/// </p>
		/// </summary>
		public virtual string FireInstanceId
		{
			get { return fireInstanceId; }
			set { fireInstanceId = value; }
		}

		/// <summary>
		/// Returns the date/time on which the trigger must stop firing. This 
		/// defines the final boundary for trigger firings &#x8212; the trigger will
		/// not fire after to this date and time. If this value is null, no end time
		/// boundary is assumed, and the trigger can continue indefinitely.
		/// 
		/// Sets the date/time on which the trigger must stop firing. This defines
		/// the final boundary for trigger firings &#x8212; the trigger will not
		/// fire after to this date and time. If this value is null, no end time
		/// boundary is assumed, and the trigger can continue indefinitely.
		/// </summary>
		public NullableDateTime EndTime
		{
			get { return endTime; }

			set
			{
				DateTime sTime = StartTime;

				if (value.HasValue && (sTime > value.Value))
				{
					throw new ArgumentException("End time cannot be before start time");
				}

				endTime = value;
			}
		}

		/// <summary>
		/// Returns the date/time on which the trigger may begin firing. This 
		/// defines the initial boundary for trigger firings &#x8212; the trigger
		/// will not fire prior to this date and time.
		/// </summary>
		public DateTime StartTime
		{
			get { return startTime; }

			set
			{
				if (EndTime.HasValue && EndTime.Value < value)
				{
					throw new ArgumentException("End time cannot be before start time");
				}

				if (HasMillisecondPrecision)
				{
					// round off millisecond...	
					DateTime cl = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
					startTime = cl;
				}
				else
				{
					startTime = value;
				}
		
			}
		}

		/// <summary>
		/// Tells whether this Trigger instance can handle events
		/// in millisecond precision.
		/// </summary>
		public abstract bool HasMillisecondPrecision
		{
			get;
		}

		

		/// <summary> <p>
		/// Create a <code>Trigger</code> with no specified name, group, or <code>{@link org.quartz.JobDetail}</code>.
		/// </p>
		/// 
		/// <p>
		/// Note that the {@link #setName(String)},{@link #setGroup(String)}and
		/// the {@link #setJobName(String)}and {@link #setJobGroup(String)}methods
		/// must be called before the <code>Trigger</code> can be placed into a
		/// {@link Scheduler}.
		/// </p>
		/// </summary>
		public Trigger()
		{
			// do nothing...
		}

        /// <summary>
        /// Create a <code>Trigger</code> with the given name, and group.
        /// <p>
        /// Note that the JobName and JobGroup properties must be set before the <code>Trigger</code>
        /// can be placed into a {@link Scheduler}.
        /// </p>
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">if <code>null</code>, Scheduler.DEFAULT_GROUP will be used.</param>
		public Trigger(string name, string group)
		{
			Name = name;
			Group = group;
		}

        /// <summary>
        /// Create a <code>Trigger</code> with the given name, and group.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">if <code>null</code>, Scheduler.DEFAULT_GROUP will be used.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="jobGroup">The job group.</param>
        /// <exception cref="ArgumentException"> ArgumentException
        /// if name is null or empty, or the group is an empty string.
        /// </exception>
		public Trigger(string name, string group, string jobName, string jobGroup)
		{
			Name = name;
			Group = group;
			JobName = jobName;
			JobGroup = jobGroup;
		}


        /// <summary>
        /// Add the specified name of a <code>TriggerListener</code> to
        /// the end of the <code>Trigger</code>'s list of listeners.
        /// </summary>
        /// <param name="listenerName">Name of the listener.</param>
		public virtual void AddTriggerListener(string listenerName)
		{
			triggerListeners.Add(listenerName);
		}

		/// <summary>
		/// Remove the specified name of a <code>ITriggerListener</code>
		/// from the <code>Trigger</code>'s list of listeners.
		/// </summary>
		/// <returns> true if the given name was found in the list, and removed
		/// </returns>
		public virtual bool RemoveTriggerListener(string listenerName)
		{
			Boolean tempBoolean;
			tempBoolean = triggerListeners.Contains(listenerName);
			triggerListeners.Remove(listenerName);
			return tempBoolean;
		}

		/// <summary>
		/// This method should not be used by the Quartz client.
		/// <p>
		/// Called when the <code>{@link Scheduler}</code> has decided to 'fire'
		/// the trigger (Execute the associated <code>Job</code>), in order to
		/// give the <code>Trigger</code> a chance to update itself for its next
		/// triggering (if any).
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="JobExecutionException">
		/// </seealso>
		public abstract void Triggered(ICalendar cal);

		/// <summary>
		/// This method should not be used by the Quartz client.
		/// <p>
		/// Called by the scheduler at the time a <code>Trigger</code> is first
		/// added to the scheduler, in order to have the <code>Trigger</code>
		/// compute its first fire time, based on any associated calendar.
		/// </p>
		/// 
		/// <p>
		/// After this method has been called, <code>getNextFireTime()</code>
		/// should return a valid answer.
		/// </p>
		/// 
		/// </summary>
		/// <returns> 
		/// The first time at which the <code>Trigger</code> will be fired
		/// by the scheduler, which is also the same value <code>getNextFireTime()</code>
		/// will return (until after the first firing of the <code>Trigger</code>).
		/// </returns>
		public abstract NullableDateTime ComputeFirstFireTime(ICalendar cal);

        /// <summary>
        /// This method should not be used by the Quartz client.
        /// <p>
        /// Called after the <code>{@link Scheduler}</code> has executed the
        /// <code>{@link org.quartz.JobDetail}</code> associated with the <code>Trigger</code>
        /// in order to get the final instruction code from the trigger.
        /// </p>
        /// </summary>
        /// <param name="context">is the <code>JobExecutionContext</code> that was used by the
        /// <code>Job</code>'s<code>Execute(xx)</code> method.</param>
        /// <param name="result">is the <code>JobExecutionException</code> thrown by the
        /// <code>Job</code>, if any (may be null).</param>
        /// <returns>
        /// one of the Trigger.INSTRUCTION_XXX constants.
        /// </returns>
        /// <seealso cref="INSTRUCTION_NOOP" />
        /// <seealso cref="INSTRUCTION_RE_EXECUTE_JOB" />
        /// <seealso cref="INSTRUCTION_DELETE_TRIGGER" />
        /// <seealso cref="INSTRUCTION_SET_TRIGGER_COMPLETE" />
        /// <seealso cref="Triggered" />
		public abstract int ExecutionComplete(JobExecutionContext context, JobExecutionException result);

		/// <summary> 
		/// Used by the <code>{@link Scheduler}</code> to determine whether or not
		/// it is possible for this <code>Trigger</code> to fire again.
		/// <p>
		/// If the returned value is <code>false</code> then the <code>Scheduler</code>
		/// may remove the <code>Trigger</code> from the <code>{@link org.quartz.spi.JobStore}</code>.
		/// </p>
		/// </summary>
		public abstract bool MayFireAgain();

		/// <summary>
		/// Returns the next time at which the <code>Trigger</code> will fire. If
		/// the trigger will not fire again, <code>null</code> will be returned.
		/// The value returned is not guaranteed to be valid until after the <code>Trigger</code>
		/// has been added to the scheduler.
		/// </summary>
		public abstract NullableDateTime GetNextFireTime();

		/// <summary>
		/// Returns the previous time at which the <code>Trigger</code> will fire.
		/// If the trigger has not yet fired, <code>null</code> will be returned.
		/// </summary>
		public abstract NullableDateTime GetPreviousFireTime();

		/// <summary>
		/// Returns the next time at which the <code>Trigger</code> will fire,
		/// after the given time. If the trigger will not fire after the given time,
		/// <code>null</code> will be returned.
		/// </summary>
		public abstract NullableDateTime GetFireTimeAfter(NullableDateTime afterTime);

		/// <summary>
		/// Validates the misfire instruction.
		/// </summary>
		/// <param name="misfireInstruction">The misfire instruction.</param>
		/// <returns></returns>
		protected abstract bool ValidateMisfireInstruction(int misfireInstruction);

		/// <summary> 
		/// This method should not be used by the Quartz client.
		/// <p>
		/// To be implemented by the concrete classes that extend this class.
		/// </p>
		/// <p>
		/// The implementation should update the <code>Trigger</code>'s state
		/// based on the MISFIRE_INSTRUCTION_XXX that was selected when the <code>Trigger</code>
		/// was created.
		/// </p>
		/// </summary>
		public abstract void UpdateAfterMisfire(ICalendar cal);

		/// <summary> 
		/// This method should not be used by the Quartz client.
		/// <p>
		/// The implementation should update the <code>Trigger</code>'s state
		/// based on the given new version of the associated <code>Calendar</code>
		/// (the state should be updated so that it's next fire time is appropriate
		/// given the Calendar's new settings). 
		/// </p>
		/// </summary>
		/// <param name="cal"> </param>
		/// <param name="misfireThreshold"></param>
		public abstract void UpdateWithNewCalendar(ICalendar cal, long misfireThreshold);

		/// <summary>
		/// Validates whether the properties of the <code>JobDetail</code> are
		/// valid for submission into a <code>Scheduler</code>.
		/// </summary>
		public virtual void Validate()
		{
			if (name == null)
			{
				throw new SchedulerException("Trigger's name cannot be null", SchedulerException.ERR_CLIENT_ERROR);
			}

			if (group == null)
			{
				throw new SchedulerException("Trigger's group cannot be null", SchedulerException.ERR_CLIENT_ERROR);
			}

			if (jobName == null)
			{
				throw new SchedulerException("Trigger's related Job's name cannot be null", SchedulerException.ERR_CLIENT_ERROR);
			}

			if (jobGroup == null)
			{
				throw new SchedulerException("Trigger's related Job's group cannot be null", SchedulerException.ERR_CLIENT_ERROR);
			}
		}

		/// <summary>
		/// Return a simple string representation of this object.
		/// </summary>
		public override string ToString()
		{
			return
				string.Format(
					"Trigger '{0}':  triggerClass: '{1} isVolatile: {2} calendar: '{3}' misfireInstruction: {4} nextFireTime: {5}",
					FullName, GetType().FullName, Volatile, CalendarName, MisfireInstruction, GetNextFireTime());
		}

		/// <summary>
		/// Compare the next fire time of this <code>Trigger</code> to that of
		/// another.
		/// </summary>
		public virtual int CompareTo(object obj)
		{
			Trigger other = (Trigger) obj;

			NullableDateTime myTime = GetNextFireTime();
			NullableDateTime otherTime = other.GetNextFireTime();

			if (!myTime.HasValue && !otherTime.HasValue)
			{
				return 0;
			}

			if (!myTime.HasValue)
			{
				return 1;
			}

			if (!otherTime.HasValue)
			{
				return - 1;
			}

			if ((myTime.Value < otherTime.Value))
			{
				return - 1;
			}

			if ((myTime.Value > otherTime.Value))
			{
				return 1;
			}

			return 0;
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
			if (!(obj is Trigger))
			{
				return false;
			}

			Trigger other = (Trigger) obj;

			if (!other.Name.Equals(Name))
			{
				return false;
			}
			if (!other.Group.Equals(Group))
			{
				return false;
			}

			return true;
		}


        /// <summary>
        /// Serves as a hash function for a particular type. <see cref="M:System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"></see>.
        /// </returns>
		public override int GetHashCode()
		{
			return FullName.GetHashCode();
		}

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
		public virtual object Clone()
		{
			Trigger copy;
			try
			{
				copy = (Trigger) MemberwiseClone();
			}
			catch (Exception)
			{
				throw new Exception("Not Cloneable.");
			}
			return copy;
		}
	}
}