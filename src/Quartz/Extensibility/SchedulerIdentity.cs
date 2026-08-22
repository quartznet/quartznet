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

namespace Quartz.Extensibility;

/// <summary>
/// Who the job store is storing for, handed to <see cref="IJobStore.Initialize" /> once before the
/// store is used.
/// </summary>
/// <remarks>
/// The identity cannot be a constructor argument, because it is not fully known at construction: with
/// <see cref="QuartzSchedulerOptions.GenerateInstanceId" /> the instance id is produced by an
/// <see cref="IInstanceIdGenerator" /> that runs after the container has built the object graph.
/// Initialization is the first moment the value is settled, which is why it arrives here — the same
/// reasoning, and the same shape, as <see cref="Quartz.Impl.AdoJobStore.SemaphoreContext" /> on
/// <see cref="Quartz.Impl.AdoJobStore.ISemaphore.Initialize" />.
/// </remarks>
public sealed record SchedulerIdentity
{
    /// <summary>
    /// The name of the scheduler, shared by every node of a cluster.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The identifier of this scheduler node, unique within a cluster. A store records it against the
    /// firings this node owns, so that a listing can say which node is running what.
    /// </summary>
    public required string InstanceId { get; init; }
}
