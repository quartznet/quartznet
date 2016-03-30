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
using System.Runtime.Serialization;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Conveys the state of a fired-trigger record.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    [DataContract]
    public class FiredTriggerRecord
    {
        /// <summary>
        /// Gets or sets the fire instance id.
        /// </summary>
        /// <value>The fire instance id.</value>
        [DataMember]
        public virtual string FireInstanceId { get; set; }

        /// <summary>
        /// Gets or sets the fire timestamp.
        /// </summary>
        /// <value>The fire timestamp.</value>
        [DataMember]
        public virtual DateTimeOffset FireTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the scheduled fire timestamp.
        /// </summary>
        [DataMember]
        public virtual DateTimeOffset ScheduleTimestamp { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether job disallows concurrent execution.
        /// </summary>
        [DataMember]
        public virtual bool JobDisallowsConcurrentExecution { get; set; }

        /// <summary>
        /// Gets or sets the job key.
        /// </summary>
        /// <value>The job key.</value>
        [DataMember]
        public virtual JobKey JobKey { get; set; }

        /// <summary>
        /// Gets or sets the scheduler instance id.
        /// </summary>
        /// <value>The scheduler instance id.</value>
        [DataMember]
        public virtual string SchedulerInstanceId { get; set; }

        /// <summary>
        /// Gets or sets the trigger key.
        /// </summary>
        /// <value>The trigger key.</value>
        [DataMember]
        public virtual TriggerKey TriggerKey { get; set; }

        /// <summary>
        /// Gets or sets the state of the fire instance.
        /// </summary>
        /// <value>The state of the fire instance.</value>
        [DataMember]
        public virtual string FireInstanceState { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [job requests recovery].
        /// </summary>
        /// <value><c>true</c> if [job requests recovery]; otherwise, <c>false</c>.</value>
        [DataMember]
        public virtual bool JobRequestsRecovery { get; set; }

        /// <summary>
        /// Gets or sets the priority.
        /// </summary>
        /// <value>The priority.</value>
        [DataMember]
        public virtual int Priority { get; set; }
    }
}