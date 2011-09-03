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

using Quartz.Impl;
using Quartz.Job;
using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// <code>JobBuilder</code> is used to instantiate <see cref="IJobDetail" />s.
    /// </summary>
    /// <remarks>
    /// <para>Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <code>TriggerBuilder</code>, <code>JobBuilder</code>, 
    /// <code>DateBuilder</code>, <code>JobKey</code>, <code>TriggerKey</code> 
    /// and the various <code>ScheduleBuilder</code> implementations.</para>
    /// 
    /// <para>Client code can then use the DSL to write code such as this:</para>
    /// <pre>
    ///         IJobDetail job = JobBuilder.Create&lt;MyJob&gt;()
    ///             .WithIdentity("myJob")
    ///             .Build();
    ///             
    ///         ITrigger trigger = TriggerBuilder.Create() 
    ///             .WithIdentity("myTrigger", "myTriggerGroup")
    ///             .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
    ///             .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
    ///             .Build();
    ///         
    ///         scheduler.scheduleJob(job, trigger);
    /// </pre>
    /// </remarks>
    /// <seealso cref="TriggerBuilder" />
    /// <seealso cref="DateBuilder" />
    /// <seealso cref="IJobDetail" />
    public class JobBuilder
    {
        private JobKey key;
        private string description;
        private Type jobType = typeof (NoOpJob);
        private bool durability;
        private bool shouldRecover;

        private JobDataMap jobDataMap = new JobDataMap();

        private JobBuilder()
        {
        }

        /// <summary>
        /// Create a JobBuilder with which to define a <code>JobDetail</code>.
        /// </summary>
        /// <returns>a new JobBuilder</returns>
        public static JobBuilder Create()
        {
            return new JobBuilder();
        }

        /// <summary>
        /// Create a JobBuilder with which to define a <code>JobDetail</code>,
        /// and set the class name of the <code>Job</code> to be executed.
        /// </summary>
        /// <returns>a new JobBuilder</returns>
        public static JobBuilder Create(Type jobType)
        {
            JobBuilder b = new JobBuilder();
            b.OfType(jobType);
            return b;
        }

        /// <summary>
        /// Create a JobBuilder with which to define a <code>JobDetail</code>,
        /// and set the class name of the <code>Job</code> to be executed.
        /// </summary>
        /// <returns>a new JobBuilder</returns>
        public static JobBuilder Create<T>() where T : IJob
        {
            JobBuilder b = new JobBuilder();
            b.OfType(typeof(T));
            return b;
        }

        /// <summary>
        /// Produce the <code>JobDetail</code> instance defined by this 
        /// <code>JobBuilder</code>.
        /// </summary>
        /// <returns>the defined JobDetail.</returns>
        public IJobDetail Build()
        {
            JobDetailImpl job = new JobDetailImpl();

            job.JobType = jobType;
            job.Description = description;
            if (key == null)
            {
                key = new JobKey(Guid.NewGuid().ToString(), null);
            }
            job.Key = key;
            job.Durable = durability;
            job.RequestsRecovery = shouldRecover;


            if (!jobDataMap.IsEmpty)
            {
                job.JobDataMap = jobDataMap;
            }

            return job;
        }

        /// <summary>
        /// Use a <code>JobKey</code> with the given name and default group to
        /// identify the JobDetail.
        /// </summary>
        /// <remarks>
        /// <para>If none of the 'withIdentity' methods are set on the JobBuilder,
        /// then a random, unique JobKey will be generated.</para>
        /// </remarks>
        /// <param name="name">the name element for the Job's JobKey</param>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="JobKey" /> 
        /// <seealso cref="IJobDetail.Key" />
        public JobBuilder WithIdentity(string name)
        {
            key = new JobKey(name, null);
            return this;
        }

        /// <summary>
        /// Use a <code>JobKey</code> with the given name and group to
        /// identify the JobDetail.
        /// </summary>
        /// <remarks>
        /// <para>If none of the 'withIdentity' methods are set on the JobBuilder,
        /// then a random, unique JobKey will be generated.</para>
        /// </remarks>
        /// <param name="name">the name element for the Job's JobKey</param>
        /// <param name="group"> the group element for the Job's JobKey</param>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="JobKey" />
        /// <seealso cref="IJobDetail.Key" />
        public JobBuilder WithIdentity(string name, string group)
        {
            key = new JobKey(name, group);
            return this;
        }

        /// <summary>
        /// Use a <code>JobKey</code> to identify the JobDetail.
        /// </summary>
        /// <remarks>
        /// <para>If none of the 'withIdentity' methods are set on the JobBuilder,
        /// then a random, unique JobKey will be generated.</para>
        /// </remarks>
        /// <param name="key">the Job's JobKey</param>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="JobKey" />
        /// <seealso cref="IJobDetail.Key" />
        public JobBuilder WithIdentity(JobKey key)
        {
            this.key = key;
            return this;
        }

        /// <summary>
        /// Set the given (human-meaningful) description of the Job.
        /// </summary>
        /// <param name="description"> the description for the Job</param>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.Description" />
        public JobBuilder WithDescription(string description)
        {
            this.description = description;
            return this;
        }

        /// <summary>
        /// Set the class which will be instantiated and executed when a
        /// Trigger fires that is associated with this JobDetail.
        /// </summary>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobType" />
        public JobBuilder OfType<T>()
        {
            return OfType(typeof(T));
        }

        /// <summary>
        /// Set the class which will be instantiated and executed when a
        /// Trigger fires that is associated with this JobDetail.
        /// </summary>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobType" />
        public JobBuilder OfType(Type type)
        {
            jobType = type;
            return this;
        }

        /// <summary>
        /// Instructs the <code>Scheduler</code> whether or not the <code>Job</code>
        /// should be re-executed if a 'recovery' or 'fail-over' situation is
        /// encountered.
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <code>false</code>.
        /// </remarks>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.RequestsRecovery" />
        public JobBuilder RequestRecovery()
        {
            this.shouldRecover = true;
            return this;
        }

        /// <summary>
        /// Instructs the <code>Scheduler</code> whether or not the <code>Job</code>
        /// should be re-executed if a 'recovery' or 'fail-over' situation is
        /// encountered.
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <code>false</code>.
        /// </remarks>
        /// <param name="shouldRecover"></param>
        /// <returns>the updated JobBuilder</returns>
        public JobBuilder RequestRecovery(bool shouldRecover)
        {
            this.shouldRecover = shouldRecover;
            return this;
        }

        /// <summary>
        /// Whether or not the <code>Job</code> should remain stored after it is
        /// orphaned (no <code><see cref="ITrigger" />s</code> point to it).
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <code>false</code>.
        /// </remarks>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.Durable" />
        public JobBuilder StoreDurably()
        {
            this.durability = true;
            return this;
        }

        /// <summary>
        /// Whether or not the <code>Job</code> should remain stored after it is
        /// orphaned (no <code><see cref="ITrigger" />s</code> point to it).
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <code>false</code>.
        /// </remarks>
        /// <param name="durability">the value to set for the durability property.</param>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.Durable" />
        public JobBuilder StoreDurably(bool durability)
        {
            this.durability = durability;
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(string key, string value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(string key, int value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(string key, long value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(string key, float value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(string key, double value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(string key, bool value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Set the JobDetail's <see cref="JobDataMap" />, adding any values to it
        /// that were already set on this JobBuilder using any of the
        /// other 'usingJobData' methods. 
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(JobDataMap newJobDataMap)
        {
            // add any existing data to this new map
            foreach (string key in jobDataMap.KeySet())
            {
                newJobDataMap.Put(key, jobDataMap.Get(key));
            }
            jobDataMap = newJobDataMap; // set new map as the map to use
            return this;
        }
    }
}