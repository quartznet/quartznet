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
/// Conveys a scheduler-instance state record.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed record SchedulerStateRecord(string SchedulerInstanceId, DateTimeOffset CheckinTimestamp, TimeSpan CheckinInterval)
{
    /// <summary>
    /// The Quartz version the node runs, as it stamped its row at its last check-in — <c>4.6.0</c> — or
    /// <see langword="null" /> for a node older than 4.6, which never writes the column (#4067).
    /// </summary>
    /// <remarks>
    /// A node fires rounds of work that allows concurrent execution without <c>TRIGGER_ACCESS</c> only
    /// while every live peer's row carries 4.6 or later: an older node's misfire and edit writes are by
    /// key, and only the lock kept them off a row such a round had just fired.
    /// </remarks>
    public string? QuartzVersion { get; init; }
}
