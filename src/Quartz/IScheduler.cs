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
/// This is the main interface of a Quartz Scheduler.
/// </summary>
/// <remarks>
/// 	<para>
///         A <see cref="IScheduler"/> maintains a registry of
///         <see cref="IJobDetail"/>s and <see cref="ITrigger"/>s. Once
///         registered, the <see cref="IScheduler"/> is responsible for executing
///         <see cref="IJob"/> s when their associated <see cref="ITrigger"/> s
///         fire (when their scheduled time arrives).
///     </para>
/// 	<para>
/// 		<see cref="IScheduler"/> instances are produced by a
///         <see cref="ISchedulerFactory"/>. A scheduler that has already been
///         created/initialized can be found and used through the same factory that
///         produced it. After a <see cref="IScheduler"/> has been created, it is in
///         "stand-by" mode, and must have its <see cref="IScheduler.Start"/> method
///         called before it will fire any <see cref="IJob"/>s.
///     </para>
/// 	<para>
/// 		<see cref="IJob"/> s are to be created by the 'client program', by
///         defining a class that implements the <see cref="IJob"/> interface.
///         <see cref="IJobDetail"/> objects are then created (also by the client) to
///         define a individual instances of the <see cref="IJob"/>.
///         <see cref="IJobDetail"/> instances can then be registered with the
///         <see cref="IScheduler"/> via the %IScheduler.ScheduleJob(JobDetail,
///         Trigger)% or %IScheduler.AddJob(JobDetail, AddJobOptions)% method.
///     </para>
/// 	<para>
/// 		<see cref="ITrigger"/> s can then be defined to fire individual
///         <see cref="IJob"/> instances based on given schedules.
///         <see cref="ISimpleTrigger"/> s are most useful for one-time firings, or
///         firing at an exact moment in time, with N repeats with a given delay between
///         them. <see cref="ICronTrigger"/> s allow scheduling based on time of day,
///         day of week, day of month, and month of year.
///     </para>
/// 	<para>
/// 		<see cref="IJob"/> s and <see cref="ITrigger"/> s have a name and
///         group associated with them, which should uniquely identify them within a single
///         <see cref="IScheduler"/>. The 'group' feature may be useful for creating
///         logical groupings or categorizations of <see cref="IJob"/>s and
///         <see cref="ITrigger"/>s. If you don't have need for assigning a group to a
///         given <see cref="IJob"/>s of <see cref="ITrigger"/>s, then you can use
///         the <see cref="Key{T}.DefaultGroup"/> constant defined on
///         this interface.
///     </para>
/// 	<para>
///         Stored <see cref="IJob"/> s can also be 'manually' triggered through the
///         use of the %IScheduler.TriggerJob(JobKey)% function.
///     </para>
/// 	<para>
///         Client programs may also be interested in the 'listener' interfaces that are
///         available from Quartz. The <see cref="IJobListener"/> interface provides
///         notifications of <see cref="IJob"/> executions. The
///         <see cref="ITriggerListener"/> interface provides notifications of
///         <see cref="ITrigger"/> firings. The <see cref="ISchedulerListener"/>
///         interface provides notifications of <see cref="IScheduler"/> events and
///         errors.  Listeners can be associated with local schedulers through the
///         <see cref="IListenerManager" /> interface.
///     </para>
/// 	<para>
///         The setup/configuration of a <see cref="IScheduler"/> instance is very
///         customizable. Please consult the documentation distributed with Quartz.
///     </para>
/// 	<para>
///         Disposing an instance releases what that instance owns, and the rule is ownership rather
///         than convention. A <b>local</b> scheduler owns the execution it drives, so disposing it is
///         <see cref="Shutdown(bool, CancellationToken)"/> with <c>waitForJobsToComplete: false</c> —
///         the scheduler cannot be restarted afterwards, and disposing one that is already shut down
///         does nothing. Call <c>Shutdown(waitForJobsToComplete: true)</c> yourself first when running
///         jobs should be allowed to finish; <c>await using</c> is the shape for "stop this when the
///         block ends", not for a graceful drain.
///     </para>
/// 	<para>
///         A <b>proxy</b> for a scheduler in another process — <c>HttpScheduler</c> — owns only the
///         connection to it. Disposing one releases that and never shuts the remote scheduler down: a
///         client leaving is not an instruction to stop scheduling for everybody else.
///     </para>
/// </remarks>
/// <seealso cref="IJob"/>
/// <seealso cref="IJobDetail"/>
/// <seealso cref="ITrigger"/>
/// <seealso cref="IJobListener"/>
/// <seealso cref="ITriggerListener"/>
/// <seealso cref="ISchedulerListener"/>
/// <author>Marko Lahma (.NET)</author>
public interface IScheduler : IAsyncDisposable
{
    /// <summary>
    /// Returns the name of the <see cref="IScheduler" />.
    /// </summary>
    string SchedulerName { get; }

    /// <summary>
    /// Returns the instance Id of the <see cref="IScheduler" />.
    /// </summary>
    string SchedulerInstanceId { get; }

