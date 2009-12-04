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

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz
{
	/// <summary>
	/// The base abstract class to be extended by all triggers.
	/// </summary>
	/// <remarks>
	/// <p>
	/// <see cref="Trigger" />s have a name and group associated with them, which
	/// should uniquely identify them within a single <see cref="IScheduler" />.
	/// </p>
	/// 
	/// <p>
	/// <see cref="Trigger" />s are the 'mechanism' by which <see cref="IJob" /> s
	/// are scheduled. Many <see cref="Trigger" /> s can point to the same <see cref="IJob" />,
	/// but a single <see cref="Trigger" /> can only point to one <see cref="IJob" />.
	/// </p>
	/// 
	/// <p>
	/// Triggers can 'send' parameters/data to <see cref="IJob" />s by placing contents
	/// into the <see cref="JobDataMap" /> on the <see cref="Trigger" />.
	/// </p>
    /// </remarks>
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
		/// The default value for priority.
		/// </summary>
		public const int DefaultPriority = 5;

        private string name;
        private string group = SchedulerConstants.DefaultGroup;
        private string jobName;
        private string jobGroup = SchedulerConstants.DefaultGroup;
        private string description;
        private JobDataMap jobDataMap;
        private bool volatility = false;
        private string calendarName = null;
        private string fireInstanceId = null;

        private int misfireInstruction = Quartz.MisfireInstruction.InstructionNotSet;

	    private ArrayList triggerListeners = new ArrayList();

        private NullableDateTime endTimeUtc;
        private DateTime startTimeUtc;
		private int priority = DefaultPriority;
		[NonSerialized] 
		private Key key = null;

		/// <summary>
		/// Get or sets the name of this <see cref="Trigger" />.
		/// </summary>
		/// <exception cref="ArgumentException">If name is null or empty.</exception>
		public virtual string Name
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
		/// Get the group of this <see cref="Trigger" />. If <see langword="null" />, Scheduler.DefaultGroup will be used.
		/// </summary>
		/// <exception cref="ArgumentException"> 
		/// if group is an empty string.
		/// </exception>
		public virtual string Group
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
					value = SchedulerConstants.DefaultGroup;
				}

				group = value;
			}
		}

		/// <summary>
		/// Get or set the name of the associated <see cref="JobDetail" />.
		/// </summary> 
		/// <exception cref="ArgumentException"> 
		/// if jobName is null or empty.
		/// </exception>
		public virtual string JobName
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
		/// Gets or sets the name of the associated <see cref="JobDetail" />'s
		/// group. If set with <see langword="null" />, Scheduler.DefaultGroup will be used.
		/// </summary>
		/// <exception cref="ArgumentException"> ArgumentException
		/// if group is an empty string.
		/// </exception>
		public virtual string JobGroup
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
					value = SchedulerConstants.DefaultGroup;
				}

				jobGroup = value;
			}
		}

		/// <summary>
		/// Returns the 'full name' of the <see cref="Trigger" /> in the format
		/// "group.name".
		/// </summary>
		public virtual string FullName
		{
			get { return group + "." + name; }
		}

		/// <summary>
		/// Gets the key.
		/// </summary>
		/// <value>The key.</value>
		public virtual Key Key 
		{
			get
			{
				if(key == null) 
				{
					key = new Key(Name, Group);
				}

				return key;
			}
		}

		/// <summary>
		/// Returns the 'full name' of the <see cref="IJob" /> that the <see cref="Trigger" />
		/// points to, in the format "group.name".
		/// </summary>
		public virtual string FullJobName
		{
			get { return jobGroup + "." + jobName; }
		}

		/// <summary>
		/// Get or set the description given to the <see cref="Trigger" /> instance by
		/// its creator (if any).
		/// </summary>
		public virtual string Description
		{
			get { return description; }
			set { description = value; }
		}

		/// <summary>
		/// Get or set  the <see cref="ICalendar" /> with the given name with
		/// this Trigger. Use <see langword="null" /> when setting to dis-associate a Calendar.
		/// </summary>
		public virtual string CalendarName
		{
			get { return calendarName; }
			set { calendarName = value; }
		}

		/// <summary>
		/// Get or set the <see cref="JobDataMap" /> that is associated with the 
		/// <see cref="Trigger" />.
		/// <p>
		/// Changes made to this map during job execution are not re-persisted, and
		/// in fact typically result in an illegal state.
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
		/// Whether or not the <see cref="Trigger" /> should be persisted in the
		/// <see cref="IJobStore" /> for re-use after program  restarts.
		/// <p>
		/// If not explicitly set, the default value is <see langword="false" />.
		/// </p>
		/// </summary>
		public virtual bool Volatile
		{
			get { return volatility; }
            set { volatility = value; }
		}

		/// <summary>
		/// Returns an array of <see cref="string" /> s containing the names of all
		/// <see cref="ITriggerListener" />s assigned to the <see cref="Trigger" />,
		/// in the order in which they should be notified.
		/// </summary>
		public virtual string[] TriggerListenerNames
		{
			get { return (string[]) triggerListeners.ToArray(typeof (string)); }
            set 
            { 
                ClearAllTriggerListeners();
                if (value != null)
                {
                    foreach (string triggerListenerName in value)
                    {
                        AddTriggerListener(triggerListenerName);
                    }
                }
            }
		}

		
		/// <summary>
		/// Remove all <see cref="ITriggerListener" />s from the <see cref="Trigger" />.
		/// </summary>
		public virtual void ClearAllTriggerListeners() 
		{
			triggerListeners.Clear();
		}

		
		/// <summary>
		/// Returns the last UTC time at which the <see cref="Trigger" /> will fire, if
		/// the Trigger will repeat indefinitely, null will be returned.
		/// <p>
		/// Note that the return time *may* be in the past.
		/// </p>
		/// </summary>
        public abstract NullableDateTime FinalFireTimeUtc { get; }

        /// <summary>
		/// Get or set the instruction the <see cref="IScheduler" /> should be given for
		/// handling misfire situations for this <see cref="Trigger" />- the
		/// concrete <see cref="Trigger" /> type that you are using will have
		/// defined a set of additional MISFIRE_INSTRUCTION_XXX
		/// constants that may be passed to this method.
		/// <p>
        /// If not explicitly set, the default value is <see cref="Quartz.MisfireInstruction.InstructionNotSet" />.
		/// </p>
		/// </summary>
        /// <seealso cref="Quartz.MisfireInstruction.InstructionNotSet" />
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
		/// Usable by <see cref="IJobStore" />
		/// implementations, in order to facilitate 'recognizing' instances of fired
		/// <see cref="Trigger" /> s as their jobs complete execution.
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
        public virtual NullableDateTime EndTimeUtc
		{
			get { return endTimeUtc; }

			set
			{
				DateTime sTime = StartTimeUtc;

				if (value.HasValue && (sTime > value.Value))
				{
					throw new ArgumentException("End time cannot be before start time");
				}

				endTimeUtc = DateTimeUtil.AssumeUniversalTime(value);
			}
		}

		/// <summary>
        /// The time at which the trigger's scheduling should start.  May or may not
        /// be the first actual fire time of the trigger, depending upon the type of
        /// trigger and the settings of the other properties of the trigger.  However
        /// the first actual first time will not be before this date.
        /// </summary>
        /// <remarks>
        /// Setting a value in the past may cause a new trigger to compute a first
        /// fire time that is in the past, which may cause an immediate misfire
        /// of the trigger.
        /// </remarks>
		public virtual DateTime StartTimeUtc
		{
			get { return startTimeUtc; }

			set
			{
				if (EndTimeUtc.HasValue && EndTimeUtc.Value < value)
				{
					throw new ArgumentException("End time cannot be before start time");
				}

				if (HasMillisecondPrecision)
				{
					// round off millisecond...	
					startTimeUtc = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
				}
				else
				{
					startTimeUtc = value;
				}

			    startTimeUtc = DateTimeUtil.AssumeUniversalTime(startTimeUtc);
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

		/// <summary>
		/// Create a <see cref="Trigger" /> with no specified name, group, or <see cref="JobDetail" />.
		/// </summary>
		/// <remarks>
		/// Note that the <see cref="Name" />, <see cref="Group" /> and
		/// the <see cref="JobName" /> and <see cref="JobGroup" /> properties
		/// must be set before the <see cref="Trigger" /> can be placed into a
		/// <see cref="IScheduler" />.
        /// </remarks>
		public Trigger()
		{
			// do nothing...
		}

        /// <summary>
        /// Create a <see cref="Trigger" /> with the given name, and default group.
        /// </summary>
        /// <remarks>
        /// Note that the <see cref="JobName" /> and <see cref="JobGroup" />
        /// properties must be set before the <see cref="Trigger" />
        /// can be placed into a <see cref="IScheduler" />.
        /// </remarks>
        /// <param name="name">The name.</param>
        protected Trigger(string name) : this(name, null)
        {
        }

        /// <summary>
        /// Create a <see cref="Trigger" /> with the given name, and group.
        /// </summary>
        /// <remarks>
        /// Note that the <see cref="JobName" /> and <see cref="JobGroup" />
        /// properties must be set before the <see cref="Trigger" />
        /// can be placed into a <see cref="IScheduler" />.
        /// </remarks>
        /// <param name="name">The name.</param>
        /// <param name="group">if <see langword="null" />, Scheduler.DefaultGroup will be used.</param>
		public Trigger(string name, string group)
		{
			Name = name;
			Group = group;
		}

        /// <summary>
        /// Create a <see cref="Trigger" /> with the given name, and group.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">if <see langword="null" />, Scheduler.DefaultGroup will be used.</param>
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
		/// The priority of a <see cref="Trigger" /> acts as a tie breaker such that if 
        /// two <see cref="Trigger" />s have the same scheduled fire time, then Quartz
        /// will do its best to give the one with the higher priority first access 
        /// to a worker thread.
		/// </summary>
		/// <remarks>
		/// If not explicitly set, the default value is <i>5</i>.
		/// </remarks>
		/// <returns></returns>
		/// <see cref="DefaultPriority" />
		public virtual int Priority
		{
			get { return priority; }
			set { priority = value; }
		}


		/// <summary>
        /// Add the specified name of a <see cref="ITriggerListener" /> to
        /// the end of the <see cref="Trigger" />'s list of listeners.
        /// </summary>
        /// <param name="listenerName">Name of the listener.</param>
		public virtual void AddTriggerListener(string listenerName)
		{
			if (triggerListeners.Contains(listenerName)) 
			{
				throw new ArgumentException(
					string.Format(CultureInfo.InvariantCulture, "Trigger listener '{0}' is already registered for trigger: {1}", listenerName, FullName));
			}

			triggerListeners.Add(listenerName);
		}

		/// <summary>
		/// Remove the specified name of a <see cref="ITriggerListener" />
		/// from the <see cref="Trigger" />'s list of listeners.
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
		/// Called when the <see cref="IScheduler" /> has decided to 'fire'
		/// the trigger (Execute the associated <see cref="IJob" />), in order to
		/// give the <see cref="Trigger" /> a chance to update itself for its next
		/// triggering (if any).
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="JobExecutionException">
		/// </seealso>
		public abstract void Triggered(ICalendar cal);


        /// <summary>
        /// This method should not be used by the Quartz client.
        /// </summary>
        /// <remarks>
        /// <p>
        /// Called by the scheduler at the time a <see cref="Trigger" /> is first
        /// added to the scheduler, in order to have the <see cref="Trigger" />
        /// compute its first fire time, based on any associated calendar.
        /// </p>
        /// 
        /// <p>
        /// After this method has been called, <see cref="GetNextFireTimeUtc" />
        /// should return a valid answer.
        /// </p>
        /// </remarks>
        /// <returns> 
        /// The first time at which the <see cref="Trigger" /> will be fired
        /// by the scheduler, which is also the same value <see cref="GetNextFireTimeUtc" />
        /// will return (until after the first firing of the <see cref="Trigger" />).
        /// </returns>        
		public abstract NullableDateTime ComputeFirstFireTimeUtc(ICalendar cal);

        /// <summary>
        /// This method should not be used by the Quartz client.
        /// </summary>
        /// <remarks>
        /// Called after the <see cref="IScheduler" /> has executed the
        /// <see cref="JobDetail" /> associated with the <see cref="Trigger" />
        /// in order to get the final instruction code from the trigger.
        /// </remarks>
        /// <param name="context">
        /// is the <see cref="JobExecutionContext" /> that was used by the
        /// <see cref="IJob" />'s<see cref="IJob.Execute" /> method.</param>
        /// <param name="result">is the <see cref="JobExecutionException" /> thrown by the
        /// <see cref="IJob" />, if any (may be null).
        /// </param>
        /// <returns>
        /// One of the <see cref="SchedulerInstruction"/> members.
        /// </returns>
        /// <seealso cref="SchedulerInstruction.NoInstruction" />
        /// <seealso cref="SchedulerInstruction.ReExecuteJob" />
        /// <seealso cref="SchedulerInstruction.DeleteTrigger" />
        /// <seealso cref="SchedulerInstruction.SetTriggerComplete" />
        /// <seealso cref="Triggered" />
        public virtual SchedulerInstruction ExecutionComplete(JobExecutionContext context, JobExecutionException result)
        {
            if (result != null && result.RefireImmediately)
            {
                return SchedulerInstruction.ReExecuteJob;
            }

            if (result != null && result.UnscheduleFiringTrigger)
            {
                return SchedulerInstruction.SetTriggerComplete;
            }

            if (result != null && result.UnscheduleAllTriggers)
            {
                return SchedulerInstruction.SetAllJobTriggersComplete;
            }

            if (result != null && !result.RefireImmediately)
                return SchedulerInstruction.NoInstruction;

            if (!GetMayFireAgain())
            {
                return SchedulerInstruction.DeleteTrigger;
            }

            return SchedulerInstruction.NoInstruction;
        }

		/// <summary> 
		/// Used by the <see cref="IScheduler" /> to determine whether or not
		/// it is possible for this <see cref="Trigger" /> to fire again.
		/// <p>
		/// If the returned value is <see langword="false" /> then the <see cref="IScheduler" />
		/// may remove the <see cref="Trigger" /> from the <see cref="IJobStore" />.
		/// </p>
		/// </summary>
		public abstract bool GetMayFireAgain();

        /// <summary>
        /// Returns the next time at which the <see cref="Trigger" /> is scheduled to fire. If
        /// the trigger will not fire again, <see langword="null" /> will be returned.  Note that
        /// the time returned can possibly be in the past, if the time that was computed
        /// for the trigger to next fire has already arrived, but the scheduler has not yet
        /// been able to fire the trigger (which would likely be due to lack of resources
        /// e.g. threads).
        /// </summary>
        ///<remarks>
        /// The value returned is not guaranteed to be valid until after the <see cref="Trigger" />
        /// has been added to the scheduler.
        /// </remarks>
        /// <seealso cref="TriggerUtils.ComputeFireTimesBetween(Trigger, ICalendar , DateTime, DateTime)" />
        /// <returns></returns>
		public abstract NullableDateTime GetNextFireTimeUtc();
		
        /// <summary>
		/// Returns the previous time at which the <see cref="Trigger" /> fired.
		/// If the trigger has not yet fired, <see langword="null" /> will be returned.
		/// </summary>
		public abstract NullableDateTime GetPreviousFireTimeUtc();

		/// <summary>
		/// Returns the next time at which the <see cref="Trigger" /> will fire,
		/// after the given time. If the trigger will not fire after the given time,
		/// <see langword="null" /> will be returned.
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
		/// The implementation should update the <see cref="Trigger" />'s state
		/// based on the MISFIRE_INSTRUCTION_XXX that was selected when the <see cref="Trigger" />
		/// was created.
		/// </p>
		/// </summary>
		public abstract void UpdateAfterMisfire(ICalendar cal);

		/// <summary> 
		/// This method should not be used by the Quartz client.
		/// <p>
		/// The implementation should update the <see cref="Trigger" />'s state
		/// based on the given new version of the associated <see cref="ICalendar" />
		/// (the state should be updated so that it's next fire time is appropriate
		/// given the Calendar's new settings). 
		/// </p>
		/// </summary>
		/// <param name="cal"> </param>
		/// <param name="misfireThreshold"></param>
		public abstract void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold);

		/// <summary>
		/// Validates whether the properties of the <see cref="JobDetail" /> are
		/// valid for submission into a <see cref="IScheduler" />.
		/// </summary>
		public virtual void Validate()
		{
			if (name == null)
			{
				throw new SchedulerException("Trigger's name cannot be null", SchedulerException.ErrorClientError);
			}

			if (group == null)
			{
				throw new SchedulerException("Trigger's group cannot be null", SchedulerException.ErrorClientError);
			}

			if (jobName == null)
			{
				throw new SchedulerException("Trigger's related Job's name cannot be null", SchedulerException.ErrorClientError);
			}

			if (jobGroup == null)
			{
				throw new SchedulerException("Trigger's related Job's group cannot be null", SchedulerException.ErrorClientError);
			}
		}

        /// <summary>
        /// Gets a value indicating whether this instance has additional properties
        /// that should be considered when for example saving to database. 
        /// </summary>
        /// <remarks>
        /// If trigger implementation has additional properties that need to be saved 
        /// with base properties you need to make your class override this property with value true.
        /// Returning true will effectively mean that ADOJobStore needs to serialize 
        /// this trigger instance to make sure additional properties are also saved.
        /// </remarks>
        /// <value>
        /// 	<c>true</c> if this instance has additional properties; otherwise, <c>false</c>.
        /// </value>
	    public virtual bool HasAdditionalProperties
	    {
	        get { return false; }
	    }

		/// <summary>
		/// Return a simple string representation of this object.
		/// </summary>
		public override string ToString()
		{
			return
				string.Format(
                    CultureInfo.InvariantCulture,
					"Trigger '{0}':  triggerClass: '{1} isVolatile: {2} calendar: '{3}' misfireInstruction: {4} nextFireTime: {5}",
					FullName, GetType().FullName, Volatile, CalendarName, MisfireInstruction, GetNextFireTimeUtc());
		}

		/// <summary>
		/// Compare the next fire time of this <see cref="Trigger" /> to that of
		/// another.
		/// </summary>
		public virtual int CompareTo(object obj)
		{
			Trigger other = (Trigger) obj;

            NullableDateTime myTime = GetNextFireTimeUtc();
			NullableDateTime otherTime = other.GetNextFireTimeUtc();

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
            if ((obj == null) || !(obj is Trigger))
		    {
		        return false;
		    }

            Trigger trigger = (Trigger) obj;

            return (trigger.Name == Name) && (trigger.Group == Group);
		}


        /// <summary>
        /// Serves as a hash function for a particular type. <see cref="M:System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"></see>.
        /// </returns>
		public override int GetHashCode()
		{
			return TriggerWrapper.GetTriggerNameKey(Name,Group).GetHashCode();
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
				
				copy.triggerListeners = (ArrayList) triggerListeners.Clone();

				// Shallow copy the jobDataMap.  Note that this means that if a user
				// modifies a value object in this map from the cloned Trigger
				// they will also be modifying this Trigger. 
				if (jobDataMap != null) 
				{
					copy.jobDataMap = (JobDataMap)jobDataMap.Clone();
				}
			}
			catch (Exception ex)
			{
				throw new Exception("Not Cloneable.", ex);
			}
			return copy;
		}

	}

}


