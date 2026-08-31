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

using System.Data.Common;
using System.Reflection;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What pausing and resuming a set costs, asserted against a faked <see cref="IDriverDelegate" />
/// rather than a database.
/// </summary>
/// <remarks>
/// Every one of these paths used to walk its set and call the single-key member, which is two or three
/// statements per key inside the trigger-access lock (#3424). What only this level can show is the
/// shape of what replaced them: one read of the stored states, then one statement per transition the
/// set turns out to need — a number that depends on which states the set holds and never on how many
/// keys it holds. The answers themselves are unchanged, and <c>JobStoreContractTest</c> proves that
/// against a real database.
/// </remarks>
public class PauseResumeStatementCountTest
{
    private static readonly DateTimeOffset NextFireTime = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly JobKey FirstJob = new("first", "jobs");
    private static readonly JobKey SecondJob = new("second", "jobs");

    private LockRunningJobStore store;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        driverDelegate = A.Fake<IDriverDelegate>();
        store = new LockRunningJobStore();
        store.DirectDelegate = driverDelegate;

        // A faked ValueTask<List<T>> yields null, which the store would then enumerate.
        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
                A<ConnectionAndTransactionHolder>._, A<FiredTriggerQuery>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<FiredTriggerRecord>>([]));

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJobs(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerKey>>([]));

        A.CallTo(() => driverDelegate.SelectJobKeysInGroup(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<JobKey>>([]));

        A.CallTo(() => driverDelegate.SelectPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<string>>([]));
    }

    [Test]
    public async Task PausingASetReadsItsStatesOnceAndWritesOneStatementPerTransition()
    {
        GivenHeaders(
            Header("waiting1", FirstJob, StoredTriggerState.Waiting),
            Header("waiting2", FirstJob, StoredTriggerState.Waiting),
            Header("blocked", SecondJob, StoredTriggerState.Blocked),
            Header("complete", SecondJob, StoredTriggerState.Complete));

        List<TriggerKey> paused = await store.PauseTriggers(Keys("waiting1", "waiting2", "blocked", "complete"));

        paused.Select(key => key.Name).Should().Equal(["waiting1", "waiting2", "blocked"],
            "a completed trigger is in no state a pause supersedes, and the answer keeps the order it was asked in");

        // One read answers the whole set.
        A.CallTo(() => driverDelegate.SelectStoredTriggerHeaders(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.SelectTriggerState(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        // Two transitions are wanted — to paused and to paused-blocked — so two statements, not eight.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<StoredTriggerState>._,
                A<IReadOnlyCollection<StoredTriggerState>>._,
                A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);

        A.CallTo(() => driverDelegate.UpdateTriggerState(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// Waiting and acquired both become paused, so they are one statement and not two.
    /// </summary>
    [Test]
    public async Task PausingASetOfOneTransitionWritesOneStatement()
    {
        GivenHeaders(
            Header("a", FirstJob, StoredTriggerState.Waiting),
            Header("b", FirstJob, StoredTriggerState.Acquired));

        await store.PauseTriggers(Keys("a", "b"));

        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>.That.Matches(keys => keys.Count == 2),
                StoredTriggerState.Paused,
                A<IReadOnlyCollection<StoredTriggerState>>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// Whether a job blocks its triggers depends on the job, so two jobs is two questions however many
    /// triggers they own between them.
    /// </summary>
    [Test]
    public async Task ResumingASetAsksTheBlockedQuestionOncePerJobRatherThanPerTrigger()
    {
        GivenHeaders(
            Header("a", FirstJob, StoredTriggerState.Paused),
            Header("b", FirstJob, StoredTriggerState.Paused),
            Header("c", SecondJob, StoredTriggerState.Paused));

        List<TriggerKey> resumed = await store.ResumeTriggers(Keys("a", "b", "c"));

        resumed.Should().HaveCount(3);

        A.CallTo(() => driverDelegate.SelectStoredTriggerHeaders(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.SelectTriggerHeader(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
                A<ConnectionAndTransactionHolder>._, A<FiredTriggerQuery>._, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);

        // All three make the same transition, so they travel in one statement.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<StoredTriggerState>._,
                A<IReadOnlyCollection<StoredTriggerState>>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The two carry different old states, so each names its own: a resume must never move a trigger
    /// out of a state it was not in.
    /// </summary>
    [Test]
    public async Task ResumingSeparatesThePausedFromThePausedBlocked()
    {
        GivenHeaders(
            Header("a", FirstJob, StoredTriggerState.Paused),
            Header("b", FirstJob, StoredTriggerState.PausedBlocked));

        await store.ResumeTriggers(Keys("a", "b"));

        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<StoredTriggerState>._,
                A<IReadOnlyCollection<StoredTriggerState>>.That.Matches(states => states.Contains(StoredTriggerState.Paused)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<StoredTriggerState>._,
                A<IReadOnlyCollection<StoredTriggerState>>.That.Matches(states => states.Contains(StoredTriggerState.PausedBlocked)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ATriggerWithNoNextFireTimeIsNotResumed()
    {
        GivenHeaders(new StoredTriggerHeader(
            new TriggerKey("spent", "g1"), FirstJob, StoredTriggerState.Paused, null, AdoConstants.TriggerTypeSimple));

        List<TriggerKey> resumed = await store.ResumeTriggers(Keys("spent"));

        resumed.Should().BeEmpty("a trigger that will never fire again has nothing to resume to");

        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<StoredTriggerState>._,
                A<IReadOnlyCollection<StoredTriggerState>>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// The matched jobs' triggers are one read rather than one read per job, and their keys rather than
    /// the triggers themselves: pausing decides on the stored state, and building each trigger would
    /// read a schedule nothing here looks at.
    /// </summary>
    [Test]
    public async Task PausingAMatcherOfJobsReadsEveryMatchedJobsTriggersInOneStatement()
    {
        A.CallTo(() => driverDelegate.SelectJobKeysInGroup(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<JobKey>>([FirstJob, SecondJob]));

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJobs(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerKey>>([new TriggerKey("a", "g1"), new TriggerKey("b", "g1")]));

        GivenHeaders(
            Header("a", FirstJob, StoredTriggerState.Waiting),
            Header("b", SecondJob, StoredTriggerState.Waiting));

        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupStartsWith("jo"));

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJobs(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(
                A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.SelectTriggersForJob(
                A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    private void GivenHeaders(params StoredTriggerHeader[] headers)
    {
        A.CallTo(() => driverDelegate.SelectStoredTriggerHeaders(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<StoredTriggerHeader>>(headers.Where(header => keys.Contains(header.Key)).ToList()));
    }

    private static StoredTriggerHeader Header(string name, JobKey jobKey, StoredTriggerState state)
    {
        return new StoredTriggerHeader(new TriggerKey(name, "g1"), jobKey, state, NextFireTime, AdoConstants.TriggerTypeSimple);
    }

    private static TriggerKey[] Keys(params string[] names) => [.. names.Select(name => new TriggerKey(name, "g1"))];

    /// <summary>
    /// An <see cref="AdoJobStoreBase" /> whose lock runs its callback, over a connection holder that
    /// reaches no database.
    /// </summary>
    private sealed class LockRunningJobStore : AdoJobStoreBase
    {
        public LockRunningJobStore()
            : base(TestJobStores.Dependencies())
        {
        }

        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic);
                fieldInfo.Should().NotBeNull();
                fieldInfo.SetValue(this, value);
            }
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return txCallback(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }
    }
}
