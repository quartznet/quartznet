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

using Quartz.Spi;
using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// <code>TriggerBuilder</code> is used to instantiate {@link Trigger}s.
    /// </summary>
    /// <remarks>
    /// <p>Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <code>TriggerBuilder</code>, <code>JobBuilder</code>,
    /// <code>DateBuilder</code>, <code>JobKey</code>, <code>TriggerKey</code>
    /// and the various <code>ScheduleBuilder</code> implementations.</p>
    /// <p>Client code can then use the DSL to write code such as this:</p>
    /// <pre>
    /// JobDetail job = newJob(MyJob.class)
    /// .withIdentity("myJob")
    /// .build();
    /// Trigger trigger = newTrigger()
    /// .withIdentity(triggerKey("myTrigger", "myTriggerGroup"))
    /// .withSchedule(simpleSchedule()
    /// .withIntervalInHours(1)
    /// .repeatForever())
    /// .startAt(futureDate(10, MINUTES))
    /// .build();
    /// scheduler.scheduleJob(job, trigger);
    /// </pre>
    /// </remarks>
    /// <seealso cref="JobBuilder" />
    /// <seealso cref="IScheduleBuilder" />
    /// <seealso cref="DateBuilder" />
    /// <seealso cref="ITrigger" />
    public class TriggerBuilder<T>where T : ITrigger
    {
        private TriggerKey key;
        private string description;
        private DateTimeOffset startTime = DateTimeOffset.UtcNow;
        private DateTimeOffset? endTime;
        private int priority = TriggerConstants.DefaultPriority;
        private string calendarName;
        private JobKey jobKey;
        private JobDataMap jobDataMap = new JobDataMap();

        private IScheduleBuilder scheduleBuilder;

        private TriggerBuilder()
        {
        }

        /// <summary>
        /// Create a new TriggerBuilder with which to define a
        /// specification for a Trigger.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new TriggerBuilder</returns>
        public static TriggerBuilder<T> Create()
        {
            return new TriggerBuilder<T>();
        }

        /// <summary>
        /// Produce the <code>Trigger</code>.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>a Trigger that meets the specifications of the builder.</returns>
        public T Build()
        {
            if (scheduleBuilder == null)
            {
                scheduleBuilder = SimpleScheduleBuilder.Create();
            }
            IMutableTrigger trig = scheduleBuilder.Build();

            trig.CalendarName = calendarName;
            trig.Description = description;
            trig.EndTimeUtc = endTime;
            if (key == null)
            {
                key = new TriggerKey(Key<string>.CreateUniqueName(null), null);
            }
            trig.Key = key;
            if (jobKey != null)
            {
                trig.JobKey = jobKey;
            }
            trig.Priority = priority;
            trig.StartTimeUtc = startTime;

            if (!jobDataMap.IsEmpty)
            {
                trig.JobDataMap = jobDataMap;
            }

            return (T) trig;
        }

        /// <summary>
        /// Use a <code>TriggerKey</code> with the given name and default group to
        /// identify the Trigger.
        /// </summary>
        /// <remarks>
        /// <p>If none of the 'withIdentity' methods are set on the TriggerBuilder,
        /// then a random, unique TriggerKey will be generated.</p>
        /// </remarks>
        /// <param name="name">the name element for the Trigger's TriggerKey</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="TriggerKey" />
        /// <seealso cref="ITrigger.Key" />
        public TriggerBuilder<T> WithIdentity(string name)
        {
            key = new TriggerKey(name, null);
            return this;
        }

        /// <summary>
        /// Use a TriggerKey with the given name and group to
        /// identify the Trigger.
        /// </summary>
        /// <remarks>
        /// <p>If none of the 'withIdentity' methods are set on the TriggerBuilder,
        /// then a random, unique TriggerKey will be generated.</p>
        /// </remarks>
        /// <param name="name">the name element for the Trigger's TriggerKey</param>
        /// <param name="group">the group element for the Trigger's TriggerKey</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="TriggerKey" />
        /// <seealso cref="ITrigger.Key" />
        public TriggerBuilder<T> WithIdentity(string name, string group)
        {
            key = new TriggerKey(name, group);
            return this;
        }

        /// <summary>
        /// Use the given TriggerKey to identify the Trigger.
        /// </summary>
        /// <remarks>
        /// <p>If none of the 'withIdentity' methods are set on the TriggerBuilder,
        /// then a random, unique TriggerKey will be generated.</p>
        /// </remarks>
        /// <param name="key">the TriggerKey for the Trigger to be built</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="TriggerKey" />
        /// <seealso cref="ITrigger.Key" />
        public TriggerBuilder<T> WithIdentity(TriggerKey key)
        {
            this.key = key;
            return this;
        }

        /// <summary>
        /// Set the given (human-meaningful) description of the Trigger.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="description">the description for the Trigger</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.Description" />
        public TriggerBuilder<T> WithDescription(string description)
        {
            this.description = description;
            return this;
        }

        /// <summary>
        /// Set the Trigger's priority.  When more than one Trigger have the same
        /// fire time, the scheduler will fire the one with the highest priority
        /// first.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="priority">the priority for the Trigger</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="TriggerConstants.DefaultPriority" />
        /// <seealso cref="ITrigger.Priority" />
        public TriggerBuilder<T> WithPriority(int priority)
        {
            this.priority = priority;
            return this;
        }

        /// <summary>
        /// Set the name of the {@link Calendar} that should be applied to this
        /// Trigger's schedule.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="calendarName">the name of the Calendar to reference.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ICalendar" />
        /// <seealso cref="ITrigger.CalendarName" />
        public TriggerBuilder<T> ModifiedByCalendar(string calendarName)
        {
            this.calendarName = calendarName;
            return this;
        }

        /// <summary>
        /// Set the time the Trigger should start at - the trigger may or may
        /// not fire at this time - depending upon the schedule configured for
        /// the Trigger.  However the Trigger will NOT fire before this time,
        /// regardless of the Trigger's schedule.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="startTimeUtc">the start time for the Trigger.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.StartTimeUtc" />
        /// <seealso cref="DateBuilder" />
        public TriggerBuilder<T> StartAt(DateTimeOffset startTimeUtc)
        {
            this.startTime = startTimeUtc;
            return this;
        }

        /// <summary>
        /// Set the time the Trigger should start at to the current moment -
        /// the trigger may or may not fire at this time - depending upon the
        /// schedule configured for the Trigger.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.StartTimeUtc" />
        public TriggerBuilder<T> StartNow()
        {
            this.startTime = DateTimeOffset.UtcNow;
            return this;
        }

        /// <summary>
        /// Set the time at which the Trigger will no longer fire - even if it's
        /// schedule has remaining repeats.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="endTimeUtc">the end time for the Trigger.  If null, the end time is indefinite.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.EndTimeUtc" />
        /// <seealso cref="DateBuilder" />
        public TriggerBuilder<T> EndAt(DateTimeOffset? endTimeUtc)
        {
            this.endTime = endTimeUtc;
            return this;
        }

        /// <summary>
        /// Set the {@link ScheduleBuilder} that will be used to define the
        /// Trigger's schedule.
        /// </summary>
        /// <remarks>
        /// <p>The particular <code>SchedulerBuilder</code> used will dictate
        /// the concrete type of Trigger that is produced by the TriggerBuilder.</p>
        /// </remarks>
        /// <param name="scheduleBuilder">the SchedulerBuilder to use.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="IScheduleBuilder" />
        /// <seealso cref="SimpleScheduleBuilder" />
        /// <seealso cref="CronScheduleBuilder" />
        /// <seealso cref="CalendarIntervalScheduleBuilder" />
        public TriggerBuilder<T> WithSchedule(IScheduleBuilder scheduleBuilder)
        {
            this.scheduleBuilder = scheduleBuilder;
            return this;
        }

        /// <summary>
        /// Set the identity of the Job which should be fired by the produced
        /// Trigger.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobKey">the identity of the Job to fire.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobKey" />
        public TriggerBuilder<T> ForJob(JobKey jobKey)
        {
            this.jobKey = jobKey;
            return this;
        }

        /// <summary>
        /// Set the identity of the Job which should be fired by the produced
        /// Trigger - a <code>JobKey</code> will be produced with the given
        /// name and default group.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobName">the name of the job (in default group) to fire.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobKey" />
        public TriggerBuilder<T> ForJob(string jobName)
        {
            this.jobKey = new JobKey(jobName, null);
            return this;
        }

        /// <summary>
        /// Set the identity of the Job which should be fired by the produced
        /// Trigger - a <code>JobKey</code> will be produced with the given
        /// name and group.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobName">the name of the job to fire.</param>
        /// <param name="jobGroup">the group of the job to fire.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobKey" />
        public TriggerBuilder<T> ForJob(string jobName, string jobGroup)
        {
            this.jobKey = new JobKey(jobName, jobGroup);
            return this;
        }

        /// <summary>
        /// Set the identity of the Job which should be fired by the produced
        /// Trigger, by extracting the JobKey from the given job.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobDetail">the Job to fire.</param>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobKey" />
        public TriggerBuilder<T> ForJob(IJobDetail jobDetail)
        {
            JobKey k = jobDetail.Key;
            if (k.Name == null)
            {
                throw new ArgumentException("The given job has not yet had a name assigned to it.");
            }
            this.jobKey = k;
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(string key, string value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(string key, int value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(string key, long value)
        {
            jobDataMap.Put(key, value);
            return this;
        }


        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(string key, float value)
        {
            jobDataMap.Put(key, value);
            return this;
        }


        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(string key, double value)
        {
            jobDataMap.Put(key, value);
            return this;
        }


        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(string key, decimal value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(string key, bool value)
        {
            jobDataMap.Put(key, value);
            return this;
        }


        /// <summary>
        /// Add the given key-value pair to the Trigger's {@link JobDataMap}.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated TriggerBuilder</returns>
        /// <seealso cref="ITrigger.JobDataMap" />
        public TriggerBuilder<T> UsingJobData(JobDataMap newJobDataMap)
        {
            // add any existing data to this new map
            foreach (string key in jobDataMap.Keys)
            {
                newJobDataMap.Put(key, jobDataMap.Get(key));
            }
            jobDataMap = newJobDataMap; // set new map as the map to use
            return this;
        }
    }
}