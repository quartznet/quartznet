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
using Quartz.Spi;

namespace Quartz;

/// <summary>
/// Internal interface for schedule builders that support deferred H (hash)
/// token resolution. <see cref="TriggerBuilder"/> uses this to pass the
/// trigger key before <see cref="IScheduleBuilder.Build"/> is called.
/// </summary>
internal interface IHashKeyAwareScheduleBuilder
{
    bool RequiresHashKey { get; }
    void SetHashKey(TriggerKey key);
}

/// <summary>
/// CronScheduleBuilder is a <see cref="IScheduleBuilder" /> that defines
/// <see cref="CronExpression" />-based schedules for <see cref="ITrigger" />s.
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
/// <para>
/// Client code can then use the DSL to write code such as this:
/// </para>
/// <code>
/// IJobDetail job = JobBuilder.Create&lt;MyJob&gt;()
///   .WithIdentity("myJob")
///   .Build();
/// ITrigger trigger = newTrigger()
///  .WithIdentity(triggerKey("myTrigger", "myTriggerGroup"))
///  .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
///  .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
///  .Build();
/// scheduler.scheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="CronExpression" />
/// <seealso cref="ICronTrigger" />
/// <seealso cref="IScheduleBuilder" />
/// <seealso cref="SimpleScheduleBuilder" />
/// <seealso cref="CalendarIntervalScheduleBuilder" />
/// <seealso cref="TriggerBuilder" />
public sealed class CronScheduleBuilder : ScheduleBuilder<ICronTrigger>, IHashKeyAwareScheduleBuilder
{
    private CronExpression? cronExpression;
    private readonly string? deferredHashExpression;
    private TimeZoneInfo? deferredTimeZone;
    private int misfireInstruction = MisfireInstruction.SmartPolicy;

    private CronScheduleBuilder(CronExpression cronExpression)
    {
        if (cronExpression is null)
        {
            Throw.ArgumentNullException(nameof(cronExpression), "cronExpression cannot be null");
        }
        this.cronExpression = cronExpression;
    }

    /// <summary>
    /// Creates a CronScheduleBuilder with a deferred H (hash) expression
    /// that will be resolved when the trigger key is provided.
    /// </summary>
    private CronScheduleBuilder(string deferredHashExpression)
    {
        this.deferredHashExpression = deferredHashExpression;
    }

    /// <summary>
    /// Build the actual Trigger -- NOT intended to be invoked by end users,
    /// but will rather be invoked by a TriggerBuilder which this
    /// ScheduleBuilder is given to.
    /// </summary>
    /// <seealso cref="TriggerBuilder.WithSchedule" />
    public override IMutableTrigger Build()
    {
        if (cronExpression is null)
        {
            Throw.FormatException(
                "Cron expression contains H (hash) tokens which require a trigger identity for resolution. "
                + "Use TriggerBuilder with WithIdentity(), or provide an explicit hash key via "
                + "CronScheduleBuilder.CronScheduleWithHash() or new CronExpression(expression, hashKey).");
        }

        CronTriggerImpl ct = new CronTriggerImpl();

        ct.CronExpression = cronExpression;
        ct.TimeZone = cronExpression.TimeZone;
        ct.MisfireInstruction = misfireInstruction;

        return ct;
    }

    /// <summary>
    /// Create a CronScheduleBuilder with the given cron-expression - which
    /// is presumed to be valid cron expression (and hence only a RuntimeException
    /// will be thrown if it is not).
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="cronExpression">the cron expression to base the schedule on.</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    public static CronScheduleBuilder CronSchedule(string cronExpression)
    {
        if (cronExpression is null)
        {
            Throw.ArgumentException("cronExpression cannot be null", nameof(cronExpression));
        }

        if (CronExpression.ContainsHashToken(cronExpression))
        {
            string resolved = CronExpression.ResolveHash(cronExpression, 0);
            CronExpression.ValidateExpression(resolved);
            return new CronScheduleBuilder(cronExpression);
        }

        CronExpression.ValidateExpression(cronExpression);
        return CronScheduleNoParseException(cronExpression);
    }

    /// <summary>
    /// Create a CronScheduleBuilder with the given cron-expression string - which
    /// may not be a valid cron expression (and hence a ParseException will be thrown
    /// f it is not).
    /// </summary>
    /// <param name="presumedValidCronExpression">the cron expression string to base the schedule on</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    private static CronScheduleBuilder CronScheduleNoParseException(string presumedValidCronExpression)
    {
        try
        {
            return CronSchedule(new CronExpression(presumedValidCronExpression));
        }
        catch (FormatException e)
        {
            // all methods of construction ensure the expression is valid by this point...
            Throw.FormatException("CronExpression '" + presumedValidCronExpression + "' is invalid, which should not be possible, please report bug to Quartz developers.", e);
            return default;
        }
    }

