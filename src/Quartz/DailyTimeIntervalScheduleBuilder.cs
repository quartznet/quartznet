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
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Lets <see cref="TriggerBuilder{TJob}" /> hand a schedule builder its clock before it builds, so
/// that schedule computation deferred to <see cref="IScheduleBuilder.Build" /> runs against the same
/// <see cref="TimeProvider" /> the trigger builder carries.
/// </summary>
/// <remarks>
/// This is the sibling of <see cref="IHashKeyAwareScheduleBuilder" />: both exist because a schedule
/// builder learns some inputs only from the trigger builder that finally builds it. A schedule
/// builder whose <see cref="IScheduleBuilder.Build" /> is called directly falls back to
/// <see cref="TimeProvider.System" />.
/// </remarks>
internal interface ITimeProviderAwareScheduleBuilder
{
    /// <summary>
    /// Hand the builder the clock to compute against. Called by
    /// <see cref="TriggerBuilder{TJob}.Build" /> before <see cref="IScheduleBuilder.Build" />.
    /// </summary>
    void SetTimeProvider(TimeProvider timeProvider);
}

/// <summary>
/// A <see cref="IScheduleBuilder"/> implementation that build schedule for DailyTimeIntervalTrigger.
/// </summary>
/// <remarks>
/// <para>
/// This builder provide an extra convenient method for you to set the trigger's EndTimeOfDay. You may
/// use either endingDailyAt() or EndingDailyAfterCount() to set the value. The later will auto calculate
/// your EndTimeOfDay by using the interval, IntervalUnit and StartTimeOfDay to perform the calculation.
/// </para>
/// <para>
/// When using EndingDailyAfterCount(), you should note that it is used to calculating EndTimeOfDay. So
/// if your startTime on the first day is already pass by a time that would not add up to the count you
/// expected, until the next day comes. Remember that DailyTimeIntervalTrigger will use StartTimeOfDay
/// and endTimeOfDay as fresh per each day!
/// </para>
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
///         IJobDetail job = JobBuilder.Create&lt;MyJob>()
///             .WithIdentity("myJob")
///             .Build();
///
///         ITrigger trigger = TriggerBuilder.Create()
///             .WithIdentity("myTrigger", "myTriggerGroup")
///             .WithDailyTimeIntervalSchedule(x =>
///                        x.WithInterval(15, IntervalUnit.Minute)
///                        .StartingDailyAt(new TimeOnly(8, 0)))
///             .Build();
///
///         await scheduler.ScheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <author>James House</author>
/// <author>Zemian Deng saltnlight5@gmail.com</author>
/// <author>Nuno Maia (.NET)</author>
public sealed class DailyTimeIntervalScheduleBuilder : IScheduleBuilder, ITimeProviderAwareScheduleBuilder
{
    private TimeProvider timeProvider = TimeProvider.System;

    private int interval = 1;
    private IntervalUnit intervalUnit = IntervalUnit.Minute;
    private HashSet<DayOfWeek>? daysOfWeek;
    private TimeOnly? startTimeOfDay;
    private TimeOnly? endTimeOfDay;
    private int? endingDailyAfterCount;
    private int repeatCount = DailyTimeIntervalTriggerImpl.RepeatIndefinitely;
    private TimeZoneInfo? timeZone;

    private int misfireInstruction = MisfireInstruction.SmartPolicy;

