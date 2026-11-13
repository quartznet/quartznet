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

namespace Quartz;

/// <summary>
/// Says a trigger's schedule in words, for a person to read.
/// </summary>
/// <remarks>
/// An extension rather than a member of <see cref="ITrigger" /> or of each trigger interface, so that no
/// interface grows and every hand-written trigger keeps compiling. It reads only what each trigger
/// interface already exposes.
/// </remarks>
public static class TriggerScheduleDescriptionExtensions
{
    /// <summary>
    /// The trigger's schedule in English: <c>Every weekday at 07:00 (America/Chicago)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For display only.</b> The wording may change in any release. Do not parse it, compare it or store
    /// it as a key: read the trigger's own members, such as <see cref="ICronTrigger.CronExpressionString" />,
    /// for the schedule itself.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// A cron trigger reads its expression, including <c>L</c>, <c>LW</c>, <c>W</c>, <c>#</c>, the year field and
    /// an expression parsed with <see cref="CronFormat.Unix" />: <c>At 12:00 on the last day of every month (UTC)</c>.
    /// A shape it cannot phrase is the expression itself: <c>0 0-5 9-17 * * ? (UTC)</c>.
    /// </description></item>
    /// <item><description>
    /// A simple trigger reads its interval and repeat count: <c>Every 5 minutes, 3 times</c>,
    /// <c>30 seconds after each run completes</c>, <c>Once</c>. It has no time zone, so it names none.
    /// </description></item>
    /// <item><description>
    /// A calendar-interval trigger reads its interval: <c>Every 2 months (UTC)</c>.
    /// </description></item>
    /// <item><description>
    /// A daily-time-interval trigger reads its interval, window and days:
    /// <c>Every 15 minutes from 09:00 to 17:00, Monday to Friday (UTC)</c>.
    /// </description></item>
    /// <item><description>
    /// A recurrence trigger reads its rule: <c>Monthly on the second Tuesday (UTC)</c>. A rule it cannot phrase,
    /// such as one with <c>BYSETPOS</c>, is the rule itself.
    /// </description></item>
    /// </list>
    /// <para>
    /// Every kind but the simple trigger names the time zone it is read in. A trigger with a calendar adds
    /// <c>, excluding calendar 'holidays'</c>. The time of day of a calendar-interval or recurrence trigger
    /// comes from its start time, which the text does not repeat.
    /// </para>
    /// </remarks>
    /// <param name="trigger">The trigger to describe.</param>
    /// <returns>
    /// The schedule in words, or <see langword="null" /> for a trigger kind this does not know, such as an
    /// <see cref="ITrigger" /> of your own, and for a cron or recurrence trigger with no expression yet.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="trigger" /> is <see langword="null" />.</exception>
    public static string? DescribeSchedule(this ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return ScheduleDescriber.Describe(trigger);
    }
}
