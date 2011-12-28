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
using System;

namespace Quartz
{
    /// <summary>
    /// Uniquely identifies a <see cref="IJobDetail" />.
    /// </summary>
    /// <remarks>
    /// <para>Keys are composed of both a name and group, and the name must be unique
    /// within the group.  If only a group is specified then the default group
    /// name will be used.</para> 
    /// 
    /// <para>Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />, 
    /// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" /> 
    /// and the various <see cref="IScheduleBuilder" /> implementations.</para>
    /// 
    /// <para>Client code can then use the DSL to write code such as this:</para>
    /// <code>
    /// IJobDetail job = JobBuilder.Create&lt;MyJob>()
    ///     .WithIdentity("myJob")
    ///     .Build();
    /// ITrigger trigger = TriggerBuilder.Create()
    ///     .WithIdentity("myTrigger", "myTriggerGroup")
    ///     .WithSimpleSchedule(x => x
    ///         .WithIntervalInHours(1)
    ///         .RepeatForever())
    ///     .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
    ///     .Build();
    /// scheduler.scheduleJob(job, trigger);
    /// </code>
    /// </remarks>
    /// <seealso cref="IJob"/>
    /// <seealso cref="Key{T}.DefaultGroup" />
    [Serializable]
    public sealed class JobKey : Key<JobKey>
    {
        public JobKey(string name) : base(name, null)
        {
        }

        public JobKey(string name, string group) : base(name, group)
        {
        }

        public static JobKey Create(string name)
        {
            return new JobKey(name, null);
        }

        public static JobKey Create(string name, string group)
        {
            return new JobKey(name, group);
        }
    }
}