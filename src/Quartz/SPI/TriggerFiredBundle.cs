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

using Quartz.Core;
using Quartz.Util;

namespace Quartz.Spi
{
    /// <summary>
    /// A simple class (structure) used for returning execution-time data from the
    /// JobStore to the <see cref="QuartzSchedulerThread" />.
    /// </summary>
    /// <seealso cref="QuartzScheduler" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class TriggerFiredBundle
    {
        private readonly JobDetail job;
        private readonly Trigger trigger;
        private readonly ICalendar cal;
        private readonly bool jobIsRecovering;
        private readonly DateTime? fireTimeUtc;
        private readonly DateTime? scheduledFireTimeUtc;
        private readonly DateTime? prevFireTimeUtc;
        private readonly DateTime? nextFireTimeUtc;

        /// <summary>
        /// Gets the job detail.
        /// </summary>
        /// <value>The job detail.</value>
        public virtual JobDetail JobDetail
        {
            get { return job; }
        }

        /// <summary>
        /// Gets the trigger.
        /// </summary>
        /// <value>The trigger.</value>
        public virtual Trigger Trigger
        {
            get { return trigger; }
        }

        /// <summary>
        /// Gets the calendar.
        /// </summary>
        /// <value>The calendar.</value>
        public virtual ICalendar Calendar
        {
            get { return cal; }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="TriggerFiredBundle"/> is recovering.
        /// </summary>
        /// <value><c>true</c> if recovering; otherwise, <c>false</c>.</value>
        public virtual bool Recovering
        {
            get { return jobIsRecovering; }
        }

        /// <returns> 
        /// Returns the UTC fire time.
        /// </returns>
        public virtual DateTime? FireTimeUtc
        {
            get { return fireTimeUtc; }
        }

        /// <summary>
        /// Gets the next UTC fire time.
        /// </summary>
        /// <value>The next fire time.</value>
        /// <returns> Returns the nextFireTimeUtc.</returns>
        public virtual DateTime? NextFireTimeUtc
        {
            get { return nextFireTimeUtc; }
        }

        /// <summary>
        /// Gets the previous UTC fire time.
        /// </summary>
        /// <value>The previous fire time.</value>
        /// <returns> Returns the previous fire time. </returns>
        public virtual DateTime? PrevFireTimeUtc
        {
            get { return prevFireTimeUtc; }
        }

        /// <returns> 
        /// Returns the scheduled UTC fire time.
        /// </returns>
        public virtual DateTime? ScheduledFireTimeUtc
        {
            get { return scheduledFireTimeUtc; }
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="TriggerFiredBundle"/> class.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="cal">The calendar.</param>
        /// <param name="jobIsRecovering">if set to <c>true</c> [job is recovering].</param>
        /// <param name="fireTimeUtc">The fire time.</param>
        /// <param name="scheduledFireTimeUtc">The scheduled fire time.</param>
        /// <param name="prevFireTimeUtc">The previous fire time.</param>
        /// <param name="nextFireTimeUtc">The next fire time.</param>
        public TriggerFiredBundle(JobDetail job, Trigger trigger, ICalendar cal, bool jobIsRecovering,
                                  DateTime? fireTimeUtc, 
                                  DateTime? scheduledFireTimeUtc,
                                  DateTime? prevFireTimeUtc,
                                  DateTime? nextFireTimeUtc)
        {
            this.job = job;
            this.trigger = trigger;
            this.cal = cal;
            this.jobIsRecovering = jobIsRecovering;
            this.fireTimeUtc = DateTimeUtil.AssumeUniversalTime(fireTimeUtc);
            this.scheduledFireTimeUtc = DateTimeUtil.AssumeUniversalTime(scheduledFireTimeUtc);
            this.prevFireTimeUtc = DateTimeUtil.AssumeUniversalTime(prevFireTimeUtc);
            this.nextFireTimeUtc = DateTimeUtil.AssumeUniversalTime(nextFireTimeUtc);
        }
    }
}