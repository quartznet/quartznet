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

using Quartz.Core;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The look loop every wait for a firing follows (#4414), driven over a scheduler and a history that say what
/// each test needs: the answers a store reaches only on a cluster, over HTTP, or in a race.
/// </summary>
public sealed class FiringWaitTest
{
    private const string SchedulerName = "firing-wait";

    private static readonly TriggerKey key = new("t", "wait");
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    [TestCase(MisfireReason.Missed, "*misfired*")]
    [TestCase(MisfireReason.Overlap, "*overlap policy skipped*")]
    public async Task AGoneTriggerWithAMisfireRowWasNotExecutedAndSaysWhy(MisfireReason reason, string message)
    {
        IExecutionHistoryStore history = History(executions: [], misfires: [Misfire(reason)]);
        await using FiringWatch watch = Watch(State(TriggerState.None), history);

        FiringResult result = await watch.Result(waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Outcome.Should().Be(ExecutionOutcome.NotExecuted);
        result.Misfire!.Reason.Should().Be(reason);
        result.Message.Should().Match(message);
    }

    [Test]
    public async Task AGoneTriggerWithAVetoRowWasVetoed()
    {
        IExecutionHistoryStore history = History(executions: [], misfires: [Misfire(MisfireReason.Vetoed)]);
        await using FiringWatch watch = Watch(State(TriggerState.None), history);

        FiringResult result = await watch.Result(waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.Vetoed);
        result.Message.Should().BeNull();
    }

    [Test]
    public async Task AGoneTriggerWhoseRowNeverComesIsUnknownOnceTheGraceIsOver()
    {
        IExecutionHistoryStore history = History(executions: [], misfires: []);
        await using FiringWatch watch = new(key, State(TriggerState.None), SchedulerName, history, pollsHistory: true, signal: null)
        {
            RowGrace = TimeSpan.FromMilliseconds(100)
        };

        FiringResult result = await watch.Result(waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Unknown);
        A.CallTo(() => history.QueryExecutions(A<ExecutionHistoryQuery>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceOrMore();
    }

    [Test]
    public void AHistoryOtherNodesWriteIsLookedAtForFiveSecondsAndTwiceTheWriteBehind()
    {
        FiringWatch watch = Watch(State(TriggerState.None), History(executions: [], misfires: []));

        watch.RowGrace.Should().Be(FiringWait.MinimumRowGrace, "a history that is not this process's own may lag its firing");
    }

    [Test]
    public async Task AWaitWithNoLocalSignalThatRunsOutIsPending()
    {
        IScheduler scheduler = State(TriggerState.Normal);
        await using FiringWatch watch = Watch(scheduler, history: null);

        FiringResult result = await watch.Result(TimeSpan.FromMilliseconds(300));

        result.Status.Should().Be(FiringWaitStatus.Pending);
        result.TriggerKey.Should().Be(key);
        A.CallTo(() => scheduler.GetTriggerState(key, A<CancellationToken>._)).MustHaveHappenedTwiceOrMore();
    }

    /// <summary>
    /// A poll of a node that may not hear the completion reads the history first, and takes a final row the
    /// first look did not see; an attempt the trigger retried is passed over.
    /// </summary>
    [Test]
    public async Task APollThatReadsTheHistoryTakesTheFirstNewFinalRow()
    {
        ExecutionHistoryEntry earlier = Row("earlier", retryScheduled: false);
        ExecutionHistoryEntry attempt = Row("attempt", retryScheduled: true);
        ExecutionHistoryEntry last = Row("last", retryScheduled: false);

        IExecutionHistoryStore history = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => history.QueryExecutions(A<ExecutionHistoryQuery>._, A<CancellationToken>._))
            .ReturnsNextFromSequence(
                Page([earlier]),
                Page([attempt, earlier]),
                Page([last, attempt, earlier]));
        A.CallTo(() => history.QueryMisfires(A<MisfireHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<MisfireHistoryEntry>([], HasMore: false));

        await using FiringWatch watch = Watch(State(TriggerState.Normal), history);

        FiringResult result = await watch.Result(waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Execution.Should().BeSameAs(last);
        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
    }

    [Test]
    public async Task ATriggerInErrorWithNoCompletionHereWasNotExecuted()
    {
        await using FiringWatch watch = Watch(State(TriggerState.Error), history: null);

        FiringResult result = await watch.Result(waitLimit);

        result.Outcome.Should().Be(ExecutionOutcome.NotExecuted);
        result.Message.Should().Contain("ERROR");
    }

    [Test]
    public async Task ATriggerSeenInErrorWhileItsCompletionIsStoredIsAnsweredByTheCompletion()
    {
        FiringWatches watches = new();
        FiringSignal signal = watches.Watch(key);
        FiringWatches.Settling settling = watches.Settle(key)!;
        settling.Result = new FiringResult
        {
            TriggerKey = key,
            Status = FiringWaitStatus.Ended,
            Outcome = ExecutionOutcome.NotExecuted,
            Message = "the shell's own words"
        };

        await using FiringWatch watch = new(key, State(TriggerState.Error), SchedulerName, history: null, pollsHistory: false, signal);

        Task<FiringResult> wait = watch.Result(waitLimit).AsTask();
        await Task.Delay(100);
        watches.Release(settling);

        (await wait.WaitAsync(waitLimit)).Message.Should().Be("the shell's own words");
    }

    [Test]
    public async Task AGoneTriggerWhoseCompletionIsNeverReleasedIsPendingAtTheDeadline()
    {
        FiringWatches watches = new();
        FiringSignal signal = watches.Watch(key);
        watches.Settle(key).Should().NotBeNull();

        await using FiringWatch watch = new(key, State(TriggerState.None), SchedulerName, history: null, pollsHistory: false, signal);

        FiringResult result = await watch.Result(TimeSpan.FromMilliseconds(200));

        result.Status.Should().Be(FiringWaitStatus.Pending);
    }

    [TestCase(JobRunResult.Succeeded, null, ExecutionOutcome.Succeeded)]
    [TestCase(JobRunResult.Skipped, null, ExecutionOutcome.Succeeded)]
    [TestCase(JobRunResult.Cancelled, null, ExecutionOutcome.Cancelled)]
    [TestCase(JobRunResult.Failed, "thrown", ExecutionOutcome.Failed)]
    [TestCase(JobRunResult.Failed, null, ExecutionOutcome.Succeeded)]
    public void ARowsOutcomeIsWhatTheShellWouldHaveSaid(JobRunResult result, string? exceptionMessage, ExecutionOutcome expected)
    {
        ExecutionHistoryEntry row = Row("row", retryScheduled: false) with { Result = result, ExceptionMessage = exceptionMessage };

        FiringWait.OutcomeOf(row).Should().Be(expected,
            "a job that reported a failure without throwing succeeded as far as the scheduler saw");
    }

    [Test]
    public void TheRegistryAnswersAndFailsWhatIsLeftAndRefusesWaitsAfterShuttingDown()
    {
        FiringWatches watches = new();
        FiringSignal answered = watches.Watch(key);
        FiringSignal other = watches.Watch(new TriggerKey("other", "wait"));

        FiringWatches.Settling settling = watches.Settle(key)!;
        FiringSignal late = watches.Watch(key);
        late.Settled.Should().BeTrue("a wait that begins while the completion is stored is that completion's");
        watches.Release(settling);

        answered.Task.Result.Status.Should().Be(FiringWaitStatus.Ended, "a settling released without an answer still ends the wait");
        late.Task.IsCompletedSuccessfully.Should().BeTrue();

        FiringWatches.Settling unanswered = watches.Settle(new TriggerKey("other", "wait"))!;
        watches.ShutDown();
        watches.Release(unanswered);

        other.Task.IsFaulted.Should().BeTrue();
        other.Task.Exception!.InnerException.Should().BeOfType<SchedulerException>().Which.Message.Should().Contain("wait.other");
        watches.Any.Should().BeFalse();

        Action again = () => watches.Watch(key);
        again.Should().Throw<SchedulerException>().WithMessage("The Scheduler has been Shutdown.");
        other.Dispose();
    }

    private static FiringWatch Watch(IScheduler scheduler, IExecutionHistoryStore? history)
    {
        return new FiringWatch(key, scheduler, SchedulerName, history, pollsHistory: history is not null, signal: null);
    }

    private static IScheduler State(TriggerState state)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.GetTriggerState(key, A<CancellationToken>._)).Returns(state);
        return scheduler;
    }

    private static IExecutionHistoryStore History(List<ExecutionHistoryEntry> executions, List<MisfireHistoryEntry> misfires)
    {
        IExecutionHistoryStore history = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => history.QueryExecutions(A<ExecutionHistoryQuery>._, A<CancellationToken>._))
            .Returns(Page(executions));
        A.CallTo(() => history.QueryMisfires(A<MisfireHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<MisfireHistoryEntry>(misfires, HasMore: false));
        return history;
    }

    private static PagedResult<ExecutionHistoryEntry> Page(List<ExecutionHistoryEntry> rows) => new(rows, HasMore: false);

    private static ExecutionHistoryEntry Row(string entryId, bool retryScheduled)
    {
        return new ExecutionHistoryEntry(SchedulerName, "node", "jobs", "job", key.Group, key.Name, DateTimeOffset.UtcNow, TimeSpan.Zero, Succeeded: true, ExceptionMessage: null)
        {
            EntryId = entryId,
            RetryScheduled = retryScheduled,
            Result = JobRunResult.Succeeded
        };
    }

    private static MisfireHistoryEntry Misfire(MisfireReason reason)
    {
        return new MisfireHistoryEntry(SchedulerName, "node", key.Group, key.Name, new JobKey("job", "jobs"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            Reason = reason
        };
    }
}
