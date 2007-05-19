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

using Quartz.Spi;

namespace Quartz
{
	/// <summary> <p>
	/// A context bundle containing handles to various environment information, that
	/// is given to a <code>{@link org.quartz.JobDetail}</code> instance as it is
	/// executed, and to a <code>{@link Trigger}</code> instance after the
	/// execution completes.
	/// </p>
	/// 
	/// <p>
	/// The <code>JobDataMap</code> found on this object (via the 
	/// <code>getMergedJobDataMap()</code> method) serves as a convenience -
	/// it is a merge of the <code>JobDataMap</code> found on the 
	/// <code>JobDetail</code> and the one found on the <code>Trigger</code>, with 
	/// the value in the latter overriding any same-named values in the former.
	/// <i>It is thus considered a 'best practice' that the Execute code of a Job
	/// retrieve data from the JobDataMap found on this object</i>  NOTE: Do not
	/// expect value 'set' into this JobDataMap to somehow be set back onto a
	/// <code>StatefulJob</code>'s own JobDataMap.
	/// </p>
	/// 
	/// <p>
	/// <code>JobExecutionContext</code> s are also returned from the 
	/// <code>Scheduler.getCurrentlyExecutingJobs()</code>
	/// method. These are the same instances as those past into the jobs that are
	/// currently executing within the scheduler. The exception to this is when your
	/// application is using Quartz remotely (i.e. via RMI) - in which case you get
	/// a clone of the <code>JobExecutionContext</code>s, and their references to
	/// the <code>Scheduler</code> and <code>Job</code> instances have been lost (a
	/// clone of the <code>JobDetail</code> is still available - just not a handle
	/// to the job instance that is running).
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="JobDetail" /> 
	/// <seealso cref="IScheduler" />
	/// <seealso cref="IJob" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="JobDataMap" />
	/// <author>James House</author>
	[Serializable]
	public class JobExecutionContext
	{
		/// <summary>
		/// Get a handle to the <code>Scheduler</code> instance that fired the
		/// <code>Job</code>.
		/// </summary>
		public virtual IScheduler Scheduler
		{
			get { return scheduler; }
		}

		/// <summary> <p>
		/// Get a handle to the <code>Trigger</code> instance that fired the
		/// <code>Job</code>.
		/// </p>
		/// </summary>
		public virtual Trigger Trigger
		{
			get { return trigger; }
		}

		/// <summary> <p>
		/// Get a handle to the <code>Calendar</code> referenced by the <code>Trigger</code>
		/// instance that fired the <code>Job</code>.
		/// </p>
		/// </summary>
		public virtual ICalendar Calendar
		{
			get { return calendar; }
		}

		/// <summary> <p>
		/// If the <code>Job</code> is being re-executed because of a 'recovery'
		/// situation, this method will return <code>true</code>.
		/// </p>
		/// </summary>
		public virtual bool Recovering
		{
			get { return recovering; }
		}

        /// <summary>
        /// Gets the refire count.
        /// </summary>
        /// <value>The refire count.</value>
		public virtual int RefireCount
		{
			get { return numRefires; }
		}

		/// <summary>
		/// Get the convenience <code>JobDataMap</code> of this execution context.
		/// <p>
		/// The <code>JobDataMap</code> found on this object serves as a convenience -
		/// it is a merge of the <code>JobDataMap</code> found on the 
		/// <code>JobDetail</code> and the one found on the <code>Trigger</code>, with 
		/// the value in the latter overriding any same-named values in the former.
		/// <i>It is thus considered a 'best practice' that the Execute code of a Job
		/// retrieve data from the JobDataMap found on this object</i>
		/// </p>
		/// 
		/// <p>NOTE: Do not expect value 'set' into this JobDataMap to somehow be 
		/// set back onto a <code>StatefulJob</code>'s own JobDataMap.
		/// </p>
		/// 
		/// <p>
		/// Attempts to change the contents of this map typically result in an 
		/// <code>IllegalStateException</code>.
		/// </p>
		/// 
		/// </summary>
		public virtual JobDataMap MergedJobDataMap
		{
			get { return jobDataMap; }
		}

		/// <summary> <p>
		/// Get the <code>JobDetail</code> associated with the <code>Job</code>.
		/// </p>
		/// </summary>
		public virtual JobDetail JobDetail
		{
			get { return jobDetail; }
		}

		/// <summary> <p>
		/// Get the instance of the <code>Job</code> that was created for this
		/// execution.
		/// </p>
		/// 
		/// <p>
		/// Note: The Job instance is not available through remote scheduler
		/// interfaces.
		/// </p>
		/// </summary>
		public virtual IJob JobInstance
		{
			get { return job; }
		}

		/// <summary> The actual time the trigger fired. For instance the scheduled time may
		/// have been 10:00:00 but the actual fire time may have been 10:00:03 if
		/// the scheduler was too busy.
		/// 
		/// </summary>
		/// <returns> Returns the fireTime.
		/// </returns>
		/// <seealso cref="ScheduledFireTime">
		/// </seealso>
		public NullableDateTime FireTime
		{
			get { return fireTime; }
		}

