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

using System.Diagnostics;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Diagnostics;

/// <summary>
/// Traces and times every operation a scheduler asks of its store.
/// </summary>
/// <remarks>
/// <para>
/// The spans this emits used to come from thirty-three call sites inside <c>AdoJobStoreBase</c>, which
/// meant the in-memory store and any store outside this repository — a community package's, an
/// application's own — produced no store telemetry at all, and that the one store which did was
/// carrying a concern that has nothing to do
/// with talking to a database. As a decorator it is the same spans for every store there is, and the
/// enrichment the ADO store used to add by hand (the batch size it was asked for, the number of triggers
/// it came back with) is derived here from the arguments and the results.
/// </para>
/// <para>
/// It costs two boolean reads per operation when nobody is watching. Both signals are asked before
/// anything else happens — <see cref="ActivitySource.HasListeners" /> and the histogram's
/// <c>Enabled</c> — and if neither is on, the call returns the inner store's <see cref="ValueTask" />
/// directly: no closure, no async state machine, no timestamp, no activity.
/// </para>
/// <para>
/// Applied once, outermost, where the store is resolved into a scheduler's resources. It is deliberately
/// not a second <c>IJobStore</c> registration: <c>UseJobStore(instance)</c> and keyed resolution go on
/// meaning what they meant, and code that needs the real store reaches it through
/// <see cref="JobStores.Unwrap" />.
/// </para>
/// </remarks>
internal sealed class TracingJobStore : DelegatingJobStore
{
    private readonly ActivitySource activitySource;
    private readonly Meters meters;
    private readonly TimeProvider timeProvider;

    private string schedulerName = "";
    private string schedulerId = "";

    internal TracingJobStore(IJobStore jobStore, Meters meters, TimeProvider timeProvider)
        : this(jobStore, meters, timeProvider, QuartzActivitySource.Instance)
    {
    }

    internal TracingJobStore(IJobStore jobStore, Meters meters, TimeProvider timeProvider, ActivitySource activitySource)
        : base(jobStore)
    {
        this.meters = meters;
        this.timeProvider = timeProvider;
        this.activitySource = activitySource;
    }

    /// <summary>
    /// Takes the identity every span and measurement is tagged with, then hands it on.
    /// </summary>
    /// <remarks>
    /// Initialization is not one of the traced operations. It happens once, before the scheduler is
    /// running, and a span for it would be a root of its own with nothing to be a child of.
    /// </remarks>
    public override ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        schedulerName = identity.SchedulerName;
        schedulerId = identity.InstanceId;

