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
    /// Object representing a job or trigger key.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class TriggerStatus
    {
        // TODO: Repackage under spi or root pkg ?, put status constants here.

        private readonly string status;

        private readonly DateTimeOffset? nextFireTime;

        /// <summary>
        /// Construct a new TriggerStatus with the status name and nextFireTime.
        /// </summary>
        /// <param name="status">The trigger's status</param>
        /// <param name="nextFireTime">The next time trigger will fire</param>
        public TriggerStatus(string status, DateTimeOffset? nextFireTime)
        {
            this.status = status;
            this.nextFireTime = nextFireTime;
        }

        public JobKey JobKey { get; set; }

        public TriggerKey Key { get; set; }

        public string Status
        {
            get { return status; }
        }

        public DateTimeOffset? NextFireTimeUtc
        {
            get { return nextFireTime; }
        }

        /// <summary>
        /// Return the string representation of the TriggerStatus.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "status: " + Status + ", next Fire = " + NextFireTimeUtc;
        }
    }
}