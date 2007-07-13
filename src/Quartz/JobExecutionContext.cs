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
using Nullables;

using Quartz.Spi;

namespace Quartz
{
	/// <summary>
	/// A context bundle containing handles to various environment information, that
	/// is given to a <see cref="JobDetail" /> instance as it is
	/// executed, and to a <see cref="Trigger" /> instance after the
	/// execution completes.
	/// </summary>
	/// <remarks>
	/// <p>
	/// The <see cref="JobDataMap" /> found on this object (via the 
	/// <see cref="MergedJobDataMap" /> method) serves as a convenience -
	/// it is a merge of the <see cref="JobDataMap" /> found on the 
	/// <see cref="JobDetail" /> and the one found on the <see cref="Trigger" />, with 
	/// the value in the latter overriding any same-named values in the former.
	/// <i>It is thus considered a 'best practice' that the Execute code of a Job
	/// retrieve data from the JobDataMap found on this object</i>  NOTE: Do not
	/// expect value 'set' into this JobDataMap to somehow be set back onto a
	/// <see cref="IStatefulJob" />'s own JobDataMap.
	/// </p>
	/// 
	/// <p>
	/// <see cref="JobExecutionContext" /> s are also returned from the 
	/// <see cref="IScheduler.GetCurrentlyExecutingJobs()" />
	/// method. These are the same instances as those past into the jobs that are
	/// currently executing within the scheduler. The exception to this is when your
	/// application is using Quartz remotely (i.e. via RMI) - in which case you get
	/// a clone of the <see cref="JobExecutionContext" />s, and their references to
	/// the <see cref="IScheduler" /> and <see cref="IJob" /> instances have been lost (a
	/// clone of the <see cref="JobDetail" /> is still available - just not a handle
	/// to the job instance that is running).
	/// </p>
    /// </remarks>
	/// <seealso cref="JobDetail" /> 
	/// <seealso cref="IScheduler" />
	/// <seealso cref="IJob" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="JobDataMap" />
	/// <author>James House</author>
	[Serializable]
	public class JobExecutionContext
	{
        [NonSerialized]
        private readonly IScheduler scheduler;
        private readonly Trigger trigger;
        private readonly JobDetail jobDetail;
        private readonly JobDataMap jobDataMap;
        [NonSerialized]
        private readonly IJob job;

        private readonly ICalendar calendar;
        private readonly bool recovering = false;
        private int numRefires = 0;
        private readonly NullableDateTime fireTime;
        private readonly NullableDateTime scheduledFireTime;
        private readonly NullableDateTime prevFireTime;
        private readonly NullableDateTime nextFireTime;
        private long jobRunTime = -1;
        private object result;

        private readonly IDictionary data = new Hashtable();

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
        }

		/// <summary>
		/// Get a handle to the <see cref="IScheduler" /> instance that fired the
		/// <see cref="IJob" />.
		/// </summary>
		public virtual IScheduler Scheduler
		{
			get { return scheduler; }
		}

		/// <summary>
		/// Get a handle to the <see cref="Trigger" /> instance that fired the
		/// <see cref="IJob" />.
		/// </summary>
		public virtual Trigger Trigger
		{
			get { return trigger; }
		}

		/// <summary>
		/// Get a handle to the <see cref="ICalendar" /> referenced by the <see cref="Trigger" />
		/// instance that fired the <see cref="IJob" />.
		/// </summary>
		public virtual ICalendar Calendar
		{
			get { return calendar; }
		}

