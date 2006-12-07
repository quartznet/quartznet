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

using Nullables;

using Quartz.Core;

namespace Quartz.Spi
{
	/// <summary>
	/// A simple class (structure) used for returning execution-time data from the
	/// JobStore to the <code>QuartzSchedulerThread</code>.
	/// </summary>
	/// <seealso cref="QuartzScheduler" />
	/// <author>James House</author>
	[Serializable]
	public class TriggerFiredBundle
	{
		public virtual JobDetail JobDetail
		{
			get { return job; }
		}

		public virtual Trigger Trigger
		{
			get { return trigger; }
		}

		public virtual ICalendar Calendar
		{
			get { return cal; }
		}

		public virtual bool Recovering
		{
			get { return jobIsRecovering; }
		}

		/// <returns> Returns the fireTime.
		/// </returns>
		public virtual NullableDateTime FireTime
		{
			get { return fireTime; }
		}

		/// <returns> Returns the nextFireTime.
		/// </returns>
		public virtual NullableDateTime NextFireTime
		{
			get { return nextFireTime; }
		}

		/// <returns> Returns the prevFireTime.
		/// </returns>
		public virtual NullableDateTime PrevFireTime
		{
			get { return prevFireTime; }
		}

		/// <returns> Returns the scheduledFireTime.
		/// </returns>
		public virtual NullableDateTime ScheduledFireTime
		{
			get { return scheduledFireTime; }
		}


		private JobDetail job;
		private Trigger trigger;
		private ICalendar cal;
		private bool jobIsRecovering;
		private NullableDateTime fireTime;
		private NullableDateTime scheduledFireTime;
		private NullableDateTime prevFireTime;
		private NullableDateTime nextFireTime;

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		public TriggerFiredBundle(JobDetail job, Trigger trigger, ICalendar cal, bool jobIsRecovering,
		                          NullableDateTime fireTime, NullableDateTime scheduledFireTime, NullableDateTime prevFireTime,
		                          NullableDateTime nextFireTime)
		{
			this.job = job;
			this.trigger = trigger;
			this.cal = cal;
			this.jobIsRecovering = jobIsRecovering;
			this.fireTime = fireTime;
			this.scheduledFireTime = scheduledFireTime;
			this.prevFireTime = prevFireTime;
			this.nextFireTime = nextFireTime;
		}
	}
}