		/// <summary> The scheduled time the trigger fired for. For instance the scheduled
		/// time may have been 10:00:00 but the actual fire time may have been
		/// 10:00:03 if the scheduler was too busy.
		/// 
		/// </summary>
		/// <returns> Returns the scheduledFireTime.
		/// </returns>
		/// <seealso cref="FireTime">
		/// </seealso>
		public NullableDateTime ScheduledFireTime
		{
			get { return scheduledFireTime; }
		}

		/// <summary>
		/// Gets the previous fire time.
		/// </summary>
		/// <value>The previous fire time.</value>
		public NullableDateTime PreviousFireTime
		{
			get { return prevFireTime; }
		}

		/// <summary>
		/// Gets the next fire time.
		/// </summary>
		/// <value>The next fire time.</value>
		public NullableDateTime NextFireTime
		{
			get { return nextFireTime; }
		}

		/// <summary>
		/// Returns the result (if any) that the <code>Job</code> set before its 
		/// execution completed (the type of object set as the result is entirely up 
		/// to the particular job).
		/// 
		/// <p>
		/// The result itself is meaningless to Quartz, but may be informative
		/// to <code>{@link JobListener}s</code> or 
		/// <code>{@link TriggerListener}s</code> that are watching the job's 
		/// execution.
		/// </p> 
		/// 
		/// Set the result (if any) of the <code>Job</code>'s execution (the type of 
		/// object set as the result is entirely up to the particular job).
		/// 
		/// <p>
		/// The result itself is meaningless to Quartz, but may be informative
		/// to <code>{@link JobListener}s</code> or 
		/// <code>{@link TriggerListener}s</code> that are watching the job's 
		/// execution.
		/// </p> 
		/// 
		/// </summary>
		public virtual object Result
		{
			get { return result; }

			set { result = value; }
		}

		/// <summary> The amount of time the job ran for (in milliseconds).  The returned 
		/// value will be -1 until the job has actually completed (or thrown an 
		/// exception), and is therefore generally only useful to 
		/// <code>JobListener</code>s and <code>TriggerListener</code>s.
		/// </summary>
		public virtual long JobRunTime
		{
			get { return jobRunTime; }
			set { jobRunTime = value; }
		}

		[NonSerialized] private IScheduler scheduler;
		private Trigger trigger;
		private JobDetail jobDetail;
		private JobDataMap jobDataMap;
		[NonSerialized] private IJob job;

		private ICalendar calendar;
		private bool recovering = false;
		private int numRefires = 0;
		private NullableDateTime fireTime;
		private NullableDateTime scheduledFireTime;
		private NullableDateTime prevFireTime;
		private NullableDateTime nextFireTime;
		private long jobRunTime = - 1;
		private object result;

		private IDictionary data = new Hashtable();

		/// <summary> <p>
		/// Create a JobExcecutionContext with the given context data.
		/// </p>
		/// </summary>
		public JobExecutionContext(IScheduler scheduler, TriggerFiredBundle firedBundle, IJob job)
		{
			this.scheduler = scheduler;
			trigger = firedBundle.Trigger;
			calendar = firedBundle.Calendar;
			jobDetail = firedBundle.JobDetail;
			this.job = job;
			recovering = firedBundle.Recovering;
			fireTime = firedBundle.FireTime;
			scheduledFireTime = firedBundle.ScheduledFireTime;
			prevFireTime = firedBundle.PrevFireTime;
			nextFireTime = firedBundle.NextFireTime;

			jobDataMap = new JobDataMap();
			jobDataMap.PutAll(jobDetail.JobDataMap);
			jobDataMap.PutAll(trigger.JobDataMap);

			jobDataMap.Mutable = false;
			trigger.JobDataMap.Mutable = false;
		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary>
		/// Increments the refire count.
		/// </summary>
		public virtual void IncrementRefireCount()
		{
			numRefires++;
		}

		/// <summary>
		/// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
		/// </returns>
		public override string ToString()
		{
			return
				"JobExecutionContext:" + " trigger: '" + Trigger.FullName + " job: " + JobDetail.FullName + " fireTime: '" +
				FireTime.Value.ToString("r") + " scheduledFireTime: " + ScheduledFireTime.Value.ToString("r") +
				" previousFireTime: '" +
				PreviousFireTime.Value.ToString("r") + " nextFireTime: " + NextFireTime.Value.ToString("r") + " isRecovering: " +
				Recovering +
				" refireCount: " + RefireCount;
		}

		/// <summary> 
		/// Put the specified value into the context's data map with the given key.
		/// Possibly useful for sharing data between listeners and jobs.
		/// <p>
		/// NOTE: this data is volatile - it is lost after the job execution
		/// completes, and all TriggerListeners and JobListeners have been 
		/// notified.
		/// </p> 
		/// </summary>
		/// <param name="key">
		/// </param>
		/// <param name="objectValue">
		/// </param>
		public virtual void Put(object key, object objectValue)
		{
			data[key] = objectValue;
		}

		/// <summary> 
		/// Get the value with the given key from the context's data map.
		/// </summary>
		/// <param name="key">
		/// </param>
		public virtual object Get(object key)
		{
			return data[key];
		}
	}
}