    /// <summary>
    /// Create a CronScheduleBuilder with the given cron-expression.
    /// </summary>
    /// <param name="cronExpression">the cron expression to base the schedule on.</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    public static CronScheduleBuilder CronSchedule(CronExpression cronExpression)
    {
        return new CronScheduleBuilder(cronExpression);
    }

    /// <summary>
    /// Create a CronScheduleBuilder with H (hash) tokens resolved immediately
    /// using the given hash key.
    /// </summary>
    /// <param name="cronExpression">Cron expression that may contain H tokens</param>
    /// <param name="hashKey">A stable string key used to derive hash values (e.g., trigger name)</param>
    /// <returns>the new CronScheduleBuilder</returns>
    public static CronScheduleBuilder CronScheduleWithHash(string cronExpression, string hashKey)
    {
        return CronSchedule(new CronExpression(cronExpression, hashKey));
    }

    /// <summary>
    /// Create a CronScheduleBuilder with H (hash) tokens resolved immediately
    /// using the given integer hash seed.
    /// </summary>
    /// <param name="cronExpression">Cron expression that may contain H tokens</param>
    /// <param name="hashSeed">An integer seed used to derive hash values</param>
    /// <returns>the new CronScheduleBuilder</returns>
    public static CronScheduleBuilder CronScheduleWithHash(string cronExpression, int hashSeed)
    {
        return CronSchedule(new CronExpression(cronExpression, hashSeed));
    }

    /// <summary>
    /// Create a CronScheduleBuilder with a cron-expression that sets the
    /// schedule to fire every day at the given time (hour and minute).
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="hour">the hour of day to fire</param>
    /// <param name="minute">the minute of the given hour to fire</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    public static CronScheduleBuilder DailyAtHourAndMinute(int hour, int minute)
    {
        DateBuilder.ValidateHour(hour);
        DateBuilder.ValidateMinute(minute);

        string cronExpression = $"0 {minute} {hour} ? * *";

        return CronScheduleNoParseException(cronExpression);
    }

    /// <summary>
    /// Create a CronScheduleBuilder with a cron-expression that sets the
    /// schedule to fire at the given day at the given time (hour and minute) on the given days of the week.
    /// </summary>
    /// <param name="hour">the hour of day to fire</param>
    /// <param name="minute">the minute of the given hour to fire</param>
    /// <param name="daysOfWeek">the days of the week to fire</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    public static CronScheduleBuilder AtHourAndMinuteOnGivenDaysOfWeek(int hour, int minute, params DayOfWeek[] daysOfWeek)
    {
        if (daysOfWeek is null || daysOfWeek.Length == 0)
        {
            Throw.ArgumentException("You must specify at least one day of week.");
        }

        DateBuilder.ValidateHour(hour);
        DateBuilder.ValidateMinute(minute);

        string cronExpression = $"0 {minute} {hour} ? * {(int) daysOfWeek[0] + 1}";

        for (int i = 1; i < daysOfWeek.Length; i++)
        {
            cronExpression = cronExpression + "," + ((int) daysOfWeek[i] + 1);
        }

        return CronScheduleNoParseException(cronExpression);
    }

    /// <summary>
    /// Create a CronScheduleBuilder with a cron-expression that sets the
    /// schedule to fire one per week on the given day at the given time
    /// (hour and minute).
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="dayOfWeek">the day of the week to fire</param>
    /// <param name="hour">the hour of day to fire</param>
    /// <param name="minute">the minute of the given hour to fire</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    public static CronScheduleBuilder WeeklyOnDayAndHourAndMinute(DayOfWeek dayOfWeek, int hour, int minute)
    {
        DateBuilder.ValidateHour(hour);
        DateBuilder.ValidateMinute(minute);

        string cronExpression = $"0 {minute} {hour} ? * {(int) dayOfWeek + 1}";

        return CronScheduleNoParseException(cronExpression);
    }

    /// <summary>
    /// Create a CronScheduleBuilder with a cron-expression that sets the
    /// schedule to fire one per month on the given day of month at the given
    /// time (hour and minute).
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="dayOfMonth">the day of the month to fire</param>
    /// <param name="hour">the hour of day to fire</param>
    /// <param name="minute">the minute of the given hour to fire</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    public static CronScheduleBuilder MonthlyOnDayAndHourAndMinute(int dayOfMonth, int hour, int minute)
    {
        DateBuilder.ValidateDayOfMonth(dayOfMonth);
        DateBuilder.ValidateHour(hour);
        DateBuilder.ValidateMinute(minute);

        string cronExpression = $"0 {minute} {hour} {dayOfMonth} * ?";

        return CronScheduleNoParseException(cronExpression);
    }

