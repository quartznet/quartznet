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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Selects rows of the FIRED_TRIGGERS table, for both
/// <see cref="IDriverDelegate.SelectFiredTriggerRecords" /> and
/// <see cref="IDriverDelegate.DeleteFiredTriggers" />.
/// </summary>
/// <remarks>
/// The filters combine with AND, and each one that is left null drops out of the statement entirely.
/// A query with every filter null therefore selects — or deletes — every fired trigger of the scheduler.
/// </remarks>
public sealed record FiredTriggerQuery
{
    /// <summary>
    /// Limits the result to the fired triggers of one trigger.
    /// </summary>
    public TriggerKey? Trigger { get; init; }

    /// <summary>
    /// Limits the result to the fired triggers of one job.
    /// </summary>
    public JobKey? Job { get; init; }

    /// <summary>
    /// Limits the result to the fired triggers owned by one scheduler instance.
    /// </summary>
    public string? InstanceName { get; init; }
}
