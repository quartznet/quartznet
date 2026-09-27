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

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Which completions take <c>TRIGGER_ACCESS</c> and which do not, watched through the store's lock
/// handler and its driver delegate (#3863).
/// </summary>
/// <remarks>
/// <para>
/// A firing that may run beside itself ends by writing its own rows and nothing else, so its
/// completion runs in a transaction with no lock. The lock stays for everything whose write is decided
/// from a read the lock keeps still — a job that disallows concurrent execution, a retry or an error
/// instruction, an overlap policy that held the trigger, a trigger of a job that is not durable, a
/// <c>Skip</c> trigger on its last occurrence — and a completion that finds continuations awaiting it
/// takes the lock after the fact, before it has written anything.
/// </para>
/// <para>
/// The delegate is a fake, so the calls it sees are the statements the completion would issue, in
/// order; the lock handler is a fake, so an acquisition is a call rather than an inference.
/// </para>
/// </remarks>
public sealed class LockFreeCompletionTest
{
    private const string Group = "lockFree";

    [Test]
    public async Task ARepeatingTriggerOfAConcurrentJobCompletesWithoutTheLock()
    {
        Harness harness = Harness.Create();
        IJobDetail job = Job(durable: true);
        IOperableTrigger firing = Repeating(job);

        await harness.Store.FiringComplete(Context(firing, job, SchedulerInstruction.NoInstruction));

        harness.Calls.Should().Equal(
            ["SelectAwaitingContinuations", "DeleteFiredTrigger"],
            "the completion asks once whether anything awaits it, deletes its own fired row, and takes no lock");
        A.CallTo(() => harness.LockHandler.AcquireLock(A<Guid>._, A<ConnectionAndTransactionHolder>._, A<SchedulerLock>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task ASpentOneOffOfADurableJobCompletesWithoutTheLock()
    {
        Harness harness = Harness.Create();
        IJobDetail job = Job(durable: true);
        IOperableTrigger firing = Spent(job);
        harness.RowIsSpent(firing, job);

        await harness.Store.FiringComplete(Context(firing, job, SchedulerInstruction.DeleteTrigger));

        harness.Calls.Should().Equal(
            ["SelectAwaitingContinuations", "DeleteTrigger", "DeleteFiredTriggers"],
            "the one scan stands in for the deletion's own, the trigger row goes by key with its fired rows, and no lock is taken");
        // A durable job is never an orphan, so the count that decides an orphan's fate is not asked.
        A.CallTo(() => harness.Delegate.CountTriggersForJob(A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AFiringWithContinuationsAwaitingItTakesTheLockBeforeWritingAndSettlesThemOnce()
    {
        RecordingLoggerProvider recorder = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        Harness harness = Harness.Create(loggerFactory);

        IJobDetail job = Job(durable: true);
        IOperableTrigger firing = Repeating(job);
        TriggerKey child = new("child", Group);
        harness.ContinuationAwaits(firing.Key, child, ContinuationCondition.OnSuccess);

        await harness.Store.FiringComplete(Context(firing, job, SchedulerInstruction.NoInstruction, ExecutionOutcome.Succeeded));

        harness.Calls.Should().Equal(
            ["SelectAwaitingContinuations", "AcquireLock TriggerAccess", "SelectAwaitingContinuations", "ReleaseContinuation", "DeleteFiredTrigger", "ReleaseLock TriggerAccess"],
            "the lock-free attempt finds the continuation, writes nothing, and the whole completion runs again under the lock, "
            + "where the continuation is released once and the fired row deleted once");

        recorder.Entries.Should().ContainSingle(x => x.EventId.Id == 3045,
            "the escalation is logged at debug, for whoever wonders why a completion took the lock");
    }

    /// <summary>
    /// The completions whose writes are decided from reads only the lock keeps still. Each takes it
    /// exactly once, first thing, and never asks about continuations without it.
    /// </summary>
    [TestCaseSource(nameof(CompletionsThatKeepTheLock))]
    public async Task ACompletionThatWritesFromAReadTheLockKeepsStillTakesItFirst(LockCase testCase)
    {
        Harness harness = Harness.Create();
        (IJobDetail job, IOperableTrigger firing) = testCase.Build();
        harness.RowIsSpent(firing, job);
        harness.Store.LockAllOperations = testCase.LockAllOperations;

        await harness.Store.FiringComplete(Context(firing, job, testCase.Instruction, testCase.Outcome));

        harness.Calls.Should().StartWith("AcquireLock TriggerAccess",
            "{0}: nothing is read or written before the lock is held", testCase.Name);
        harness.Calls.Should().ContainSingle(x => x == "AcquireLock TriggerAccess",
            "{0}: the lock is taken once, by the one transaction that does the work", testCase.Name);
        harness.Calls.Should().EndWith("ReleaseLock TriggerAccess");
    }

    public static IEnumerable<LockCase> CompletionsThatKeepTheLock()
    {
        yield return new LockCase("a job that disallows concurrent execution", () => Fired(Job(durable: true, nonConcurrent: true), Repeating));

        foreach (SchedulerInstruction instruction in Enum.GetValues<SchedulerInstruction>())
        {
            if (instruction is SchedulerInstruction.NoInstruction or SchedulerInstruction.DeleteTrigger)
            {
                continue;
            }

            yield return new LockCase($"the {instruction} instruction", () => Fired(Job(durable: true), Repeating))
            {
                Instruction = instruction,
                Outcome = instruction == SchedulerInstruction.RetryTrigger ? ExecutionOutcome.Failed : ExecutionOutcome.Succeeded
            };
        }

        yield return new LockCase("a BufferOne firing, which may let go of its trigger", () => Fired(Job(durable: true), job => Repeating(job, OverlapPolicy.BufferOne)));
        yield return new LockCase("a CancelPrevious firing, which may let go of its trigger", () => Fired(Job(durable: true), job => Repeating(job, OverlapPolicy.CancelPrevious)));
        yield return new LockCase("a Skip firing whose next occurrence is the trigger's last", () => Fired(Job(durable: true), job => OnItsLastOccurrence(job, OverlapPolicy.Skip)));
        yield return new LockCase("a spent one-off of a job that is not durable", () => Fired(Job(durable: false), Spent)) { Instruction = SchedulerInstruction.DeleteTrigger };
        yield return new LockCase("a leftover trigger of a job that is not durable", () => Fired(Job(durable: false), Spent));
        yield return new LockCase("SQLite, where every operation is under the lock", () => Fired(Job(durable: true), Repeating)) { LockAllOperations = true };
    }

    /// <summary>
    /// The completions that write their own rows and nothing else.
    /// </summary>
    [TestCaseSource(nameof(CompletionsThatTakeNoLock))]
    public async Task ACompletionThatWritesOnlyItsOwnRowsTakesNoLock(LockCase testCase)
    {
        Harness harness = Harness.Create();
        (IJobDetail job, IOperableTrigger firing) = testCase.Build();
        harness.RowIsSpent(firing, job);

        await harness.Store.FiringComplete(Context(firing, job, testCase.Instruction, testCase.Outcome));

        harness.Calls.Should().NotContain(x => x.StartsWith("AcquireLock", StringComparison.Ordinal),
            "{0}: every statement is on a row of this firing's own", testCase.Name);
    }

    public static IEnumerable<LockCase> CompletionsThatTakeNoLock()
    {
        yield return new LockCase("a Skip firing with occurrences left", () => Fired(Job(durable: true), job => Repeating(job, OverlapPolicy.Skip)));
        yield return new LockCase("an AllowAll firing", () => Fired(Job(durable: true), job => Repeating(job, OverlapPolicy.AllowAll)));
        yield return new LockCase("a repeating trigger of a job that is not durable, which stays", () => Fired(Job(durable: false), Repeating));
        yield return new LockCase("a leftover trigger of a durable job", () => Fired(Job(durable: true), Spent));
        yield return new LockCase("a vetoed firing", () => Fired(Job(durable: true), Repeating)) { Outcome = ExecutionOutcome.Vetoed };
        yield return new LockCase("a firing that never happened", () => Fired(Job(durable: true), Repeating)) { Outcome = ExecutionOutcome.NotExecuted };
    }

    [Test]
    public async Task AFiringThatNeverHappenedAndKeepsItsTriggerAsksNothingAboutContinuations()
    {
        Harness harness = Harness.Create();
        IJobDetail job = Job(durable: true);
        IOperableTrigger firing = Repeating(job);

        await harness.Store.FiringComplete(Context(firing, job, SchedulerInstruction.NoInstruction, ExecutionOutcome.NotExecuted));

        harness.Calls.Should().Equal(["DeleteFiredTrigger"],
            "an occurrence that did not happen settles no continuation and deletes no trigger, so there is nothing a scan could find");
    }

    [Test]
    public async Task ALockFreeCompletionThatFailsRunsAgainUnderTheLock()
    {
        RecordingLoggerProvider recorder = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        Harness harness = Harness.Create(loggerFactory);

        IJobDetail job = Job(durable: true);
        IOperableTrigger firing = Repeating(job);
        harness.FirstFiredRowDeleteFails();

        Func<Task> act = async () => await harness.Store.FiringComplete(Context(firing, job, SchedulerInstruction.NoInstruction));

        await act.Should().NotThrowAsync("the locked path is the one that retries, and it is where a failed lock-free attempt goes");

        harness.Calls.Should().Equal(
            ["SelectAwaitingContinuations", "DeleteFiredTrigger", "AcquireLock TriggerAccess", "SelectAwaitingContinuations", "DeleteFiredTrigger", "ReleaseLock TriggerAccess"],
            "the failed attempt is rolled back and the whole completion runs again under the lock");

        recorder.Entries.Should().ContainSingle(x => x.EventId.Id == 3046 && x.Level == LogLevel.Warning,
            "the failure is reported, with its exception, because the locked rerun may not fail the same way and it would otherwise go unrecorded");
    }

    private static (IJobDetail Job, IOperableTrigger Firing) Fired(IJobDetail job, Func<IJobDetail, IOperableTrigger> trigger)
    {
        return (job, trigger(job));
    }

    private static TriggeredJobCompleteContext Context(
        IOperableTrigger firing,
        IJobDetail job,
        SchedulerInstruction instruction,
        ExecutionOutcome outcome = ExecutionOutcome.Succeeded)
    {
        return new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = job,
            Instruction = instruction,
            Outcome = outcome
        };
    }

    private static IJobDetail Job(bool durable, bool nonConcurrent = false)
    {
        return nonConcurrent
            ? JobBuilder.Create<NonConcurrentJob>().WithIdentity("job", Group).StoreDurably(durable).Build()
            : JobBuilder.Create<ConcurrentJob>().WithIdentity("job", Group).StoreDurably(durable).Build();
    }

    /// <summary>A trigger with an hour to its next occurrence and no end: its completion leaves the row.</summary>
    private static IOperableTrigger Repeating(IJobDetail job) => Repeating(job, OverlapPolicy.Default);

    private static IOperableTrigger Repeating(IJobDetail job, OverlapPolicy policy)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("fired", Group)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithOverlapPolicy(policy)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        trigger.FireInstanceId = "fire-1";
        return trigger;
    }

    /// <summary>A one-off as its firing leaves it: no fire time left, so the completion deletes the row.</summary>
    private static IOperableTrigger Spent(IJobDetail job)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("fired", Group)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        trigger.Triggered(calendar: null);
        trigger.NextFireTimeUtc.Should().BeNull("a one-off has nothing left once it has fired, which is the case under test");
        trigger.FireInstanceId = "fire-1";
        return trigger;
    }

    /// <summary>A trigger of two occurrences as its first firing leaves it: the next one is its last.</summary>
    private static IOperableTrigger OnItsLastOccurrence(IJobDetail job, OverlapPolicy policy)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("fired", Group)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(1))
            .WithOverlapPolicy(policy)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        trigger.Triggered(calendar: null);
        trigger.GetFireTimeAfter(trigger.NextFireTimeUtc!.Value).Should().BeNull("the occurrence after this firing's is the trigger's last, which is the case under test");
        trigger.FireInstanceId = "fire-1";
        return trigger;
    }

    /// <summary>One completion and the store it runs against, with the reads it needs answered.</summary>
    public sealed record LockCase(string Name, Func<(IJobDetail Job, IOperableTrigger Firing)> Build)
    {
        public SchedulerInstruction Instruction { get; init; } = SchedulerInstruction.NoInstruction;

        public ExecutionOutcome Outcome { get; init; } = ExecutionOutcome.Succeeded;

        public bool LockAllOperations { get; init; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The store over a fake delegate and a fake lock handler, recording the calls the assertions read.
    /// </summary>
    private sealed class Harness
    {
        private Harness(CompletingStore store, IDriverDelegate driverDelegate, ILockHandler lockHandler)
        {
            Store = store;
            Delegate = driverDelegate;
            LockHandler = lockHandler;
        }

        public CompletingStore Store { get; }

        public IDriverDelegate Delegate { get; }

        public ILockHandler LockHandler { get; }

        /// <summary>The lock and statement calls in the order they were made.</summary>
        public List<string> Calls { get; } = [];

        public static Harness Create(ILoggerFactory? loggerFactory = null)
        {
            CompletingStore store = new(loggerFactory);
            IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
            ILockHandler lockHandler = A.Fake<ILockHandler>();

            store.DirectDelegate = driverDelegate;
            store.DirectSignaler = A.Fake<ISchedulerSignaler>();
            store.LockHandler = lockHandler;

            Harness harness = new(store, driverDelegate, lockHandler);

            A.CallTo(() => lockHandler.AcquireLock(A<Guid>._, A<ConnectionAndTransactionHolder>._, A<SchedulerLock>._, A<CancellationToken>._))
                .Invokes(call => harness.Calls.Add("AcquireLock " + call.GetArgument<SchedulerLock>(2)))
                .Returns(new ValueTask<bool>(true));
            A.CallTo(() => lockHandler.ReleaseLock(A<Guid>._, A<SchedulerLock>._, A<CancellationToken>._))
                .Invokes(call => harness.Calls.Add("ReleaseLock " + call.GetArgument<SchedulerLock>(1)));

            A.CallTo(() => driverDelegate.SelectAwaitingContinuations(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
                .Invokes(() => harness.Calls.Add("SelectAwaitingContinuations"))
                .Returns(new ValueTask<List<AwaitingContinuation>>(new List<AwaitingContinuation>()));
            A.CallTo(() => driverDelegate.ReleaseContinuation(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<StoredTriggerState>._, A<DateTimeOffset>._, A<CancellationToken>._))
                .Invokes(() => harness.Calls.Add("ReleaseContinuation"))
                .Returns(new ValueTask<int>(1));
            A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
                .Invokes(() => harness.Calls.Add("DeleteTrigger"))
                .Returns(new ValueTask<int>(1));
            A.CallTo(() => driverDelegate.DeleteFiredTriggers(A<ConnectionAndTransactionHolder>._, A<FiredTriggerQuery>._, A<CancellationToken>._))
                .Invokes(() => harness.Calls.Add("DeleteFiredTriggers"))
                .Returns(new ValueTask<int>(1));
            A.CallTo(() => driverDelegate.DeleteFiredTrigger(A<ConnectionAndTransactionHolder>._, A<string>._, A<CancellationToken>._))
                .Invokes(() => harness.Calls.Add("DeleteFiredTrigger"))
                .Returns(new ValueTask<int>(1));

            // The unblocking fan-out of a DisallowConcurrentExecution job re-checks the job's triggers
            // for misfires; none of these tests has a trigger to find there.
            A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<StoredTriggerState>._, A<CancellationToken>._))
                .Returns(new ValueTask<List<TriggerKey>>(new List<TriggerKey>()));

            return harness;
        }

        /// <summary>
        /// The row as a spent one-off's firing leaves it, for the completion's re-read before it deletes.
        /// </summary>
        public void RowIsSpent(IOperableTrigger firing, IJobDetail job)
        {
            A.CallTo(() => Delegate.SelectTriggerHeader(A<ConnectionAndTransactionHolder>._, firing.Key, A<CancellationToken>._))
                .Returns(new ValueTask<StoredTriggerHeader?>(new StoredTriggerHeader(firing.Key, job.Key, StoredTriggerState.Complete, null, AdoConstants.TriggerTypeSimple)));
        }

        /// <summary>
        /// A trigger awaiting <paramref name="parent" />, stored an hour ago with nothing to stop its
        /// release: settlement reads it whole, so the row is a real trigger.
        /// </summary>
        public void ContinuationAwaits(TriggerKey parent, TriggerKey child, ContinuationCondition condition)
        {
            A.CallTo(() => Delegate.SelectAwaitingContinuations(A<ConnectionAndTransactionHolder>._, parent, A<CancellationToken>._))
                .Invokes(() => Calls.Add("SelectAwaitingContinuations"))
                .Returns(new ValueTask<List<AwaitingContinuation>>([new AwaitingContinuation(child, condition)]));

            IOperableTrigger awaiting = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity(child)
                .ForJob(new JobKey("child-job", Group))
                .StartAt(DateTimeOffset.UtcNow.AddHours(-1))
                .Build();

            A.CallTo(() => Delegate.SelectTrigger(A<ConnectionAndTransactionHolder>._, child, A<CancellationToken>._))
                .Returns(new ValueTask<IOperableTrigger?>(awaiting));
        }

        /// <summary>
        /// The first delete of the fired row fails with something that is not transient — the shape a
        /// lost race with an editor of the same rows takes — and every one after it succeeds.
        /// </summary>
        public void FirstFiredRowDeleteFails()
        {
            A.CallTo(() => Delegate.DeleteFiredTrigger(A<ConnectionAndTransactionHolder>._, A<string>._, A<CancellationToken>._))
                .Invokes(() => Calls.Add("DeleteFiredTrigger"))
                .Throws(new InvalidOperationException("the row went under the statement"))
                .Once();
        }
    }

    /// <summary>
    /// The ADO harness from <see cref="AdoJobStoreBaseTest" />, with the lock wrapper running the work
    /// it is given so that the public <c>FiringComplete</c> reaches the lock handler and the delegate.
    /// </summary>
    private sealed class CompletingStore : AdoJobStoreBaseTest.TestAdoJobStoreBase
    {
        public CompletingStore(ILoggerFactory? loggerFactory)
            : base(loggerFactory: loggerFactory)
        {
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }
    }

    private sealed class ConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    private sealed class NonConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
