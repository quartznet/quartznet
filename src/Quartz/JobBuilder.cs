using System;

using Quartz.Job;
using Quartz.Util;

namespace Quartz
{
    /**
 * <code>JobBuilder</code> is used to instantiate {@link JobDetail}s.
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
 * @see TriggerBuilder
 * @see DateBuilder 
 * @see JobDetail
 */
    public class JobBuilder
    {
        private JobKey key;
        private string description;
        private Type jobClass = typeof (NoOpJob);
        private bool durability;
        private bool shouldRecover;

        private JobDataMap jobDataMap = new JobDataMap();

        private JobBuilder()
        {
        }

        /**
     * Create a JobBuilder with which to define a <code>JobDetail</code>.
     * 
     * @return a new JobBuilder
     */

        public static JobBuilder NewJob()
        {
            return new JobBuilder();
        }

        /**
     * Create a JobBuilder with which to define a <code>JobDetail</code>,
     * and set the class name of the <code>Job</code> to be executed.
     * 
     * @return a new JobBuilder
     */
        public static JobBuilder NewJob(Type jobType)
    {
        JobBuilder b = new JobBuilder();
        b.OfType(jobType);
        return b;
    }

        /**
     * Produce the <code>JobDetail</code> instance defined by this 
     * <code>JobBuilder</code>.
     * 
     * @return the defined JobDetail.
     */

        public JobDetailImpl Build()
        {
            JobDetailImpl job = new JobDetailImpl();

            job.setJobClass(jobClass);
            job.setDescription(description);
            if (key == null)
            {
                key = new JobKey(Key<string>.CreateUniqueName(null), null);
            }
            job.setKey(key);
            job.setDurability(durability);
            job.setRequestsRecovery(shouldRecover);


            if (!jobDataMap.isEmpty())
            {
                job.setJobDataMap(jobDataMap);
            }

            return job;
        }

        /**
     * Use a <code>JobKey</code> with the given name and default group to
     * identify the JobDetail.
     * 
     * <p>If none of the 'withIdentity' methods are set on the JobBuilder,
     * then a random, unique JobKey will be generated.</p>
     * 
     * @param name the name element for the Job's JobKey
     * @return the updated JobBuilder
     * @see JobKey
     * @see JobDetail#getKey()
     */

        public JobBuilder WithIdentity(string name)
        {
            key = new JobKey(name, null);
            return this;
        }

        /**
     * Use a <code>JobKey</code> with the given name and group to
     * identify the JobDetail.
     * 
     * <p>If none of the 'withIdentity' methods are set on the JobBuilder,
     * then a random, unique JobKey will be generated.</p>
     * 
     * @param name the name element for the Job's JobKey
     * @param group the group element for the Job's JobKey
     * @return the updated JobBuilder
     * @see JobKey
     * @see JobDetail#getKey()
     */

        public JobBuilder WithIdentity(string name, string group)
        {
            key = new JobKey(name, group);
            return this;
        }

        /**
     * Use a <code>JobKey</code> to identify the JobDetail.
     * 
     * <p>If none of the 'withIdentity' methods are set on the JobBuilder,
     * then a random, unique JobKey will be generated.</p>
     * 
     * @param key the Job's JobKey
     * @return the updated JobBuilder
     * @see JobKey
     * @see JobDetail#getKey()
     */

        public JobBuilder WithIdentity(JobKey key)
        {
            this.key = key;
            return this;
        }

        /**
     * Set the given (human-meaningful) description of the Job.
     * 
     * @param description the description for the Job
     * @return the updated JobBuilder
     * @see JobDetail#getDescription()
     */

        public JobBuilder WithDescription(string description)
        {
            this.description = description;
            return this;
        }

        /**
     * Set the class which will be instantiated and executed when a
     * Trigger fires that is associated with this JobDetail.
     * 
     * @param jobType a class implementing the Job interface.
     * @return the updated JobBuilder
     * @see JobDetail#getJobClass()
     */
        public JobBuilder OfType(Type jobType)
    {
        this.jobClass = jobType;
        return this;
    }

