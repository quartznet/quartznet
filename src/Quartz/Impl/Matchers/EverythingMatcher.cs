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

using Quartz.Util;

namespace Quartz.Impl.Matchers
{
    /// <summary>
    /// Matches on the complete key being equal (both name and group).
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <author>jhouse</author>
    public class EverythingMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
    {
        protected EverythingMatcher()
        {
        }

        /// <summary>
        /// Create an EverythingMatcher that matches all jobs.
        /// </summary>
        /// <returns></returns>
        public static EverythingMatcher<JobKey> AllJobs()
        {
            return new EverythingMatcher<JobKey>();
        }

        /// <summary>
        /// Create an EverythingMatcher that matches all triggers.
        /// </summary>
        /// <returns></returns>
        public static EverythingMatcher<TriggerKey> AllTriggers()
        {
            return new EverythingMatcher<TriggerKey>();
        }

        public bool IsMatch(TKey key)
        {
            return true;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            return obj.GetType().Equals(GetType());
        }

        public override int GetHashCode()
        {
            return GetType().Name.GetHashCode();
        }
    }
}