    /// <summary>
    /// The <see cref="TimeZoneInfo" /> in which to base the schedule.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="tz">the time-zone for the schedule.</param>
    /// <returns>the updated CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression.TimeZone" />
    public CronScheduleBuilder InTimeZone(TimeZoneInfo tz)
    {
        if (cronExpression is not null)
        {
            cronExpression.TimeZone = tz;
        }
        else
        {
            deferredTimeZone = tz;
        }
        return this;
    }


    /// <summary>
    /// If the Trigger misfires, use the
    /// <see cref="MisfireInstruction.IgnoreMisfirePolicy" /> instruction.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated CronScheduleBuilder</returns>
    /// <seealso cref="MisfireInstruction.IgnoreMisfirePolicy" />
    public CronScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
    {
        misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
        return this;
    }

    /// <summary>
    /// If the Trigger misfires, use the <see cref="MisfireInstruction.CronTrigger.DoNothing" />
    /// instruction.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated CronScheduleBuilder</returns>
    /// <seealso cref="MisfireInstruction.CronTrigger.DoNothing" />
    public CronScheduleBuilder WithMisfireHandlingInstructionDoNothing()
    {
        misfireInstruction = MisfireInstruction.CronTrigger.DoNothing;
        return this;
    }

    /// <summary>
    /// If the Trigger misfires, use the <see cref="MisfireInstruction.CronTrigger.FireOnceNow" />
    /// instruction.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated CronScheduleBuilder</returns>
    /// <seealso cref="MisfireInstruction.CronTrigger.FireOnceNow" />
    public CronScheduleBuilder WithMisfireHandlingInstructionFireAndProceed()
    {
        misfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;
        return this;
    }

    internal CronScheduleBuilder WithMisfireHandlingInstruction(int readMisfireInstructionFromString)
    {
        misfireInstruction = readMisfireInstructionFromString;
        return this;
    }

    bool IHashKeyAwareScheduleBuilder.RequiresHashKey => deferredHashExpression is not null;

    void IHashKeyAwareScheduleBuilder.SetHashKey(TriggerKey key)
    {
        if (deferredHashExpression is not null)
        {
            // Use unambiguous encoding to avoid hash collisions between different keys.
            // Default-group keys are prefixed with ':' (discriminator) so they cannot collide
            // with the non-default format which always starts with a digit (length prefix).
            string hashKey = key.Group == SchedulerConstants.DefaultGroup
                ? $":{key.Name}"
                : $"{key.Group.Length}:{key.Group}{key.Name}";
            cronExpression = new CronExpression(deferredHashExpression, hashKey);
            if (deferredTimeZone is not null)
            {
                cronExpression.TimeZone = deferredTimeZone;
            }
        }
    }
}

/// <summary>
/// Extension methods that attach <see cref="CronScheduleBuilder" /> to <see cref="TriggerBuilder" />.
/// </summary>
public static class CronScheduleTriggerBuilderExtensions
{
    public static TriggerBuilder WithCronSchedule(this TriggerBuilder triggerBuilder, string cronExpression)
    {
        CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(cronExpression);
        return triggerBuilder.WithSchedule(builder);
    }

    public static TriggerBuilder WithCronSchedule(this TriggerBuilder triggerBuilder, string cronExpression, Action<CronScheduleBuilder> action)
    {
        CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(cronExpression);
        action(builder);
        return triggerBuilder.WithSchedule(builder);
    }

    /// <summary>
    /// Set the trigger's schedule to a cron expression with H (hash) tokens
    /// resolved using the given hash key.
    /// </summary>
    public static TriggerBuilder WithCronSchedule(this TriggerBuilder triggerBuilder, string cronExpression, string hashKey)
    {
        CronScheduleBuilder builder = CronScheduleBuilder.CronScheduleWithHash(cronExpression, hashKey);
        return triggerBuilder.WithSchedule(builder);
    }

    /// <summary>
    /// Set the trigger's schedule to a cron expression with H (hash) tokens
    /// resolved using the given hash key, with additional schedule configuration.
    /// </summary>
    public static TriggerBuilder WithCronSchedule(this TriggerBuilder triggerBuilder, string cronExpression, string hashKey, Action<CronScheduleBuilder> action)
    {
        CronScheduleBuilder builder = CronScheduleBuilder.CronScheduleWithHash(cronExpression, hashKey);
        action(builder);
        return triggerBuilder.WithSchedule(builder);
    }
}