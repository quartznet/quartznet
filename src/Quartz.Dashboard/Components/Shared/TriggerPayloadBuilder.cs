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

using System.Diagnostics.CodeAnalysis;

using Quartz.Impl.Triggers;

namespace Quartz.Dashboard.Components.Shared;

/// <summary>
/// Builds the trigger the dashboard posts back when a schedule is edited, by copying the trigger the
/// API returned and changing only the schedule.
/// </summary>
/// <remarks>
/// Re-assembling the trigger from the fields the page displays silently dropped whatever it did not
/// list. A null calendar name came back as an empty string, which every job store reads as a calendar
/// it then cannot find, so the trigger stopped firing (#3294); the node pin was dropped altogether;
/// and the trigger type was hardcoded, so a custom cron trigger was rewritten as a plain one. Cloning
/// keeps every field the trigger has, its runtime type included, so a trigger type derived from
/// <see cref="CronTriggerImpl" /> survives the edit as itself.
/// </remarks>
internal static class TriggerPayloadBuilder
{
    /// <summary>
    /// Produces <paramref name="trigger" /> with its cron expression replaced by
    /// <paramref name="cronExpression" />, or <see langword="false" /> when the trigger is not one
    /// this can faithfully rebuild.
    /// </summary>
    public static bool TryWithCronExpression(ITrigger trigger, string cronExpression, [NotNullWhen(true)] out ITrigger? payload)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        // A cron trigger that is not a CronTriggerImpl has a schedule this cannot set without
        // knowing the type, and guessing means posting back some other trigger than the one on
        // screen. Refuse it rather than reschedule the wrong thing.
        if (trigger is not CronTriggerImpl cronTrigger)
        {
            payload = null;
            return false;
        }

        CronTriggerImpl copy = (CronTriggerImpl) cronTrigger.Clone();
        copy.CronExpressionString = cronExpression;

        // The schedule just changed, so the stored next fire time belongs to the old expression, and
        // RescheduleJob honours a non-null one verbatim. Clearing it lets the first fire be computed
        // from the expression the user just entered.
        copy.NextFireTimeUtc = null;

        payload = copy;
        return true;
    }
}
