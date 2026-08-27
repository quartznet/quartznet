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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What a job listener's failure costs the firing it failed on, and what it must not cost anybody else.
/// </summary>
/// <remarks>
/// <para>
/// A listener can fail in either of two shapes, and until #3502 they were not handled alike. An
/// <c>async</c> listener hands back a faulted task; a listener whose guard clause throws before it
/// returns anything fails while the scheduler is still evaluating the call. Only the first was wrapped
/// in the exception <see cref="JobRunShell" /> catches, so the second escaped the run shell entirely and
/// the firing was never handed back to the store — leaving a job that forbids concurrent execution
/// blocked behind a firing that had already been abandoned. Every case here therefore runs twice, once
/// per shape, and the two shapes must be indistinguishable.
/// </para>
/// <para>
/// The scheduler's own record of what is executing rides along with the same fault: it used to be kept
/// by the listener loop, so a listener that stopped the loop stopped the bookkeeping too and left the
/// firing listed as executing for as long as the process lived.
/// </para>
/// <para>
/// Nothing here waits for a length of time or measures one. The store records a firing after it has
/// acted on it, so a test that waits for a record and then asks a question is asking a store that has
/// already settled.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class JobListenerFailureTest
{
    private const string Group = "job-listener-failure";

    /// <summary>
    /// How long a test is willing to wait for a firing to reach the store before declaring the
    /// scheduler stuck. Long enough that a loaded build agent never trips it, and never used as a
    /// measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The two ways a listener can fail, which the scheduler has to treat as one.
    /// </summary>
    public enum FailureShape
    {
        /// <summary>Throws before handing anything back, as a guard clause in a method that is not <c>async</c> does.</summary>
        Synchronous,

        /// <summary>Hands back a faulted task, as an <c>async</c> method does.</summary>
        Asynchronous,
    }

    /// <summary>
    /// Which side of the job the listener fails on.
    /// </summary>
    public enum FailureSide
    {
        /// <summary><see cref="IJobListener.JobToBeExecuted" />, which stops the job running.</summary>
        BeforeTheJob,

        /// <summary><see cref="IJobListener.JobWasExecuted" />, which is too late to stop anything.</summary>
        AfterTheJob,
    }

    /// <summary>
    /// The wedge #3502 reported: the firing is abandoned, and the job's other trigger has to be let go
    /// of anyway — which only happens if the abandoned firing is completed at the store.
    /// </summary>
    [TestCase(FailureShape.Synchronous)]
    [TestCase(FailureShape.Asynchronous)]
    public async Task AListenerFailingBeforeTheJobStillCompletesTheFiringAndLetsTheSiblingTriggerFire(FailureShape shape)
    {
        TriggerKey wedgedKey = new TriggerKey("wedged", Group);
        CallLog<TriggerKey> runs = new();

        // Fails only for the first trigger's firing, so that the second one running the job is the
        // scheduler carrying on rather than this listener having lost interest.
        FailingJobListener listener = new(shape, FailureSide.BeforeTheJob, context => context.Trigger.Key.Equals(wedgedKey));

        (IScheduler scheduler, CompletionRecordingJobStore store) = await BuildScheduler($"listener-wedge-{shape}");

        try
        {
            scheduler.ListenerManager.AddJobListener(listener);

            IJobDetail job = JobBuilder.Create<NonConcurrentRecordingJob>()
                .WithIdentity("job", Group)
                .UsingJobData(new JobDataMap { [NonConcurrentRecordingJob.RunLogKey] = runs })
                .Build();

            // Two triggers of one job that forbids concurrent execution, both already due and the
            // wedged one due first. Committing its firing blocks the sibling, and completing that
            // firing is the only thing that lets the sibling go again.
            DateTimeOffset due = DateTimeOffset.UtcNow;

            ITrigger wedged = Repeating(wedgedKey, job, due);
            ITrigger sibling = Repeating(new TriggerKey("sibling", Group), job, due.AddMilliseconds(1));

            await scheduler.ScheduleJob(job, [wedged, sibling]);
            await scheduler.Start();

            await ShouldObserve(store.Completions.Reaches(1),
                "a firing a listener abandoned still has to be reported to the store, or the trigger is never "
                + "handed back and every trigger of a job that forbids concurrent execution stays blocked behind it");

            store.Completions.Entries[0].Should().Be(
                new CompletedFiring(wedgedKey, job.Key, SchedulerInstruction.NoInstruction),
                "a firing that never happened settles nothing about the schedule");

            store.Releases.Entries.Should().BeEmpty(
                "the firing was already committed, so it is completed rather than released - releasing does "
                + "not unblock the job's other triggers");

            await ShouldObserve(store.Completions.Reaches(2),
                "the sibling was blocked by a firing that has now been completed, so it is free to fire");

            store.Completions.Entries[1].Should().Be(
                new CompletedFiring(sibling.Key, job.Key, SchedulerInstruction.NoInstruction),
                "the sibling's own firing is an ordinary one");

            runs.Entries.Should().Equal([sibling.Key],
                "the job ran exactly once, for the trigger whose firing no listener stopped - which is what "
                + "says the wedged firing neither ran the job nor kept the job to itself");

            (await scheduler.GetTriggerState(wedgedKey)).Should().Be(TriggerState.Normal,
                "the wedged trigger has firings ahead of it, and a failed listener does not take them away");
            (await scheduler.GetTriggerState(sibling.Key)).Should().Be(TriggerState.Normal,
                "a trigger that has fired and finished is waiting for its next turn, not blocked");

            ExecutingJobs(scheduler).Should().BeEmpty(
                "both firings are over, so nothing is executing");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// The phantom entry #3502 reported: the scheduler's record of what it is running is its own, and a
    /// user listener cannot be what keeps it honest.
    /// </summary>
    [TestCase(FailureShape.Synchronous, FailureSide.BeforeTheJob)]
    [TestCase(FailureShape.Asynchronous, FailureSide.BeforeTheJob)]
    [TestCase(FailureShape.Synchronous, FailureSide.AfterTheJob)]
    [TestCase(FailureShape.Asynchronous, FailureSide.AfterTheJob)]
    public async Task AFailedListenerLeavesNothingListedAsExecuting(FailureShape shape, FailureSide side)
    {
        CallLog<TriggerKey> runs = new();
        FailingJobListener listener = new(shape, side, _ => true);

        (IScheduler scheduler, CompletionRecordingJobStore store) = await BuildScheduler($"listener-phantom-{shape}-{side}");

        try
        {
            scheduler.ListenerManager.AddJobListener(listener);

            IJobDetail job = JobBuilder.Create<NonConcurrentRecordingJob>()
                .WithIdentity("job", Group)
                .UsingJobData(new JobDataMap { [NonConcurrentRecordingJob.RunLogKey] = runs })
                .Build();

            TriggerKey triggerKey = new TriggerKey("once", Group);
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            await ShouldObserve(store.Completions.Reaches(1),
                "the firing has to be over before what is executing means anything");

            runs.Entries.Should().HaveCount(side == FailureSide.BeforeTheJob ? 0 : 1,
                "a listener that fails on the way in stops the job running, and one that fails afterwards is too late to");

            ExecutingJobs(scheduler).Should().BeEmpty(
                "the firing is over, and an operator reading the list of executing jobs must not be shown one "
                + "that a listener's failure stranded there");

            (await scheduler.GetMetadata()).LocalExecutingJobs.Should().Be(0,
                "the count the metadata reports is the same record, and it is what a dashboard shows");

            (await scheduler.GetMetadata()).JobsExecuted.Should().Be(1,
                "the firing was dispatched, which is what this counts - a listener stopping it does not unfire it");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// A trigger with nothing left to fire is finished whether its last firing ran or a listener
    /// abandoned it, and the scheduler listeners hear so either way.
    /// </summary>
    [TestCase(FailureShape.Synchronous, FailureSide.BeforeTheJob)]
    [TestCase(FailureShape.Asynchronous, FailureSide.BeforeTheJob)]
    [TestCase(FailureShape.Synchronous, FailureSide.AfterTheJob)]
    [TestCase(FailureShape.Asynchronous, FailureSide.AfterTheJob)]
    public async Task AFailedListenerDoesNotSwallowTheTriggersFinalizedNotification(FailureShape shape, FailureSide side)
    {
        CallLog<TriggerKey> runs = new();
        FailingJobListener listener = new(shape, side, _ => true);
        FinalizedRecordingSchedulerListener finalized = new();

        (IScheduler scheduler, _) = await BuildScheduler($"listener-finalized-{shape}-{side}");

        try
        {
            scheduler.ListenerManager.AddJobListener(listener);
            scheduler.ListenerManager.AddSchedulerListener(finalized);

            IJobDetail job = JobBuilder.Create<NonConcurrentRecordingJob>()
                .WithIdentity("job", Group)
                .UsingJobData(new JobDataMap { [NonConcurrentRecordingJob.RunLogKey] = runs })
                .Build();

            TriggerKey triggerKey = new TriggerKey("once", Group);
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            await ShouldObserve(finalized.Finalized.Reaches(1),
                "the trigger had one firing in it and it is spent, so the scheduler listeners have to be told "
                + "it will never fire again - the listener's failure is not theirs to inherit");

            finalized.Finalized.Entries.Should().Equal([triggerKey]);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// A scheduler whose in-memory store records every firing it is handed back.
    /// </summary>
    private static async Task<(IScheduler Scheduler, CompletionRecordingJobStore Store)> BuildScheduler(string instanceName)
    {
        CompletionRecordingJobStore store = null;
        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = instanceName)
            .UseJobStore(provider =>
            {
                store = new CompletionRecordingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                return store;
            })
            .BuildScheduler();

        return (scheduler, store);
    }

    private static ITrigger Repeating(TriggerKey key, IJobDetail job, DateTimeOffset startAt)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
    }

    /// <summary>
    /// The firings the scheduler is running, which is what an operator is shown.
    /// </summary>
    private static List<IJobExecutionContext> ExecutingJobs(IScheduler scheduler)
    {
        return ((StdScheduler) scheduler).scheduler.GetCurrentlyExecutingJobs();
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    /// <summary>
    /// A job that records which trigger fired it, through a log handed to it in its data map so that
    /// nothing here is static.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class NonConcurrentRecordingJob : IJob
    {
        public const string RunLogKey = "run log";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((CallLog<TriggerKey>) context.MergedJobDataMap[RunLogKey]).Record(context.Trigger.Key);
            return default;
        }
    }

    /// <summary>
    /// Fails on one side of the job, in whichever of the two shapes it was built for, for the firings it
    /// was told to fail for and no others.
    /// </summary>
    private sealed class FailingJobListener : IJobListener
    {
        private readonly FailureShape shape;
        private readonly FailureSide side;
        private readonly Func<IJobExecutionContext, bool> failFor;

        public FailingJobListener(FailureShape shape, FailureSide side, Func<IJobExecutionContext, bool> failFor)
        {
            this.shape = shape;
            this.side = side;
            this.failFor = failFor;
        }

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return side == FailureSide.BeforeTheJob && failFor(context)
                ? Fail("the listener could not prepare for this job")
                : default;
        }

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException jobException,
            CancellationToken cancellationToken = default)
        {
            return side == FailureSide.AfterTheJob && failFor(context)
                ? Fail("the listener could not record this job")
                : default;
        }

        private ValueTask Fail(string message)
        {
            if (shape == FailureShape.Synchronous)
            {
                // What a guard clause in a method that is not async does: the caller is handed an
                // exception where it expected a ValueTask, before there is anything to await.
                throw new InvalidOperationException(message);
            }

            // What an async method does: the exception arrives in the task the caller was handed.
            return ValueTask.FromException(new InvalidOperationException(message));
        }
    }

    private sealed class FinalizedRecordingSchedulerListener : ISchedulerListener
    {
        public CallLog<TriggerKey> Finalized { get; } = new();

        public ValueTask TriggerFinalized(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Finalized.Record(trigger.Key);
            return default;
        }
    }

    /// <summary>
    /// Records each firing the scheduler handed back, after the real store has acted on it — so a test
    /// that waits for a record and then asks the store a question is asking a store that has settled.
    /// </summary>
    private sealed class CompletionRecordingJobStore : DelegatingJobStore
    {
        public CompletionRecordingJobStore(IJobStore jobStore) : base(jobStore)
        {
        }

        /// <summary>The completions the scheduler reported, instruction included.</summary>
        public CallLog<CompletedFiring> Completions { get; } = new();

        /// <summary>The triggers handed back through <see cref="ReleaseAcquiredTrigger" />.</summary>
        public CallLog<TriggerKey> Releases { get; } = new();

        public override async ValueTask TriggeredJobComplete(
            IOperableTrigger trigger,
            IJobDetail jobDetail,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            await base.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken).ConfigureAwait(false);
            Completions.Record(new CompletedFiring(trigger.Key, jobDetail.Key, triggerInstructionCode));
        }

        public override async ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
        {
            await base.ReleaseAcquiredTrigger(trigger, cancellationToken).ConfigureAwait(false);
            Releases.Record(trigger.Key);
        }
    }
}
