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

    public class SimpleScheduleBuilder : ScheduleBuilder
    {
        private long interval = 0;
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

        public static SimpleScheduleBuilder simpleSchedule()
        {
            return new SimpleScheduleBuilder();
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
            st.setRepeatInterval(interval);
            st.setRepeatCount(repeatCount);

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

        public SimpleScheduleBuilder withIntervalInMilliseconds(long intervalInMillis)
        {
            this.interval = intervalInMillis;
            return this;
        }

        /**
     * Specify a repeat interval in seconds - which will then be multiplied
     * by 1000 to produce milliseconds. 
     * 
     * @param intervalInSeconds the number of seconds at which the trigger should repeat.
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#getRepeatInterval()
     * @see #withRepeatCount(int)
     */

        public SimpleScheduleBuilder withIntervalInSeconds(int intervalInSeconds)
        {
            this.interval = intervalInSeconds*1000L;
            return this;
        }

        /**
     * Specify a repeat interval in minutes - which will then be multiplied
     * by 60 * 1000 to produce milliseconds. 
     * 
     * @param intervalInMinutes the number of seconds at which the trigger should repeat.
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#getRepeatInterval()
     * @see #withRepeatCount(int)
     */

        public SimpleScheduleBuilder withIntervalInMinutes(int intervalInMinutes)
        {
            this.interval = intervalInMinutes*DateBuilder.MILLISECONDS_IN_MINUTE;
            return this;
        }

        /**
     * Specify a repeat interval in minutes - which will then be multiplied
     * by 60 * 60 * 1000 to produce milliseconds. 
     * 
     * @param intervalInHours the number of seconds at which the trigger should repeat.
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#getRepeatInterval()
     * @see #withRepeatCount(int)
     */

        public SimpleScheduleBuilder withIntervalInHours(int intervalInHours)
        {
            this.interval = intervalInHours*DateBuilder.MILLISECONDS_IN_HOUR;
            return this;
        }

        /**
     * Specify a the number of time the trigger will repeat - total number of 
     * firings will be this number + 1. 
     * 
     * @param repeatCount the number of seconds at which the trigger should repeat.
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#getRepeatCount()
     * @see #withIntervalInMilliseconds(long)
     * @see #withIntervalInSeconds(int)
     * @see #withIntervalInMinutes(int)
     * @see #withIntervalInHours(int)
     */

        public SimpleScheduleBuilder withRepeatCount(int repeatCount)
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

        public SimpleScheduleBuilder repeatForever()
        {
            this.repeatCount = SimpleTrigger.RepeatIndefinitely;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link SimpleTrigger#MISFIRE_INSTRUCTION_FIRE_NOW} instruction.
     * 
     * @return the updated SimpleScheduleBuilder
     * @see SimpleTrigger#MISFIRE_INSTRUCTION_FIRE_NOW
     */

        public SimpleScheduleBuilder withMisfireHandlingInstructionFireNow()
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

        public SimpleScheduleBuilder withMisfireHandlingInstructionNextWithExistingCount()
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

        public SimpleScheduleBuilder withMisfireHandlingInstructionNextWithRemainingCount()
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

        public SimpleScheduleBuilder withMisfireHandlingInstructionNowWithExistingCount()
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

        public SimpleScheduleBuilder withMisfireHandlingInstructionNowWithRemainingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
            return this;
        }
    }
}