        return base.Initialize(identity, cancellationToken);
    }

    public override ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ScheduleJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ScheduleJob(job, trigger, cancellationToken);
        }

        operation.Job(job.Key).Trigger(trigger.Key).Start();
        return Complete(operation, (InnerJobStore, job, trigger, cancellationToken),
            static s => s.InnerJobStore.ScheduleJob(s.job, s.trigger, s.cancellationToken));
    }

    public override ValueTask AddJob(IJobDetail job, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AddJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AddJob(job, options, cancellationToken);
        }

        operation.Job(job.Key).Start();
        return Complete(operation, (InnerJobStore, job, options, cancellationToken),
            static s => s.InnerJobStore.AddJob(s.job, s.options, s.cancellationToken));
    }

    public override ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ScheduleJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ScheduleJobs(triggersAndJobs, options, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, triggersAndJobs, options, cancellationToken),
            static s => s.InnerJobStore.ScheduleJobs(s.triggersAndJobs, s.options, s.cancellationToken));
    }

    public override ValueTask AddTrigger(IOperableTrigger trigger, AddTriggerOptions options = default, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AddTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AddTrigger(trigger, options, cancellationToken);
        }

        operation.Trigger(trigger.Key).Start();
        return Complete(operation, (InnerJobStore, trigger, options, cancellationToken),
            static s => s.InnerJobStore.AddTrigger(s.trigger, s.options, s.cancellationToken));
    }

    public override ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AddCalendar);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AddCalendar(calendarName, calendar, options, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, calendarName, calendar, options, cancellationToken),
            static s => s.InnerJobStore.AddCalendar(s.calendarName, s.calendar, s.options, s.cancellationToken));
    }

    public override ValueTask Clear(CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.Clear);
        if (!operation.IsRecording)
        {
            return InnerJobStore.Clear(cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, cancellationToken),
            static s => s.InnerJobStore.Clear(s.cancellationToken));
    }

    public override ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseAll);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseAll(cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, cancellationToken),
            static s => s.InnerJobStore.PauseAll(s.cancellationToken));
    }

    public override ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeAll);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeAll(cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, cancellationToken),
            static s => s.InnerJobStore.ResumeAll(s.cancellationToken));
    }

    public override ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ReleaseAcquiredTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ReleaseAcquiredTrigger(trigger, cancellationToken);
        }

        operation.Trigger(trigger.Key).Start();
        return Complete(operation, (InnerJobStore, trigger, cancellationToken),
            static s => s.InnerJobStore.ReleaseAcquiredTrigger(s.trigger, s.cancellationToken));
    }

    public override ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.TriggeredJobComplete);
        if (!operation.IsRecording)
        {
            return InnerJobStore.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken);
        }

        operation.Trigger(trigger.Key).Job(jobDetail.Key).Start();
        return Complete(operation, (InnerJobStore, trigger, jobDetail, triggerInstructionCode, cancellationToken),
            static s => s.InnerJobStore.TriggeredJobComplete(s.trigger, s.jobDetail, s.triggerInstructionCode, s.cancellationToken));
    }

    public override ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteJob(jobKey, cancellationToken);
        }

        operation.Job(jobKey).Start();
        return Complete(operation, (InnerJobStore, jobKey, cancellationToken),
            static s => s.InnerJobStore.DeleteJob(s.jobKey, s.cancellationToken));
    }

    public override ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteJobs(jobKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, jobKeys, cancellationToken),
            static s => s.InnerJobStore.DeleteJobs(s.jobKeys, s.cancellationToken));
    }

    public override ValueTask<List<JobKey>> DeleteJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteJobs(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, matcher, cancellationToken),
            static s => s.InnerJobStore.DeleteJobs(s.matcher, s.cancellationToken));
    }

    public override ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteTrigger(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, (InnerJobStore, triggerKey, cancellationToken),
            static s => s.InnerJobStore.DeleteTrigger(s.triggerKey, s.cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteTriggers(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, triggerKeys, cancellationToken),
            static s => s.InnerJobStore.DeleteTriggers(s.triggerKeys, s.cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> DeleteTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteTriggers(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, matcher, cancellationToken),
            static s => s.InnerJobStore.DeleteTriggers(s.matcher, s.cancellationToken));
    }

    public override ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteCalendar);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteCalendar(calendarName, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, calendarName, cancellationToken),
            static s => s.InnerJobStore.DeleteCalendar(s.calendarName, s.cancellationToken));
    }

    public override ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ReplaceTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ReplaceTrigger(triggerKey, trigger, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, (InnerJobStore, triggerKey, trigger, cancellationToken),
            static s => s.InnerJobStore.ReplaceTrigger(s.triggerKey, s.trigger, s.cancellationToken));
    }

    public override ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.UpdateTriggerDetails);
        if (!operation.IsRecording)
        {
            return InnerJobStore.UpdateTriggerDetails(triggerKey, update, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, (InnerJobStore, triggerKey, update, cancellationToken),
            static s => s.InnerJobStore.UpdateTriggerDetails(s.triggerKey, s.update, s.cancellationToken));
    }

    public override ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResetTriggerFromErrorState);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResetTriggerFromErrorState(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, (InnerJobStore, triggerKey, cancellationToken),
            static s => s.InnerJobStore.ResetTriggerFromErrorState(s.triggerKey, s.cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResetTriggersFromErrorState);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResetTriggersFromErrorState(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, triggerKeys, cancellationToken),
            static s => s.InnerJobStore.ResetTriggersFromErrorState(s.triggerKeys, s.cancellationToken));
    }

    public override ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseTrigger(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, (InnerJobStore, triggerKey, cancellationToken),
            static s => s.InnerJobStore.PauseTrigger(s.triggerKey, s.cancellationToken));
    }

    public override ValueTask<List<string>> PauseTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseTriggerGroups);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseTriggerGroups(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, matcher, cancellationToken),
            static s => s.InnerJobStore.PauseTriggerGroups(s.matcher, s.cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseTriggers(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, triggerKeys, cancellationToken),
            static s => s.InnerJobStore.PauseTriggers(s.triggerKeys, s.cancellationToken));
    }

    public override ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseJob(jobKey, cancellationToken);
        }

        operation.Job(jobKey).Start();
        return Complete(operation, (InnerJobStore, jobKey, cancellationToken),
            static s => s.InnerJobStore.PauseJob(s.jobKey, s.cancellationToken));
    }

    public override ValueTask<List<string>> PauseJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseJobGroups);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseJobGroups(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, matcher, cancellationToken),
            static s => s.InnerJobStore.PauseJobGroups(s.matcher, s.cancellationToken));
    }

    public override ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseJobs(jobKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, jobKeys, cancellationToken),
            static s => s.InnerJobStore.PauseJobs(s.jobKeys, s.cancellationToken));
    }

    public override ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeTrigger(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, (InnerJobStore, triggerKey, cancellationToken),
            static s => s.InnerJobStore.ResumeTrigger(s.triggerKey, s.cancellationToken));
    }

    public override ValueTask<List<string>> ResumeTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeTriggerGroups);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeTriggerGroups(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, matcher, cancellationToken),
            static s => s.InnerJobStore.ResumeTriggerGroups(s.matcher, s.cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeTriggers(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, triggerKeys, cancellationToken),
            static s => s.InnerJobStore.ResumeTriggers(s.triggerKeys, s.cancellationToken));
    }

    public override ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeJob(jobKey, cancellationToken);
        }

        operation.Job(jobKey).Start();
        return Complete(operation, (InnerJobStore, jobKey, cancellationToken),
            static s => s.InnerJobStore.ResumeJob(s.jobKey, s.cancellationToken));
    }

    public override ValueTask<List<string>> ResumeJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeJobGroups);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeJobGroups(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, matcher, cancellationToken),
            static s => s.InnerJobStore.ResumeJobGroups(s.matcher, s.cancellationToken));
    }

    public override ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeJobs(jobKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, (InnerJobStore, jobKeys, cancellationToken),
            static s => s.InnerJobStore.ResumeJobs(s.jobKeys, s.cancellationToken));
    }

    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AcquireNextTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AcquireNextTriggers(request, cancellationToken);
        }

        operation.Tag(ActivityTags.BatchSize, request.MaxCount).Start();
        return CompleteAcquisition(operation, (InnerJobStore, request, cancellationToken),
            static s => s.InnerJobStore.AcquireNextTriggers(s.request, s.cancellationToken));
    }

    public override ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.TriggersFired);
        if (!operation.IsRecording)
        {
            return InnerJobStore.TriggersFired(triggers, cancellationToken);
        }

        operation.Tag(ActivityTags.TriggerCount, triggers.Count).Start();
        return Complete(operation, (InnerJobStore, triggers, cancellationToken),
            static s => s.InnerJobStore.TriggersFired(s.triggers, s.cancellationToken));
    }

    /// <summary>
    /// Opens an operation, or returns nothing at all when neither signal is being collected.
    /// </summary>
    private StoreOperation Begin(string operationName)
    {
        // HasListeners first: it is a volatile read, where CreateActivity walks the source's listeners
        // and consults their samplers. Nothing subscribed is the case this has to be free in.
        Activity? activity = activitySource.HasListeners()
            ? activitySource.CreateActivity(operationName, ActivityKind.Client)
            : null;

        bool measured = meters.JobStoreOperationEnabled;

        if (activity is null && !measured)
        {
            return default;
        }

        if (activity is not null)
        {
            // Before Start, so a listener that reads the activity as it starts sees the identity on it.
            activity.SetTag(ActivityTags.SchedulerName, schedulerName);
            activity.SetTag(ActivityTags.SchedulerId, schedulerId);
        }

        return new StoreOperation(this, activity, operationName, measured ? timeProvider.GetTimestamp() : 0, measured);
    }

    /// <summary>
    /// Runs the store call and closes the operation, whichever way the call ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The call is deferred rather than invoked as an argument because a store is allowed to throw
    /// before it returns a <see cref="ValueTask" /> — <c>AcquireNextTriggers</c> validates its request in
    /// a synchronous prologue, and any store may. An argument-position call would let that throw past
    /// the <see langword="finally" /> below, leaving an activity started, never stopped, and installed
    /// as <see cref="Activity.Current" /> for the rest of the asynchronous flow.
    /// </para>
    /// <para>
    /// The delegate is <see langword="static" /> and the arguments travel beside it as a value tuple,
    /// which is what keeps the guard in every override free: a lambda that captured a parameter would
    /// have its display class allocated on entry to the override, before the <c>IsRecording</c> check
    /// had a chance to return — forty bytes per store call on a scheduler nobody is watching. A static
    /// lambda captures nothing and is cached once per call site.
    /// </para>
    /// </remarks>
    private static async ValueTask Complete<TState>(StoreOperation operation, TState state, Func<TState, ValueTask> call)
    {
        Exception? failure = null;
        try
        {
            await call(state).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            operation.Stop(failure);
        }
    }

    /// <inheritdoc cref="Complete{TState}(StoreOperation, TState, Func{TState, ValueTask})" />
    private static async ValueTask<TResult> Complete<TState, TResult>(StoreOperation operation, TState state, Func<TState, ValueTask<TResult>> call)
    {
        Exception? failure = null;
        try
        {
            return await call(state).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            operation.Stop(failure);
        }
    }

    /// <summary>
    /// The one operation whose enrichment is only knowable once the store has answered.
    /// </summary>
    private static async ValueTask<List<IOperableTrigger>> CompleteAcquisition<TState>(
        StoreOperation operation,
        TState state,
        Func<TState, ValueTask<List<IOperableTrigger>>> call)
    {
        Exception? failure = null;
        try
        {
            List<IOperableTrigger> acquired = await call(state).ConfigureAwait(false);

            // How many of the batch size the store could actually fill, which is the number that says
            // whether a scheduler is idle or starved.
            operation.Tag(ActivityTags.TriggerCount, acquired.Count);
            return acquired;
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            operation.Stop(failure);
        }
    }

    /// <summary>
    /// One store call in flight: the span it is being traced on, and when it started.
    /// </summary>
    /// <remarks>
    /// A struct, and <see langword="default" /> when neither signal is being collected, so that
    /// <see cref="IsRecording" /> is the whole of what a store operation pays for on a scheduler nobody
    /// is watching.
    /// </remarks>
    private readonly struct StoreOperation
    {
        private readonly TracingJobStore? store;
        private readonly Activity? activity;
        private readonly string operationName;
        private readonly long startTimestamp;
        private readonly bool measured;

        internal StoreOperation(TracingJobStore store, Activity? activity, string operationName, long startTimestamp, bool measured)
        {
            this.store = store;
            this.activity = activity;
            this.operationName = operationName;
            this.startTimestamp = startTimestamp;
            this.measured = measured;
        }

        internal bool IsRecording => store is not null;

        internal StoreOperation Job(JobKey key)
        {
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag(ActivityTags.JobGroup, key.Group);
                activity.SetTag(ActivityTags.JobName, key.Name);
            }

            return this;
        }

        internal StoreOperation Trigger(TriggerKey key)
        {
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag(ActivityTags.TriggerGroup, key.Group);
                activity.SetTag(ActivityTags.TriggerName, key.Name);
            }

            return this;
        }

        internal StoreOperation Tag(string name, object value)
        {
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag(name, value);
            }

            return this;
        }

        internal void Start()
        {
            activity?.Start();
        }

        internal void Stop(Exception? exception)
        {
            if (activity is not null)
            {
                if (exception is not null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                    activity.AddException(exception);
                }

                activity.Stop();
            }

            if (measured)
            {
                store!.meters.RecordJobStoreOperation(
                    store.schedulerName,
                    store.schedulerId,
                    operationName,
                    store.timeProvider.GetElapsedTime(startTimestamp),
                    exception);
            }
        }
    }
}