    /// <summary>
    /// Every day, <see cref="DayOfWeek.Sunday"/> through <see cref="DayOfWeek.Saturday"/>.
    /// </summary>
    /// <remarks>
    /// Callers reach this through <see cref="OnEveryDay" />, which is also the builder's default.
    /// </remarks>
    internal static readonly IReadOnlySet<DayOfWeek> AllDaysOfTheWeek = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>());

    /// <summary>
    /// The business days of the week (for locales similar to the USA),
    /// <see cref="DayOfWeek.Monday"/> through <see cref="DayOfWeek.Friday"/>.
    /// </summary>
    /// <remarks>
    /// Callers reach this through <see cref="OnMondayThroughFriday" />.
    /// </remarks>
    internal static readonly IReadOnlySet<DayOfWeek> MondayThroughFriday = new HashSet<DayOfWeek>
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    };

    /// <summary>
    /// The weekend days of the week (for locales similar to the USA),
    /// <see cref="DayOfWeek.Saturday"/> and <see cref="DayOfWeek.Sunday"/>.
    /// </summary>
    /// <remarks>
    /// Callers reach this through <see cref="OnSaturdayAndSunday" />.
    /// </remarks>
    internal static readonly IReadOnlySet<DayOfWeek> SaturdayAndSunday = new HashSet<DayOfWeek>
    {
        DayOfWeek.Sunday,
        DayOfWeek.Saturday
    };

    private DailyTimeIntervalScheduleBuilder()
    {
    }

    /// <summary>
    /// Create a DailyTimeIntervalScheduleBuilder
    /// </summary>
    /// <remarks>
    /// The clock <see cref="EndingDailyAfterCount" /> computes against comes from the
    /// <see cref="TriggerBuilder{TJob}" /> that builds the trigger, so a scheduler configured with a
    /// custom <see cref="TimeProvider" /> is honored without handing one to this builder.
    /// </remarks>
    /// <returns>The new DailyTimeIntervalScheduleBuilder</returns>
    public static DailyTimeIntervalScheduleBuilder Create()
    {
        return new DailyTimeIntervalScheduleBuilder();
    }

    void ITimeProviderAwareScheduleBuilder.SetTimeProvider(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Build the actual Trigger -- NOT intended to be invoked by end users,
    /// but will rather be invoked by a TriggerBuilder which this
    /// ScheduleBuilder is given to.
    /// </summary>
    public IMutableTrigger Build()
    {
        // Deferred from EndingDailyAfterCount so the computation runs against the trigger
        // builder's clock and the schedule's final start time, interval and time zone. Computed
        // into a local so the builder stays reusable: a later Build() recomputes.
        TimeOnly? effectiveEndTimeOfDay = endTimeOfDay;
        if (endingDailyAfterCount.HasValue)
        {
            effectiveEndTimeOfDay = ComputeEndTimeOfDayFromCount(endingDailyAfterCount.Value);
        }

        DailyTimeIntervalTriggerImpl st = new DailyTimeIntervalTriggerImpl();
        st.RepeatInterval = interval;
        st.RepeatIntervalUnit = intervalUnit;
        st.MisfireInstructionCode = misfireInstruction;
        st.RepeatCount = repeatCount;
        st.timeZone = timeZone;

        if (daysOfWeek is not null)
        {
            st.DaysOfWeek = new HashSet<DayOfWeek>(daysOfWeek);
        }
        else
        {
            st.DaysOfWeek = new HashSet<DayOfWeek>(AllDaysOfTheWeek);
        }

        st.EndTimeOfDay = effectiveEndTimeOfDay ?? DailyTimeIntervalTriggerImpl.DefaultEndTimeOfDay;
        st.StartTimeOfDay = startTimeOfDay ?? DailyTimeIntervalTriggerImpl.DefaultStartTimeOfDay;

        return st;
    }

    /// <summary>
    /// Specify the time unit and interval for the Trigger to be produced.
    /// </summary>
    /// <param name="interval">the interval at which the trigger should repeat.</param>
    /// <param name="unit"> the time unit (IntervalUnit) of the interval.</param>
    /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
    /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
    /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
    public DailyTimeIntervalScheduleBuilder WithInterval(int interval, IntervalUnit unit)
    {
        if (!(unit == IntervalUnit.Second ||
              unit == IntervalUnit.Minute || unit == IntervalUnit.Hour))
        {
            Throw.ArgumentException("Invalid repeat IntervalUnit (must be Second, Minute or Hour).");
        }

        ValidateInterval(interval);
        this.interval = interval;
        intervalUnit = unit;
        return this;
    }

    /// <summary>
    /// Set the trigger to fire on the given days of the week.
    /// </summary>
    /// <param name="onDaysOfWeek">the days of the week to fire on; pass them as separate arguments
    /// or as any collection.</param>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    public DailyTimeIntervalScheduleBuilder OnDaysOfTheWeek(params IReadOnlyCollection<DayOfWeek> onDaysOfWeek)
    {
        if (onDaysOfWeek is null || onDaysOfWeek.Count == 0)
        {
            Throw.ArgumentException("Days of week must be an non-empty set.");
        }

        foreach (DayOfWeek day in onDaysOfWeek)
        {
            if (!AllDaysOfTheWeek.Contains(day))
            {
                Throw.ArgumentException("Invalid value for day of week: " + day);
            }
        }

        daysOfWeek = new HashSet<DayOfWeek>(onDaysOfWeek);
        return this;
    }

    /// <summary>
    /// Set the trigger to fire on the days from Monday through Friday.
    /// </summary>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    public DailyTimeIntervalScheduleBuilder OnMondayThroughFriday()
    {
        daysOfWeek = new HashSet<DayOfWeek>(MondayThroughFriday);
        return this;
    }

    /// <summary>
    /// Set the trigger to fire on the days Saturday and Sunday.
    /// </summary>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    public DailyTimeIntervalScheduleBuilder OnSaturdayAndSunday()
    {
        daysOfWeek = new HashSet<DayOfWeek>(SaturdayAndSunday);
        return this;
    }

    /// <summary>
    /// Set the trigger to fire on all days of the week.
    /// </summary>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    public DailyTimeIntervalScheduleBuilder OnEveryDay()
    {
        daysOfWeek = new HashSet<DayOfWeek>(AllDaysOfTheWeek);
        return this;
    }

    /// <summary>
    /// The time of day for this trigger to start firing each day. Defaults to <c>00:00:00</c>.
    /// </summary>
    /// <param name="timeOfDay">the time of day, with one-second resolution.</param>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="timeOfDay" /> carries precision finer than a whole second.
    /// </exception>
    public DailyTimeIntervalScheduleBuilder StartingDailyAt(TimeOnly timeOfDay)
    {
        TimeOnlyExtensions.ValidateWholeSeconds(timeOfDay, nameof(timeOfDay));
        startTimeOfDay = timeOfDay;
        return this;
    }

    /// <summary>
    /// The time of day for this trigger to end firing each day. Defaults to <c>23:59:59</c>.
    /// </summary>
    /// <param name="timeOfDay">the time of day, with one-second resolution.</param>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="timeOfDay" /> carries precision finer than a whole second.
    /// </exception>
    public DailyTimeIntervalScheduleBuilder EndingDailyAt(TimeOnly timeOfDay)
    {
        TimeOnlyExtensions.ValidateWholeSeconds(timeOfDay, nameof(timeOfDay));
        endTimeOfDay = timeOfDay;
        endingDailyAfterCount = null;
        return this;
    }

    /// <summary>
    /// End the daily window after the given number of firings: the EndTimeOfDay is calculated from
    /// the count, the interval and the StartTimeOfDay.
    /// </summary>
    /// <remarks>
    /// The calculation is deferred to <see cref="Build" />, so it sees the schedule's final start
    /// time, interval and time zone regardless of the order the builder was configured in, and it
    /// runs against the clock of the <see cref="TriggerBuilder{TJob}" /> that builds the trigger.
    /// A count too large for the daily window is therefore reported by <see cref="Build" />, not by
    /// this call.
    /// </remarks>
    /// <param name="count">the number of firings per day (&gt;= 1).</param>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    public DailyTimeIntervalScheduleBuilder EndingDailyAfterCount(int count)
    {
        if (count <= 0)
        {
            Throw.ArgumentException("Ending daily after count must be a positive number!");
        }

        endingDailyAfterCount = count;
        endTimeOfDay = null;
        return this;
    }

    /// <summary>
    /// Resolves <see cref="EndingDailyAfterCount" /> into a concrete end time of day, against the
    /// builder's clock and the schedule as finally configured.
    /// </summary>
    private TimeOnly ComputeEndTimeOfDayFromCount(int count)
    {
        if (startTimeOfDay is null)
        {
            Throw.ArgumentException("You must set the StartingDailyAt() when using EndingDailyAfterCount()!");
        }

        DateTimeOffset today = timeProvider.GetUtcNow();
        DateTimeOffset startTimeOfDayDate = startTimeOfDay.Value.OnDate(today);
        DateTimeOffset tomorrow = new DateTimeOffset(startTimeOfDayDate.AddDays(1).UtcDateTime.Date, TimeSpan.Zero);

        //apply proper offsets according to timezone
        TimeZoneInfo targetTimeZone = timeZone ?? TimeZoneInfo.Local;
        startTimeOfDayDate = TimeZones.ResolveLocal(startTimeOfDayDate.DateTime, targetTimeZone);
        tomorrow = TimeZones.ResolveLocal(tomorrow.DateTime, targetTimeZone);

        TimeSpan remainingMillisInDay = tomorrow - startTimeOfDayDate;
        TimeSpan intervalInMillis;
        if (intervalUnit == IntervalUnit.Second)
        {
            intervalInMillis = TimeSpan.FromSeconds(interval);
        }
        else if (intervalUnit == IntervalUnit.Minute)
        {
            intervalInMillis = TimeSpan.FromMinutes(interval);
        }
        else if (intervalUnit == IntervalUnit.Hour)
        {
            intervalInMillis = TimeSpan.FromHours(interval);
        }
        else
        {
            Throw.ArgumentException("The IntervalUnit: " + intervalUnit + " is invalid for this trigger.");
            return default;
        }

        if (remainingMillisInDay < intervalInMillis)
        {
            Throw.ArgumentException("The startTimeOfDay is too late with given Interval and IntervalUnit values.");
        }

        long maxNumOfCount = remainingMillisInDay.Ticks / intervalInMillis.Ticks;
        if (count > maxNumOfCount)
        {
            Throw.ArgumentException("The given count " + count + " is too large! The max you can set is " + maxNumOfCount);
        }

        TimeSpan incrementInMillis = TimeSpan.FromTicks((count - 1) * intervalInMillis.Ticks);
        DateTimeOffset endTimeOfDayDate = startTimeOfDayDate.Add(incrementInMillis);

        if (endTimeOfDayDate >= tomorrow)
        {
            Throw.ArgumentException("The given count " + count + " is too large! The max you can set is " + maxNumOfCount);
        }

        DateTime date = today.Date;
        date = date.Add(endTimeOfDayDate.TimeOfDay);
        return new TimeOnly(date.Hour, date.Minute, date.Second);
    }

    /// <summary>
    /// Say what the trigger should do when it misses a firing.
    /// </summary>
    /// <param name="instruction">the policy to apply; defaults to
    /// <see cref="DailyTimeIntervalTriggerMisfireInstruction.SmartPolicy" />.</param>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    /// <seealso cref="DailyTimeIntervalTriggerMisfireInstruction" />
    public DailyTimeIntervalScheduleBuilder WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction instruction)
    {
        misfireInstruction = (int) instruction;
        return this;
    }

    /// <summary>
    /// Set the number of times per day for interval to repeat.
    /// </summary>
    /// <remarks>
    /// Note: total fires per day = 1 (at startTimeOfDay) + repeatCount.
    /// The trigger resets each day and repeats on subsequent valid days.
    /// </remarks>
    public DailyTimeIntervalScheduleBuilder WithRepeatCount(int repeatCount)
    {
        this.repeatCount = repeatCount;
        return this;
    }

    /// <summary>
    /// TimeZone in which to base the schedule.
    /// </summary>
    /// <param name="timeZone">the time-zone for the schedule; <see langword="null" /> means the
    /// system's local time zone.</param>
    /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
    /// <seealso cref="IDailyTimeIntervalTrigger.TimeZone" />
    public DailyTimeIntervalScheduleBuilder InTimeZone(TimeZoneInfo? timeZone)
    {
        this.timeZone = timeZone;
        return this;
    }

    // ReSharper disable once UnusedParameter.Local
    private static void ValidateInterval(int interval)
    {
        if (interval <= 0)
        {
            Throw.ArgumentException("Interval must be a positive value.");
        }
    }
}