        /**
     * Instructs the <code>Scheduler</code> whether or not the <code>Job</code>
     * should be re-executed if a 'recovery' or 'fail-over' situation is
     * encountered.
     * 
     * <p>
     * If not explicitly set, the default value is <code>false</code>.
     * </p>
     * 
     * @return the updated JobBuilder
     * @see JobDetail#requestsRecovery()
     */

        public JobBuilder RequestRecovery()
        {
            this.shouldRecover = true;
            return this;
        }

        /**
     * Instructs the <code>Scheduler</code> whether or not the <code>Job</code>
     * should be re-executed if a 'recovery' or 'fail-over' situation is
     * encountered.
     * 
     * <p>
     * If not explicitly set, the default value is <code>false</code>.
     * </p>
     * 
     * @param shouldRecover
     * @return the updated JobBuilder
     */

        public JobBuilder RequestRecovery(bool shouldRecover)
        {
            this.shouldRecover = shouldRecover;
            return this;
        }

        /**
     * Whether or not the <code>Job</code> should remain stored after it is
     * orphaned (no <code>{@link Trigger}s</code> point to it).
     * 
     * <p>
     * If not explicitly set, the default value is <code>false</code>.
     * </p>
     * 
     * @return the updated JobBuilder
     * @see JobDetail#isDurable()
     */

        public JobBuilder StoreDurably()
        {
            this.durability = true;
            return this;
        }

        /**
     * Whether or not the <code>Job</code> should remain stored after it is
     * orphaned (no <code>{@link Trigger}s</code> point to it).
     * 
     * <p>
     * If not explicitly set, the default value is <code>false</code>.
     * </p>
     * 
     * @param durability the value to set for the durability property.
     * @return the updated JobBuilder
     * @see JobDetail#isDurable()
     */

        public JobBuilder StoreDurably(bool durability)
        {
            this.durability = durability;
            return this;
        }

        /**
     * Add the given key-value pair to the JobDetail's {@link JobDataMap}.
     * 
     * @return the updated JobBuilder
     * @see JobDetail#getJobDataMap()
     */

        public JobBuilder UsingJobData(string key, string value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
     * Add the given key-value pair to the JobDetail's {@link JobDataMap}.
     * 
     * @return the updated JobBuilder
     * @see JobDetail#getJobDataMap()
     */

        public JobBuilder UsingJobData(string key, int value)
        {
            jobDataMap.put(key, value);
            return this;
        }

        /**
     * Add the given key-value pair to the JobDetail's {@link JobDataMap}.
     * 
     * @return the updated JobBuilder
     * @see JobDetail#getJobDataMap()
     */

        public JobBuilder UsingJobData(string key, long value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
     * Add the given key-value pair to the JobDetail's {@link JobDataMap}.
     * 
     * @return the updated JobBuilder
     * @see JobDetail#getJobDataMap()
     */

        public JobBuilder UsingJobData(string key, Float value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
     * Add the given key-value pair to the JobDetail's {@link JobDataMap}.
     * 
     * @return the updated JobBuilder
     * @see JobDetail#getJobDataMap()
     */

        public JobBuilder UsingJobData(string key, Double value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
     * Add the given key-value pair to the JobDetail's {@link JobDataMap}.
     * 
     * @return the updated JobBuilder
     * @see JobDetail#getJobDataMap()
     */

        public JobBuilder UsingJobData(string key, Boolean value)
        {
            jobDataMap.Put(key, value);
            return this;
        }

        /**
     * Set the JobDetail's {@link JobDataMap}, adding any values to it
     * that were already set on this JobBuilder using any of the
     * other 'usingJobData' methods. 
     * 
     * @return the updated JobBuilder
     * @see JobDetail#getJobDataMap()
     */

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