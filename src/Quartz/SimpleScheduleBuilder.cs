#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz;

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
/// IJobDetail job = JobBuilder.Create&lt;MyJob>()
///     .WithIdentity("myJob")
///     .Build();
/// ITrigger trigger = TriggerBuilder.Create()
///     .WithIdentity("myTrigger", "myTriggerGroup")
///     .WithSimpleSchedule(x => x
///         .WithInterval(TimeSpan.FromHours(1))
///         .RepeatForever())
///     .StartAt(DateBuilder.Create().AtHourMinuteAndSecond(10, 0, 0).Build())
///     .Build();
/// await scheduler.ScheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="ISimpleTrigger" />
/// <seealso cref="CalendarIntervalScheduleBuilder" />
/// <seealso cref="CronScheduleBuilder" />
/// <seealso cref="IScheduleBuilder" />
/// <seealso cref="TriggerBuilder" />
public sealed class SimpleScheduleBuilder : IScheduleBuilder
{
    private TimeSpan interval = TimeSpan.Zero;
    private int repeatCount;
    private int misfireInstruction = MisfireInstruction.SmartPolicy;

    private SimpleScheduleBuilder()
    {
    }

    /// <summary>
    /// Create a SimpleScheduleBuilder.
    /// </summary>
    /// <returns>the new SimpleScheduleBuilder</returns>
    public static SimpleScheduleBuilder Create()
    {
        return new SimpleScheduleBuilder();
    }

    /// <summary>
    /// Build the actual Trigger -- NOT intended to be invoked by end users,
    /// but will rather be invoked by a TriggerBuilder which this
    /// ScheduleBuilder is given to.
    /// </summary>
    /// <seealso cref="TriggerBuilder{TJob}.WithSchedule(IScheduleBuilder)" />
    public IMutableTrigger Build()
    {
        SimpleTriggerImpl st = new SimpleTriggerImpl();
        st.RepeatInterval = interval;
        st.RepeatCount = repeatCount;
        st.MisfireInstructionCode = misfireInstruction;

        return st;
    }

    /// <summary>
    /// Specify the interval at which the trigger repeats.
    /// </summary>
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
    /// Specify a the number of time the trigger will repeat - total number of
    /// firings will be this number + 1.
    /// </summary>
    /// <param name="repeatCount">the number of times the trigger should repeat.</param>
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
    /// Say what the trigger should do when it misses a firing.
    /// </summary>
    /// <param name="instruction">the policy to apply; defaults to
    /// <see cref="SimpleTriggerMisfireInstruction.SmartPolicy" />.</param>
    /// <returns>the updated SimpleScheduleBuilder</returns>
    /// <seealso cref="SimpleTriggerMisfireInstruction" />
    public SimpleScheduleBuilder WithMisfireInstruction(SimpleTriggerMisfireInstruction instruction)
    {
        misfireInstruction = (int) instruction;
        return this;
    }
}
