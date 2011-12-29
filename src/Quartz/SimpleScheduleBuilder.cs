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
    /// <summary>
    /// SimpleScheduleBuilder is a <see cref="IScheduleBuilder" />
    /// that defines strict/literal interval-based schedules for
    /// <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />,
    /// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" />
    /// and the various <see cref="IScheduleBuilder" /> implementations.
    /// </para>
    /// <para>Client code can then use the DSL to write code such as this:</para>
    /// <code>
    /// JobDetail job = JobBuilder.Create&lt;MyJob>()
    ///     .WithIdentity("myJob")
    ///     .Build();
    /// Trigger trigger = TriggerBuilder.Create()
    ///     .WithIdentity(triggerKey("myTrigger", "myTriggerGroup"))
    ///     .WithSimpleSchedule(x => x
    ///         .WithIntervalInHours(1)
    ///         .RepeatForever())
    ///     .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
    ///     .Build();
    /// scheduler.scheduleJob(job, trigger);
    /// </code>
    /// </remarks>
    /// <seealso cref="ISimpleTrigger" />
    /// <seealso cref="CalendarIntervalScheduleBuilder" />
    /// <seealso cref="CronScheduleBuilder" />
    /// <seealso cref="IScheduleBuilder" />
    /// <seealso cref="TriggerBuilder" />
    public class SimpleScheduleBuilder : ScheduleBuilder<ISimpleTrigger>
    {
        private TimeSpan interval = TimeSpan.Zero;
        private int repeatCount;
        private int misfireInstruction = MisfireInstruction.SmartPolicy;

        protected SimpleScheduleBuilder()
        {
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder Create()
        {
            return new SimpleScheduleBuilder();
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat forever with a 1 minute interval.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatMinutelyForever()
        {
            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromMinutes(1))
                .RepeatForever();

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat forever with an interval
        /// of the given number of minutes.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatMinutelyForever(int minutes)
        {
            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromMinutes(minutes))
                .RepeatForever();

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat forever with a 1 second interval.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatSecondlyForever()
        {
            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromSeconds(1))
                .RepeatForever();

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat forever with an interval
        /// of the given number of seconds.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatSecondlyForever(int seconds)
        {
            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromSeconds(seconds))
                .RepeatForever();

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat forever with a 1 hour interval.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatHourlyForever()
        {
            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromHours(1))
                .RepeatForever();

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat forever with an interval
        /// of the given number of hours.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatHourlyForever(int hours)
        {
            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromHours(hours))
                .RepeatForever();

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat the given number
        /// of times - 1  with a 1 minute interval.
        /// </summary>
        /// <remarks>
        /// <para>Note: Total count = 1 (at start time) + repeat count</para>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatMinutelyForTotalCount(int count)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromMinutes(1))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat the given number
        /// of times - 1  with an interval of the given number of minutes.
        /// </summary>
        /// <remarks>
        /// <para>Note: Total count = 1 (at start time) + repeat count</para>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatMinutelyForTotalCount(int count, int minutes)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromMinutes(minutes))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat the given number
        /// of times - 1  with a 1 second interval.
        /// </summary>
        /// <remarks>
        /// <para>Note: Total count = 1 (at start time) + repeat count</para>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatSecondlyForTotalCount(int count)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromSeconds(1))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat the given number
        /// of times - 1  with an interval of the given number of seconds.
        /// </summary>
        /// <remarks>
        /// <para>Note: Total count = 1 (at start time) + repeat count</para>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatSecondlyForTotalCount(int count, int seconds)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromSeconds(seconds))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat the given number
        /// of times - 1  with a 1 hour interval.
        /// </summary>
        /// <remarks>
        /// <para>Note: Total count = 1 (at start time) + repeat count</para>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatHourlyForTotalCount(int count)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromHours(1))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /// <summary>
        /// Create a SimpleScheduleBuilder set to repeat the given number
        /// of times - 1  with an interval of the given number of hours.
        /// </summary>
        /// <remarks>
        /// <para>Note: Total count = 1 (at start time) + repeat count</para>
        /// </remarks>
        /// <returns>the new SimpleScheduleBuilder</returns>
        public static SimpleScheduleBuilder RepeatHourlyForTotalCount(int count, int hours)
        {
            if (count < 1)
            {
                throw new ArgumentException("Total count of firings must be at least one! Given count: " + count);
            }

            SimpleScheduleBuilder sb = Create()
                .WithInterval(TimeSpan.FromHours(hours))
                .WithRepeatCount(count - 1);

            return sb;
        }

        /// <summary>
        /// Build the actual Trigger -- NOT intended to be invoked by end users,
        /// but will rather be invoked by a TriggerBuilder which this
        /// ScheduleBuilder is given to.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <seealso cref="TriggerBuilder.WithSchedule(IScheduleBuilder)" />
        public override IMutableTrigger Build()
        {
            SimpleTriggerImpl st = new SimpleTriggerImpl();
            st.RepeatInterval = interval;
            st.RepeatCount = repeatCount;
            st.MisfireInstruction = misfireInstruction;

            return st;
        }

        /// <summary>
        /// Specify a repeat interval in milliseconds.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="timeSpan">the time span at which the trigger should repeat.</param>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="ISimpleTrigger.RepeatInterval" />
        /// <seealso cref="WithRepeatCount(int)" />
        public SimpleScheduleBuilder WithInterval(TimeSpan timeSpan)
        {
            interval = timeSpan;
            return this;
        }

        /// <summary>
        /// Specify a repeat interval in seconds.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="seconds">the time span at which the trigger should repeat.</param>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="ISimpleTrigger.RepeatInterval" />
        /// <seealso cref="WithRepeatCount(int)" />
        public SimpleScheduleBuilder WithIntervalInSeconds(int seconds)
        {
            return WithInterval(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>
        /// Specify a the number of time the trigger will repeat - total number of
        /// firings will be this number + 1.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="repeatCount">the number of seconds at which the trigger should repeat.</param>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="ISimpleTrigger.RepeatCount" />
        /// <seealso cref="RepeatForever" />
        public SimpleScheduleBuilder WithRepeatCount(int repeatCount)
        {
            this.repeatCount = repeatCount;
            return this;
        }

        /// <summary>
        /// Specify that the trigger will repeat indefinitely.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="ISimpleTrigger.RepeatCount" />
        /// <seealso cref="SimpleTriggerImpl.RepeatIndefinitely" />
        /// <seealso cref="WithInterval" />
        public SimpleScheduleBuilder RepeatForever()
        {
            repeatCount = SimpleTriggerImpl.RepeatIndefinitely;
            return this;
        }


        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.IgnoreMisfirePolicy" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated CronScheduleBuilder</returns>
        ///  <seealso cref="MisfireInstruction.IgnoreMisfirePolicy" />
        public SimpleScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.SimpleTrigger.FireNow" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.SimpleTrigger.FireNow" />
        public SimpleScheduleBuilder WithMisfireHandlingInstructionFireNow()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount" />
        public SimpleScheduleBuilder WithMisfireHandlingInstructionNextWithExistingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount" />
        public SimpleScheduleBuilder WithMisfireHandlingInstructionNextWithRemainingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount" />
        public SimpleScheduleBuilder WithMisfireHandlingInstructionNowWithExistingCount()
        {
            misfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated SimpleScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount" />
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

        public SimpleScheduleBuilder WithIntervalInMinutes(int minutes)
        {
            return WithInterval(TimeSpan.FromMinutes(minutes));
        }

        public SimpleScheduleBuilder WithIntervalInHours(int hours)
        {
            return WithInterval(TimeSpan.FromHours(hours));
        }
    }

    /// <summary>
    /// Extension methods that attach <see cref="SimpleScheduleBuilder" /> to <see cref="TriggerBuilder" />.
    /// </summary>
    public static class SimpleScheduleTriggerBuilderExtensions
    {
        public static TriggerBuilder WithSimpleSchedule(this TriggerBuilder triggerBuilder, Action<SimpleScheduleBuilder> action)
        {
            SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create();
            action(builder);
            return triggerBuilder.WithSchedule(builder);
        }

        public static TriggerBuilder WithSimpleSchedule(this TriggerBuilder triggerBuilder)
        {
            SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create();
            return triggerBuilder.WithSchedule(builder);
        }
    }
}