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

namespace Quartz.Util;

/// <summary>
/// The trigger settings a scheduling file states as a string, read the same way whoever read the file.
/// </summary>
/// <remarks>
/// Three readers declare triggers: the <c>Quartz:Schedule</c> section of <c>appsettings.json</c>, a
/// standalone <c>quartz_jobs.json</c>, and <c>quartz_jobs.xml</c>. A file in any of them declaring the
/// same trigger has to produce the same trigger, and a value whose meaning is decided in three parsers
/// has three meanings the first time one of them is edited. So they all come here, and each hands in
/// the phrase it names a trigger by so its own diagnostics read as they always did.
/// </remarks>
internal static class SchedulingFileValues
{
    /// <summary>
    /// Reads a trigger's retry policy from its stored form, for example <c>fixed;3;00:00:30</c>.
    /// </summary>
    /// <param name="value">The value the file stated, or <see langword="null" /> when it stated none.</param>
    /// <param name="trigger">How the reader names the trigger in a diagnostic, for example <c>Trigger 'nightly'</c>.</param>
    /// <exception cref="SchedulerConfigException">
    /// The file stated something that is not a retry policy. Refused as the file is read, rather than
    /// scheduling a trigger that silently never retries.
    /// </exception>
    internal static RetryPolicy? ReadRetryPolicy(string? value, string trigger)
    {
        if (value is null)
        {
            return null;
        }

        if (!RetryPolicy.TryParse(value, out RetryPolicy? policy))
        {
            throw new SchedulerConfigException($"{trigger}: '{value}' is not a retry policy.");
        }

        return policy;
    }

    /// <summary>
    /// Reads a trigger's preferred node: a scheduler instance id, <c>*</c> for an automatic pin, or
    /// nothing at all.
    /// </summary>
    /// <param name="value">The value the file stated, or <see langword="null" /> when it stated none.</param>
    /// <param name="trigger">How the reader names the trigger in a diagnostic, for example <c>Trigger 'nightly'</c>.</param>
    /// <remarks>
    /// A file that states no node leaves the trigger unpinned, which is also what a trigger built with
    /// no <c>WithPreferredNode</c> gets — so re-reading a file clears a pin that was set some other way,
    /// exactly as it clears an execution group or a retry policy.
    /// </remarks>
    /// <exception cref="SchedulerConfigException">
    /// The file stated a name the pinning protocol reserves. Refused as the file is read, rather than
    /// scheduling a trigger pinned to a node that cannot exist.
    /// </exception>
    internal static PreferredNode ReadPreferredNode(string? value, string trigger)
    {
        if (value is null)
        {
            return PreferredNode.None;
        }

        if (value.Trim() == PreferredNode.AutoSentinel)
        {
            return PreferredNode.Auto;
        }

        try
        {
            return PreferredNode.For(value);
        }
        catch (ArgumentException exception)
        {
            throw new SchedulerConfigException(
                $"{trigger}: '{value}' is not a preferred node - the pinning protocol reserves that name. "
                + "State a scheduler instance id, '*' to pin the trigger to whichever node fires it first, "
                + "or nothing at all to leave it unpinned.",
                exception);
        }
    }
}
