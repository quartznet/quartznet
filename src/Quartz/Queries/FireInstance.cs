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
/// One firing of a trigger: reserved by a scheduler node, or running on it. The listing projection
/// behind <see cref="IScheduler.QueryFireInstances" />.
/// </summary>
/// <remarks>
/// <para>
/// A fire instance is not a job execution context. It carries what a store can answer about a firing
/// from anywhere in a cluster — keys, times, which node — and nothing that lives only in the process
/// running the job: no job instance, no merged job data, no result, no cancellation handle. Code that
/// needs those is looking at an execution it hosts itself, and reaches them through an
/// <see cref="IJobListener" /> holding the contexts.
/// </para>
/// <para>
/// The store-side sibling is <see cref="Quartz.Impl.AdoJobStore.FiredTriggerRecord" />, which is one
/// FIRED_TRIGGERS row in full, for the ADO.NET store's recovery passes. This one is the store-neutral
/// projection every job store can produce.
/// </para>
/// </remarks>
/// <param name="FireInstanceId">Identifies this firing, and only this one: a trigger that fires again
/// gets a new value. This is the value <see cref="IJobExecutionContext.FireInstanceId" /> reports and
/// <see cref="IScheduler.InterruptFireInstance" /> takes.</param>
/// <param name="TriggerKey">The trigger that fired.</param>
/// <param name="JobKey">The job being run, or <see langword="null" /> while the firing is merely
/// <see cref="FireInstanceState.Acquired" /> — the job is not loaded until the firing starts.</param>
/// <param name="SchedulerInstanceId">The instance id of the scheduler node that reserved the firing or
/// is running it. Matches <see cref="IScheduler.SchedulerInstanceId" /> on that node.</param>
/// <param name="State">Whether the firing is reserved or running.</param>
/// <param name="FireTimeUtc">When the firing was recorded by the node that owns it: the reservation
/// time while <see cref="FireInstanceState.Acquired" />, the execution start once
/// <see cref="FireInstanceState.Executing" />. Elapsed time is therefore
/// <c>observerNow - FireTimeUtc</c> — a subtraction that mixes the observer's clock with the firing
/// node's, so it carries any clock skew between them and can come out negative; clamp it at zero.</param>
/// <param name="ScheduledFireTimeUtc">The fire time the schedule called for, as the owning node
/// recorded it. After a misfire this is the <em>rescheduled</em> time rather than the originally
/// scheduled one, so it can differ from <see cref="IJobExecutionContext.ScheduledFireTimeUtc" /> and
/// <c>FireTimeUtc - ScheduledFireTimeUtc</c> is not misfire lateness.</param>
/// <param name="ExecutionGroup">The execution group the trigger carried when it fired, if any. Rows
/// written by a 4.0 preview before this column was populated report <see langword="null" />.</param>
public sealed record FireInstance(
    string FireInstanceId,
    TriggerKey TriggerKey,
    JobKey? JobKey,
    string SchedulerInstanceId,
    FireInstanceState State,
    DateTimeOffset FireTimeUtc,
    DateTimeOffset? ScheduledFireTimeUtc,
    string? ExecutionGroup);
