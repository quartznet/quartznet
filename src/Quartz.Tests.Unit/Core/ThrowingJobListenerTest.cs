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

using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What the scheduler does when a job listener throws, on either side of the job.
/// </summary>
/// <remarks>
/// <para>
/// The two sides are not symmetrical, and the asymmetry is the point. A listener that throws in
/// <see cref="IJobListener.JobToBeExecuted" /> stops the firing — the job does not run — and the run
/// shell has to hand the trigger back anyway, or a job that forbids concurrent execution would stay
/// blocked behind a firing that never happened. A listener that throws in
/// <see cref="IJobListener.JobWasExecuted" /> is too late to stop anything: the job has run, the trigger
/// has already decided what it wants done, and that verdict still has to reach the store.
/// </para>
/// <para>
/// Both failures are announced to the scheduler listeners and neither stops the scheduler, which is why
/// each test goes on to fire a second job the listener leaves alone.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class ThrowingJobListenerTest
{
    private const string Group = "throwing-job-listener";

    /// <summary>
    /// How long a test is willing to wait for a firing to reach the store. Long enough that a loaded
    /// build agent never trips it, and never used as a measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    [Test]
    public async Task AListenerThrowingBeforeTheJobStopsTheFiringAndStillFreesTheTrigger()
    {
        ExecutionRecord record = new();
        ThrowingJobListener listener = new(new JobKey("job", Group), failOnToBeExecuted: true);
        ErrorRecordingSchedulerListener errors = new();

        CompletionWatchingJobStore store = null;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "throws-before-the-job")
                .UseJobStore(provider =>
                {
                    store = new CompletionWatchingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddJobListener(listener);
            scheduler.ListenerManager.AddSchedulerListener(errors);

            IJobDetail job = JobBuilder.Create<NonConcurrentRecordingJob>()
                .WithIdentity("job", Group)
                .UsingJobData(new JobDataMap { [ExecutionRecord.JobDataKey] = record })
                .Build();

            ITrigger fired = TriggerBuilder.Create()
                .WithIdentity("fired", Group)
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build();

            ITrigger sibling = TriggerBuilder.Create()
                .WithIdentity("sibling", Group)
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow.AddHours(2))
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build();

            await scheduler.ScheduleJob(job, [fired, sibling]);

            TriggerState siblingDuringFiring = TriggerState.None;
            store.BeforeCompletion = async () => siblingDuringFiring = await store.GetTriggerState(sibling.Key);

            await scheduler.Start();

            await ShouldObserve(store.Completions.Reaches(1),
                "the abandoned firing still has to be reported to the store");

            record.Ran.Should().BeFalse(
                "a listener that failed on the way in stops the firing, which is what the message says it does");

            store.Completions.Entries.Should().Equal(
                [new CompletedFiring(fired.Key, job.Key, SchedulerInstruction.NoInstruction)],
                "a firing that never happened settles nothing about the schedule");

            store.Releases.Entries.Should().BeEmpty(
                "the firing was already committed, so it is completed rather than released - releasing "
                + "would leave the job's other triggers blocked behind a job that never ran");

            siblingDuringFiring.Should().Be(TriggerState.Blocked,
                "committing the firing blocked the job's other trigger, whatever the listeners then did");

            (await scheduler.GetTriggerState(fired.Key)).Should().Be(TriggerState.Normal,
                "the trigger has firings ahead of it and a failed listener does not take them away");
            (await scheduler.GetTriggerState(sibling.Key)).Should().Be(TriggerState.Normal,
                "completing the firing is what lets the blocked trigger go again");

            SchedulerErrorContext error = errors.Errors.Entries.Should().ContainSingle(
                "the failure is the scheduler listeners' only sight of it").Subject;

            error.Message.Should().Contain("Job will NOT be executed",
                "the report has to say that the failure cost the firing");
            error.TriggerKey.Should().Be(fired.Key);
            error.JobKey.Should().Be(job.Key);
            error.Exception.Should().BeOfType<JobExecutionProcessException>(
                "the listener's own exception is wrapped in the one that names the listener and the firing");

            await ShouldGoOnScheduling(scheduler);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task AListenerThrowingAfterTheJobDoesNotUndoTheFiring()
    {
        ExecutionRecord record = new();
        ThrowingJobListener listener = new(new JobKey("job", Group), failOnToBeExecuted: false);
        ErrorRecordingSchedulerListener errors = new();
        RecordingTriggerListener triggerListener = new();

        CompletionWatchingJobStore store = null;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "throws-after-the-job")
                .UseJobStore(provider =>
                {
                    store = new CompletionWatchingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddJobListener(listener);
            scheduler.ListenerManager.AddTriggerListener(triggerListener);
            scheduler.ListenerManager.AddSchedulerListener(errors);

            IJobDetail job = JobBuilder.Create<NonConcurrentRecordingJob>()
                .WithIdentity("job", Group)
                .UsingJobData(new JobDataMap { [ExecutionRecord.JobDataKey] = record })
                .Build();

            // A trigger with one firing in it, so that the instruction the trigger reaches is not the one
            // a failed notification would fall back to: DeleteTrigger has to survive the failure.
            ITrigger once = TriggerBuilder.Create()
                .WithIdentity("once", Group)
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, once);
            await scheduler.Start();

            await ShouldObserve(store.Completions.Reaches(1),
                "the finished firing still has to be reported to the store");

            record.Ran.Should().BeTrue(
                "the listener throws after the job, so the job has to have run for this test to prove anything");

            store.Completions.Entries.Should().Equal(
                [new CompletedFiring(once.Key, job.Key, SchedulerInstruction.DeleteTrigger)],
                "the trigger decided it was finished before any listener was told, and that verdict is not "
                + "the failed listener's to overturn");

            (await scheduler.GetTriggerState(once.Key)).Should().Be(TriggerState.None,
                "the trigger was deleted, and a trigger that is gone has no state");

            triggerListener.Completed.Entries.Should().BeEmpty(
                "the run shell abandons the notifications at the one that failed, so the trigger listeners "
                + "are not told about a completion the job listeners could not be told about");

            SchedulerErrorContext error = errors.Errors.Entries.Should().ContainSingle().Subject;

            error.Message.Should().Contain("error will be ignored",
                "the report has to say that the failure cost nothing, which is the opposite of the other side");
            error.TriggerKey.Should().Be(once.Key);
            error.JobKey.Should().Be(job.Key);

            await ShouldGoOnScheduling(scheduler);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Fires a job the throwing listener leaves alone, which is how a test says the scheduler is still
    /// working rather than merely still running.
    /// </summary>
    private static async Task ShouldGoOnScheduling(IScheduler scheduler)
    {
        ExecutionRecord record = new();

        IJobDetail job = JobBuilder.Create<RecordingJob>()
            .WithIdentity("unaffected", Group)
            .UsingJobData(new JobDataMap { [ExecutionRecord.JobDataKey] = record })
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("unaffected", Group)
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        await ShouldObserve(record.Executed,
            "one listener failing must not stop the scheduler firing everything else it holds");
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    /// <summary>
    /// Whether the job ran, handed to it through its data map so that nothing here is static.
    /// </summary>
    private sealed class ExecutionRecord
    {
        public const string JobDataKey = "record";

        private readonly TaskCompletionSource executed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Ran { get; private set; }

        public Task Executed => executed.Task;

        public void Record()
        {
            Ran = true;
            executed.TrySetResult();
        }
    }

    [DisallowConcurrentExecution]
    public sealed class NonConcurrentRecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((ExecutionRecord) context.MergedJobDataMap[ExecutionRecord.JobDataKey]).Record();
            return default;
        }
    }

    public sealed class RecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((ExecutionRecord) context.MergedJobDataMap[ExecutionRecord.JobDataKey]).Record();
            return default;
        }
    }

    /// <summary>
    /// Fails on one side of one named job, and does nothing anywhere else — so a test can watch the
    /// scheduler carry on with a job this listener has no quarrel with.
    /// </summary>
    /// <remarks>
    /// The failure arrives as a faulted task, which is the shape an <c>async</c> listener produces and
    /// the shape the scheduler is written to handle: <c>QuartzScheduler.NotifyJobListeners</c> awaits
    /// what the listener returned inside its try block. A listener that instead throws
    /// <em>synchronously</em> — a guard clause in a method that is not <c>async</c> — is not handled the
    /// same way, because the call producing the task is evaluated outside that try. That is a real hole
    /// and it is reported rather than papered over here; this fixture is about the handled path.
    /// </remarks>
    private sealed class ThrowingJobListener : IJobListener
    {
        private readonly JobKey failFor;
        private readonly bool failOnToBeExecuted;

        public ThrowingJobListener(JobKey failFor, bool failOnToBeExecuted)
        {
            this.failFor = failFor;
            this.failOnToBeExecuted = failOnToBeExecuted;
        }

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (failOnToBeExecuted && context.JobDetail.Key.Equals(failFor))
            {
                return ValueTask.FromException(new InvalidOperationException("the listener could not prepare for this job"));
            }

            return default;
        }

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException jobException,
            CancellationToken cancellationToken = default)
        {
            if (!failOnToBeExecuted && context.JobDetail.Key.Equals(failFor))
            {
                return ValueTask.FromException(new InvalidOperationException("the listener could not record this job"));
            }

            return default;
        }
    }

    private sealed class RecordingTriggerListener : ITriggerListener
    {
        public CallLog<TriggerKey> Completed { get; } = new();

        public ValueTask TriggerComplete(
            ITrigger trigger,
            IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            Completed.Record(trigger.Key);
            return default;
        }
    }

    private sealed class ErrorRecordingSchedulerListener : ISchedulerListener
    {
        public CallLog<SchedulerErrorContext> Errors { get; } = new();

        public ValueTask SchedulerError(
            IScheduler scheduler,
            SchedulerErrorContext error,
            CancellationToken cancellationToken = default)
        {
            Errors.Record(error);
            return default;
        }
    }
}
