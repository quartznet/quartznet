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
/// The misfire instruction a scheduler gives every trigger whose own instruction nobody stated, in a
/// vocabulary every trigger family shares.
/// </summary>
/// <remarks>
/// <para>
/// Instruction codes are per family — <c>1</c> is <c>FireNow</c> to a simple trigger and
/// <c>FireAndProceed</c> to a cron one — so the default is stated here and mapped to each family's own
/// instruction when a trigger is scheduled:
/// </para>
/// <list type="table">
/// <listheader><term>Default</term><description>Simple; cron, calendar-interval, daily-time-interval and recurrence</description></listheader>
/// <item><term><see cref="SmartPolicy" /></term><description><c>SmartPolicy</c>; <c>SmartPolicy</c></description></item>
/// <item><term><see cref="IgnoreMisfires" /></term><description><c>IgnoreMisfires</c>; <c>IgnoreMisfires</c></description></item>
/// <item><term><see cref="FireAndProceed" /></term><description><see cref="SimpleTriggerMisfireInstruction.FireNow" />; <c>FireAndProceed</c></description></item>
/// <item><term><see cref="DoNothing" /></term><description><see cref="SimpleTriggerMisfireInstruction.NextWithRemainingCount" />; <c>DoNothing</c></description></item>
/// </list>
/// <para>
/// A simple trigger has no exact match for the last two. <c>FireNow</c> fires once now, and on a
/// repeating trigger re-anchors the remaining repeats at now, which is <c>NowWithRemainingCount</c>.
/// <c>NextWithRemainingCount</c> waits for the next scheduled time and counts the missed firings as
/// fired, so the series still ends where it would have; a one-shot trigger that misfires under it never
/// fires. A trigger type of your own is in none of the families and keeps its own smart policy.
/// </para>
/// <para>
/// Only an unstated instruction takes the default. Every <c>WithMisfireInstruction</c>, every
/// assignment of <c>MisfireInstructionCode</c>, a trigger read from a job store or deserialized, the
/// <c>MisfireInstruction</c> of a <c>[CronTrigger]</c> or <c>[SimpleTrigger]</c> and of the JSON
/// schedule all state it — <c>SmartPolicy</c> included, which then stays the smart policy. The default
/// is resolved when <c>ScheduleJob</c>, <c>ScheduleJobs</c>, <c>ScheduleTrigger</c> or
/// <c>RescheduleJob</c> stores the trigger, so the stored instruction is always explicit and an older
/// node reads the same number.
/// </para>
/// </remarks>
/// <seealso cref="QuartzSchedulerOptions.DefaultMisfireHandling" />
public enum DefaultMisfireHandling
{
    /// <summary>
    /// The family's smart policy, which is what every trigger did before there was a default.
    /// </summary>
    SmartPolicy = 0,

    /// <summary>
    /// Never evaluate the trigger for a misfire: fire it as soon as it can, and once for every firing it
    /// missed.
    /// </summary>
    IgnoreMisfires = 1,

    /// <summary>
    /// Fire once now, then go on with the schedule.
    /// </summary>
    FireAndProceed = 2,

    /// <summary>
    /// Skip the missed firings and wait for the next scheduled one.
    /// </summary>
    DoNothing = 3,
}
