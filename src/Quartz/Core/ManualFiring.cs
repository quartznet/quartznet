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

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Core;

/// <summary>
/// The one-shot trigger a manual firing stores: what <see cref="IScheduler.TriggerJob" /> and
/// <see cref="SchedulerJobExtensions.TriggerJob{TInput}" /> both build, so the two firings cannot drift
/// apart.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>MT_</c> and a random number, in the default group; due now; repeating never; marked with
/// <see cref="SchedulerConstants.ManualTrigger" />; and in the execution group the caller resolved with
/// <see cref="ManualFiringExecutionGroup" />, set as a name rather than read as a template.
/// </para>
/// <para>
/// The misfire instruction is stated as the smart policy, which for a trigger that never repeats is
/// fire-now. Stating it is what keeps a scheduler's <see cref="QuartzSchedulerOptions.DefaultMisfireHandling" />
/// off it on the <c>ScheduleJob</c> path the extension takes: under <see cref="DefaultMisfireHandling.DoNothing" />
/// a late manual run would otherwise be dropped rather than run. The stored code is the <c>0</c> it always was.
/// </para>
/// </remarks>
internal static class ManualFiring
{
    /// <summary>
    /// A new key for a manual firing's trigger.
    /// </summary>
    internal static TriggerKey NewTriggerKey()
    {
        long r = NextLong();
        if (r < 0)
        {
            r = -r;
        }

        return new TriggerKey("MT_" + Convert.ToString(r, CultureInfo.InvariantCulture), TriggerKey.DefaultGroup);
    }

    /// <summary>
    /// Builds the trigger that fires <paramref name="jobKey" /> once, now.
    /// </summary>
    /// <param name="jobKey">The job to fire.</param>
    /// <param name="data">The firing's own job data, used as the trigger's map; <see langword="null" /> for none.</param>
    /// <param name="executionGroup">The execution group the firing counts against, already resolved.</param>
    /// <param name="timeProvider">The clock "now" is read from.</param>
    internal static SimpleTriggerImpl CreateTrigger(JobKey jobKey, JobDataMap? data, string? executionGroup, TimeProvider timeProvider)
    {
        TriggerKey key = NewTriggerKey();
        SimpleTriggerImpl trigger = new(
            key.Name,
            key.Group,
            jobKey.Name,
            jobKey.Group,
            timeProvider.GetUtcNow(),
            null,
            0,
            TimeSpan.Zero,
            timeProvider);

        trigger.MisfireInstructionCode = MisfireInstruction.SmartPolicy;
        trigger.ComputeFirstFireTimeUtc(null);
        if (data is not null)
        {
            trigger.JobDataMap = data;
        }

        trigger.ExecutionGroup = executionGroup;
        Mark(trigger);
        return trigger;
    }

    /// <summary>
    /// Marks a trigger as a manual firing's, so its run is recorded as manual.
    /// </summary>
    /// <remarks>
    /// On the trigger's map, the store-neutral place a firing reads back: the key persists wherever the
    /// trigger does, as <see cref="SchedulerConstants.TraceParent" /> does.
    /// </remarks>
    internal static void Mark(IMutableTrigger trigger)
    {
        trigger.JobDataMap[SchedulerConstants.ManualTrigger] = "true";
    }

    /// <summary>
    /// A random number, positive or negative.
    /// </summary>
    private static long NextLong()
    {
        long temporaryLong = QuartzRandom.Next();
        temporaryLong = (temporaryLong << 32) + QuartzRandom.Next();
        if (QuartzRandom.Next(-1, 1) < 0)
        {
            return -temporaryLong;
        }

        return temporaryLong;
    }
}
