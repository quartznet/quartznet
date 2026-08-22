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
/// Where a scheduler is in its lifecycle, as one value rather than the three booleans
/// <see cref="IScheduler.IsStarted" />, <see cref="IScheduler.InStandbyMode" /> and
/// <see cref="IScheduler.IsShutdown" /> that a reader would otherwise have to combine — and combine in
/// the right order, since a shut-down scheduler is not "started" but also not merely stopped.
/// </summary>
/// <remarks>
/// Both the numeric values and the member names are a wire contract: the HTTP API returns this enum as
/// its name, and still accepts the numeric form. Members are never renamed, renumbered or reordered;
/// new ones are appended.
/// </remarks>
public enum SchedulerStatus
{
    /// <summary>
    /// The scheduler has been created but never started, or its state could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The scheduler has been started and is firing triggers.
    /// </summary>
    Running = 1,

    /// <summary>
    /// The scheduler is in standby: it is alive, but fires nothing until it is started again.
    /// </summary>
    Standby = 2,

    /// <summary>
    /// The scheduler has been shut down and cannot be restarted.
    /// </summary>
    Shutdown = 3
}
