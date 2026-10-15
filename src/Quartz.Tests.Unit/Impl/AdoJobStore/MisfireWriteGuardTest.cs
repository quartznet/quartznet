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

using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The misfire handler's write is held to the fire time it read the row with, and a row a firing round
/// moved in between is neither re-armed, reported misfired nor finalized (#4067).
/// </summary>
/// <remarks>
/// The handler reads under <c>TRIGGER_ACCESS</c>, but a firing round of work that allows concurrent
/// execution takes no lock, so a row exactly a misfire threshold late is in both their reads and the
/// round's fire can commit between the handler's read and its write. The delegate is a fake, so a row
/// that moved is a key the write does not answer; the listeners are a fake signaler, told through the
/// unit of work's after-commit notifications, which the test raises by hand as the transaction wrapper
/// would after the commit.
/// </remarks>
public sealed class MisfireWriteGuardTest
{
    private const string Group = "misfire-guard";

    /// <summary>Log event <c>MisfireLog.MisfireWriteSkipped</c>.</summary>
    private const int MisfireWriteSkipped = 3608;

    private static readonly DateTimeOffset Missed = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AMisfireWriteIsHeldToTheFireTimeTheRowWasReadWith()
    {
        Harness harness = Harness.Create();
        IOperableTrigger first = Repeating("first", Missed);
        IOperableTrigger second = Repeating("second", Missed.AddMinutes(1));
        harness.GivenMisfired(first, second);
        List<MisfiredTriggerUpdate> written = harness.WritesTake();

        await harness.Store.RecoverMisfiredJobs(harness.Conn, recovering: false);

        written.Select(x => x.Trigger.Key.Name).Should().Equal(["first", "second"]);
        written[0].ExpectedNextFireTimeUtc.Should().Be(Missed,
            "the write is held to the fire time the row was read with — the occurrence that was missed — not the one the policy moved it to");
        written[1].ExpectedNextFireTimeUtc.Should().Be(Missed.AddMinutes(1));
        written.Should().OnlyContain(x => x.Trigger.NextFireTimeUtc != x.ExpectedNextFireTimeUtc,
            "the policy moved every trigger on, which is what makes the fire time as read a version stamp");

        A.CallTo(() => harness.Delegate.UpdateMisfiredTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<MisfiredTriggerUpdate>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AMisfireWhoseRowMovedIsNeitherReportedNorFinalized()
    {
        RecordingLoggerProvider recorder = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        Harness harness = Harness.Create(loggerFactory);

        IOperableTrigger moved = OneOff("moved", Missed);
        IOperableTrigger kept = OneOff("kept", Missed.AddMinutes(1));
        harness.GivenMisfired(moved, kept);
        harness.WritesTakeOnly(kept.Key);

        RecoverMisfiredJobsResult pass = await harness.Store.RecoverMisfiredJobs(harness.Conn, recovering: false);
        await harness.Commit();

        pass.ProcessedMisfiredTriggerCount.Should().Be(2, "both rows were looked at, and the pass says how many it handled");

        A.CallTo(() => harness.Signaler.NotifyTriggerListenersMisfired(A<ITrigger>.That.Matches(t => t.Key.Name == "kept"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => harness.Signaler.NotifyTriggerListenersMisfired(A<ITrigger>.That.Matches(t => t.Key.Name == "moved"), A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => harness.Signaler.NotifySchedulerListenersFinalized(A<ITrigger>.That.Matches(t => t.Key.Name == "kept"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => harness.Signaler.NotifySchedulerListenersFinalized(A<ITrigger>.That.Matches(t => t.Key.Name == "moved"), A<CancellationToken>._))
            .MustNotHaveHappened();

        recorder.Entries.Should().ContainSingle(x => x.EventId.Id == MisfireWriteSkipped)
            .Which.Message.Should().Contain("moved", "the row the round fired is logged at debug, with the fire time the pass read");
    }

    [Test]
    public async Task AMisfireIsReportedToTheListenersOnlyOnceItsWriteHasCommitted()
    {
        Harness harness = Harness.Create();
        IOperableTrigger trigger = Repeating("late", Missed);
        harness.GivenMisfired(trigger);
        harness.WritesTake();

        await harness.Store.RecoverMisfiredJobs(harness.Conn, recovering: false);

        A.CallTo(() => harness.Signaler.NotifyTriggerListenersMisfired(A<ITrigger>._, A<CancellationToken>._))
            // the listeners run listener code, which has no business inside the store's transaction or hearing of a write that may roll back
            .MustNotHaveHappened();

        await harness.Commit();

        A.CallTo(() => harness.Signaler.NotifyTriggerListenersMisfired(
                A<ITrigger>.That.Matches(t => t.Key.Equals(trigger.Key) && t.NextFireTimeUtc == Missed),
                A<CancellationToken>._))
            // the listeners are told about the trigger as it was before the policy, as the in-memory store tells them
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AFailedPolicyParksTheTriggerOnlyWhileTheRowIsTheOneRead()
    {
        Harness harness = Harness.Create(configureStore: options => options.MaxConsecutiveFireFailures = 1);
        IOperableTrigger throwing = Throwing("throwing", Missed);
        harness.GivenMisfired(throwing);
        A.CallTo(() => harness.Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, throwing.Key, StoredTriggerState.Error, StoredTriggerState.Waiting, Missed, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));

        await harness.Store.RecoverMisfiredJobs(harness.Conn, recovering: false);
        await harness.Commit();

        A.CallTo(() => harness.Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, throwing.Key, StoredTriggerState.Error, StoredTriggerState.Waiting, Missed, A<CancellationToken>._))
            // the park is a compare-and-swap on the state and fire time the pass read, not a write by key
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => harness.Delegate.UpdateTriggerState(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => harness.Signaler.NotifySchedulerListenersTriggerInError(throwing.Key, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AFailedPolicyWhoseRowMovedParksNothing()
    {
        RecordingLoggerProvider recorder = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        Harness harness = Harness.Create(loggerFactory, options => options.MaxConsecutiveFireFailures = 1);
        IOperableTrigger throwing = Throwing("throwing", Missed);
        harness.GivenMisfired(throwing);
        A.CallTo(() => harness.Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, throwing.Key, StoredTriggerState.Error, StoredTriggerState.Waiting, Missed, A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        await harness.Store.RecoverMisfiredJobs(harness.Conn, recovering: false);
        await harness.Commit();

        A.CallTo(() => harness.Signaler.NotifySchedulerListenersTriggerInError(A<TriggerKey>._, A<CancellationToken>._))
            // the row the pass read was fired meanwhile, so nothing was parked and nobody is told it was
            .MustNotHaveHappened();
        recorder.Entries.Should().ContainSingle(x => x.EventId.Id == MisfireWriteSkipped);
    }

    private static IOperableTrigger Repeating(string name, DateTimeOffset nextFireTimeUtc)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(new JobKey("job", Group))
            .StartAt(nextFireTimeUtc)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())
            .Build();

        trigger.NextFireTimeUtc = nextFireTimeUtc;
        return trigger;
    }

    private static IOperableTrigger OneOff(string name, DateTimeOffset nextFireTimeUtc)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(new JobKey("job", Group))
            .StartAt(nextFireTimeUtc)
            .WithSimpleSchedule(x => x.WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount))
            .Build();

        trigger.NextFireTimeUtc = nextFireTimeUtc;
        return trigger;
    }

    private static IOperableTrigger Throwing(string name, DateTimeOffset nextFireTimeUtc)
    {
        ThrowingPolicyTrigger trigger = new()
        {
            Key = new TriggerKey(name, Group),
            JobKey = new JobKey("job", Group),
            StartTimeUtc = nextFireTimeUtc,
        };

        trigger.NextFireTimeUtc = nextFireTimeUtc;
        return trigger;
    }

    /// <summary>A trigger whose misfire policy throws, as one with an unreachable calendar does (#4006).</summary>
    private sealed class ThrowingPolicyTrigger : SimpleTriggerImpl
    {
        public override void UpdateAfterMisfire(ICalendar? calendar)
        {
            throw new InvalidOperationException("the policy cannot be applied");
        }
    }

    /// <summary>
    /// The store over a fake delegate, a fake signaler and a unit of work the test commits by hand.
    /// </summary>
    private sealed class Harness
    {
        private Harness(AdoJobStoreBaseTest.TestAdoJobStoreBase store, IDriverDelegate driverDelegate, ISchedulerSignaler signaler)
        {
            Store = store;
            Delegate = driverDelegate;
            Signaler = signaler;
        }

        public AdoJobStoreBaseTest.TestAdoJobStoreBase Store { get; }

        public IDriverDelegate Delegate { get; }

        public ISchedulerSignaler Signaler { get; }

        public ConnectionAndTransactionHolder Conn { get; } = new(A.Fake<DbConnection>(), null);

        public static Harness Create(ILoggerFactory? loggerFactory = null, Action<AdoJobStoreOptions>? configureStore = null)
        {
            AdoJobStoreBaseTest.TestAdoJobStoreBase store = new(loggerFactory: loggerFactory, configureStore: configureStore);
            IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
            ISchedulerSignaler signaler = A.Fake<ISchedulerSignaler>();
            store.DirectDelegate = driverDelegate;
            store.DirectSignaler = signaler;
            return new Harness(store, driverDelegate, signaler);
        }

        public void GivenMisfired(params IOperableTrigger[] triggers)
        {
            A.CallTo(() => Delegate.SelectMisfiredTriggersToRecover(A<ConnectionAndTransactionHolder>._, StoredTriggerState.Waiting, A<DateTimeOffset>._, A<int>._, A<CancellationToken>._))
                .Returns(new ValueTask<MisfiredTriggerBatch>(new MisfiredTriggerBatch([.. triggers], HasMore: false)));
        }

        /// <summary>Every row takes the write; the updates are handed back for the assertions.</summary>
        public List<MisfiredTriggerUpdate> WritesTake()
        {
            List<MisfiredTriggerUpdate> written = [];
            A.CallTo(() => Delegate.UpdateMisfiredTriggersIfUnchanged(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<MisfiredTriggerUpdate>>._, A<CancellationToken>._))
                .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyList<MisfiredTriggerUpdate> updates, CancellationToken _) =>
                {
                    written.AddRange(updates);
                    return new ValueTask<List<TriggerKey>>(updates.Select(x => x.Trigger.Key).ToList());
                });

            return written;
        }

        /// <summary>Only the given rows take the write; every other row moved under the pass.</summary>
        public void WritesTakeOnly(params TriggerKey[] keys)
        {
            A.CallTo(() => Delegate.UpdateMisfiredTriggersIfUnchanged(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<MisfiredTriggerUpdate>>._, A<CancellationToken>._))
                .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyList<MisfiredTriggerUpdate> updates, CancellationToken _) =>
                    new ValueTask<List<TriggerKey>>(updates.Select(x => x.Trigger.Key).Where(keys.Contains).ToList()));
        }

        /// <summary>What the transaction wrapper does once the unit of work has committed.</summary>
        public async Task Commit()
        {
            foreach (Func<ISchedulerSignaler, CancellationToken, ValueTask> notify in Conn.TakeNotificationsAfterCommit() ?? [])
            {
                await notify(Signaler, CancellationToken.None);
            }
        }
    }
}