    /// <summary>
    /// The clock this scheduler reads: what it calls "now" when it decides a trigger is due, and what a
    /// trigger built for it should compute its fire times from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scheduler configured with a <see cref="System.TimeProvider" /> of its own — a test driving one
    /// forward by hand, an application on a clock it controls — answers with that one. Code that builds
    /// a trigger for a scheduler should read the clock from the scheduler rather than reach for
    /// <see cref="System.TimeProvider.System" />, or it computes "in ten minutes" against a different
    /// clock from the one the scheduling loop will compare the answer to. That includes a job
    /// rescheduling itself from inside <c>Execute</c>, which reads
    /// <c>context.Scheduler.TimeProvider</c>.
    /// </para>
    /// <para>
    /// A default implementation answers <see cref="System.TimeProvider.System" />, so a scheduler
    /// written outside this repository needs no change and reports what its triggers would have used
    /// anyway. A proxy for a scheduler in another process answers the same, and cannot do better: the
    /// clock that matters is the remote scheduler's, and it is not this process's to read.
    /// </para>
    /// </remarks>
    TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>
    /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
    /// </summary>
    SchedulerContext Context { get; }

    /// <summary>
    /// Where the <see cref="IScheduler" /> is in its lifecycle.
    /// </summary>
    /// <remarks>
    /// The value is an 'instantaneous' snapshot: by the time it is read, the scheduler may already have
    /// moved on. A scheduler in another process answers this over the network.
    /// </remarks>
    /// <seealso cref="Start" />
    /// <seealso cref="Standby" />
    /// <seealso cref="Shutdown(bool, CancellationToken)" />
    SchedulerStatus Status { get; }

    /// <summary>
    /// Get a <see cref="SchedulerMetadata" /> object describing the settings
    /// and capabilities of the scheduler instance.
    /// </summary>
    /// <remarks>
    /// Note that the data returned is an 'instantaneous' snap-shot, and that as
    /// soon as it's returned, the metadata values may be different.
    /// </remarks>
    ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the firings the scheduler knows about — by default the ones that are running — as
    /// <see cref="FireInstance" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a persistent job store this sees the whole cluster, not just this node, because a firing is
    /// a durable record rather than process-local state. Filter by
    /// <see cref="FireInstanceQuery.SchedulerInstanceId" /> to ask about one node —
    /// <see cref="SchedulerInstanceId" /> for this one.
    /// </para>
    /// <para>
    /// The result is an 'instantaneous' snapshot: by the time it is returned, firings may have started
    /// or finished.
    /// </para>
    /// <para>
    /// A firing an <see cref="ITriggerListener" /> vetoes does not linger here: applying the veto
    /// completes the firing, which removes it. It can be listed for the instant between the store
    /// recording the firing and the veto being decided, and never after.
    /// </para>
    /// </remarks>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <seealso cref="FireInstance" />
    ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the scheduler nodes the job store knows about, as <see cref="ClusterNode" />s — this node
    /// first, then the rest by instance id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This node is always listed, and is the only one whose <see cref="ClusterNode.IsCurrentNode" /> is
    /// <see langword="true" />; its <see cref="ClusterNode.InstanceId" /> is
    /// <see cref="SchedulerInstanceId" />. A scheduler that is not clustered — the in-memory store, or a
    /// persistent one with clustering switched off — answers with that single node and no check-in
    /// times, which is the truthful answer rather than an empty list.
    /// </para>
    /// <para>
    /// The states are what this node believes, read off its own clock against the check-in stamps the
    /// other nodes wrote, and they are decided by the same predicate cluster recovery applies. A node
    /// reported <see cref="ClusterNodeState.Failed" /> has its in-flight work taken over on the next
    /// check-in pass, after which it stops being listed.
    /// </para>
    /// <para>
    /// The result is an 'instantaneous' snapshot: by the time it is returned a node may have checked in
    /// or been swept away. Join it with <see cref="QueryFireInstances" /> on
    /// <see cref="FireInstance.SchedulerInstanceId" /> to see what each node is running.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <seealso cref="ClusterNode" />
    ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a reference to the scheduler's <see cref="IListenerManager" />,
    /// through which listeners may be registered.
    /// </summary>
    /// <returns>the scheduler's <see cref="IListenerManager" /></returns>
    /// <seealso cref="ListenerManager" />
    /// <seealso cref="IJobListener" />
    /// <seealso cref="ITriggerListener" />
    /// <seealso cref="ISchedulerListener" />
    IListenerManager ListenerManager { get; }

