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

namespace Quartz;

/// <summary>
/// Gives a trigger its schedule, one method per schedule kind.
/// </summary>
/// <remarks>
/// <para>
/// Each method is generic in the receiver and returns it unchanged, so the chain keeps whatever
/// type it started with — <see cref="TriggerBuilder{TJob}" /> when building a trigger directly,
/// <see cref="ITriggerConfigurator{TJob}" /> when configuring one through the container — and the
/// two read identically.
/// </para>
/// <para>
/// Every kind offers the same two shapes: configure a fresh builder inline, or hand over one that
/// was built elsewhere.
/// </para>
/// </remarks>
public static class TriggerConfiguratorExtensions
{
    /// <summary>
    /// Set the trigger to fire on a fixed interval, optionally repeating.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="configure">configures the schedule; omit for a schedule that fires once.</param>
    public static TConfigurator WithSimpleSchedule<TConfigurator>(
        this TConfigurator configurator,
        Action<SimpleScheduleBuilder>? configure = null) where TConfigurator : ITriggerConfigurator
    {
        SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create();
        configure?.Invoke(builder);
        configurator.WithSchedule(builder);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on the given fixed-interval schedule.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="schedule">the schedule to use.</param>
    public static TConfigurator WithSimpleSchedule<TConfigurator>(
        this TConfigurator configurator,
        SimpleScheduleBuilder schedule) where TConfigurator : ITriggerConfigurator
    {
        configurator.WithSchedule(schedule);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on a fixed interval, repeating forever unless a repeat count is given.
    /// </summary>
    /// <remarks>
    /// The shorthand for the schedule almost every fixed-interval trigger wants, so that
    /// <c>WithSimpleSchedule(x =&gt; x.WithInterval(interval).RepeatForever())</c> can be written
    /// <c>WithSimpleSchedule(interval)</c>. Reach for the delegate overload when the schedule needs
    /// more than an interval and a count — a misfire instruction, say.
    /// </remarks>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="interval">the interval at which the trigger repeats.</param>
    /// <param name="repeatCount">
    /// How many times the trigger repeats <em>after</em> its first firing, so the total number of
    /// firings is one more than this — the same number
    /// <see cref="SimpleScheduleBuilder.WithRepeatCount" /> and
    /// <see cref="ISimpleTrigger.RepeatCount" /> carry, with no arithmetic of its own.
    /// <see langword="null" />, the default, repeats forever.
    /// </param>
    public static TConfigurator WithSimpleSchedule<TConfigurator>(
        this TConfigurator configurator,
        TimeSpan interval,
        int? repeatCount = null) where TConfigurator : ITriggerConfigurator
    {
        SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create().WithInterval(interval);
        if (repeatCount is null)
        {
            builder.RepeatForever();
        }
        else
        {
            builder.WithRepeatCount(repeatCount.Value);
        }

        configurator.WithSchedule(builder);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on a cron schedule.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="cronExpression">the cron expression the trigger fires on.</param>
    /// <param name="configure">configures the rest of the schedule, such as its time zone.</param>
    public static TConfigurator WithCronSchedule<TConfigurator>(
        this TConfigurator configurator,
        string cronExpression,
        Action<CronScheduleBuilder>? configure = null) where TConfigurator : ITriggerConfigurator
    {
        CronScheduleBuilder builder = CronScheduleBuilder.Create(cronExpression);
        configure?.Invoke(builder);
        configurator.WithSchedule(builder);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on a cron schedule defined by an already-built
    /// <see cref="CronExpression" />.
    /// </summary>
    /// <remarks>
    /// This is also the overload to reach for when the expression carries <c>H</c> (hash) tokens
    /// that should be spread by something other than the trigger's own key:
    /// <c>WithCronSchedule(new CronExpression(expression, hashKey))</c>.
    /// </remarks>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="cronExpression">the cron expression the trigger fires on.</param>
    /// <param name="configure">configures the rest of the schedule, such as its time zone.</param>
    public static TConfigurator WithCronSchedule<TConfigurator>(
        this TConfigurator configurator,
        CronExpression cronExpression,
        Action<CronScheduleBuilder>? configure = null) where TConfigurator : ITriggerConfigurator
    {
        CronScheduleBuilder builder = CronScheduleBuilder.Create(cronExpression);
        configure?.Invoke(builder);
        configurator.WithSchedule(builder);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on a cron schedule assembled with a
    /// <see cref="CronExpressionBuilder" />, so the fluent chain closes without naming
    /// <see cref="CronScheduleBuilder" />.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="cronExpression">the builder holding the assembled expression; it is built here.</param>
    /// <param name="configure">configures the rest of the schedule, such as its time zone.</param>
    public static TConfigurator WithCronSchedule<TConfigurator>(
        this TConfigurator configurator,
        CronExpressionBuilder cronExpression,
        Action<CronScheduleBuilder>? configure = null) where TConfigurator : ITriggerConfigurator
    {
        ArgumentNullException.ThrowIfNull(cronExpression);
        return configurator.WithCronSchedule(cronExpression.Build(), configure);
    }

    /// <summary>
    /// Set the trigger to fire on the given cron schedule.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="schedule">the schedule to use.</param>
    public static TConfigurator WithCronSchedule<TConfigurator>(
        this TConfigurator configurator,
        CronScheduleBuilder schedule) where TConfigurator : ITriggerConfigurator
    {
        configurator.WithSchedule(schedule);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on a calendar interval — one that counts days, weeks, months or
    /// years rather than a fixed amount of time.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="configure">configures the schedule.</param>
    public static TConfigurator WithCalendarIntervalSchedule<TConfigurator>(
        this TConfigurator configurator,
        Action<CalendarIntervalScheduleBuilder>? configure = null) where TConfigurator : ITriggerConfigurator
    {
        CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create();
        configure?.Invoke(builder);
        configurator.WithSchedule(builder);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on the given calendar-interval schedule.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="schedule">the schedule to use.</param>
    public static TConfigurator WithCalendarIntervalSchedule<TConfigurator>(
        this TConfigurator configurator,
        CalendarIntervalScheduleBuilder schedule) where TConfigurator : ITriggerConfigurator
    {
        configurator.WithSchedule(schedule);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire one or more times a day, within a daily time window.
    /// </summary>
    /// <remarks>
    /// The interval decides how often the trigger fires inside the window. For a single execution
    /// per day, call <see cref="DailyTimeIntervalScheduleBuilder.EndingDailyAfterCount" /> with 1
    /// or set the interval to cover the whole window.
    /// </remarks>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="configure">configures the schedule.</param>
    public static TConfigurator WithDailyTimeIntervalSchedule<TConfigurator>(
        this TConfigurator configurator,
        Action<DailyTimeIntervalScheduleBuilder>? configure = null) where TConfigurator : ITriggerConfigurator
    {
        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
        configure?.Invoke(builder);
        configurator.WithSchedule(builder);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to fire on the given daily-time-interval schedule.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="schedule">the schedule to use.</param>
    public static TConfigurator WithDailyTimeIntervalSchedule<TConfigurator>(
        this TConfigurator configurator,
        DailyTimeIntervalScheduleBuilder schedule) where TConfigurator : ITriggerConfigurator
    {
        configurator.WithSchedule(schedule);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to use an RFC 5545 RRULE-based schedule.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="recurrenceRule">
    /// An RFC 5545 RRULE string, e.g. "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR".
    /// </param>
    /// <param name="configure">configures the rest of the schedule, such as its time zone.</param>
    public static TConfigurator WithRecurrenceSchedule<TConfigurator>(
        this TConfigurator configurator,
        string recurrenceRule,
        Action<RecurrenceScheduleBuilder>? configure = null) where TConfigurator : ITriggerConfigurator
    {
        RecurrenceScheduleBuilder builder = RecurrenceScheduleBuilder.Create(recurrenceRule);
        configure?.Invoke(builder);
        configurator.WithSchedule(builder);
        return configurator;
    }

    /// <summary>
    /// Set the trigger to use the given RFC 5545 RRULE-based schedule.
    /// </summary>
    /// <param name="configurator">the trigger being configured.</param>
    /// <param name="schedule">the schedule to use.</param>
    public static TConfigurator WithRecurrenceSchedule<TConfigurator>(
        this TConfigurator configurator,
        RecurrenceScheduleBuilder schedule) where TConfigurator : ITriggerConfigurator
    {
        configurator.WithSchedule(schedule);
        return configurator;
    }
}
