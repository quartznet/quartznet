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

using System.Collections.Concurrent;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="SchedulerWaitExtensions" /> in process (#4414): a stored job fired and waited for, and a firing
/// already triggered waited for, on the in-memory store.
/// </summary>
/// <remarks>
/// The cluster and the database history are <c>FiringWaitSqliteTest</c>; <c>HttpScheduler</c> is
/// <c>FiringWaitOverHttpTest</c> in <c>Quartz.Tests.AspNetCore</c>.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerWaitExtensionsTest
{
    private const string Group = "wait";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private readonly List<ServiceProvider> containers = [];
    private readonly List<IScheduler> schedulers = [];

    [SetUp]
    public void SetUp()
    {
        Gates.Reset();
        Runs.Reset();
    }

    [TearDown]
    public async Task TearDown()
    {
        Gates.OpenAll();

        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        foreach (ServiceProvider container in containers)
        {
            await container.DisposeAsync();
        }

        schedulers.Clear();
        containers.Clear();
    }

    // ---------------------------------------------------------------------------------------------
    // How a firing ended
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task AJobThatSucceedsEndsTheWaitWithItsRunOnceTheOneOffIsGone()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "succeeds");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        result.TriggerKey.Name.Should().StartWith("MT_", "it is the firing TriggerJob makes");
        result.TriggerKey.Group.Should().Be(TriggerKey.DefaultGroup);
        result.Execution.Should().NotBeNull();
        result.Execution!.Manual.Should().BeTrue("the trigger carries the manual mark");
        result.Execution.FireInstanceId.Should().NotBeNullOrEmpty();
        result.Execution.JobName.Should().Be(job.Name);
        result.Misfire.Should().BeNull();
        (await scheduler.GetTriggerState(result.TriggerKey)).Should().Be(TriggerState.None,
            "the wait is answered after the store committed the completion, so the one-off is already gone");
    }

    [Test]
    public async Task AJobThatReportsSkippedSucceededAndItsRowSaysSkipped()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<SkippingJob>(scheduler, "skips");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.Succeeded, "the job returned without throwing");
        result.Execution!.Result.Should().Be(JobRunResult.Skipped);
        result.Execution.Summary.Should().Be("nothing to do");
    }

    [Test]
    public async Task AJobThatThrowsFailedWithItsOwnMessage()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<ThrowingJob>(scheduler, "throws");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.Failed);
        result.Execution!.ExceptionMessage.Should().Be(ThrowingJob.Message,
            "the row carries what the job threw, not the scheduler's wrapper around it");
        result.Execution.RetryScheduled.Should().BeFalse();
    }

    /// <summary>
    /// The attempts the trigger answers with another one end nothing: the wait is still open while the third
    /// attempt runs, and it is that attempt that answers.
    /// </summary>
    [Test]
    public async Task ARetriedOccurrenceEndsAtTheAttemptThatIsItsLastWord()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<FlakyJob>(scheduler, "flaky");

        Task<FiringResult> wait = scheduler.TriggerJobAndWait(job, waitLimit).AsTask();

        await Gates.Reached(job.Name).WaitAsync(waitLimit);
        Runs.Of(job.Name).Should().Be(3, "two failed attempts, and the third holding on its gate");
        wait.IsCompleted.Should().BeFalse("the two attempts the policy answered with a retry are not the occurrence's end");

        Gates.Open(job.Name);
        FiringResult result = await wait.WaitAsync(waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        result.Execution!.RetryAttempt.Should().Be(2);
        result.Execution.RetryScheduled.Should().BeFalse();
    }

    [Test]
    public async Task AnOccurrenceWhoseRetriesRunOutFailed()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<AlwaysFailingJob>(scheduler, "always-fails");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.Failed);
        result.Execution!.RetryAttempt.Should().Be(1, "the policy allows one retry");
        result.Execution.RetryScheduled.Should().BeFalse("the last attempt is the occurrence's last word");
        Runs.Of(job.Name).Should().Be(2);
    }

    [Test]
    public async Task AVetoedFiringEndsTheWaitWithTheVetoRow()
    {
        IScheduler scheduler = await Started();
        scheduler.ListenerManager.AddTriggerListener(new VetoingListener());
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "vetoed");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Outcome.Should().Be(ExecutionOutcome.Vetoed);
        result.Execution.Should().BeNull("the job never ran");
        result.Misfire.Should().NotBeNull();
        result.Misfire!.Reason.Should().Be(MisfireReason.Vetoed);
        result.Misfire.TriggerName.Should().Be(result.TriggerKey.Name);
        Runs.Of(job.Name).Should().Be(0);

        FiringResult afterwards = await scheduler.WaitForFiring(result.TriggerKey, waitLimit);
        afterwards.Outcome.Should().Be(ExecutionOutcome.Vetoed, "a veto of a firing already gone is read from the misfire feed");
        afterwards.Misfire.Should().Be(result.Misfire, "it is the row the recorder wrote");
    }

    [Test]
    public async Task AJobItsFactoryCannotBuildWasNotExecutedAndSaysWhy()
    {
        IScheduler scheduler = await Started(configure: q => q.UseJobFactory(new ThrowingJobFactory()));
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "unbuildable");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Outcome.Should().Be(ExecutionOutcome.NotExecuted);
        result.Message.Should().Be(ThrowingJobFactory.Message, "the factory said what went wrong");
        result.Execution.Should().BeNull();
        (await scheduler.GetTriggerState(result.TriggerKey)).Should().Be(TriggerState.Error,
            "a job that cannot be built parks its triggers in ERROR, as before");

        FiringResult afterwards = await scheduler.WaitForFiring(result.TriggerKey, waitLimit);
        afterwards.Outcome.Should().Be(ExecutionOutcome.NotExecuted, "a trigger in ERROR fires no more");
        afterwards.Message.Should().Contain("ERROR");
    }

    [Test]
    public async Task AJobThatCannotBeInstantiatedWasNotExecutedAndSaysWhy()
    {
        IScheduler scheduler = await Started(configure: q => q.UseJobFactory(new ThrowingJobFactory(new InvalidOperationException("no constructor"))));
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "uninstantiable");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.NotExecuted);
        result.Message.Should().Contain("Problem instantiating type").And.Contain("no constructor");
    }

    [Test]
    public async Task AFiringAListenerAbandonsAsItBeginsWasNotExecuted()
    {
        IScheduler scheduler = await Started();
        scheduler.ListenerManager.AddTriggerListener(new FailingTriggerListener(failWhenFired: true));
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "abandoned");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.NotExecuted);
        result.Message.Should().Contain("abandoned before the job ran");
        Runs.Of(job.Name).Should().Be(0);
    }

    [Test]
    public async Task AJobListenerThatFailsAfterTheRunStillLetsTheRunAnswer()
    {
        IScheduler scheduler = await Started(history: false);
        scheduler.ListenerManager.AddJobListener(new FailingJobListener());
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "job-listener-fails");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        result.Execution.Should().NotBeNull();
    }

    [Test]
    public async Task ATriggerListenerThatFailsAfterTheRunStillLetsTheRunAnswer()
    {
        IScheduler scheduler = await Started(history: false);
        scheduler.ListenerManager.AddTriggerListener(new FailingTriggerListener(failWhenFired: false));
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "trigger-listener-fails");
        TriggerKey key = new("trigger-listener-fails", Group);

        // Recurring, because a trigger that may fire again is the one whose completion notification can fail
        // the firing's exit.
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartAt(TimeProvider.System.GetUtcNow().AddMilliseconds(500))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build());

        FiringResult result = await scheduler.WaitForFiring(key, waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        result.Execution!.TriggerName.Should().Be(key.Name);
    }

    // ---------------------------------------------------------------------------------------------
    // Timeout, cancellation and shutdown
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task AWaitThatRunsOutAnswersPendingAndLeavesTheJobRunning()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<HeldJob>(scheduler, "held");

        FiringResult pending = await scheduler.TriggerJobAndWait(job, TimeSpan.FromMilliseconds(200));

        pending.Status.Should().Be(FiringWaitStatus.Pending);
        pending.Outcome.Should().BeNull();
        pending.TriggerKey.Name.Should().StartWith("MT_", "the key to wait again with");
        await Gates.Reached(job.Name).WaitAsync(waitLimit);

        Gates.Open(job.Name);
        FiringResult ended = await scheduler.WaitForFiring(pending.TriggerKey, waitLimit);

        ended.Status.Should().Be(FiringWaitStatus.Ended);
        ended.Outcome.Should().Be(ExecutionOutcome.Succeeded, "the timeout stopped the wait, never the job");
        Runs.Completed(job.Name).Should().BeTrue();
    }

    [Test]
    public async Task ACancelledWaitThrowsAndLeavesTheJobRunning()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<HeldJob>(scheduler, "held-cancelled");
        using CancellationTokenSource cancel = new();

        Task<FiringResult> wait = scheduler.TriggerJobAndWait(job, waitLimit, cancel.Token).AsTask();
        await Gates.Reached(job.Name).WaitAsync(waitLimit);
        await cancel.CancelAsync();

        Func<Task> act = () => wait;
        await act.Should().ThrowAsync<OperationCanceledException>();

        Gates.Open(job.Name);
        await Runs.CompletedTask(job.Name).WaitAsync(waitLimit);
        ((StdScheduler) scheduler).scheduler.FiringWatches.Any.Should().BeFalse("the cancelled wait took itself out");
    }

    [Test]
    public async Task AWaitStillOpenWhenTheSchedulerShutsDownThrowsNamingTheTrigger()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<HeldJob>(scheduler, "held-shutdown");

        Task<FiringResult> wait = scheduler.TriggerJobAndWait(job, waitLimit).AsTask();
        await Gates.Reached(job.Name).WaitAsync(waitLimit);

        await scheduler.Shutdown(waitForJobsToComplete: false);

        Func<Task> act = () => wait;
        (await act.Should().ThrowAsync<SchedulerException>())
            .WithMessage("The scheduler shut down before the firing of trigger 'DEFAULT.MT_*' ended.");
    }

    [Test]
    public async Task AFiringThatEndsInsideTheShutdownWindowAnswersItsWait()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<HeldJob>(scheduler, "held-window");

        Task<FiringResult> wait = scheduler.TriggerJobAndWait(job, waitLimit).AsTask();
        await Gates.Reached(job.Name).WaitAsync(waitLimit);

        Task shutdown = scheduler.Shutdown(waitForJobsToComplete: false).AsTask();
        await Task.Delay(300);
        Gates.Open(job.Name);

        FiringResult result = await wait.WaitAsync(waitLimit);
        await shutdown.WaitAsync(waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended, "the completion landed inside the window the shutdown gives running firings");
        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
    }

    /// <summary>
    /// A shutdown that throws before its window is over still fails the waits it leaves open, so a wait with no
    /// timeout does not wait for ever on a scheduler that is down.
    /// </summary>
    [Test]
    public async Task AShutdownThatFailsStillFailsTheWaitsItLeavesOpen()
    {
        IScheduler scheduler = await Started(configure: q => q.ConfigureScheduler(options => options.ShutdownJobInterruption = ShutdownJobInterruption.Always));
        JobKey job = await AddDurable<TokenFaultingJob>(scheduler, "faulting-token");

        Task<FiringResult> wait = scheduler.TriggerJobAndWait(job, Timeout.InfiniteTimeSpan).AsTask();
        await Gates.Reached(job.Name).WaitAsync(waitLimit);

        Func<Task> shutdown = async () => await scheduler.Shutdown(waitForJobsToComplete: false);
        await shutdown.Should().ThrowAsync<Exception>("the job's token callback throws as the shutdown interrupts the job");

        Func<Task> act = () => wait.WaitAsync(TimeSpan.FromSeconds(5));
        (await act.Should().ThrowAsync<SchedulerException>())
            .WithMessage("The scheduler shut down before the firing of trigger 'DEFAULT.MT_*' ended.");
    }

    [Test]
    public async Task AWaitOnASchedulerThatHasShutDownIsRefused()
    {
        IScheduler scheduler = await Started();
        await scheduler.Shutdown();

        Func<Task> act = async () => await scheduler.WaitForFiring(new TriggerKey("any", Group), waitLimit);

        await act.Should().ThrowAsync<SchedulerException>().WithMessage("The Scheduler has been Shutdown.");
    }

    [TestCase(-2)]
    [TestCase(-100)]
    public async Task ANegativeTimeoutOtherThanInfiniteIsRefusedBeforeAnythingIsStored(int milliseconds)
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "negative");

        Func<Task> trigger = async () => await scheduler.TriggerJobAndWait(job, TimeSpan.FromMilliseconds(milliseconds));
        Func<Task> wait = async () => await scheduler.WaitForFiring(new TriggerKey("any", Group), TimeSpan.FromMilliseconds(milliseconds));

        await trigger.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
        await wait.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
        (await scheduler.GetTriggersOfJob(job)).Should().BeEmpty();
    }

    [Test]
    public async Task AnInfiniteTimeoutWaitsForTheRun()
    {
        IScheduler scheduler = await Started();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "infinite");

        FiringResult result = await scheduler.TriggerJobAndWait(job, Timeout.InfiniteTimeSpan).AsTask().WaitAsync(waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
    }

    // ---------------------------------------------------------------------------------------------
    // A firing already triggered
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task AFiringThatEndedBeforeTheWaitIsReadFromTheHistory()
    {
        (IScheduler scheduler, ServiceProvider container) = await StartedWithContainer();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "ended-before");

        TriggerKey key = await scheduler.TriggerJob(job, "input");
        ExecutionHistoryEntry row = await Recorded(container, scheduler, key);

        FiringResult result = await scheduler.WaitForFiring(key, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        result.Execution!.EntryId.Should().Be(row.EntryId, "the answer is the row the history recorded");
    }

    [Test]
    public async Task AFiringThatEndedBeforeTheWaitIsUnknownWithoutAHistory()
    {
        IScheduler scheduler = await Started(history: false);
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "ended-unrecorded");

        TriggerKey key = await scheduler.TriggerJob(job, "input");
        await Runs.CompletedTask(job.Name).WaitAsync(waitLimit);
        await WaitUntilGone(scheduler, key);

        FiringResult result = await scheduler.WaitForFiring(key, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Unknown, "nothing records how a firing that is gone ended");
        result.Outcome.Should().BeNull();
    }

    [Test]
    public async Task AWaitWithoutAHistoryIsStillAnsweredByTheCompletion()
    {
        IScheduler scheduler = await Started(history: false);
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "unrecorded");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Execution.Should().NotBeNull("the shell builds the row the history would have recorded");
        result.Execution!.EntryId.Should().BeNull("no history named it");
        result.Execution.Manual.Should().BeTrue();
    }

    /// <summary>
    /// A recurring trigger that already has a final row: the wait answers the next occurrence, not the row it
    /// found at its first look.
    /// </summary>
    [Test]
    public async Task ARecurringTriggerIsAnsweredByItsNextOccurrence()
    {
        (IScheduler scheduler, ServiceProvider container) = await StartedWithContainer();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "recurring");
        TriggerKey key = new("recurring", Group);
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
            .Build());
        ExecutionHistoryEntry earlier = await Recorded(container, scheduler, key);

        FiringResult result = await scheduler.WaitForFiring(key, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Execution!.EntryId.Should().NotBe(earlier.EntryId, "the earlier row was there before the wait began");
        result.Execution.FiredAtUtc.Should().BeAfter(earlier.FiredAtUtc);
    }

    /// <summary>
    /// A trigger with a final row of its own, unscheduled while it is waited on: the row the first look saw
    /// is not this wait's answer, so nothing says how the awaited firing ended.
    /// </summary>
    [Test]
    public async Task APausedTriggerUnscheduledWhileWaitedOnIsUnknown()
    {
        (IScheduler scheduler, ServiceProvider container) = await StartedWithContainer();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "unscheduled");
        TriggerKey key = new("unscheduled", Group);
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build());
        await Recorded(container, scheduler, key);
        await scheduler.PauseTrigger(key);

        Task<FiringResult> wait = scheduler.WaitForFiring(key, waitLimit).AsTask();
        await Task.Delay(100);
        wait.IsCompleted.Should().BeFalse("a paused trigger is still there to fire");
        await scheduler.UnscheduleJob(key);

        FiringResult result = await wait.WaitAsync(waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Unknown, "the trigger went without a firing, and its earlier row is not this wait's");
    }

    // ---------------------------------------------------------------------------------------------
    // The protocol
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A wait that begins while the completion it is waiting for is on its way to the store is answered by
    /// that completion: the shell releases the waits registered up to the release, not up to the settle.
    /// </summary>
    [Test]
    public async Task AWaitThatBeginsWhileTheCompletionIsBeingStoredIsAnsweredByIt()
    {
        GatedCompletionStore? store = null;
        IScheduler scheduler = await Started(history: false, configure: q => q.UseJobStore(provider =>
        {
            store = new GatedCompletionStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
            return store;
        }));
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "late");

        Task<FiringResult> first = scheduler.TriggerJobAndWait(job, waitLimit).AsTask();
        TriggerKey key = await store!.Holding.WaitAsync(waitLimit);

        Task<FiringResult> late = scheduler.WaitForFiring(key, TimeSpan.FromSeconds(5)).AsTask();
        await Task.Delay(300);
        late.IsCompleted.Should().BeFalse("the completion is held before the store commits it");

        store.Release();
        FiringResult answered = await first.WaitAsync(waitLimit);
        FiringResult lateAnswer = await late.WaitAsync(waitLimit);

        lateAnswer.Status.Should().Be(FiringWaitStatus.Ended, "the late wait joined the settling and was released with it");
        lateAnswer.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        lateAnswer.Execution!.FireInstanceId.Should().Be(answered.Execution!.FireInstanceId, "both waits were answered by the same firing");
    }

    [Test]
    public async Task ATakenNameIsRetriedUnderANewOneAndTheResultCarriesTheStoredKey()
    {
        CollidingStore? store = null;
        IScheduler scheduler = await Started(configure: q => q.UseJobStore(provider =>
        {
            store = new CollidingStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
            return store;
        }));
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "collides");

        FiringResult result = await scheduler.TriggerJobAndWait(job, waitLimit);

        store!.Attempted.Should().HaveCount(2);
        store.Attempted[0].Should().NotBe(store.Attempted[1]);
        result.TriggerKey.Should().Be(store.Attempted[1], "the result carries the key that was stored");
        result.Status.Should().Be(FiringWaitStatus.Ended);
        ((StdScheduler) scheduler).scheduler.FiringWatches.Any.Should().BeFalse("the wait on the refused key went with it");
    }

    // ---------------------------------------------------------------------------------------------
    // Binding and refusals
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Read off the trigger each call stored, on a scheduler that has not started: a timeout of zero looks once
    /// and answers <see cref="FiringWaitStatus.Pending" />, leaving the trigger there.
    /// </summary>
    [Test]
    public async Task EachOverloadBindsAsItReads()
    {
        IScheduler scheduler = await Built();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "binding");
        JobDataMap map = new() { ["reason"] = "rerun" };

        FiringResult plain = await scheduler.TriggerJobAndWait(job, TimeSpan.Zero);
        FiringResult withMap = await scheduler.TriggerJobAndWait(job, map, TimeSpan.Zero);
        FiringResult withNull = await scheduler.TriggerJobAndWait(job, null, TimeSpan.Zero);
        FiringResult withInput = await scheduler.TriggerJobAndWait(job, "payload", TimeSpan.Zero);

        new[] { plain, withMap, withNull, withInput }.Should().OnlyContain(x => x.Status == FiringWaitStatus.Pending);
        (await DataOf(scheduler, plain)).ContainsKey(SchedulerConstants.JobInput).Should().BeFalse("TimeSpan binds the non-generic overload");
        (await DataOf(scheduler, withMap)).GetString("reason").Should().Be("rerun", "a map binds the JobDataMap overload");
        (await DataOf(scheduler, withMap)).ContainsKey(SchedulerConstants.JobInput).Should().BeFalse();
        (await DataOf(scheduler, withNull)).ContainsKey(SchedulerConstants.JobInput).Should().BeFalse("null binds the JobDataMap overload");
        (await DataOf(scheduler, withInput)).GetString(SchedulerConstants.JobInput).Should().Be("payload", "anything else is an input");
        map.ContainsKey(SchedulerConstants.ManualTrigger).Should().BeFalse("the caller's map is copied, not marked");
    }

    [Test]
    public async Task ATokenAsTheInputIsRefused()
    {
        IScheduler scheduler = await Built();
        JobKey job = await AddDurable<SucceedingJob>(scheduler, "token-input");
        using CancellationTokenSource source = new();

        Func<Task> act = async () => await scheduler.TriggerJobAndWait(job, source.Token, TimeSpan.Zero);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not a job input*").WithParameterName("input");
        (await scheduler.GetTriggersOfJob(job)).Should().BeEmpty();
    }

    [Test]
    public async Task TheNamedKeyOverloadNeverReachesTheInterfaceMember()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.TimeProvider).Returns(TimeProvider.System);
        A.CallTo(() => scheduler.GetJobDetail(A<JobKey>._, A<CancellationToken>._)).Returns((IJobDetail?) null);
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));
        TriggerKey named = new("request-42", "api");
        JobDataMap map = new() { ["reason"] = "rerun" };

        await scheduler.TriggerJob(new JobKey("job", Group), map, named);

        A.CallTo(() => scheduler.TriggerJob(A<JobKey>._, A<JobDataMap?>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>.That.Matches(x => x.Key.Equals(named)), A<ScheduleJobOptions>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TheNamedKeyIsStoredManualMarkedInTheJobsGroupAndRefusedTwice()
    {
        IScheduler scheduler = await Built();
        JobKey job = await AddDurable<GpuJob>(scheduler, "named");
        TriggerKey named = new("request-42", "api");

        await scheduler.TriggerJob(job, new JobDataMap { ["reason"] = "rerun" }, named);

        ITrigger stored = (await scheduler.GetTrigger(named))!;
        stored.Should().NotBeNull("the trigger is stored under the key the caller named");
        stored.JobKey.Should().Be(job);
        stored.JobDataMap.GetString(SchedulerConstants.ManualTrigger).Should().Be("true");
        stored.JobDataMap.GetString("reason").Should().Be("rerun");
        stored.ExecutionGroup.Should().Be(GpuJob.ExecutionGroupName, "the group TriggerJob would give it");

        Func<Task> again = async () => await scheduler.TriggerJob(job, null, named);
        await again.Should().ThrowAsync<ObjectAlreadyExistsException>("a named key is never re-keyed, which is what makes a retried request safe");
    }

    [Test]
    public async Task TheContainersSchedulerAndADecoratorWait()
    {
        (IScheduler built, ServiceProvider container) = await StartedWithContainer();
        IScheduler injected = container.GetRequiredService<IScheduler>();
        injected.Should().NotBeSameAs(built, "the container's IScheduler is the deferred handle");
        JobKey job = await AddDurable<SucceedingJob>(built, "through");

        FiringResult deferred = await injected.TriggerJobAndWait(job, waitLimit);
        FiringResult decorated = await new DelegatingScheduler(injected).TriggerJobAndWait(job, waitLimit);

        deferred.Status.Should().Be(FiringWaitStatus.Ended);
        decorated.Status.Should().Be(FiringWaitStatus.Ended);
    }

    [Test]
    public async Task ASchedulerOfOnesOwnIsRefused()
    {
        IScheduler own = A.Fake<IScheduler>();

        Func<Task> direct = async () => await own.TriggerJobAndWait(new JobKey("job", Group), waitLimit);
        Func<Task> decorated = async () => await new DelegatingScheduler(own).WaitForFiring(new TriggerKey("trigger", Group), waitLimit);

        await direct.Should().ThrowAsync<NotSupportedException>().WithMessage("*cannot wait for a firing*");
        await decorated.Should().ThrowAsync<NotSupportedException>().WithMessage("*cannot wait for a firing*");
        A.CallTo(() => own.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task NullArgumentsAreRefused()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        JobKey job = new("job", Group);

        Func<Task> noScheduler = async () => await SchedulerWaitExtensions.TriggerJobAndWait(null!, job, waitLimit);
        Func<Task> noJob = async () => await scheduler.TriggerJobAndWait(null!, waitLimit);
        Func<Task> noTypedJob = async () => await scheduler.TriggerJobAndWait(null!, "input", waitLimit);
        Func<Task> noTrigger = async () => await scheduler.WaitForFiring(null!, waitLimit);
        Func<Task> noNamedKey = async () => await scheduler.TriggerJob(job, null, (TriggerKey) null!);

        await noScheduler.Should().ThrowAsync<ArgumentNullException>().WithParameterName("scheduler");
        await noJob.Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobKey");
        await noTypedJob.Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobKey");
        await noTrigger.Should().ThrowAsync<ArgumentNullException>().WithParameterName("triggerKey");
        await noNamedKey.Should().ThrowAsync<ArgumentNullException>().WithParameterName("triggerKey");
    }

    [Test]
    public void NobodyWaitingCostsTheRegistryNothing()
    {
        FiringWatches watches = new();
        TriggerKey key = new("t", Group);

        watches.Any.Should().BeFalse();
        watches.Settle(key).Should().BeNull("with nobody waiting on the key there is nothing to build");

        FiringSignal signal = watches.Watch(key);
        watches.Any.Should().BeTrue();
        signal.Dispose();
        watches.Any.Should().BeFalse("a disposed wait takes itself out");
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private async Task<IScheduler> Started(bool history = true, Action<IQuartzBuilder>? configure = null)
    {
        (IScheduler scheduler, _) = await StartedWithContainer(history, configure);
        return scheduler;
    }

    private async Task<(IScheduler Scheduler, ServiceProvider Container)> StartedWithContainer(bool history = true, Action<IQuartzBuilder>? configure = null)
    {
        (IScheduler scheduler, ServiceProvider container) = await Build(history, configure);
        await scheduler.Start();
        return (scheduler, container);
    }

    private async Task<IScheduler> Built()
    {
        (IScheduler scheduler, _) = await Build(history: true, configure: null);
        return scheduler;
    }

    private async Task<(IScheduler Scheduler, ServiceProvider Container)> Build(bool history, Action<IQuartzBuilder>? configure)
    {
        ServiceCollection services = new();
        services.AddLogging();
        if (history)
        {
            services.AddQuartzExecutionHistory();
        }

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "wait-" + Guid.NewGuid().ToString("N");
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });
            configure?.Invoke(q);
        });

        ServiceProvider container = services.BuildServiceProvider();
        containers.Add(container);

        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        schedulers.Add(scheduler);
        return (scheduler, container);
    }

    private static async Task<JobKey> AddDurable<TJob>(IScheduler scheduler, string name) where TJob : IJob
    {
        JobKey key = new(name, Group);
        await scheduler.AddJob(JobBuilder.Create<TJob>().WithIdentity(key).StoreDurably().Build());
        return key;
    }

    private static async Task<JobDataMap> DataOf(IScheduler scheduler, FiringResult result)
    {
        return (await scheduler.GetTrigger(result.TriggerKey))!.JobDataMap;
    }

    private static async Task<ExecutionHistoryEntry> Recorded(ServiceProvider container, IScheduler scheduler, TriggerKey key)
    {
        IExecutionHistoryStore store = container.GetRequiredService<IExecutionHistoryStore>();
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;

        while (true)
        {
            PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
            {
                SchedulerName = scheduler.SchedulerName,
                Trigger = key
            });

            if (page.Items.Count > 0)
            {
                return page.Items[0];
            }

            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the recorder writes as the firing completes");
            await Task.Delay(20);
        }
    }

    private static async Task WaitUntilGone(IScheduler scheduler, TriggerKey key)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (await scheduler.GetTriggerState(key) != TriggerState.None)
        {
            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "a one-off is removed as its firing completes");
            await Task.Delay(20);
        }
    }

    /// <summary>Gates a held job waits on, by job name.</summary>
    private static class Gates
    {
        private static ConcurrentDictionary<string, TaskCompletionSource> opened = new();
        private static ConcurrentDictionary<string, TaskCompletionSource> reached = new();

        public static Task Reached(string name) => Source(reached, name).Task;

        public static async Task Hold(string name)
        {
            Source(reached, name).TrySetResult();
            await Source(opened, name).Task;
        }

        public static void Open(string name) => Source(opened, name).TrySetResult();

        public static void OpenAll()
        {
            foreach (TaskCompletionSource gate in opened.Values)
            {
                gate.TrySetResult();
            }
        }

        public static void Reset()
        {
            OpenAll();
            opened = new ConcurrentDictionary<string, TaskCompletionSource>();
            reached = new ConcurrentDictionary<string, TaskCompletionSource>();
        }

        private static TaskCompletionSource Source(ConcurrentDictionary<string, TaskCompletionSource> sources, string name)
        {
            return sources.GetOrAdd(name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    /// <summary>How many times each job ran, and whether its last run returned, by job name.</summary>
    private static class Runs
    {
        private static ConcurrentDictionary<string, int> counts = new();
        private static ConcurrentDictionary<string, TaskCompletionSource> completed = new();

        public static int Begin(IJobExecutionContext context) => counts.AddOrUpdate(context.JobDetail.Key.Name, 1, (_, n) => n + 1);

        public static void End(IJobExecutionContext context) => Source(context.JobDetail.Key.Name).TrySetResult();

        public static int Of(string name) => counts.GetValueOrDefault(name);

        public static bool Completed(string name) => Source(name).Task.IsCompleted;

        public static Task CompletedTask(string name) => Source(name).Task;

        public static void Reset()
        {
            counts = new ConcurrentDictionary<string, int>();
            completed = new ConcurrentDictionary<string, TaskCompletionSource>();
        }

        private static TaskCompletionSource Source(string name)
        {
            return completed.GetOrAdd(name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    public sealed class SucceedingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Begin(context);
            Runs.End(context);
            return default;
        }
    }

    [ExecutionGroup(ExecutionGroupName)]
    public sealed class GpuJob : IJob
    {
        public const string ExecutionGroupName = "gpu";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class SkippingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Begin(context);
            context.Result = JobRunReport.Skipped("nothing to do");
            return default;
        }
    }

    public sealed class ThrowingJob : IJob
    {
        public const string Message = "the job's own message";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Begin(context);
            throw new InvalidOperationException(Message);
        }
    }

    /// <summary>Fails its first two attempts, and holds the third on its gate before it returns.</summary>
    [RetryPolicy(2, "00:00:00.050")]
    public sealed class FlakyJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Begin(context);
            if (context.RetryAttempt < 2)
            {
                throw new InvalidOperationException("attempt " + context.RetryAttempt);
            }

            await Gates.Hold(context.JobDetail.Key.Name);
        }
    }

    [RetryPolicy(1, "00:00:00.050")]
    public sealed class AlwaysFailingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Begin(context);
            throw new InvalidOperationException("attempt " + context.RetryAttempt);
        }
    }

    /// <summary>
    /// Holds on its gate with a callback on its token that throws, so the shutdown's interrupt fails.
    /// </summary>
    public sealed class TokenFaultingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static () => throw new InvalidOperationException("the job's token callback failed"));
            await Gates.Hold(context.JobDetail.Key.Name);
        }
    }

    /// <summary>
    /// Holds on its gate, deaf to its token, so a shutdown cannot end it early.
    /// </summary>
    public sealed class HeldJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Begin(context);
            await Gates.Hold(context.JobDetail.Key.Name);
            Runs.End(context);
        }
    }

    private sealed class VetoingListener : ITriggerListener
    {
        public string Name => "vetoing";

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(true);
        }
    }

    private sealed class ThrowingJobFactory(Exception? failure = null) : IJobFactory
    {
        public const string Message = "the factory could not build the job";

        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            throw failure ?? new SchedulerException(Message);
        }

        public ValueTask ReturnJob(JobScope jobScope, CancellationToken cancellationToken = default) => default;
    }

    private sealed class FailingTriggerListener(bool failWhenFired) : ITriggerListener
    {
        public string Name => "failing-trigger-listener";

        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return failWhenFired ? throw new SchedulerException("the listener refused the firing") : default;
        }

        public ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        {
            return failWhenFired ? default : throw new SchedulerException("the listener failed after the run");
        }
    }

    private sealed class FailingJobListener : IJobListener
    {
        public string Name => "failing-job-listener";

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            throw new SchedulerException("the listener failed after the run");
        }
    }

    /// <summary>
    /// The in-memory store, holding the first completion it is handed until the test releases it.
    /// </summary>
    private sealed class GatedCompletionStore(IJobStore inner) : DelegatingJobStore(inner)
    {
        private readonly TaskCompletionSource<TriggerKey> holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TriggerKey> Holding => holding.Task;

        public void Release() => released.TrySetResult();

        public override async ValueTask FiringComplete(TriggeredJobCompleteContext context, CancellationToken cancellationToken = default)
        {
            if (holding.TrySetResult(context.Trigger.Key))
            {
                await released.Task;
            }

            await base.FiringComplete(context, cancellationToken);
        }
    }

    /// <summary>
    /// The in-memory store, refusing the first trigger it is asked to add as one it already has.
    /// </summary>
    private sealed class CollidingStore(IJobStore inner) : DelegatingJobStore(inner)
    {
        public List<TriggerKey> Attempted { get; } = [];

        public override ValueTask AddTrigger(IOperableTrigger trigger, AddTriggerOptions options = default, CancellationToken cancellationToken = default)
        {
            Attempted.Add(trigger.Key);
            if (Attempted.Count == 1)
            {
                throw new ObjectAlreadyExistsException(trigger);
            }

            return base.AddTrigger(trigger, options, cancellationToken);
        }
    }
}