    /// <summary>
    /// Starts the <see cref="IScheduler" />'s threads that fire <see cref="ITrigger" />s, taking it to
    /// <see cref="SchedulerStatus.Running" />. A newly built scheduler is
    /// <see cref="SchedulerStatus.Created" /> and fires nothing until this is called; so is one that
    /// <see cref="Standby" /> has stood down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The misfire/recovery process will be started, if it is the initial call
    /// to this method on this scheduler instance.
    /// </para>
    /// <para>
    /// Starting a scheduler that is already <see cref="SchedulerStatus.Running" /> does nothing at all:
    /// no listener is told the scheduler started, and the job store is not told it resumed, because
    /// neither happened.
    /// </para>
    /// </remarks>
    /// <exception cref="SchedulerException">
    /// The scheduler has been shut down, or is shutting down. That is terminal, so there is nothing to
    /// start.
    /// </exception>
    /// <seealso cref="StartDelayed(TimeSpan, CancellationToken)"/>
    /// <seealso cref="Standby"/>
    /// <seealso cref="Shutdown"/>
    ValueTask Start(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <see cref="Start" /> after the indicated delay.
    /// (This call does not block). This can be useful within applications that
    /// have initializers that create the scheduler immediately, before the
    /// resources needed by the executing jobs have been fully initialized.
    /// </summary>
    /// <seealso cref="Start"/>
    /// <seealso cref="Standby"/>
    /// <seealso cref="Shutdown"/>
    ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Temporarily halts the <see cref="IScheduler" />'s firing of <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="Start" /> is called (to bring the scheduler out of
    /// stand-by mode), trigger misfire instructions will NOT be applied
    /// during the execution of the <see cref="Start" /> method - any misfires
    /// will be detected immediately afterward (by the <see cref="IJobStore" />'s
    /// normal process).
    /// </para>
    /// <para>
    /// The scheduler is not destroyed, and can be re-started at any time. A scheduler that is running
    /// becomes <see cref="SchedulerStatus.Standby" />.
    /// </para>
    /// <para>
    /// Standing down a scheduler that is not running does nothing at all: no listener is told it went
    /// into standby, and the job store is not told it paused, because neither happened. A scheduler
    /// that has never been started is already firing nothing and stays
    /// <see cref="SchedulerStatus.Created" />, which is the more precise answer than standby; one
    /// already in standby is in the state being asked for.
    /// </para>
    /// </remarks>
    /// <exception cref="SchedulerException">
    /// The scheduler has been shut down, or is shutting down. Neither is a state to be stood down from.
    /// </exception>
    /// <seealso cref="Start"/>
    /// <seealso cref="PauseAll"/>
    ValueTask Standby(CancellationToken cancellationToken = default);

    /// <summary>
    /// Halts the <see cref="IScheduler" />'s firing of <see cref="ITrigger" />s,
    /// and cleans up all resources associated with the Scheduler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scheduler cannot be re-started.
    /// </para>
    /// <para>
    /// The scheduler is <see cref="SchedulerStatus.ShuttingDown" /> for the duration and
    /// <see cref="SchedulerStatus.Shutdown" /> once its plugins and job store are down. It does not
    /// pass through <see cref="SchedulerStatus.Standby" /> on the way, and no listener is told it stood
    /// down: a scheduler being torn down is not one waiting to be started again.
    /// </para>
    /// </remarks>
    /// <param name="waitForJobsToComplete">
    /// if <see langword="true" /> the scheduler will not allow this method
    /// to return until all currently executing jobs have completed.
    /// </param>
    /// <param name="cancellationToken">
    /// Bounds the wait for running jobs, so that a shutdown can be given a deadline. Cancelling it stops
    /// the scheduler waiting; it does not cancel the jobs, and the shutdown itself always runs to the
    /// end, so the job store, the plugins and the listeners are told the scheduler has stopped either
    /// way.
    /// </param>
    ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the given <see cref="IJobDetail" /> to the
    /// Scheduler, and associate the given <see cref="ITrigger" /> with
    /// it.
    /// </summary>
    /// <remarks>
    /// If the given Trigger does not reference any <see cref="IJob" />, then it
    /// will be set to reference the Job passed with it into this method.
    /// </remarks>
    ValueTask<DateTimeOffset> ScheduleJob(
        IJobDetail jobDetail,
        ITrigger trigger,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ScheduleJob(IJobDetail, ITrigger, CancellationToken)" />
    /// <param name="jobDetail">The job to store.</param>
    /// <param name="trigger">The trigger to store.</param>
    /// <param name="options">
    /// Whether an already stored job or trigger with the same key is over-written. The whole operation
    /// is one store operation under one lock, so an upsert needs no read-then-write of its own and
    /// cannot lose a race with another node doing the same thing.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <remarks>
    /// <para>
    /// <paramref name="options" /> has no default, unlike everywhere else it appears. Giving it one
    /// would make <c>ScheduleJob(job, trigger)</c> ambiguous between this overload and the one above.
    /// </para>
    /// </remarks>
    ValueTask<DateTimeOffset> ScheduleJob(
        IJobDetail jobDetail,
        ITrigger trigger,
        ScheduleJobOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule the given <see cref="ITrigger" /> with the
    /// <see cref="IJob" /> identified by the <see cref="ITrigger" />'s settings.
    /// </summary>
    ValueTask<DateTimeOffset> ScheduleJob(
        ITrigger trigger,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ScheduleJob(ITrigger, CancellationToken)" />
    /// <param name="trigger">The trigger to store.</param>
    /// <param name="options">
    /// Whether an already stored trigger with the same key is over-written. Replacing is one store
    /// operation under the store's own lock, so scheduling over an existing trigger needs no
    /// <c>CheckExists</c> / <c>UnscheduleJob</c> / <c>ScheduleJob</c> dance and cannot lose a race with
    /// another node doing the same thing.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <remarks>
    /// <para>
    /// A replaced trigger keeps its <see cref="ITrigger.PreviousFireTimeUtc" />, so a job that reads
    /// <see cref="IJobExecutionContext.PreviousFireTimeUtc" /> is not told the schedule has never fired
    /// merely because its trigger was rewritten.
    /// </para>
    /// <para>
    /// <paramref name="options" /> has no default, unlike everywhere else it appears. Giving it one
    /// would make <c>ScheduleJob(trigger)</c> ambiguous between this overload and the one above.
    /// </para>
    /// </remarks>
    ValueTask<DateTimeOffset> ScheduleJob(
        ITrigger trigger,
        ScheduleJobOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule all the given jobs with the related set of triggers.
    /// </summary>
    /// <remarks>
    /// <para>If any of the given jobs or triggers already exist (or more
    /// specifically, if the keys are not unique) and <see cref="ScheduleJobOptions.Replace" />
    /// is not set then an exception will be thrown.</para>
    /// </remarks>
    ValueTask ScheduleJobs(
        IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule the given job with the related set of triggers.
    /// </summary>
    /// <remarks>
    /// If any of the given job or triggers already exist (or more
    /// specifically, if the keys are not unique) and <see cref="ScheduleJobOptions.Replace" />
    /// is not set then an exception will be thrown.
    /// </remarks>
    ValueTask ScheduleJob(
        IJobDetail jobDetail,
        IReadOnlyCollection<ITrigger> triggersForJob,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the indicated <see cref="ITrigger" /> from the scheduler.
    /// <para>If the related job does not have any other triggers, and the job is
    /// not durable, then the job will also be deleted.</para>
    /// </summary>
    ValueTask<bool> UnscheduleJob(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all of the indicated <see cref="ITrigger" />s from the scheduler.
    /// </summary>
    /// <remarks>
    /// <para>If the related job does not have any other triggers, and the job is
    /// not durable, then the job will also be deleted.</para>
    /// <para>Note that while this bulk operation is likely more efficient than
    /// invoking <see cref="UnscheduleJob" /> several
    /// times, it may have the adverse affect of holding data locks for a
    /// single long duration of time (rather than lots of small durations
    /// of time).</para>
    /// <para>One <see cref="ISchedulerListener.JobUnscheduled" /> is raised per key the removal
    /// applied to, and the scheduling change is signalled once for the whole call. A key that was
    /// not found raises nothing, as the single-key form raises nothing when it answers
    /// <see langword="false" />.</para>
    /// </remarks>
    /// <returns>
    /// The keys this call removed, in the order they were given. A key that names no trigger is
    /// simply absent, never a throw — <c>result.Count == triggerKeys.Count</c> is the "every key was
    /// found" answer, and the list itself says which ones when it is not.
    /// </returns>
    /// <seealso cref="UnscheduleJob" />
    ValueTask<List<TriggerKey>> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove every <see cref="ITrigger" /> in the matching groups from the scheduler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group is the correlation axis: everything scheduled for one saga, one tenant or one
    /// conversation shares a trigger group, and this is how the whole of it is called off in one
    /// call rather than one round trip per key — and without first listing the keys, which is a
    /// window in which another node can add one more.
    /// </para>
    /// <para>A job left with no triggers by the removal is deleted too if it is not durable,
    /// exactly as the single-key <see cref="UnscheduleJob" /> does, but the answer names triggers
    /// only.</para>
    /// <para>One <see cref="ISchedulerListener.JobUnscheduled" /> is raised per removed key, and the
    /// scheduling change is signalled once for the whole call. A matcher that matched nothing raises
    /// nothing.</para>
    /// </remarks>
    /// <param name="matcher">
    /// Selects the trigger groups to empty. Required — there is no "unschedule the default group"
    /// reading of <see langword="null" /> worth risking on a destructive call.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The keys of the triggers this call removed.</returns>
    /// <seealso cref="UnscheduleJobs(IReadOnlyCollection{TriggerKey}, CancellationToken)" />
    ValueTask<List<TriggerKey>> UnscheduleJobs(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given key, and store the new given one - which must be associated
    /// with the same job (the new trigger must have the job name &amp; group specified)
    /// - however, the new trigger need not have the same name as the old trigger.
    /// </summary>
    /// <param name="triggerKey">The <see cref="ITrigger" /> to be replaced.</param>
    /// <param name="newTrigger">
    ///     The new <see cref="ITrigger" /> to be stored.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="null" /> if a <see cref="ITrigger" /> with the given
    /// name and group was not found and removed from the store (and the
    /// new trigger is therefore not stored),  otherwise
    /// the first fire time of the newly scheduled trigger.
    /// </returns>
    ValueTask<DateTimeOffset?> RescheduleJob(
        TriggerKey triggerKey,
        ITrigger newTrigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates trigger metadata and selected settings without rescheduling.
    /// Fire times and trigger state are preserved. Supported properties are the description,
    /// priority, job data map, calendar name, misfire instruction, execution group and
    /// preferred node.
    /// </summary>
    /// <param name="triggerKey">The key identifying the trigger to update.</param>
    /// <param name="update">The details to update. See <see cref="TriggerDetailsUpdate"/> for available properties.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns><see langword="true"/> if the trigger was found and updated, <see langword="false"/> if not found.</returns>
    ValueTask<bool> UpdateTriggerDetails(
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the execution group limits this scheduler enforces. Execution groups allow
    /// thread limits - per node or across the cluster, as each limit's
    /// <see cref="ExecutionLimitScope"/> says - so that resource-intensive jobs do not
    /// saturate all available threads.
    /// </summary>
    /// <remarks>
    /// Limits take effect on the next trigger acquisition cycle. Pass <see langword="null"/>
    /// to clear all limits.
    /// </remarks>
    /// <param name="limits">The execution limits to apply, or <see langword="null"/> to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently configured execution group limits, or <see langword="null"/>
    /// if none are configured.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A snapshot of the current execution limits, or <see langword="null"/>.</returns>
    ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the given <see cref="IJob" /> to the Scheduler - with no associated
    /// <see cref="ITrigger" />. The <see cref="IJob" /> will be 'dormant' until
    /// it is scheduled with a <see cref="ITrigger" />, or <see cref="TriggerJob" />
    /// is called for it.
    /// </summary>
    /// <remarks>
    /// The <see cref="IJob" /> must by definition be 'durable', unless
    /// <see cref="AddJobOptions.StoreNonDurableWhileAwaitingScheduling" /> is set; if it is
    /// neither, a <see cref="SchedulerException" /> is thrown.
    /// </remarks>
    /// <param name="jobDetail">The job to store.</param>
    /// <param name="options">
    /// Whether an already stored job of the same key may be over-written, and whether a
    /// non-durable job may be stored while it awaits a trigger. Defaults to neither.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddJob(
        IJobDetail jobDetail,
        AddJobOptions options = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the identified <see cref="IJob" /> from the Scheduler - and any
    /// associated <see cref="ITrigger" />s.
    /// </summary>
    /// <returns> true if the Job was found and deleted.</returns>
    ValueTask<bool> DeleteJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the identified jobs from the Scheduler - and any
    /// associated <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>Note that while this bulk operation is likely more efficient than
    /// invoking <see cref="DeleteJob" /> several
    /// times, it may have the adverse affect of holding data locks for a
    /// single long duration of time (rather than lots of small durations
    /// of time).</para>
    /// <para>One <see cref="ISchedulerListener.JobDeleted" /> is raised per key the deletion applied
    /// to, and the scheduling change is signalled once for the whole call. A key that was not found
    /// raises nothing, as the single-key form raises nothing when it answers
    /// <see langword="false" />.</para>
    /// </remarks>
    /// <returns>
    /// The keys this call deleted, in the order they were given. A key that names no job is simply
    /// absent, never a throw — <c>result.Count == jobKeys.Count</c> is the "every key was found"
    /// answer, and the list itself says which ones when it is not.
    /// </returns>
    /// <seealso cref="DeleteJob" />
    ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete every <see cref="IJobDetail" /> in the matching groups from the Scheduler - and any
    /// associated <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group is the correlation axis: everything belonging to one tenant, one saga or one
    /// import shares a job group, and this is how the whole of it goes in one call rather than one
    /// round trip per key — and without first listing the keys, which is a window in which another
    /// node can add one more.
    /// </para>
    /// <para>Unlike <see cref="PauseJobs(GroupMatcher{JobKey}, CancellationToken)" />, nothing is
    /// remembered about the groups: a delete has no state to impose on a job added afterwards.</para>
    /// <para>One <see cref="ISchedulerListener.JobDeleted" /> is raised per deleted key, and the
    /// scheduling change is signalled once for the whole call. A matcher that matched nothing raises
    /// nothing.</para>
    /// </remarks>
    /// <param name="matcher">
    /// Selects the job groups to empty. Required — there is no "delete the default group" reading of
    /// <see langword="null" /> worth risking on a destructive call.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The keys of the jobs this call deleted.</returns>
    /// <seealso cref="DeleteJobs(IReadOnlyCollection{JobKey}, CancellationToken)" />
    ValueTask<List<JobKey>> DeleteJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trigger the identified <see cref="IJobDetail" /> (Execute it now).
    /// </summary>
    /// <param name="jobKey">
    /// The <see cref="JobKey"/> of the <see cref="IJob" /> to be executed.
    /// </param>
    /// <param name="data">
    /// the (possibly <see langword="null" />) JobDataMap to be
    /// associated with the trigger that fires the job immediately.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask TriggerJob(
        JobKey jobKey,
        JobDataMap? data = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause the <see cref="IJobDetail" /> with the given
    /// key - by pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the job exists — including a job that currently has no
    /// triggers — <see langword="false" /> if there is no job with the given key. No listener
    /// events are raised when nothing was found.
    /// </returns>
    ValueTask<bool> PauseJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause the <see cref="IJobDetail" />s with the given keys - by pausing all of their current
    /// <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// One <see cref="ISchedulerListener.JobPaused" /> is raised per key the pause applied to, and
    /// the scheduling change is signalled once for the whole call. A key that was not found raises
    /// nothing, as the single-key form raises nothing when it answers <see langword="false" />.
    /// </remarks>
    /// <returns>
    /// The keys this call found, in the order they were given — a job with no triggers is found and
    /// so is present. A key that names no job is simply absent, never a throw.
    /// </returns>
    /// <seealso cref="PauseJob" />
    /// <seealso cref="ResumeJobs(IReadOnlyCollection{JobKey}, CancellationToken)" />
    ValueTask<List<JobKey>> PauseJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all of the <see cref="IJobDetail" />s in the
    /// matching groups - by pausing all of their <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Scheduler will "remember" that the groups are paused, and impose the
    /// pause on any new jobs that are added to any of those groups until it is resumed.
    /// </para>
    /// <para>NOTE: There is a limitation that only exactly matched groups
    /// can be remembered as paused.  For example, if there are pre-existing
    /// job in groups "aaa" and "bbb" and a matcher is given to pause
    /// groups that start with "a" then the group "aaa" will be remembered
    /// as paused and any subsequently added jobs in group "aaa" will be paused,
    /// however if a job is added to group "axx" it will not be paused,
    /// as "axx" wasn't known at the time the "group starts with a" matcher
    /// was applied.  HOWEVER, if there are pre-existing groups "aaa" and
    /// "bbb" and a matcher is given to pause the group "axx" (with a
    /// group equals matcher) then no jobs will be paused, but it will be
    /// remembered that group "axx" is paused and later when a job is added
    /// in that group, it will become paused.</para>
    /// </remarks>
    /// <returns>The names of the job groups that were paused by this call.</returns>
    /// <seealso cref="ResumeJobs(GroupMatcher{JobKey}, CancellationToken)" />
    ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given key.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the trigger exists and was moved into the paused state by this
    /// call, <see langword="false" /> if there is no trigger with the given key, it was already
    /// paused, or it is in a state that cannot be paused (e.g. complete). No listener events are
    /// raised when nothing changed.
    /// </returns>
    ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause the <see cref="ITrigger" />s with the given keys.
    /// </summary>
    /// <remarks>
    /// One <see cref="ISchedulerListener.TriggerPaused" /> is raised per key the pause applied to,
    /// and the scheduling change is signalled once for the whole call. A key that did not move
    /// raises nothing, as the single-key form raises nothing when it answers
    /// <see langword="false" />.
    /// </remarks>
    /// <returns>
    /// The keys this call moved into the paused state, in the order they were given. A key that
    /// names no trigger, one that was already paused, and one in a state that cannot be paused are
    /// each simply absent, never a throw.
    /// </returns>
    /// <seealso cref="PauseTrigger" />
    /// <seealso cref="ResumeTriggers(IReadOnlyCollection{TriggerKey}, CancellationToken)" />
    ValueTask<List<TriggerKey>> PauseTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the groups matching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Scheduler will "remember" all the groups paused, and impose the
    /// pause on any new triggers that are added to any of those groups until it is resumed.
    /// </para>
    /// <para>NOTE: There is a limitation that only exactly matched groups
    /// can be remembered as paused.  For example, if there are pre-existing
    /// triggers in groups "aaa" and "bbb" and a matcher is given to pause
    /// groups that start with "a" then the group "aaa" will be remembered as
    /// paused and any subsequently added triggers in that group be paused,
    /// however if a trigger is added to group "axx" it will not be paused,
    /// as "axx" wasn't known at the time the "group starts with a" matcher
    /// was applied.  HOWEVER, if there are pre-existing groups "aaa" and
    /// "bbb" and a matcher is given to pause the group "axx" (with a
    /// group equals matcher) then no triggers will be paused, but it will be
    /// remembered that group "axx" is paused and later when a trigger is added
    /// in that group, it will become paused.</para>
    /// </remarks>
    /// <returns>The names of the trigger groups that were paused by this call.</returns>
    /// <seealso cref="ResumeTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
    ValueTask<List<string>> PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="IJobDetail" /> with
    /// the given key.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
    /// or more fire-times, then the <see cref="ITrigger" />'s misfire
    /// instruction will be applied.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> if the job exists — including a job that currently has no
    /// triggers — <see langword="false" /> if there is no job with the given key. No listener
    /// events are raised when nothing was found.
    /// </returns>
    ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="IJobDetail" />s with the given keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If any of the jobs' <see cref="ITrigger" />s missed one or more fire-times, then those
    /// triggers' misfire instructions will be applied.
    /// </para>
    /// <para>
    /// One <see cref="ISchedulerListener.JobResumed" /> is raised per key the resume applied to,
    /// and the scheduling change is signalled once for the whole call. A key that was not found
    /// raises nothing.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The keys this call found, in the order they were given — a job with no triggers is found and
    /// so is present. A key that names no job is simply absent, never a throw.
    /// </returns>
    /// <seealso cref="ResumeJob" />
    /// <seealso cref="PauseJobs(IReadOnlyCollection{JobKey}, CancellationToken)" />
    ValueTask<List<JobKey>> ResumeJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all of the <see cref="IJobDetail" />s
    /// in matching groups.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
    /// missed one or more fire-times, then the <see cref="ITrigger" />'s
    /// misfire instruction will be applied.
    /// </remarks>
    /// <returns>The names of the job groups that were resumed by this call.</returns>
    /// <seealso cref="PauseJobs(GroupMatcher{JobKey}, CancellationToken)" />
    ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the given
    /// key.
    /// </summary>
    /// <remarks>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> if the trigger existed in a paused state and was resumed by this
    /// call, <see langword="false" /> if there is no trigger with the given key or it was not
    /// paused. No listener events are raised when nothing changed.
    /// </returns>
    ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" />s with the given keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If a <see cref="ITrigger" /> missed one or more fire-times, then its misfire instruction
    /// will be applied.
    /// </para>
    /// <para>
    /// One <see cref="ISchedulerListener.TriggerResumed" /> is raised per key the resume applied
    /// to, and the scheduling change is signalled once for the whole call. A key that did not move
    /// raises nothing.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The keys this call resumed, in the order they were given. A key that names no trigger, and
    /// one that was not paused, are each simply absent, never a throw.
    /// </returns>
    /// <seealso cref="ResumeTrigger" />
    /// <seealso cref="PauseTriggers(IReadOnlyCollection{TriggerKey}, CancellationToken)" />
    ValueTask<List<TriggerKey>> ResumeTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s in matching groups.
    /// </summary>
    /// <remarks>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <returns>The names of the trigger groups that were resumed by this call.</returns>
    /// <seealso cref="PauseTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
    ValueTask<List<string>> ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all triggers - similar to calling <see cref="PauseTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
    /// on every group, however, after using this method <see cref="ResumeAll" />
    /// must be called to clear the scheduler's state of 'remembering' that all
    /// new triggers will be paused as they are added.
    /// </summary>
    /// <remarks>
    /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </remarks>
    /// <seealso cref="ResumeAll" />
    /// <seealso cref="PauseTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
    /// <seealso cref="Standby" />
    ValueTask PauseAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all triggers - similar to calling
    /// <see cref="ResumeTriggers(GroupMatcher{TriggerKey}, CancellationToken)" /> on every group.
    /// </summary>
    /// <remarks>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseAll" />
    ValueTask ResumeAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists jobs matching the query, as <see cref="JobHeader" />s, ordered by group and
    /// then name (ordinal). Listing never loads job data.
    /// </summary>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists triggers matching the query, as <see cref="TriggerHeader" />s, ordered by
    /// group and then name (ordinal). The header carries the trigger's current state and
    /// execution group, so listing callers need no further round trips.
    /// </summary>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists job groups matching the query, ordered by name (ordinal).
    /// </summary>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists trigger groups matching the query, ordered by name (ordinal).
    /// </summary>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists calendar names matching the query, ordered by name (ordinal).
    /// </summary>
    /// <param name="query">Which names to select and which page of them to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the given jobs in one round trip. Keys that do not exist are simply
    /// absent from the result.
    /// </summary>
    /// <param name="jobKeys">The keys of the jobs to retrieve.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the given triggers in one round trip. Keys that do not exist are simply
    /// absent from the result.
    /// </summary>
    /// <remarks>
    /// The returned triggers are snapshots of the stored ones. If you wish to modify a trigger,
    /// you must re-store it afterward (e.g. see
    /// <see cref="RescheduleJob(TriggerKey, ITrigger, CancellationToken)" />).
    /// </remarks>
    /// <param name="triggerKeys">The keys of the triggers to retrieve.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the <see cref="IJobDetail" /> for the <see cref="IJob" />
    /// instance with the given key .
    /// </summary>
    /// <remarks>
    /// The returned JobDetail object will be a snapshot of the actual stored
    /// JobDetail.  If you wish to modify the JobDetail, you must re-store the
    /// JobDetail afterward (e.g. see <see cref="AddJob" />).
    /// </remarks>
    ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the <see cref="ITrigger" /> instance with the given key.
    /// </summary>
    /// <remarks>
    /// The returned Trigger object will be a snap-shot of the actual stored
    /// trigger.  If you wish to modify the trigger, you must re-store the
    /// trigger afterward (e.g. see <see cref="RescheduleJob(TriggerKey, ITrigger, CancellationToken)" />).
    /// </remarks>
    ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current state of the identified <see cref="ITrigger" />.
    /// </summary>
    /// <seealso cref="TriggerState.Normal" />
    /// <seealso cref="TriggerState.Paused" />
    /// <seealso cref="TriggerState.Complete" />
    /// <seealso cref="TriggerState.Blocked" />
    /// <seealso cref="TriggerState.Error" />
    /// <seealso cref="TriggerState.None" />
    /// <seealso cref="TriggerState.Executing" />
    ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset the current state of the identified <see cref="ITrigger" /> from <see cref="TriggerState.Error" />
    /// to <see cref="TriggerState.Normal" /> or <see cref="TriggerState.Paused" /> as appropriate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only affects triggers that are in <see cref="TriggerState.Error" /> state - if identified trigger is not
    /// in that state then the result is a no-op.
    /// </para>
    /// <para>
    /// The result will be the trigger returning to the normal, waiting to be fired state, unless the trigger's
    /// group has been paused, in which case it will go into the <see cref="TriggerState.Paused" /> state.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> if the trigger existed in the <see cref="TriggerState.Error" /> state
    /// and was reset by this call, <see langword="false" /> if there is no trigger with the given
    /// key or it was not in the error state.
    /// </returns>
    /// <seealso cref="TriggerState"/>
    ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset every one of the identified <see cref="ITrigger" />s from <see cref="TriggerState.Error" />
    /// to <see cref="TriggerState.Normal" /> or <see cref="TriggerState.Paused" /> as appropriate.
    /// </summary>
    /// <remarks>
    /// The set is reset in one pass, under one lock or one connection. Resetting raises no
    /// scheduler-listener event and signals no scheduling change, in the plural exactly as in the
    /// singular — the reset triggers are picked up by the next acquisition cycle.
    /// </remarks>
    /// <returns>
    /// The keys this call reset, in the order they were given. A key that names no trigger, or one
    /// that was not in the <see cref="TriggerState.Error" /> state, is simply absent, never a
    /// throw.
    /// </returns>
    /// <seealso cref="ResetTriggerFromErrorState" />
    ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
    /// </summary>
    /// <param name="calendarName">Name of the calendar.</param>
    /// <param name="calendar">The calendar.</param>
    /// <param name="options">
    /// Whether an already registered calendar of the same name may be over-written, and whether
    /// the triggers that reference it have their next fire time re-computed. Defaults to neither.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddCalendar(
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions options = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the identified <see cref="ICalendar" /> from the Scheduler.
    /// </summary>
    /// <remarks>
    /// If removal of the <code>Calendar</code> would result in
    /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
    /// <see cref="SchedulerException" /> will be thrown.
    /// </remarks>
    /// <param name="calendarName">Name of the calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if the Calendar was found and deleted.</returns>
    ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the <see cref="ICalendar" /> instance with the given name.
    /// </summary>
    ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request the cancellation, within this Scheduler instance, of all
    /// currently executing instances of the identified <see cref="IJob" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If more than one instance of the identified job is currently executing,
    /// the cancellation token will be set on each instance.  However, there is a limitation that in the case that
    /// <see cref="Interrupt(JobKey, CancellationToken)" /> on one instances throws an exception, all
    /// remaining  instances (that have not yet been interrupted) will not have
    /// their <see cref="Interrupt(JobKey, CancellationToken)" /> method called.
    /// </para>
    ///
    /// <para>
    /// To interrupt one specific execution when several of the job are running, list them with
    /// <see cref="QueryFireInstances" /> and pass the one you mean to
    /// <see cref="InterruptFireInstance" />.
    /// </para>
    /// <para>
    /// This method is not cluster aware.  That is, it will only interrupt
    /// instances of the identified InterruptableJob currently executing in this
    /// Scheduler instance, not across the entire cluster.
    /// </para>
    /// </remarks>
    /// <returns>
    /// true is at least one instance of the identified job was found and interrupted.
    /// </returns>
    /// <seealso cref="QueryFireInstances" />
    ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request the cancellation, within this Scheduler instance, of the
    /// identified executing job instance.
    /// </summary>
    /// <remarks>
    /// This method is not cluster aware.  That is, it will only interrupt
    /// instances of the identified InterruptableJob currently executing in this
    /// Scheduler instance, not across the entire cluster.
    /// </remarks>
    /// <seealso cref="QueryFireInstances" />
    /// <seealso cref="IJobExecutionContext.FireInstanceId" />
    /// <seealso cref="Interrupt(JobKey, CancellationToken)" />
    /// <param name="fireInstanceId">
    /// the unique identifier of the job instance to  be interrupted (see <see cref="IJobExecutionContext.FireInstanceId" />)
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if the identified job instance was found and interrupted.</returns>
    ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="IJob" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Job exists with the given identifier</returns>
    ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <param name="triggerKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Trigger exists with the given identifier</returns>
    ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears (deletes!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar"/>s.
    /// </summary>
    ValueTask Clear(CancellationToken cancellationToken = default);
}