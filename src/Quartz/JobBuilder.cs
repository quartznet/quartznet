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

namespace Quartz
{
    /// <summary>
    /// JobBuilder is used to instantiate <see cref="IJobDetail" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The builder will always try to keep itself in a valid state, with 
    /// reasonable defaults set for calling Build() at any point.  For instance
    /// if you do not invoke <i>WithIdentity(..)</i> a job name will be generated
    /// for you.
    /// </para>
    /// <para>
    /// Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />,
    /// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" />
    /// and the various <see cref="IScheduleBuilder" /> implementations.
    /// </para>
    /// <para>
    /// Client code can then use the DSL to write code such as this:
    /// </para>
    /// <code>
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
    /// </code>
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

        protected JobBuilder()
        {
        }

        /// <summary>
        /// Create a JobBuilder with which to define a <see cref="IJobDetail" />.
        /// </summary>
        /// <returns>a new JobBuilder</returns>
        public static JobBuilder Create()
        {
            return new JobBuilder();
        }

        /// <summary>
        /// Create a JobBuilder with which to define a <see cref="IJobDetail" />,
        /// and set the class name of the job to be executed.
        /// </summary>
        /// <returns>a new JobBuilder</returns>
        public static JobBuilder Create(Type jobType)
        {
            JobBuilder b = new JobBuilder();
            b.OfType(jobType);
            return b;
        }

        /// <summary>
        /// Create a JobBuilder with which to define a <see cref="IJobDetail" />,
        /// and set the class name of the job to be executed.
        /// </summary>
        /// <returns>a new JobBuilder</returns>
        public static JobBuilder Create<T>() where T : IJob
        {
            JobBuilder b = new JobBuilder();
            b.OfType(typeof(T));
            return b;
        }

        /// <summary>
        /// Produce the <see cref="IJobDetail" /> instance defined by this JobBuilder.
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
        /// Use a <see cref="JobKey" /> with the given name and default group to
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
        /// Use a <see cref="JobKey" /> with the given name and group to
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
        /// Use a <see cref="JobKey" /> to identify the JobDetail.
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
        /// Instructs the <see cref="IScheduler" /> whether or not the job
        /// should be re-executed if a 'recovery' or 'fail-over' situation is
        /// encountered.
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </remarks>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.RequestsRecovery" />
        public JobBuilder RequestRecovery()
        {
            this.shouldRecover = true;
            return this;
        }

        /// <summary>
        /// Instructs the <see cref="IScheduler" /> whether or not the job
        /// should be re-executed if a 'recovery' or 'fail-over' situation is
        /// encountered.
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </remarks>
        /// <param name="shouldRecover"></param>
        /// <returns>the updated JobBuilder</returns>
        public JobBuilder RequestRecovery(bool shouldRecover)
        {
            this.shouldRecover = shouldRecover;
            return this;
        }

        /// <summary>
        /// Whether or not the job should remain stored after it is
        /// orphaned (no <see cref="ITrigger" />s point to it).
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <see langword="false" />
        /// - this method sets the value to <code>true</code>.
        /// </remarks>
        /// <returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.Durable" />
        public JobBuilder StoreDurably()
        {
            this.durability = true;
            return this;
        }

        /// <summary>
        /// Whether or not the job should remain stored after it is
        /// orphaned (no <see cref="ITrigger" />s point to it).
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <see langword="false" />.
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
        /// Add all the data from the given <see cref="JobDataMap" /> to the 
        /// <see cref="IJobDetail" />'s <see cref="JobDataMap" />.
        /// </summary>
        ///<returns>the updated JobBuilder</returns>
        /// <seealso cref="IJobDetail.JobDataMap" />
        public JobBuilder UsingJobData(JobDataMap newJobDataMap)
        {
            jobDataMap.PutAll(newJobDataMap);
            return this;
        }

        /// <summary>
        /// Replace the <see cref="IJobDetail" />'s <see cref="JobDataMap" /> with the
        /// given <see cref="JobDataMap" />.
        /// </summary>
        /// <param name="newJobDataMap"></param>
        /// <returns></returns>
        public JobBuilder SetJobData(JobDataMap newJobDataMap)
        {
            jobDataMap = newJobDataMap;
            return this;
        }
    }
}