		/// <summary>
		/// If the <see cref="IJob" /> is being re-executed because of a 'recovery'
		/// situation, this method will return <see langword="true" />.
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
		/// Get the convenience <see cref="JobDataMap" /> of this execution context.
		/// </summary>
		/// <remarks>
		/// <p>
		/// The <see cref="JobDataMap" /> found on this object serves as a convenience -
		/// it is a merge of the <see cref="JobDataMap" /> found on the 
		/// <see cref="JobDetail" /> and the one found on the <see cref="Trigger" />, with 
		/// the value in the latter overriding any same-named values in the former.
		/// <i>It is thus considered a 'best practice' that the Execute code of a Job
		/// retrieve data from the JobDataMap found on this object</i>
		/// </p>
		/// 
		/// <p>NOTE: Do not expect value 'set' into this JobDataMap to somehow be 
		/// set back onto a <see cref="IStatefulJob" />'s own JobDataMap.
		/// </p>
		/// 
		/// <p>
		/// Attempts to change the contents of this map typically result in an 
		/// illegal state.
		/// </p>
		/// 
        /// </remarks>
		public virtual JobDataMap MergedJobDataMap
		{
			get { return jobDataMap; }
		}

		/// <summary>
		/// Get the <see cref="JobDetail" /> associated with the <see cref="IJob" />.
		/// </summary>
		public virtual JobDetail JobDetail
		{
			get { return jobDetail; }
		}

		/// <summary>
		/// Get the instance of the <see cref="IJob" /> that was created for this
		/// execution.
		/// <p>
		/// Note: The Job instance is not available through remote scheduler
		/// interfaces.
		/// </p>
		/// </summary>
		public virtual IJob JobInstance
		{
			get { return job; }
		}

		/// <summary>
		/// The actual time the trigger fired. For instance the scheduled time may
		/// have been 10:00:00 but the actual fire time may have been 10:00:03 if
		/// the scheduler was too busy.
		/// </summary>
		/// <returns> Returns the fireTime.</returns>
		/// <seealso cref="ScheduledFireTime" />
		public NullableDateTime FireTime
		{
			get { return fireTime; }
		}

		/// <summary> 
		/// The scheduled time the trigger fired for. For instance the scheduled
		/// time may have been 10:00:00 but the actual fire time may have been
		/// 10:00:03 if the scheduler was too busy.
		/// </summary>
		/// <returns> Returns the scheduledFireTime.</returns>
		/// <seealso cref="FireTime" />
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
		/// Returns the result (if any) that the <see cref="IJob" /> set before its 
		/// execution completed (the type of object set as the result is entirely up 
		/// to the particular job).
		/// </summary>
		/// <remarks>
		/// <p>
		/// The result itself is meaningless to Quartz, but may be informative
		/// to <see cref="IJobListener" />s or 
		/// <see cref="ITriggerListener" />s that are watching the job's 
		/// execution.
		/// </p> 
		/// 
		/// Set the result (if any) of the <see cref="IJob" />'s execution (the type of 
		/// object set as the result is entirely up to the particular job).
		/// 
		/// <p>
		/// The result itself is meaningless to Quartz, but may be informative
		/// to <see cref="IJobListener" />s or 
		/// <see cref="ITriggerListener" />s that are watching the job's 
		/// execution.
		/// </p> 
        /// </remarks>
		public virtual object Result
		{
			get { return result; }
			set { result = value; }
		}

		/// <summary> 
		/// The amount of time the job ran for (in milliseconds).  The returned 
		/// value will be -1 until the job has actually completed (or thrown an 
		/// exception), and is therefore generally only useful to 
		/// <see cref="IJobListener" />s and <see cref="ITriggerListener" />s.
		/// </summary>
		public virtual long JobRunTime
		{
			get { return jobRunTime; }
			set { jobRunTime = value; }
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
				string.Format("JobExecutionContext: trigger: '{0} job: {1} fireTime: '{2} scheduledFireTime: {3} previousFireTime: '{4} nextFireTime: {5} recovering: {6} refireCount: {7}", Trigger.FullName, JobDetail.FullName, FireTime.Value.ToString("r"), ScheduledFireTime.Value.ToString("r"), PreviousFireTime.Value.ToString("r"), NextFireTime.Value.ToString("r"), Recovering, RefireCount);
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
