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
    /**
     * <code>TriggerBuilder</code> is used to instantiate {@link Trigger}s.
     *  
     * <p>Quartz provides a builder-style API for constructing scheduling-related
     * entities via a Domain-Specific Language (DSL).  The DSL can best be
     * utilized through the usage of static imports of the methods on the classes
     * <code>TriggerBuilder</code>, <code>JobBuilder</code>, 
     * <code>DateBuilder</code>, <code>JobKey</code>, <code>TriggerKey</code> 
     * and the various <code>ScheduleBuilder</code> implementations.</p>
     * 
     * <p>Client code can then use the DSL to write code such as this:</p>
     * <pre>
     *         JobDetail job = newJob(MyJob.class)
     *             .withIdentity("myJob")
     *             .build();
     *             
     *         Trigger trigger = newTrigger() 
     *             .withIdentity(triggerKey("myTrigger", "myTriggerGroup"))
     *             .withSchedule(simpleSchedule()
     *                 .withIntervalInHours(1)
     *                 .repeatForever())
     *             .startAt(futureDate(10, MINUTES))
     *             .build();
     *         
     *         scheduler.scheduleJob(job, trigger);
     * <pre>
     *  
     * @see JobBuilder
     * @see ScheduleBuilder
     * @see DateBuilder 
     * @see Trigger
     */

    public class TriggerBuilder
    {
        private TriggerKey key;
        private string description;
        private DateTimeOffset startTime = DateTimeOffset.UtcNow;
        private DateTimeOffset endTime;
        private int priority = Trigger.DefaultPriority;
        private string calendarName;
        private JobKey jobKey;
        private JobDataMap jobDataMap = new JobDataMap();

        private ScheduleBuilder scheduleBuilder;

        private TriggerBuilder()
        {
        }

        /**
         * Create a new TriggerBuilder with which to define a 
         * specification for a Trigger.
         * 
         * @return the new TriggerBuilder
         */

        public static TriggerBuilder newTrigger()
        {
            return new TriggerBuilder();
        }

        /**
         * Produce the <code>Trigger</code>.
         * 
         * @return a Trigger that meets the specifications of the builder.
         */

        public Trigger build()
        {
            if (scheduleBuilder == null)
            {
                scheduleBuilder = SimpleScheduleBuilder.simpleSchedule();
            }
            IMutableTrigger trig = scheduleBuilder.Build();

            trig.CalendarName = calendarName;
            trig.Description = description;
            trig.EndTime = endTime;
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
            trig.StartTime = startTime;

            if (!jobDataMap.IsEmpty)
            {
                trig.JobDataMap = jobDataMap;
            }

            return trig;
        }

        /**
         * Use a <code>TriggerKey</code> with the given name and default group to
         * identify the Trigger.
         * 
         * <p>If none of the 'withIdentity' methods are set on the TriggerBuilder,
         * then a random, unique TriggerKey will be generated.</p>
         * 
         * @param name the name element for the Trigger's TriggerKey
         * @return the updated TriggerBuilder
         * @see TriggerKey
         * @see Trigger#getKey()
         */

        public TriggerBuilder withIdentity(string name)
        {
            key = new TriggerKey(name, null);
            return this;
        }

        /**
         * Use a TriggerKey with the given name and group to
         * identify the Trigger.
         * 
         * <p>If none of the 'withIdentity' methods are set on the TriggerBuilder,
         * then a random, unique TriggerKey will be generated.</p>
         * 
         * @param name the name element for the Trigger's TriggerKey
         * @param group the group element for the Trigger's TriggerKey
         * @return the updated TriggerBuilder
         * @see TriggerKey
         * @see Trigger#getKey()
         */

        public TriggerBuilder withIdentity(string name, string group)
        {
            key = new TriggerKey(name, group);
            return this;
        }

        /**
         * Use the given TriggerKey to identify the Trigger.  
         * 
         * <p>If none of the 'withIdentity' methods are set on the TriggerBuilder,
         * then a random, unique TriggerKey will be generated.</p>
         * 
         * @param key the TriggerKey for the Trigger to be built
         * @return the updated TriggerBuilder
         * @see TriggerKey
         * @see Trigger#getKey()
         */

        public TriggerBuilder withIdentity(TriggerKey key)
        {
            this.key = key;
            return this;
        }

        /**
         * Set the given (human-meaningful) description of the Trigger.
         * 
         * @param description the description for the Trigger
         * @return the updated TriggerBuilder
         * @see Trigger#getDescription()
         */

        public TriggerBuilder withDescription(string description)
        {
            this.description = description;
            return this;
        }

        /**
         * Set the Trigger's priority.  When more than one Trigger have the same
         * fire time, the scheduler will fire the one with the highest priority
         * first.
         * 
         * @param priority the priority for the Trigger
         * @return the updated TriggerBuilder
         * @see Trigger#DEFAULT_PRIORITY
         * @see Trigger#getPriority()
         */

        public TriggerBuilder withPriority(int priority)
        {
            this.priority = priority;
            return this;
        }

        /**
         * Set the name of the {@link Calendar} that should be applied to this
         * Trigger's schedule.
         * 
         * @param calendarName the name of the Calendar to reference.
         * @return the updated TriggerBuilder
         * @see Calendar
         * @see Trigger#getCalendarName()
         */

        public TriggerBuilder modifiedByCalendar(string calendarName)
        {
            this.calendarName = calendarName;
            return this;
        }

        /**
         * Set the time the Trigger should start at - the trigger may or may
         * not fire at this time - depending upon the schedule configured for
         * the Trigger.  However the Trigger will NOT fire before this time,
         * regardless of the Trigger's schedule.
         *  
         * @param startTime the start time for the Trigger.
         * @return the updated TriggerBuilder
         * @see Trigger#getStartTime()
         * @see DateBuilder
         */

        public TriggerBuilder startAt(DateTimeOffset startTime)
        {
            this.startTime = startTime;
            return this;
        }

        /**
         * Set the time the Trigger should start at to the current moment - 
         * the trigger may or may not fire at this time - depending upon the 
         * schedule configured for the Trigger.  
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getStartTime()
         */

        public TriggerBuilder startNow()
        {
            this.startTime = new DateTimeOffset();
            return this;
        }

        /**
         * Set the time at which the Trigger will no longer fire - even if it's
         * schedule has remaining repeats.    
         *  
         * @param endTime the end time for the Trigger.  If null, the end time is indefinite.
         * @return the updated TriggerBuilder
         * @see Trigger#getEndTime()
         * @see DateBuilder
         */

        public TriggerBuilder endAt(DateTimeOffset endTime)
        {
            this.endTime = endTime;
            return this;
        }

        /**
         * Set the {@link ScheduleBuilder} that will be used to define the 
         * Trigger's schedule.
         * 
         * <p>The particular <code>SchedulerBuilder</code> used will dictate
         * the concrete type of Trigger that is produced by the TriggerBuilder.</p>
         * 
         * @param scheduleBuilder the SchedulerBuilder to use.
         * @return the updated TriggerBuilder
         * @see ScheduleBuilder
         * @see SimpleScheduleBuilder
         * @see CronScheduleBuilder
         * @see CalendarIntervalScheduleBuilder
         */

        public TriggerBuilder withSchedule(ScheduleBuilder scheduleBuilder)
        {
            this.scheduleBuilder = scheduleBuilder;
            return this;
        }

        /**
         * Set the identity of the Job which should be fired by the produced 
         * Trigger.
         * 
         * @param jobKey the identity of the Job to fire.
         * @return the updated TriggerBuilder
         * @see Trigger#getJobKey()
         */

        public TriggerBuilder forJob(JobKey jobKey)
        {
            this.jobKey = jobKey;
            return this;
        }

        /**
         * Set the identity of the Job which should be fired by the produced 
         * Trigger - a <code>JobKey</code> will be produced with the given
         * name and default group.
         * 
         * @param jobName the name of the job (in default group) to fire. 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobKey()
         */

        public TriggerBuilder forJob(string jobName)
        {
            this.jobKey = new JobKey(jobName, null);
            return this;
        }

        /**
         * Set the identity of the Job which should be fired by the produced 
         * Trigger - a <code>JobKey</code> will be produced with the given
         * name and group.
         * 
         * @param jobName the name of the job to fire. 
         * @param jobGroup the group of the job to fire. 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobKey()
         */

        public TriggerBuilder forJob(string jobName, string jobGroup)
        {
            this.jobKey = new JobKey(jobName, jobGroup);
            return this;
        }

        /**
         * Set the identity of the Job which should be fired by the produced 
         * Trigger, by extracting the JobKey from the given job.
         * 
         * @param jobDetail the Job to fire.
         * @return the updated TriggerBuilder
         * @see Trigger#getJobKey()
         */

        public TriggerBuilder forJob(IJobDetail jobDetail)
        {
            JobKey k = jobDetail.Key;
            if (k.Name == null)
            {
                throw new ArgumentException("The given job has not yet had a name assigned to it.");
            }
            this.jobKey = k;
            return this;
        }

        /**
         * Add the given key-value pair to the Trigger's {@link JobDataMap}.
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobDataMap()
         */

        public TriggerBuilder usingJobData(string key, string value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
         * Add the given key-value pair to the Trigger's {@link JobDataMap}.
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobDataMap()
         */

        public TriggerBuilder usingJobData(string key, int value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
         * Add the given key-value pair to the Trigger's {@link JobDataMap}.
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobDataMap()
         */

        public TriggerBuilder usingJobData(string key, long value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
         * Add the given key-value pair to the Trigger's {@link JobDataMap}.
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobDataMap()
         */

        public TriggerBuilder usingJobData(string key, float value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
         * Add the given key-value pair to the Trigger's {@link JobDataMap}.
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobDataMap()
         */

        public TriggerBuilder usingJobData(string key, Double value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
         * Add the given key-value pair to the Trigger's {@link JobDataMap}.
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobDataMap()
         */

        public TriggerBuilder usingJobData(string key, Boolean value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
         * Set the Trigger's {@link JobDataMap}, adding any values to it
         * that were already set on this TriggerBuilder using any of the
         * other 'usingJobData' methods. 
         * 
         * @return the updated TriggerBuilder
         * @see Trigger#getJobDataMap()
         */

        public TriggerBuilder UsingJobData(JobDataMap newJobDataMap)
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