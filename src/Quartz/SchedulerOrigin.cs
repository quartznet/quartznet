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
/// Where a scheduler <see cref="ISchedulerRegistry.QuerySchedulers" /> reports came from.
/// </summary>
public enum SchedulerOrigin
{
    /// <summary>
    /// Registered with the container by <c>AddQuartz()</c> or <c>AddQuartz(name, …)</c>. The container
    /// holds its object graph, and the hosted service creates and starts it.
    /// </summary>
    Container = 0,

    /// <summary>
    /// Bound into the container's <see cref="Extensibility.ISchedulerRepository" /> rather than
    /// registered as a scheduler of this container: a scheduler built by
    /// <c>QuartzSchedulerBuilder</c> and made visible by hand. Nothing in the container owns its
    /// lifetime.
    /// </summary>
    Runtime = 1,

    /// <summary>
    /// Reached through a proxy — an <c>HttpScheduler</c> registered with <c>AddQuartzHttpClient</c>.
    /// Nothing in this process runs it and <see cref="SchedulerMetadata.IsProxy" /> is true.
    /// </summary>
    /// <remarks>
    /// What separates this from <see cref="Runtime" /> is where the scheduler is, not who registered
    /// it: every member of it is a network request, so a reader — a dashboard page, an operator's
    /// listing — can tell that pausing a trigger here lands in somebody else's process, that its
    /// history is kept there, and that this process has no live event stream from it.
    /// </remarks>
    Remote = 2,

    /// <summary>
    /// A window onto a scheduler that lives in somebody else's process and is reached through the
    /// database the two share: a never-started scheduler over that store, discovered by its
    /// <c>SCHED_NAME</c> rather than registered by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing in this process runs it and nothing ever will — its thread pool creates no threads and
    /// it is never started — so its own <see cref="SchedulerStatus" /> says only that it was built.
    /// What a reader is told instead is derived from the cluster's check-ins, which is the only
    /// liveness a shared store carries.
    /// </para>
    /// <para>
    /// The store is the contract, so everything the schedule <em>is</em> — jobs, triggers, calendars,
    /// pausing, rescheduling, triggering now — works, and whichever node picks the work up honours it.
    /// Everything that is a property of one process — starting, standing down, shutting down,
    /// interrupting a running job, an execution limit held in memory — cannot be done from here at all.
    /// <see cref="SchedulerRegistration.Target" /> says which attached store the window is onto.
    /// </para>
    /// </remarks>
    Window = 3
}
