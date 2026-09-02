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
/// What happens to a job or a trigger that is declared up front and whose key the scheduler already
/// knows.
/// </summary>
/// <remarks>
/// This is <see cref="QuartzOptions.Scheduling" />, and it governs every declared job and trigger,
/// however it was declared: <c>AddJob</c> and <c>AddTrigger</c> inside <c>AddQuartz(…)</c>, the
/// <c>Quartz:Scheduling</c> configuration section, and a scheduling file read by the XML or JSON
/// plugin. It says nothing about <see cref="IScheduler" />'s own members, which take their own
/// <see cref="AddJobOptions" /> and <see cref="ScheduleJobOptions" />.
/// </remarks>
public sealed class SchedulingOptions
{
    private bool overwriteExistingData = true;

    /// <summary>
    /// Whether a declared job or trigger replaces one already stored under the same key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On by default. Turning it off makes a duplicate key an error, unless
    /// <see cref="IgnoreDuplicates" /> says to pass over it instead.
    /// </para>
    /// <para>
    /// The default is a default rather than a statement: setting <see cref="IgnoreDuplicates" /> and
    /// leaving this one alone turns it off, because "pass over what is already there" and "replace what
    /// is already there" cannot both be meant and only one of them was asked for. Setting both
    /// explicitly is still refused at startup.
    /// </para>
    /// </remarks>
    /// <seealso cref="IgnoreDuplicates" />
    public bool OverwriteExistingData
    {
        get => overwriteExistingData;
        set
        {
            overwriteExistingData = value;
            OverwriteExistingDataStated = true;
        }
    }

    /// <summary>
    /// Whether anything assigned <see cref="OverwriteExistingData" />, as opposed to leaving its default
    /// in place.
    /// </summary>
    /// <remarks>
    /// Configuration binding and a <c>Configure&lt;QuartzOptions&gt;</c> callback both go through the
    /// setter, and the binder assigns only the keys a configuration source actually carries — so "stated"
    /// is exactly what a user or a configuration file wrote.
    /// </remarks>
    internal bool OverwriteExistingDataStated { get; private set; }

    /// <summary>
    /// <see cref="OverwriteExistingData" /> as the scheduling paths read it: what was stated, or —
    /// when nothing stated it — the opposite of <see cref="IgnoreDuplicates" />.
    /// </summary>
    internal bool EffectiveOverwriteExistingData
        => OverwriteExistingDataStated ? overwriteExistingData : !IgnoreDuplicates;

    /// <summary>
    /// Whether a declared job or trigger whose key is already stored is passed over rather than
    /// reported.
    /// </summary>
    /// <remarks>
    /// Only consulted when <see cref="OverwriteExistingData" /> is off, since replacing is already an
    /// answer to a duplicate key — and setting this one is enough to turn it off, since its default is
    /// not a statement. Setting both explicitly is refused at startup rather than resolved silently in
    /// favour of replacing, which is the opposite of what asking for this one means.
    /// </remarks>
    /// <seealso cref="OverwriteExistingData"/>
    public bool IgnoreDuplicates { get; set; }

    /// <summary>
    /// Whether a replaced trigger hands its firing history to the trigger replacing it.
    /// </summary>
    /// <remarks>
    /// Only consulted when <see cref="OverwriteExistingData" /> is on, since nothing is replaced
    /// otherwise. The new trigger adopts the old one's last fire time, and computes its next fire time
    /// from there rather than from its own start time — so restarting an application does not re-fire a
    /// schedule that has already run.
    /// </remarks>
    public bool ScheduleTriggerRelativeToReplacedTrigger { get; set; }
}