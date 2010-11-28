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

using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz
{
/**
 * <code>SimpleScheduleBuilder</code> is a {@link ScheduleBuilder} 
 * that defines strict/literal interval-based schedules for 
 * <code>Trigger</code>s.
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
 * @see SimpleTrigger 
 * @see CalenderIntervalScheduleBuilder
 * @see CronScheduleBuilder
 * @see ScheduleBuilder
 * @see TriggerBuilder
 */

    public class SimpleScheduleBuilder : ScheduleBuilder<ISimpleTrigger>
    {
        private TimeSpan interval = TimeSpan.Zero;
        private int repeatCount = 0;
        private int misfireInstruction = MisfireInstruction.SmartPolicy;

        private SimpleScheduleBuilder()
        {
        }


        /**
     * Create a SimpleScheduleBuilder.
     * 
     * @return the new SimpleScheduleBuilder
     */

        public static SimpleScheduleBuilder SimpleSchedule()
        {
            return new SimpleScheduleBuilder();
        }

        /**
     * Create a SimpleScheduleBuilder set to repeat forever with a 1 minute interval.
     * 
     * @return the new SimpleScheduleBuilder
     */

        public static SimpleScheduleBuilder RepeatMinutelyForever()
        {
            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromMinutes(1))
                .RepeatForever();

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat forever with an interval
         * of the given number of minutes.
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatMinutelyForever(int minutes)
        {
            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromMinutes(minutes))
                .RepeatForever();

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat forever with a 1 second interval.
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatSecondlyForever()
        {
            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromSeconds(1))
                .RepeatForever();

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat forever with an interval
         * of the given number of seconds.
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatSecondlyForever(int seconds)
        {
            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromSeconds(seconds))
                .RepeatForever();

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat forever with a 1 hour interval.
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatHourlyForever()
        {
            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromHours(1))
                .RepeatForever();

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat forever with an interval
         * of the given number of hours.
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatHourlyForever(int hours)
        {
            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromHours(hours))
                .RepeatForever();

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat the given number
         * of times - 1  with a 1 minute interval.
         * 
         * <p>Note: Total count = 1 (at start time) + repeat count</p>
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatMinutelyForTotalCount(int count)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromMinutes(1))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat the given number
         * of times - 1  with an interval of the given number of minutes.
         * 
         * <p>Note: Total count = 1 (at start time) + repeat count</p>
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatMinutelyForTotalCount(int count, int minutes)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromMinutes(minutes))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat the given number
         * of times - 1  with a 1 second interval.
         * 
         * <p>Note: Total count = 1 (at start time) + repeat count</p>
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatSecondlyForTotalCount(int count)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromSeconds(1))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat the given number
         * of times - 1  with an interval of the given number of seconds.
         * 
         * <p>Note: Total count = 1 (at start time) + repeat count</p>
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatSecondlyForTotalCount(int count, int seconds)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromSeconds(seconds))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat the given number
         * of times - 1  with a 1 hour interval.
         * 
         * <p>Note: Total count = 1 (at start time) + repeat count</p>
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatHourlyForTotalCount(int count)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromHours(1))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /**
         * Create a SimpleScheduleBuilder set to repeat the given number
         * of times - 1  with an interval of the given number of hours.
         * 
         * <p>Note: Total count = 1 (at start time) + repeat count</p>
         * 
         * @return the new SimpleScheduleBuilder
         */

        public static SimpleScheduleBuilder RepeatHourlyForTotalCount(int count, int hours)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = SimpleSchedule()
                .WithInterval(TimeSpan.FromHours(hours))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /**
     * Build the actual Trigger -- NOT intended to be invoked by end users,
     * but will rather be invoked by a TriggerBuilder which this 
     * ScheduleBuilder is given to.
     * 
     * @see TriggerBuilder#withSchedule(ScheduleBuilder)
     */

        public override IMutableTrigger Build()
        {
            SimpleTriggerImpl st = new SimpleTriggerImpl();
            st.RepeatInterval = (interval);
            st.RepeatCount = (repeatCount);

            return st;
        }

        /**
     * Specify a repeat interval in milliseconds. 
     * 
     * @param intervalInMillis the number of seconds at which the trigger should repeat.
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#getRepeatInterval()
     * @see #withRepeatCount(int)
     */

        public SimpleScheduleBuilder WithInterval(TimeSpan timeSpan)
        {
            this.interval = timeSpan;
            return this;
        }

        /**
     * Specify a the number of time the trigger will repeat - total number of 
     * firings will be this number + 1. 
     * 
     * @param repeatCount the number of seconds at which the trigger should repeat.
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#getRepeatCount()
     * @see #repeatForever(int)
     */

        public SimpleScheduleBuilder WithRepeatCount(int repeatCount)
        {
            this.repeatCount = repeatCount;
            return this;
        }

        /**
     * Specify that the trigger will repeat indefinitely. 
     * 
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#getRepeatCount()
     * @see SimpleTrigger#REPEAT_INDEFINITELY
     * @see #withIntervalInMilliseconds(long)
     * @see #withIntervalInSeconds(int)
     * @see #withIntervalInMinutes(int)
     * @see #withIntervalInHours(int)
     */

        public SimpleScheduleBuilder RepeatForever()
        {
            this.repeatCount = SimpleTriggerImpl.RepeatIndefinitely;
            return this;
        }


        /**
         * If the Trigger misfires, use the 
         * {@link Trigger#MISFIRE_INSTRUCTION_IGNORE_MISFIRE_POLICY} instruction.
         * 
         * @return the updated CronScheduleBuilder
         * @see Trigger#MISFIRE_INSTRUCTION_IGNORE_MISFIRE_POLICY
         */

        public SimpleScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link SimpleTrigger#MISFIRE_INSTRUCTION_FIRE_NOW} instruction.
     * 
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#MISFIRE_INSTRUCTION_FIRE_NOW
     */

        public SimpleScheduleBuilder WithMisfireHandlingInstructionFireNow()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NEXT_WITH_EXISTING_COUNT} instruction.
     * 
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NEXT_WITH_EXISTING_COUNT
     */

        public SimpleScheduleBuilder WithMisfireHandlingInstructionNextWithExistingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NEXT_WITH_REMAINING_COUNT} instruction.
     * 
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NEXT_WITH_REMAINING_COUNT
     */

        public SimpleScheduleBuilder WithMisfireHandlingInstructionNextWithRemainingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NOW_WITH_EXISTING_REPEAT_COUNT} instruction.
     * 
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NOW_WITH_EXISTING_REPEAT_COUNT
     */

        public SimpleScheduleBuilder WithMisfireHandlingInstructionNowWithExistingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NOW_WITH_REMAINING_REPEAT_COUNT} instruction.
     * 
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#MISFIRE_INSTRUCTION_RESCHEDULE_NOW_WITH_REMAINING_REPEAT_COUNT
     */

        public SimpleScheduleBuilder WithMisfireHandlingInstructionNowWithRemainingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
            return this;
        }

        internal SimpleScheduleBuilder WithMisfireHandlingInstruction(int readMisfireInstructionFromString)
        {
            misfireInstruction = readMisfireInstructionFromString;
            return this;
        }
    }
}