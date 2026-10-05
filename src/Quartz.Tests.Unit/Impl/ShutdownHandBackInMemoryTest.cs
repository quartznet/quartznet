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

#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The in-memory store has no recovery to hand a firing back to, so a firing a shutdown cancels is a
/// cancellation there whatever else is true of it (#4014). And the pieces the persistent store's
/// hand-back is decided from: who cancelled a firing, and what its history row says.
/// </summary>
[NonParallelizable]
public sealed class ShutdownHandBackInMemoryTest
{
    private const string SchedulerName = "shutdown-hand-back-in-memory";

    private static readonly JobKey jobKey = new("long", "hand-back");
    private static readonly TriggerKey triggerKey = new("t-long", "hand-back");
    private static readonly TriggerKey continuationKey = new("t-after", "hand-back");

    [SetUp]
    public void ResetJob() => LongJob.Reset();

    /// <summary>
    /// A shutdown that interrupts a job requesting recovery completes the firing as cancelled: the history
    /// row says nothing of a hand-back, because nothing was handed back.
    /// </summary>
    [Test]
    public async Task AFiringTheShutdownCancelsIsCompletedAsCancelled()
    {
        ServiceCollection services = new();
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q => q.ConfigureScheduler(options =>
        {
            options.InstanceName = SchedulerName;
            options.ShutdownJobInterruption = ShutdownJobInterruption.WhenWaitingForJobs;
        }));

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.ScheduleJob(
            JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).StartNow().Build());

        await scheduler.Start();
        await LongJob.Started.WaitAsync(TimeSpan.FromSeconds(30));
        await scheduler.Shutdown(waitForJobsToComplete: true);

        container.GetRequiredService<QuartzSchedulerResources>().RecoverFiringsCancelledByShutdown.Should().BeFalse(
            "only the persistent store recovers a firing, so the scheduler never marks one for the in-memory store");

        PagedResult<ExecutionHistoryEntry> rows = await container.GetRequiredService<IExecutionHistoryStore>()
            .QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName, Take = PagedQuery.All });
        ExecutionHistoryEntry row = rows.Items.Should().ContainSingle().Subject;
        row.Result.Should().Be(JobRunResult.Cancelled);
        row.Summary.Should().BeNull("nothing was handed back");
    }

    /// <summary>
    /// Even a completion marked for hand-back is a cancellation to the in-memory store: no recovery
    /// trigger, and what waits on the cancellation is released.
    /// </summary>
    [Test]
    public async Task TheStoreCompletesAMarkedFiringAsCancelled()
    {
        RAMJobStore store = new(NullLoggerFactory.Instance, A.Fake<ISchedulerSignaler>(), TimeProvider.System);
        await store.Initialize(TestJobStores.Identity());
        try
        {
            IJobDetail job = JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().StoreDurably().Build();
            await store.AddJob(job);

            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartNow().Build();
            trigger.ComputeFirstFireTimeUtc(calendar: null);
            await store.AddTrigger(trigger);

            IOperableTrigger after = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity(continuationKey)
                .ForJob(jobKey)
                .StartAfter(triggerKey, ContinuationCondition.OnCancellation)
                .Build();
            await store.AddTrigger(after);

            List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
            {
                NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
                MaxCount = 1,
                TimeWindow = TimeSpan.Zero
            });
            TriggerFiredBundle bundle = (await store.TriggersFired(acquired)).Should().ContainSingle().Which.TriggerFiredBundle!;

            await store.FiringComplete(new TriggeredJobCompleteContext
            {
                Trigger = bundle.Trigger,
                JobDetail = bundle.JobDetail,
                Instruction = SchedulerInstruction.DeleteTrigger,
                Outcome = ExecutionOutcome.Cancelled,
                HandBackForRecovery = true
            });

            (await store.GetTriggersForJob(jobKey)).Select(x => x.Key).Should().Equal([continuationKey],
                "the in-memory store has no recovery to hand a firing back to: the spent trigger is gone, and no recovery trigger took its place");
            (await store.GetTriggerState(continuationKey)).Should().Be(TriggerState.Normal,
                "the firing completed as cancelled, which releases what waits on its cancellation");
        }
        finally
        {
            await store.Shutdown();
        }
    }

    /// <summary>
    /// Whoever asks first decides: a shutdown's interrupt after a caller's is still the caller's, and only
    /// a firing the shutdown cancelled can be handed back.
    /// </summary>
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void TheFirstRequestToStopDecidesWhoCancelledTheFiring(bool shutdownFirst, bool byShutdown)
    {
        using JobExecutionContextImpl context = NewContext();
        IInterruptableJobExecutionContext interruptable = context;

        if (shutdownFirst)
        {
            interruptable.InterruptForShutdown();
            interruptable.Interrupt();
        }
        else
        {
            interruptable.Interrupt();
            interruptable.InterruptForShutdown();
        }

        context.CancellationToken.IsCancellationRequested.Should().BeTrue();
        context.CancelledByShutdown.Should().Be(byShutdown);

        context.HandBack();
        context.HandedBack.Should().Be(byShutdown, "only a firing the shutdown cancelled can be handed back");
        context.CancelledByShutdown.Should().Be(byShutdown, "handing it back does not change who cancelled it");
    }

    /// <summary>
    /// The history row of a firing handed back is cancelled, and its summary says why, ahead of any summary
    /// the job set itself.
    /// </summary>
    [TestCase(null, JobRunClassifier.HandedBackSummary)]
    [TestCase("stopped at row 42", JobRunClassifier.HandedBackSummary + " stopped at row 42")]
    public void AFiringHandedBackIsClassifiedCancelledWithASummarySayingSo(string? jobSummary, string expected)
    {
        using JobExecutionContextImpl context = NewContext();
        ((IInterruptableJobExecutionContext) context).InterruptForShutdown();
        context.HandBack();
        context.Settle(ExecutionOutcome.Cancelled, retryScheduled: false);
        if (jobSummary is not null)
        {
            context.Result = JobRunReport.Skipped(jobSummary);
        }

        JobRunClassification run = JobRunClassifier.Classify(context, jobException: null);

        run.Result.Should().Be(JobRunResult.Cancelled);
        run.Summary.Should().Be(expected);
    }

    private static JobExecutionContextImpl NewContext()
    {
        return new JobExecutionContextImpl(A.Fake<IScheduler>(), TestUtil.NewMinimalTriggerFiredBundle(), A.Fake<IJob>());
    }

    public sealed class LongJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Started => started.Task;

        public static void Reset() => started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
    }
}
