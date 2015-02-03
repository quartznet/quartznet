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

using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// Scheduler constants.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public struct SchedulerConstants
    {
        /// <summary>
        /// A (possibly) useful constant that can be used for specifying the group
        /// that <see cref="IJob" /> and <see cref="ITrigger" /> instances belong to.
        /// </summary>
        public const string DefaultGroup = Key<string>.DefaultGroup;

        /// <summary>
        /// A constant <see cref="ITrigger" /> group name used internally by the
        /// scheduler - clients should not use the value of this constant
        /// ("RECOVERING_JOBS") for the name of a <see cref="ITrigger" />'s group.
        /// </summary>
        public const string DefaultRecoveryGroup = "RECOVERING_JOBS";

        /// <summary>
        /// A constant <see cref="ITrigger" /> group name used internally by the
        /// scheduler - clients should not use the value of this constant
        /// ("FAILED_OVER_JOBS") for the name of a <see cref="ITrigger" />'s group.
        /// </summary>
        public const string DefaultFailOverGroup = "FAILED_OVER_JOBS";

        /// <summary>
        ///  A constant <see cref="JobDataMap" /> key that can be used to retrieve the
        /// name of the original <see cref="ITrigger" /> from a recovery trigger's
        /// data map in the case of a job recovering after a failed scheduler
        /// instance.
        /// </summary>
        /// <seealso cref="IJobDetail.RequestsRecovery" />
        public const string FailedJobOriginalTriggerName = "QRTZ_FAILED_JOB_ORIG_TRIGGER_NAME";

        /// <summary>
        /// A constant <see cref="JobDataMap" /> key that can be used to retrieve the
        /// group of the original <see cref="ITrigger" /> from a recovery trigger's
        /// data map in the case of a job recovering after a failed scheduler
        /// instance.
        /// </summary>
        /// <seealso cref="IJobDetail.RequestsRecovery" />
        public const string FailedJobOriginalTriggerGroup = "QRTZ_FAILED_JOB_ORIG_TRIGGER_GROUP";

        /// <summary>
        /// A constant <see cref="JobDataMap" /> key that can be used to retrieve the
        /// fire time of the original <see cref="ITrigger" /> from a recovery
        /// trigger's data map in the case of a job recovering after a failed scheduler
        /// instance.
        /// </summary>
        /// <remarks>
        /// Note that this is the time the original firing actually occurred,
        /// which may be different from the scheduled fire time - as a trigger doesn't
        /// always fire exactly on time.
        /// </remarks>
        /// <seealso cref="IJobDetail.RequestsRecovery" />
        public const string FailedJobOriginalTriggerFiretime = "QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_AS_STRING";

        /// <summary>
        /// A constant <code>JobDataMap</code> key that can be used to retrieve the scheduled
        /// fire time of the original <code>Trigger</code> from a recovery  trigger's data
        /// map in the case of a job recovering after a failed scheduler instance.  
        /// </summary>
        /// <remarks>
        /// Note that this is the time the original firing was scheduled for, which may
        /// be different from the actual firing time - as a trigger doesn't always fire exactly on time.
        /// </remarks>
        public const string FailedJobOriginalTriggerScheduledFiretime = "QRTZ_FAILED_JOB_ORIG_TRIGGER_SCHEDULED_FIRETIME_AS_STRING";

        /// <summary>
        /// A special date time to check against when signaling scheduling change when the signaled fire date suggestion is actually irrelevant.
        /// We only want to signal the change.
        /// </summary>
        internal static DateTimeOffset? SchedulingSignalDateTime = new DateTimeOffset(1982, 6, 28, 0, 0, 0, TimeSpan.FromSeconds(0));
    }
}