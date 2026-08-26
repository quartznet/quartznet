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
/// meant the in-memory store, the Redis store and any store an application wrote produced no store
/// telemetry at all — and that the one store which did was carrying a concern that has nothing to do
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
        return Complete(operation, () => InnerJobStore.ScheduleJob(job, trigger, cancellationToken));
    }

    public override ValueTask AddJob(IJobDetail job, bool replace, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AddJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AddJob(job, replace, cancellationToken);
        }

        operation.Job(job.Key).Start();
        return Complete(operation, () => InnerJobStore.AddJob(job, replace, cancellationToken));
    }

    public override ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ScheduleJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ScheduleJobs(triggersAndJobs, replace, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.ScheduleJobs(triggersAndJobs, replace, cancellationToken));
    }

    public override ValueTask AddTrigger(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AddTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AddTrigger(trigger, replace, cancellationToken);
        }

        operation.Trigger(trigger.Key).Start();
        return Complete(operation, () => InnerJobStore.AddTrigger(trigger, replace, cancellationToken));
    }

    public override ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AddCalendar);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AddCalendar(calendarName, calendar, options, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.AddCalendar(calendarName, calendar, options, cancellationToken));
    }

    public override ValueTask Clear(CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.Clear);
        if (!operation.IsRecording)
        {
            return InnerJobStore.Clear(cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.Clear(cancellationToken));
    }

    public override ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseAll);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseAll(cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.PauseAll(cancellationToken));
    }

    public override ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeAll);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeAll(cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.ResumeAll(cancellationToken));
    }

    public override ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ReleaseAcquiredTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ReleaseAcquiredTrigger(trigger, cancellationToken);
        }

        operation.Trigger(trigger.Key).Start();
        return Complete(operation, () => InnerJobStore.ReleaseAcquiredTrigger(trigger, cancellationToken));
    }

    public override ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.TriggeredJobComplete);
        if (!operation.IsRecording)
        {
            return InnerJobStore.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken);
        }

        operation.Trigger(trigger.Key).Job(jobDetail.Key).Start();
        return Complete(operation, () => InnerJobStore.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken));
    }

    public override ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteJob(jobKey, cancellationToken);
        }

        operation.Job(jobKey).Start();
        return Complete(operation, () => InnerJobStore.DeleteJob(jobKey, cancellationToken));
    }

    public override ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteJobs(jobKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.DeleteJobs(jobKeys, cancellationToken));
    }

    public override ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteTrigger(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, () => InnerJobStore.DeleteTrigger(triggerKey, cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteTriggers(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.DeleteTriggers(triggerKeys, cancellationToken));
    }

    public override ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.DeleteCalendar);
        if (!operation.IsRecording)
        {
            return InnerJobStore.DeleteCalendar(calendarName, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.DeleteCalendar(calendarName, cancellationToken));
    }

    public override ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ReplaceTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ReplaceTrigger(triggerKey, trigger, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, () => InnerJobStore.ReplaceTrigger(triggerKey, trigger, cancellationToken));
    }

    public override ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.UpdateTriggerDetails);
        if (!operation.IsRecording)
        {
            return InnerJobStore.UpdateTriggerDetails(triggerKey, update, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, () => InnerJobStore.UpdateTriggerDetails(triggerKey, update, cancellationToken));
    }

    public override ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResetTriggerFromErrorState);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResetTriggerFromErrorState(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, () => InnerJobStore.ResetTriggerFromErrorState(triggerKey, cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResetTriggersFromErrorState);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResetTriggersFromErrorState(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.ResetTriggersFromErrorState(triggerKeys, cancellationToken));
    }

    public override ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseTrigger(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, () => InnerJobStore.PauseTrigger(triggerKey, cancellationToken));
    }

    public override ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseTriggers(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.PauseTriggers(matcher, cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseTriggers(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.PauseTriggers(triggerKeys, cancellationToken));
    }

    public override ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseJob(jobKey, cancellationToken);
        }

        operation.Job(jobKey).Start();
        return Complete(operation, () => InnerJobStore.PauseJob(jobKey, cancellationToken));
    }

    public override ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseJobs(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.PauseJobs(matcher, cancellationToken));
    }

    public override ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.PauseJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.PauseJobs(jobKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.PauseJobs(jobKeys, cancellationToken));
    }

    public override ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeTrigger);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeTrigger(triggerKey, cancellationToken);
        }

        operation.Trigger(triggerKey).Start();
        return Complete(operation, () => InnerJobStore.ResumeTrigger(triggerKey, cancellationToken));
    }

    public override ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeTriggers(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.ResumeTriggers(matcher, cancellationToken));
    }

    public override ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeTriggers(triggerKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.ResumeTriggers(triggerKeys, cancellationToken));
    }

    public override ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeJob);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeJob(jobKey, cancellationToken);
        }

        operation.Job(jobKey).Start();
        return Complete(operation, () => InnerJobStore.ResumeJob(jobKey, cancellationToken));
    }

    public override ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeJobs(matcher, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.ResumeJobs(matcher, cancellationToken));
    }

    public override ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.ResumeJobs);
        if (!operation.IsRecording)
        {
            return InnerJobStore.ResumeJobs(jobKeys, cancellationToken);
        }

        operation.Start();
        return Complete(operation, () => InnerJobStore.ResumeJobs(jobKeys, cancellationToken));
    }

    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.AcquireNextTriggers);
        if (!operation.IsRecording)
        {
            return InnerJobStore.AcquireNextTriggers(request, cancellationToken);
        }

        operation.Tag(ActivityTags.BatchSize, request.MaxCount).Start();
        return CompleteAcquisition(operation, () => InnerJobStore.AcquireNextTriggers(request, cancellationToken));
    }

    public override ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        StoreOperation operation = Begin(OperationName.JobStore.TriggersFired);
        if (!operation.IsRecording)
        {
            return InnerJobStore.TriggersFired(triggers, cancellationToken);
        }

        operation.Tag(ActivityTags.TriggerCount, triggers.Count).Start();
        return Complete(operation, () => InnerJobStore.TriggersFired(triggers, cancellationToken));
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
    /// The call arrives as a delegate rather than as an already-started <see cref="ValueTask" /> because
    /// a store is allowed to throw before it returns one — <c>AcquireNextTriggers</c> validates its
    /// request argument in a synchronous prologue, and any store may. Invoking it as an argument would
    /// let that throw past the <see langword="finally" /> below, leaving an activity started, never
    /// stopped, and installed as <see cref="Activity.Current" /> for the rest of the flow. The delegate
    /// is allocated only on the recording path, where an <see cref="Activity" /> has just been allocated
    /// anyway.
    /// </remarks>
    private static async ValueTask Complete(StoreOperation operation, Func<ValueTask> call)
    {
        Exception? failure = null;
        try
        {
            await call().ConfigureAwait(false);
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

    /// <inheritdoc cref="Complete(StoreOperation, Func{ValueTask})" />
    private static async ValueTask<T> Complete<T>(StoreOperation operation, Func<ValueTask<T>> call)
    {
        Exception? failure = null;
        try
        {
            return await call().ConfigureAwait(false);
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
    private static async ValueTask<List<IOperableTrigger>> CompleteAcquisition(
        StoreOperation operation,
        Func<ValueTask<List<IOperableTrigger>>> call)
    {
        Exception? failure = null;
        try
        {
            List<IOperableTrigger> acquired = await call().ConfigureAwait(false);

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
