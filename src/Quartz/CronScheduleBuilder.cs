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
/// Lets <see cref="TriggerBuilder{TJob}" /> hand a schedule builder the trigger's identity before
/// it builds, so that <c>H</c> (hash) tokens in a cron expression can be spread by that identity.
/// </summary>
/// <remarks>
/// <para>
/// A cron expression such as <c>"H 3 * * *"</c> means "some minute past three, chosen for me".
/// Which minute is decided by hashing a key, so that every trigger using the same expression lands
/// on a different minute instead of all firing at once. The natural key is the trigger's own
/// <see cref="TriggerKey" />, but a trigger does not know its key until
/// <see cref="TriggerBuilder{TJob}.Build" /> runs — hence the deferral.
/// </para>
/// <para>
/// This is internal because only <see cref="TriggerBuilder{TJob}" /> is in a position to call it.
/// Callers who want to choose the hash key themselves build the expression directly:
/// <c>CronScheduleBuilder.CronSchedule(new CronExpression(expression, hashKey))</c>.
/// </para>
/// </remarks>
internal interface IHashKeyAwareScheduleBuilder
{
    /// <summary>
    /// Whether this builder is holding an expression whose <c>H</c> tokens are still unresolved.
    /// </summary>
    bool RequiresHashKey { get; }

    /// <summary>
    /// Resolve the pending <c>H</c> tokens against the trigger's identity. Called by
    /// <see cref="TriggerBuilder{TJob}.Build" /> before <see cref="IScheduleBuilder.Build" />.
    /// </summary>
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
/// ITrigger trigger = TriggerBuilder.Create()
///  .WithIdentity("myTrigger", "myTriggerGroup")
///  .WithCronSchedule("0 0/5 * * * ?")
///  .Build();
/// await scheduler.ScheduleJob(job, trigger);
/// </code>
/// <para>
/// For schedules that are easier to describe than to spell as a cron string, build the expression
/// with <see cref="CronExpressionBuilder" /> and pass it to
/// <see cref="CronSchedule(CronExpression)" />.
/// </para>
/// </remarks>
/// <seealso cref="CronExpression" />
/// <seealso cref="CronExpressionBuilder" />
/// <seealso cref="ICronTrigger" />
/// <seealso cref="IScheduleBuilder" />
/// <seealso cref="SimpleScheduleBuilder" />
/// <seealso cref="CalendarIntervalScheduleBuilder" />
/// <seealso cref="TriggerBuilder" />
public sealed class CronScheduleBuilder : IScheduleBuilder, IHashKeyAwareScheduleBuilder
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
    /// <seealso cref="TriggerBuilder{TJob}.WithSchedule" />
    public IMutableTrigger Build()
    {
        if (cronExpression is null)
        {
            Throw.FormatException(
                "Cron expression contains H (hash) tokens which require a trigger identity for resolution. "
                + "Use TriggerBuilder with WithIdentity(), or provide an explicit hash key via "
                + "CronScheduleBuilder.CronSchedule(new CronExpression(expression, hashKey)).");
        }

        CronTriggerImpl ct = new CronTriggerImpl();

        ct.CronExpression = cronExpression;
        ct.TimeZone = cronExpression.TimeZone;
        ct.MisfireInstructionCode = misfireInstruction;

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
    /// <remarks>
    /// This is also the way to resolve <c>H</c> (hash) tokens against something other than the
    /// trigger's own key: <c>CronSchedule(new CronExpression(expression, hashKey))</c>.
    /// </remarks>
    /// <param name="cronExpression">the cron expression to base the schedule on.</param>
    /// <returns>the new CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression" />
    public static CronScheduleBuilder CronSchedule(CronExpression cronExpression)
    {
        return new CronScheduleBuilder(cronExpression);
    }

    /// <summary>
    /// The <see cref="TimeZoneInfo" /> in which to base the schedule.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="timeZone">the time-zone for the schedule; <see langword="null" /> means the
    /// system's local time zone.</param>
    /// <returns>the updated CronScheduleBuilder</returns>
    /// <seealso cref="CronExpression.TimeZone" />
    public CronScheduleBuilder InTimeZone(TimeZoneInfo? timeZone)
    {
        if (cronExpression is not null)
        {
            // CronExpression falls back to the local time zone when its backing field is null
            cronExpression.TimeZone = timeZone!;
        }
        else
        {
            deferredTimeZone = timeZone;
        }
        return this;
    }

    /// <summary>
    /// Say what the trigger should do when it misses a firing.
    /// </summary>
    /// <param name="instruction">the policy to apply; defaults to
    /// <see cref="CronTriggerMisfireInstruction.SmartPolicy" />.</param>
    /// <returns>the updated CronScheduleBuilder</returns>
    /// <seealso cref="CronTriggerMisfireInstruction" />
    public CronScheduleBuilder WithMisfireInstruction(CronTriggerMisfireInstruction instruction)
    {
        misfireInstruction = (int) instruction;
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
