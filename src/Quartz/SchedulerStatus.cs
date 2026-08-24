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
/// Where a scheduler is in its lifecycle. This is <see cref="IScheduler.Status" />, and it is the whole
/// of a scheduler's lifecycle state: one value to read and one value to match on, rather than several
/// booleans a reader would have to combine in the right order.
/// </summary>
/// <remarks>
/// <para>
/// The members are declared in lifecycle order, and a scheduler moves through them one way:
/// <see cref="Created" /> to <see cref="Running" />, back and forth between <see cref="Running" /> and
/// <see cref="Standby" />, then <see cref="ShuttingDown" /> and finally <see cref="Shutdown" />. Only
/// the middle pair is reversible; <see cref="Shutdown" /> is terminal.
/// </para>
/// <para>
/// The member names are a wire contract: the HTTP API returns this enum as its name, and still accepts
/// the numeric form. Names are never changed, and new members are appended.
/// </para>
/// </remarks>
public enum SchedulerStatus
{
    /// <summary>
    /// The scheduler's state could not be determined - a remote scheduler that did not answer, for
    /// instance. It is not a state a scheduler is ever in, only one a reader can be left with.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The scheduler has been built but never started, so it fires nothing yet.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Standby" />, which a scheduler reaches by being started and then stood
    /// down: both fire nothing, but only one of them has ever run, and only one has a
    /// <see cref="SchedulerMetadata.RunningSince" />. Standing a never-started scheduler down leaves it
    /// here, since it already fires nothing and "never started" is the more precise answer.
    /// </remarks>
    Created = 1,

    /// <summary>
    /// The scheduler has been started and is firing triggers.
    /// </summary>
    Running = 2,

    /// <summary>
    /// The scheduler is in standby: it is alive, but fires nothing until it is started again.
    /// </summary>
    Standby = 3,

    /// <summary>
    /// <see cref="IScheduler.Shutdown(bool, CancellationToken)" /> has been called and is running. The
    /// scheduler no longer fires triggers and no longer accepts work, and it can neither be restarted
    /// nor stood down - the only state left is <see cref="Shutdown" />.
    /// </summary>
    ShuttingDown = 4,

    /// <summary>
    /// The scheduler has been shut down and cannot be restarted. Its plugins, job store and thread pool
    /// are down with it.
    /// </summary>
    Shutdown = 5
}
