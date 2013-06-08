#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Conveys the state of a fired-trigger record.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class FiredTriggerRecord
    {
        /// <summary>
        /// Gets or sets the fire instance id.
        /// </summary>
        /// <value>The fire instance id.</value>
        public virtual string FireInstanceId { get; set; }

        /// <summary>
        /// Gets or sets the fire timestamp.
        /// </summary>
        /// <value>The fire timestamp.</value>
        public virtual DateTimeOffset FireTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the scheduled fire timestamp.
        /// </summary>
        public virtual DateTimeOffset ScheduleTimestamp { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether job disallows concurrent execution.
        /// </summary>
        public virtual bool JobDisallowsConcurrentExecution { get; set; }

        /// <summary>
        /// Gets or sets the job key.
        /// </summary>
        /// <value>The job key.</value>
        public virtual JobKey JobKey { get; set; }

        /// <summary>
        /// Gets or sets the scheduler instance id.
        /// </summary>
        /// <value>The scheduler instance id.</value>
        public virtual string SchedulerInstanceId { get; set; }

        /// <summary>
        /// Gets or sets the trigger key.
        /// </summary>
        /// <value>The trigger key.</value>
        public virtual TriggerKey TriggerKey { get; set; }

        /// <summary>
        /// Gets or sets the state of the fire instance.
        /// </summary>
        /// <value>The state of the fire instance.</value>
        public virtual string FireInstanceState { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [job requests recovery].
        /// </summary>
        /// <value><c>true</c> if [job requests recovery]; otherwise, <c>false</c>.</value>
        public virtual bool JobRequestsRecovery { get; set; }

        /// <summary>
        /// Gets or sets the priority.
        /// </summary>
        /// <value>The priority.</value>
        public virtual int Priority { get; set; }
    }
}