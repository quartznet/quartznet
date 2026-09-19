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
/// A cron schedule for the job this class declares with <see cref="QuartzJobAttribute" />. Write it
/// as many times as the job has schedules; the generator emits one <c>AddTrigger&lt;T&gt;</c> call
/// per attribute.
/// </summary>
/// <remarks>
/// <para>
/// The expression is read at build time with the parser that reads it at run time, so one that
/// cannot parse is <c>QZ0001</c> rather than an exception while the host starts. <c>H</c> is
/// resolved against the trigger's key, exactly as it is for
/// <c>WithCronSchedule</c> — see <see href="https://www.quartz-scheduler.net/documentation/quartz-4.x/cron-expressions.html">cron expressions</see>.
/// </para>
/// <para>
/// This attribute says nothing on a class that does not also carry
/// <see cref="QuartzJobAttribute" />, and the generator reports <c>QZ1003</c> rather than letting a
/// schedule be declared and silently ignored.
/// </para>
/// <para>
/// What it cannot say — a start or end time, a calendar, job data, a retry policy, a preferred node
/// — is what <c>AddTrigger&lt;T&gt;</c> is for, and the two live together: a declared job can be
/// given further triggers by hand.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [QuartzJob]
/// [CronTrigger("0 0 0/6 * * ?")]
/// [CronTrigger("0 0 12 ? * MON-FRI", Name = "weekday-noon", TimeZone = "Europe/Helsinki")]
/// public sealed class CleanupJob : IJob
/// {
///     public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
///     {
///         return default;
///     }
/// }
/// </code>
/// </example>
/// <param name="cronExpression">
/// The cron expression the trigger fires on, in Quartz's six- or seven-field form.
/// </param>
/// <seealso cref="QuartzJobAttribute" />
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CronTriggerAttribute(string cronExpression) : Attribute
{
    /// <summary>
    /// The cron expression the trigger fires on.
    /// </summary>
    public string CronExpression { get; } = cronExpression;

    /// <summary>
    /// The trigger key's name. Defaults to the job's name, then that name with <c>-2</c>, <c>-3</c>
    /// and so on for the second and later schedules a job declares.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The trigger key's group. Defaults to the job's group.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// The time zone the schedule is read in, by the id <see cref="TimeZones.FindById" /> resolves —
    /// an IANA id such as <c>"Europe/Helsinki"</c> or a Windows one such as <c>"FLE Standard Time"</c>.
    /// Defaults to the scheduler's local time zone.
    /// </summary>
    /// <remarks>
    /// Resolved when the registration runs rather than at build time: the build machine's zone
    /// database is not the run-time machine's, and an analyzer refusing a zone your server has would
    /// be worse than no analyzer.
    /// </remarks>
    public string? TimeZone { get; init; }

    /// <summary>
    /// What the trigger should do about a firing it missed. Defaults to
    /// <see cref="CronTriggerMisfireInstruction.SmartPolicy" />.
    /// </summary>
    public CronTriggerMisfireInstruction MisfireInstruction { get; init; }

    /// <summary>
    /// The trigger's priority, which decides who wins when two triggers want the same moment and
    /// only one worker is free. Defaults to <see cref="TriggerConstants.DefaultPriority" />.
    /// </summary>
    public int Priority { get; init; } = TriggerConstants.DefaultPriority;

    /// <summary>
    /// What the schedule is for, carried on the trigger. Defaults to none.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The execution group the firing counts against, for a deployment that caps how many of a kind
    /// run at once. Defaults to none.
    /// </summary>
    public string? ExecutionGroup { get; init; }
}
