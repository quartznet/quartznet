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
/// What the ADO store's key-set delete and unschedule answer, and what they cost to answer it.
/// </summary>
/// <remarks>
/// <para>
/// The end-to-end proof is <c>JobStoreContractTest</c> against a real SQLite database, and
/// <c>SmokeTestPerformer</c> against every dialect CI runs. What only this level can show is the two
/// facts the design turns on: the answer is assembled from the per-key deletes the cascade already
/// performs, so naming the keys costs no extra round trip, and the whole set runs inside **one**
/// <see cref="SchedulerLock.TriggerAccess" /> scope rather than one per key.
/// </para>
/// <para>
/// The delegate is faked, so "a key that exists" here means one whose delete reports a deleted row —
/// which is exactly what the store has to go on against a real database.
/// </para>
/// </remarks>
public class AdoBulkDeleteTest
{
    private static readonly JobKey FirstJob = new JobKey("first", "jobs");
    private static readonly JobKey SecondJob = new JobKey("second", "jobs");
    private static readonly JobKey MissingJob = new JobKey("missing", "jobs");

    private static readonly TriggerKey FirstTrigger = new TriggerKey("first", "triggers");
    private static readonly TriggerKey SecondTrigger = new TriggerKey("second", "triggers");
    private static readonly TriggerKey MissingTrigger = new TriggerKey("missing", "triggers");

    private LockCountingJobStore store;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        store = new LockCountingJobStore();
        driverDelegate = A.Fake<IDriverDelegate>();
        store.DirectDelegate = driverDelegate;

