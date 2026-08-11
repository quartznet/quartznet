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

using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Describes the settings and capabilities of a given <see cref="IScheduler" /> instance.
/// </summary>
/// <remarks>
/// The values are an instantaneous snapshot: as soon as one is returned, the scheduler may
/// already have moved on.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed record SchedulerMetadata
{
    /// <summary>
    /// The name of the <see cref="IScheduler" />.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The instance id of the <see cref="IScheduler" />.
    /// </summary>
    public required string SchedulerInstanceId { get; init; }

    /// <summary>
    /// The assembly-qualified name (without version) of the <see cref="IScheduler" />
    /// implementation. A name rather than a <see cref="Type" />, because for a proxy the type
    /// may live only in the remote process and could never be materialized here.
    /// </summary>
    public required string SchedulerTypeName { get; init; }

    /// <summary>
    /// Whether this metadata describes a proxy to a scheduler running elsewhere — e.g. an
    /// <c>HttpScheduler</c> talking to a remote HTTP API — rather than the in-process instance.
    /// When <see langword="true" />, the values are the remote scheduler's, read over the wire.
    /// </summary>
    public bool IsProxy { get; init; }

    /// <summary>
    /// Whether the scheduler has been started.
    /// </summary>
    /// <remarks>
    /// Note: <see cref="Started" /> may be <see langword="true" /> even if
    /// <see cref="InStandbyMode" /> is <see langword="true" />.
    /// </remarks>
    public bool Started { get; init; }

    /// <summary>
    /// Whether the <see cref="IScheduler" /> is in standby mode.
    /// </summary>
    /// <remarks>
    /// Note: <see cref="Started" /> may be <see langword="true" /> even if
    /// <see cref="InStandbyMode" /> is <see langword="true" />.
    /// </remarks>
    public bool InStandbyMode { get; init; }

    /// <summary>
    /// Whether the <see cref="IScheduler" /> has been shut down.
    /// </summary>
    public bool Shutdown { get; init; }

    /// <summary>
    /// The <see cref="DateTimeOffset" /> at which the scheduler started running, or
    /// <see langword="null" /> if it has not been started.
    /// </summary>
    public DateTimeOffset? RunningSince { get; init; }

    /// <summary>
    /// The number of jobs executed since the <see cref="IScheduler" /> started.
    /// </summary>
    public int JobsExecuted { get; init; }

    /// <summary>
    /// The assembly-qualified name (without version) of the <see cref="IJobStore" />
    /// implementation the <see cref="IScheduler" /> uses.
    /// </summary>
    public required string JobStoreTypeName { get; init; }

    /// <summary>
    /// Whether the <see cref="IScheduler" />'s <see cref="IJobStore" /> supports persistence.
    /// </summary>
    public bool AdoJobStoreBasesPersistence { get; init; }

    /// <summary>
    /// Whether the <see cref="IScheduler" />'s <see cref="IJobStore" /> is clustered.
    /// </summary>
    public bool JobStoreClustered { get; init; }

    /// <summary>
    /// The assembly-qualified name (without version) of the thread pool implementation the
    /// <see cref="IScheduler" /> uses.
    /// </summary>
    public required string ThreadPoolTypeName { get; init; }

    /// <summary>
    /// The number of threads in the <see cref="IScheduler" />'s thread pool.
    /// </summary>
    public int ThreadPoolSize { get; init; }

    /// <summary>
    /// The version of Quartz that is running.
    /// </summary>
    public required string Version { get; init; }
}
