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
    /// Set the trigger to fire on the given cron schedule.
    /// </summary>
    /// <remarks>
    /// This is the overload to reach for when the expression carries <c>H</c> (hash) tokens that
    /// should be spread by something other than the trigger's own key:
    /// <c>CronScheduleBuilder.Create(new CronExpression(expression, hashKey))</c>.
    /// </remarks>
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
