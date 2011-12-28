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

namespace Quartz
{
    /// <summary>
    /// Base class for <see cref="IScheduleBuilder" /> implementors.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class ScheduleBuilder<T> : IScheduleBuilder where T : ITrigger
    {
        /// <summary>
        /// Build the actual Trigger -- NOT intended to be invoked by end users,
        /// but will rather be invoked by a TriggerBuilder which this
        /// ScheduleBuilder is given to.
        /// </summary>
        /// <seealso cref="TriggerBuilder.WithSchedule" />
        public abstract IMutableTrigger Build();
    }
}