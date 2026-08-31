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
/// What a vetoed firing leaves behind in the job store.
/// </summary>
/// <remarks>
/// <para>
/// A veto is decided by a trigger listener, but it is settled in the store: the trigger the scheduler
/// took out of the waiting set has to go back into it, and the sibling triggers that
/// <see cref="DisallowConcurrentExecutionAttribute" /> blocked when the firing was committed have to be
/// let go. The run shell does that by completing the firing rather than releasing the acquired trigger —
/// only the first of the two unblocks the siblings — and nothing pinned it.
/// </para>
/// <para>
/// Nothing here waits for a length of time. The store records what the scheduler asked of it after it
/// has acted on the request, so every assertion runs once the firing is genuinely over.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class VetoedFiringTest
{
    private const string Group = "vetoed-firing";

    /// <summary>
    /// How long a test is willing to wait for the firing to reach the store. Long enough that a loaded
    /// build agent never trips it, and never used as a measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    [Test]
    public async Task AVetoedFiringReleasesItsTriggerAndUnblocksTheJobsOtherTriggers()
    {
        ExecutionRecord record = new();
        VetoingTriggerListener veto = new(new TriggerKey("vetoed", Group));
        RecordingJobListener jobListener = new();

        CompletionWatchingJobStore store = null;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "vetoed-firing-unblocks")
                .UseJobStore(provider =>
                {
                    store = new CompletionWatchingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddTriggerListener(veto);
            scheduler.ListenerManager.AddJobListener(jobListener);

            IJobDetail job = JobBuilder.Create<NonConcurrentRecordingJob>()
                .WithIdentity("job", Group)
                .UsingJobData(new JobDataMap { [ExecutionRecord.JobDataKey] = record })
                .Build();

            // Repeating, so that the veto is the only reason the trigger has nothing more to do right
            // now: a trigger with nothing left to fire is deleted rather than released, which is the
            // other case and has its own test below.
            ITrigger vetoed = TriggerBuilder.Create()
                .WithIdentity("vetoed", Group)
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

            await scheduler.ScheduleJob(job, [vetoed, sibling]);

            // Read while the vetoed firing is still open, so that "and then it was let go" is a claim
            // about something that had actually been blocked.
            TriggerState siblingDuringFiring = TriggerState.None;
            store.BeforeCompletion = async () => siblingDuringFiring = await store.GetTriggerState(sibling.Key);

            await scheduler.Start();

            await ShouldObserve(store.Completions.Reaches(1),
                "the vetoed firing has to reach the store before there is anything to assert about it");

            siblingDuringFiring.Should().Be(TriggerState.Blocked,
                "committing a firing of a job that forbids concurrent execution blocks the job's other triggers");

            store.Completions.Entries.Should().Equal(
                [new CompletedFiring(vetoed.Key, job.Key, SchedulerInstruction.NoInstruction)],
                "a veto settles nothing about the schedule, so the firing is completed with no instruction");

            store.Releases.Entries.Should().BeEmpty(
                "a committed firing is handed back through TriggeredJobComplete and never through "
                + "ReleaseAcquiredTrigger, which would leave the job's other triggers blocked forever");

            (await scheduler.GetTriggerState(vetoed.Key)).Should().Be(TriggerState.Normal,
                "the vetoed trigger still has firings ahead of it and has to be waiting for the next one");

            (await scheduler.GetTriggerState(sibling.Key)).Should().Be(TriggerState.Normal,
                "completing the firing is what lets the blocked trigger go again");

            record.Ran.Should().BeFalse("a vetoed job does not run, which is the whole point of vetoing it");

            jobListener.Vetoed.Entries.Should().Equal([job.Key],
                "the job listeners are told the execution was vetoed");
            jobListener.ToBeExecuted.Entries.Should().BeEmpty(
                "the veto happens before the job listeners are told the job is about to be executed");
            jobListener.Executed.Entries.Should().BeEmpty("nothing executed, so nothing was executed");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// The other arm of the veto branch: a trigger that was on its last firing is finished whether that
    /// firing happened or not, and the scheduler still says so.
    /// </summary>
    [Test]
    public async Task AVetoedFiringOfATriggersLastRunStillFinalizesIt()
    {
        ExecutionRecord record = new();
        VetoingTriggerListener veto = new(new TriggerKey("once", Group));
        FinalizedTriggerListener finalized = new();

        CompletionWatchingJobStore store = null;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "vetoed-firing-finalizes")
                .UseJobStore(provider =>
                {
                    store = new CompletionWatchingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddTriggerListener(veto);
            scheduler.ListenerManager.AddSchedulerListener(finalized);

            IJobDetail job = JobBuilder.Create<NonConcurrentRecordingJob>()
                .WithIdentity("job", Group)
                .UsingJobData(new JobDataMap { [ExecutionRecord.JobDataKey] = record })
                .Build();

            ITrigger once = TriggerBuilder.Create()
                .WithIdentity("once", Group)
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, once);
            await scheduler.Start();

            await ShouldObserve(store.Completions.Reaches(1),
                "the vetoed firing has to reach the store before there is anything to assert about it");

            store.Completions.Entries.Should().Equal(
                [new CompletedFiring(once.Key, job.Key, SchedulerInstruction.DeleteTrigger)],
                "a trigger with nothing left to fire is finished, veto or no veto");

            await ShouldObserve(finalized.Finalized.Reaches(1),
                "a trigger that will never fire again is announced as finalized even when its last firing was vetoed");

            finalized.Finalized.Entries.Should().Equal([once.Key]);

            (await scheduler.GetTriggerState(once.Key)).Should().Be(TriggerState.None,
                "the trigger is gone, and a trigger that is gone has no state");

            record.Ran.Should().BeFalse("a vetoed job does not run");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
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

        public bool Ran { get; set; }
    }

    [DisallowConcurrentExecution]
    public sealed class NonConcurrentRecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((ExecutionRecord) context.MergedJobDataMap[ExecutionRecord.JobDataKey]).Ran = true;
            return default;
        }
    }

    /// <summary>
    /// Vetoes one named trigger and nothing else.
    /// </summary>
    private sealed class VetoingTriggerListener : ITriggerListener
    {
        private readonly TriggerKey vetoed;

        public VetoingTriggerListener(TriggerKey vetoed)
        {
            this.vetoed = vetoed;
        }

        public ValueTask<bool> VetoJobExecution(
            ITrigger trigger,
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(trigger.Key.Equals(vetoed));
        }
    }

    private sealed class RecordingJobListener : IJobListener
    {
        public CallLog<JobKey> ToBeExecuted { get; } = new();

        public CallLog<JobKey> Executed { get; } = new();

        public CallLog<JobKey> Vetoed { get; } = new();

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ToBeExecuted.Record(context.JobDetail.Key);
            return default;
        }

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException jobException,
            CancellationToken cancellationToken = default)
        {
            Executed.Record(context.JobDetail.Key);
            return default;
        }

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Vetoed.Record(context.JobDetail.Key);
            return default;
        }
    }

    private sealed class FinalizedTriggerListener : ISchedulerListener
    {
        public CallLog<TriggerKey> Finalized { get; } = new();

        public ValueTask TriggerFinalized(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Finalized.Record(trigger.Key);
            return default;
        }
    }
}