        // A faked ValueTask<List<T>> yields null, which the store would then enumerate.
        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(
                A<ConnectionAndTransactionHolder>._,
                A<JobKey>._,
                A<CancellationToken>._))
            .Returns(new List<TriggerKey>());
    }

    [Test]
    public async Task DeletingASetOfJobsNamesTheKeysWhoseRowsWereDeleted()
    {
        GivenJobDetailDeleteAffectsRowsFor(FirstJob, SecondJob);

        List<JobKey> deleted = await store.DeleteJobs([FirstJob, MissingJob, SecondJob]);

        deleted.Should().Equal([FirstJob, SecondJob],
            "the answer is each delete's own outcome, in the order the keys were given — the key whose "
            + "delete affected no row is absent");
    }

    [Test]
    public async Task DeletingASetOfJobsCostsOneRoundTripPerKeyAndNoMore()
    {
        GivenJobDetailDeleteAffectsRowsFor(FirstJob, SecondJob);

        await store.DeleteJobs([FirstJob, MissingJob, SecondJob]);

        A.CallTo(() => driverDelegate.DeleteJobDetail(
                A<ConnectionAndTransactionHolder>._,
                A<JobKey>._,
                A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);

        store.LockScopes.Should().Be(1,
            "the set is deleted inside one TriggerAccess scope and one transaction — reporting which "
            + "keys were deleted must not cost a lock per key");
    }

    [Test]
    public async Task DeletingASetOfTriggersNamesTheKeysWhoseRowsWereDeleted()
    {
        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .Returns(0);
        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, FirstTrigger, A<CancellationToken>._))
            .Returns(1);
        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, SecondTrigger, A<CancellationToken>._))
            .Returns(1);

        List<TriggerKey> deleted = await store.DeleteTriggers([FirstTrigger, MissingTrigger, SecondTrigger]);

        deleted.Should().Equal([FirstTrigger, SecondTrigger]);
        store.LockScopes.Should().Be(1);
    }

    [Test]
    public async Task ASetInWhichNothingWasFoundIsAnEmptyAnswer()
    {
        A.CallTo(() => driverDelegate.DeleteJobDetail(A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<CancellationToken>._))
            .Returns(0);

        List<JobKey> deleted = await store.DeleteJobs([MissingJob]);

        deleted.Should().BeEmpty("nothing was there to delete, which is an answer rather than an error");
    }

    /// <summary>
    /// The group form resolves the keys itself, inside the lock it deletes them under.
    /// </summary>
    /// <remarks>
    /// That is the whole reason the member exists rather than leaving a caller to list the group and
    /// hand the keys back: between a listing and a delete another node can schedule one more thing
    /// into the group, and the delete the caller believes emptied it would miss it. So the assertion
    /// is not only the answer but where the keys came from - the store asked its delegate, inside the
    /// same TriggerAccess scope, and made no second one.
    /// </remarks>
    [Test]
    public async Task DeletingAGroupOfJobsResolvesTheKeysInsideTheSameLockThatDeletesThem()
    {
        GroupMatcher<JobKey> matcher = GroupMatcher<JobKey>.GroupEquals("jobs");
        GivenTheseJobsAreInTheGroup(matcher, FirstJob, MissingJob, SecondJob);
        GivenJobDetailDeleteAffectsRowsFor(FirstJob, SecondJob);

        List<JobKey> deleted = await store.DeleteJobs(matcher);

        deleted.Should().Equal([FirstJob, SecondJob],
            "the answer is each delete's own outcome, exactly as the key-set form's is");

        A.CallTo(() => driverDelegate.SelectJobKeysInGroup(
                A<ConnectionAndTransactionHolder>._,
                matcher,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        store.LockScopes.Should().Be(1,
            "resolving the group and emptying it are one scope and one transaction - two would be the "
            + "race the group form exists to close");
    }

    [Test]
    public async Task DeletingAGroupOfTriggersResolvesTheKeysInsideTheSameLockThatDeletesThem()
    {
        GroupMatcher<TriggerKey> matcher = GroupMatcher<TriggerKey>.GroupStartsWith("trig");
        GivenTheseTriggersAreInTheGroup(matcher, FirstTrigger, MissingTrigger, SecondTrigger);

        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .Returns(0);
        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, FirstTrigger, A<CancellationToken>._))
            .Returns(1);
        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, SecondTrigger, A<CancellationToken>._))
            .Returns(1);

        List<TriggerKey> deleted = await store.DeleteTriggers(matcher);

        deleted.Should().Equal([FirstTrigger, SecondTrigger]);

        A.CallTo(() => driverDelegate.SelectTriggerKeysInGroup(
                A<ConnectionAndTransactionHolder>._,
                matcher,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        store.LockScopes.Should().Be(1);
    }

    [Test]
    public async Task AGroupThatResolvedToNothingIsAnEmptyAnswerAndNoDeletes()
    {
        GivenTheseJobsAreInTheGroup(GroupMatcher<JobKey>.AnyGroup());
        GivenTheseTriggersAreInTheGroup(GroupMatcher<TriggerKey>.AnyGroup());

        (await store.DeleteJobs(GroupMatcher<JobKey>.AnyGroup())).Should().BeEmpty();
        (await store.DeleteTriggers(GroupMatcher<TriggerKey>.AnyGroup())).Should().BeEmpty();

        A.CallTo(() => driverDelegate.DeleteJobDetail(A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AGroupMatcherIsRequiredBeforeAnyLockIsTaken()
    {
        Func<Task> deletingNothing = async () => await store.DeleteJobs((GroupMatcher<JobKey>) null!);
        await deletingNothing.Should().ThrowAsync<ArgumentNullException>();

        Func<Task> unschedulingNothing = async () => await store.DeleteTriggers((GroupMatcher<TriggerKey>) null!);
        await unschedulingNothing.Should().ThrowAsync<ArgumentNullException>();

        store.LockScopes.Should().Be(0,
            "a malformed call must not open a transaction, let alone take the scheduler's trigger lock");
    }

    private void GivenTheseJobsAreInTheGroup(GroupMatcher<JobKey> matcher, params JobKey[] present)
    {
        A.CallTo(() => driverDelegate.SelectJobKeysInGroup(
                A<ConnectionAndTransactionHolder>._,
                matcher,
                A<CancellationToken>._))
            .Returns(new List<JobKey>(present));
    }

    private void GivenTheseTriggersAreInTheGroup(GroupMatcher<TriggerKey> matcher, params TriggerKey[] present)
    {
        A.CallTo(() => driverDelegate.SelectTriggerKeysInGroup(
                A<ConnectionAndTransactionHolder>._,
                matcher,
                A<CancellationToken>._))
            .Returns(new List<TriggerKey>(present));
    }

    private void GivenJobDetailDeleteAffectsRowsFor(params JobKey[] existing)
    {
        A.CallTo(() => driverDelegate.DeleteJobDetail(A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<CancellationToken>._))
            .Returns(0);

        foreach (JobKey key in existing)
        {
            A.CallTo(() => driverDelegate.DeleteJobDetail(A<ConnectionAndTransactionHolder>._, key, A<CancellationToken>._))
                .Returns(1);
        }
    }

    /// <summary>
    /// An <see cref="AdoJobStoreBase" /> whose lock runs its callback and counts how often it was
    /// entered, over a connection holder that reaches no database.
    /// </summary>
    private sealed class LockCountingJobStore : AdoJobStoreBase
    {
        public LockCountingJobStore()
            : base(TestJobStores.Dependencies())
        {
        }

        public int LockScopes { get; private set; }

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
            lockKind.Should().Be(SchedulerLock.TriggerAccess, "a delete mutates triggers");
            LockScopes++;
            return txCallback(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }
    }
}
