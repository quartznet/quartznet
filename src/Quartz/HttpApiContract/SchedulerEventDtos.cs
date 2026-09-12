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

namespace Quartz.HttpApiContract;

/// <summary>
/// What a scheduler has just done, as a reader of its live stream is told about it.
/// </summary>
/// <remarks>
/// <para>
/// One record for every kind of event rather than one record per kind. A live view lists them together,
/// filters them by kind and renders each as a line, so the shapes it would have to hold apart are shapes
/// it would immediately join again — and one record is one wire body, one snapshot and one
/// <c>[JsonSerializable]</c> entry rather than fourteen. <see cref="Kind" /> says which facets are
/// filled: the rest are <see langword="null" />, which is the same statement they make on every other
/// body this API writes.
/// </para>
/// <para>
/// Every event names the node that raised it. A cluster is one scheduler running in several processes,
/// each listening on its own, so without the id a reader cannot tell an event from the machine it is
/// looking at apart from an event from a peer — which is what
/// <see href="https://github.com/quartznet/quartznet/issues/3422" /> reported of the dashboard's own
/// feed.
/// </para>
/// <para>
/// Nothing here is a record of anything: the stream starts when a reader subscribes and carries what
/// happens after that. What a scheduler <em>has</em> run is <see cref="ExecutionHistoryEntryDto" />.
/// </para>
/// </remarks>
internal sealed record SchedulerEvent
{
    /// <summary>
    /// Which event this is, and therefore which of the facets below it carries.
    /// </summary>
    public SchedulerEventKind Kind { get; init; }

    /// <summary>
    /// The scheduler the event belongs to, as its own process spells it.
    /// </summary>
    /// <remarks>
    /// Carried on the event although the route already named the scheduler, because the broker fans one
    /// publication out to every subscriber of that name and a subscriber is entitled to assert what it
    /// was given. A <see cref="SchedulerEventKind.Heartbeat" /> names no scheduler: it is the route
    /// saying the connection is alive, not the scheduler saying anything.
    /// </remarks>
    public string SchedulerName { get; init; } = "";

    /// <summary>
    /// The node that raised the event, which is <see cref="IScheduler.SchedulerInstanceId" /> of the
    /// process it happened in.
    /// </summary>
    public string SchedulerInstanceId { get; init; } = "";

    /// <summary>
    /// When it happened, read off the clock of the scheduler it happened to.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>
    /// The job the event is about, where it is about one.
    /// </summary>
    public KeyDto? JobKey { get; init; }

    /// <summary>
    /// The trigger the event is about, where it is about one.
    /// </summary>
    public KeyDto? TriggerKey { get; init; }

    /// <summary>
    /// The firing the event is about, matching <see cref="IJobExecutionContext.FireInstanceId" />, where
    /// the event belongs to one.
    /// </summary>
    public string? FireInstanceId { get; init; }

    /// <summary>
    /// When the trigger fired, where the event knows a fire time.
    /// </summary>
    public DateTimeOffset? FireTimeUtc { get; init; }

    /// <summary>
    /// How long the job ran, on <see cref="SchedulerEventKind.JobExecuted" />.
    /// </summary>
    public TimeSpan? RunTime { get; init; }

    /// <summary>
    /// Whether a listener vetoed the execution, in which case the job never ran at all.
    /// </summary>
    public bool? Vetoed { get; init; }

    /// <summary>
    /// What the job faulted with, or <see langword="null" /> when it succeeded.
    /// </summary>
    public string? ExceptionMessage { get; init; }

    /// <summary>
    /// The state the scheduler is now in, on <see cref="SchedulerEventKind.SchedulerStateChanged" />.
    /// </summary>
    public SchedulerStatus? Status { get; init; }

    /// <summary>
    /// What went wrong, on <see cref="SchedulerEventKind.SchedulerError" />.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// The underlying failure behind <see cref="Message" />, where there was one.
    /// </summary>
    public string? Cause { get; init; }
}

/// <summary>
/// Which event a <see cref="SchedulerEvent" /> is. It travels as its name, and the name is the SSE
/// <c>event:</c> type of the frame that carries it.
/// </summary>
/// <remarks>
/// The first eleven are what a scheduler's listeners report and what the dashboard's hub has always
/// pushed. <see cref="JobInterrupted" /> and <see cref="TriggerInError" /> are the two listener events
/// that had no wire form; <see cref="Heartbeat" /> is not a listener event at all — the route emits it
/// so that a quiet scheduler is distinguishable from a dead connection.
/// </remarks>
internal enum SchedulerEventKind
{
    /// <summary>A job has begun running.</summary>
    JobExecuting,

    /// <summary>A job has finished, successfully, with an exception, or vetoed before it started.</summary>
    JobExecuted,

    /// <summary>A trigger has fired.</summary>
    TriggerFired,

    /// <summary>A trigger's firing has completed.</summary>
    TriggerCompleted,

    /// <summary>A trigger missed a firing and its misfire instruction has been applied.</summary>
    TriggerMisfired,

    /// <summary>A trigger has been paused.</summary>
    TriggerPaused,

    /// <summary>A trigger has been resumed.</summary>
    TriggerResumed,

    /// <summary>A job has been paused, and with it every trigger that fires it.</summary>
    JobPaused,

    /// <summary>A job has been resumed.</summary>
    JobResumed,

    /// <summary>One firing of a job was interrupted.</summary>
    JobInterrupted,

    /// <summary>A trigger was parked in the error state and will not fire until it is reset.</summary>
    TriggerInError,

    /// <summary>One node's scheduler has entered a new lifecycle state.</summary>
    SchedulerStateChanged,

    /// <summary>A scheduler reported an error it handled itself, such as a store operation it is retrying.</summary>
    SchedulerError,

    /// <summary>
    /// Nothing happened, and the connection is still open. Emitted by the event route rather than by a
    /// scheduler, and consumed by a reader rather than shown.
    /// </summary>
    Heartbeat
}
