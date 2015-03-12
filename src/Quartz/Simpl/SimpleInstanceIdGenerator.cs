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

using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary> 
    /// The default InstanceIdGenerator used by Quartz when instance id is to be
    /// automatically generated.  Instance id is of the form HOSTNAME + CURRENT_TIME.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    /// <seealso cref="IInstanceIdGenerator" />
    /// <seealso cref="HostnameInstanceIdGenerator" />
    public class SimpleInstanceIdGenerator : HostNameBasedIdGenerator
    {
        // assume ticks to be at most 20 chars long
        private const int HostNameMaxLength = IdMaxLength - 20;

        /// <summary>
        /// Generate the instance id for a <see cref="IScheduler" />
        /// </summary>
        /// <returns>The clusterwide unique instance id.</returns>
        public override string GenerateInstanceId()
        {
            string hostName = GetHostName(HostNameMaxLength);
            return hostName + SystemTime.UtcNow().Ticks;
        }
    }
}