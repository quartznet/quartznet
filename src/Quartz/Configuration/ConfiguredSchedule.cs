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

namespace Quartz.Configuration;

/// <summary>
/// The one spelling that switches a configured schedule off (#4137): a declared trigger's
/// <c>ConfigurationKey</c> value, or a schedule string in the <c>Quartz:Schedule</c> section.
/// </summary>
/// <remarks>
/// <para>
/// Exactly <c>-</c>, surrounding white space aside: Spring's <c>CRON_DISABLED</c>. It is no schedule any
/// parser accepts, so nothing that worked before means something else now; an empty or unparsable value
/// still throws.
/// </para>
/// <para>
/// A switched-off trigger is not scheduled, and one stored under its key is unscheduled as the scheduler
/// applies its registrations, so turning it off in configuration also stops a persistent store firing it.
/// </para>
/// </remarks>
internal static class ConfiguredSchedule
{
    /// <summary>
    /// The value that switches a configured schedule off.
    /// </summary>
    internal const string SwitchedOff = "-";

    /// <summary>
    /// Whether <paramref name="value" /> switches the schedule it stands for off.
    /// </summary>
    internal static bool IsSwitchedOff([NotNullWhen(true)] string? value)
    {
        return value is not null && value.AsSpan().Trim() is SwitchedOff;
    }
}

/// <summary>
/// A trigger configuration switched off: its key, the job it would have fired, and where the <c>-</c> was.
/// </summary>
/// <param name="Key">The key the trigger would have been stored under.</param>
/// <param name="JobKey">The job it would have fired.</param>
/// <param name="Source">The configuration key, or the path of the JSON schedule string, that holds <c>-</c>.</param>
internal sealed record SwitchedOffTrigger(TriggerKey Key, JobKey JobKey, string Source);
