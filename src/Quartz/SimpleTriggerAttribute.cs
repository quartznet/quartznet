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

using System.Globalization;

using Quartz.Impl.Triggers;

namespace Quartz;

/// <summary>
/// A fixed-interval schedule for the job this class declares with <see cref="QuartzJobAttribute" />.
/// Write it as many times as the job has schedules, beside any <see cref="CronTriggerAttribute" />;
/// the generator emits one <c>AddTrigger&lt;T&gt;</c> call per attribute.
/// </summary>
/// <remarks>
/// <para>
/// The trigger starts when the scheduler is built, as any <c>AddTrigger&lt;T&gt;</c> without
/// <c>StartAt</c> does: it fires as the scheduler starts, then once per interval.
/// </para>
/// <para>
/// The interval is read at build time. One that is not a positive <see cref="TimeSpan" />, and a
/// <see cref="RepeatCount" /> below <c>-1</c>, are <c>QZ0005</c> rather than an exception while the
/// host starts, and the generated registration carries the interval as ticks, so nothing parses it at
/// run time.
/// </para>
/// <para>
/// This attribute says nothing on a class that does not also carry
/// <see cref="QuartzJobAttribute" />, and the generator reports <c>QZ1003</c> rather than letting a
/// schedule be declared and silently ignored.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [QuartzJob]
/// [SimpleTrigger("00:10:00")]
/// [SimpleTrigger("00:00:05", Name = "warm-up", RepeatCount = 3)]
/// public sealed class PollInboxJob : IJob
/// {
///     public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
///     {
///         return default;
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="QuartzJobAttribute" />
/// <seealso cref="CronTriggerAttribute" />
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SimpleTriggerAttribute : Attribute
{
    /// <summary>
    /// Declares the interval as a <see cref="TimeSpan" /> in its invariant form — <c>"00:10:00"</c> is
    /// ten minutes, <c>"1.00:00:00"</c> is a day.
    /// </summary>
    /// <remarks>
    /// A string, because <see cref="TimeSpan" /> is not something an attribute argument can be. The
    /// format is <see cref="TimeSpan.Parse(string, IFormatProvider)" />'s, parsed with the invariant
    /// culture, as <see cref="JobTimeoutAttribute" />'s is.
    /// </remarks>
    /// <param name="interval">The time between firings, as a positive invariant <see cref="TimeSpan" />.</param>
    /// <exception cref="ArgumentNullException"><paramref name="interval" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="interval" /> is not a <see cref="TimeSpan" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is zero or negative.</exception>
    public SimpleTriggerAttribute(string interval)
    {
        ArgumentNullException.ThrowIfNull(interval);

        if (!TimeSpan.TryParse(interval, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            Throw.ArgumentException(
                $"'{interval}' is not a TimeSpan. Spell the trigger's interval the way TimeSpan does, invariantly: \"00:10:00\" for ten minutes, \"1.00:00:00\" for a day.",
                nameof(interval));
        }

        if (parsed <= TimeSpan.Zero)
        {
            Throw.ArgumentOutOfRangeException(nameof(interval), $"A trigger's interval has to be longer than zero, and '{interval}' is not.");
        }

        Interval = parsed;
    }

    /// <summary>
    /// The time between firings.
    /// </summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// How many times the trigger repeats after its first firing, so the total number of firings is one
    /// more than this. Defaults to <see cref="SimpleTriggerImpl.RepeatIndefinitely" />, <c>-1</c>, which
    /// repeats forever.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below <c>-1</c>.</exception>
    public int RepeatCount
    {
        get;
        init
        {
            if (value < SimpleTriggerImpl.RepeatIndefinitely)
            {
                Throw.ArgumentOutOfRangeException(
                    nameof(RepeatCount),
                    $"RepeatCount cannot be {value.ToString(CultureInfo.InvariantCulture)}: it counts the firings after the first, so it is 0 or more, or -1 to repeat forever.");
            }

            field = value;
        }
    } = SimpleTriggerImpl.RepeatIndefinitely;

    /// <summary>
    /// The trigger key's name. Defaults to the job's name, then that name with <c>-2</c>, <c>-3</c>
    /// and so on for the second and later schedules a job declares, <see cref="CronTriggerAttribute" />
    /// included.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The trigger key's group. Defaults to the job's group.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// What the trigger should do about a firing it missed. Defaults to
    /// <see cref="SimpleTriggerMisfireInstruction.SmartPolicy" />.
    /// </summary>
    public SimpleTriggerMisfireInstruction MisfireInstruction { get; init; }

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

    /// <summary>
    /// A configuration key whose value, when it is set, is the interval the trigger fires on instead
    /// of <see cref="Interval" />. Defaults to none: the schedule is the attribute's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the container's <c>IConfiguration</c> as the scheduler is built, so
    /// <c>"Jobs:Poll:Interval"</c> is the <c>Interval</c> value of the <c>Jobs:Poll</c> section, or the
    /// environment variable <c>Jobs__Poll__Interval</c>. The constructor's interval stays required: it is
    /// checked at build time, and it is the schedule wherever the key is not set or no
    /// <c>IConfiguration</c> is registered. Only the interval is read; <see cref="RepeatCount" /> and
    /// the rest stay the attribute's.
    /// </para>
    /// <para>
    /// A configured value is not checked at build time. One that is not a positive invariant
    /// <see cref="TimeSpan" />, an empty one included, stops the host from starting with a
    /// <see cref="FormatException" /> naming the key, rather than leaving a schedule that never fires.
    /// </para>
    /// </remarks>
    public string? ConfigurationKey { get; init; }
}
