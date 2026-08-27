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
/// Which rows the ADO store writes to and deletes from PAUSED_JOB_GRPS, asserted against a faked
/// <see cref="IDriverDelegate" /> rather than a database.
/// </summary>
/// <remarks>
/// <para>
/// The end-to-end proof is <c>JobStoreContractTest</c>, which runs one set of assertions against the
/// in-memory store and a real SQLite one. What that cannot show is the decision this level makes: the
/// pause is recorded per matched group and never as the matcher's own text, and the insert is skipped
/// when the row is already there — the check-then-insert that keeps two cluster nodes pausing the same
/// group from colliding on the primary key.
/// </para>
/// <para>
/// The store double here runs the lock callback, which <c>AdoJobStoreBaseTest</c>'s deliberately does
/// not: its tests call the connection-taking members directly and want the public ones inert.
/// </para>
/// </remarks>
public class PausedJobGroupsTest
{
    private RecordingJobStore store;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        store = new RecordingJobStore();
        driverDelegate = A.Fake<IDriverDelegate>();
        store.DirectDelegate = driverDelegate;

        // The members every path below passes through on its way to the paused-groups table. A faked
        // ValueTask<List<T>> is default-constructed and yields null, which the store would then
        // enumerate, so each is arranged to answer an empty list instead.
        A.CallTo(() => driverDelegate.SelectJobKeysInGroup(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<JobKey>>([]));

        A.CallTo(() => driverDelegate.SelectTriggerGroupNames(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<string>>([]));

        A.CallTo(() => driverDelegate.SelectTriggerKeysInGroup(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerKey>>([]));

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJobs(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerKey>>([]));

        // Nothing is paused yet unless a test says otherwise. The pause path asks about the whole set
        // of matched groups at once and then writes only the ones with no row.
        A.CallTo(() => driverDelegate.SelectPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<string>>([]));
    }

    [Test]
    public async Task PausingAGroupThatHoldsNoJobsStillWritesItsRow()
    {
        List<string> paused = await store.PauseJobs(GroupMatcher<JobKey>.GroupEquals("reports"));

        paused.Should().Equal(["reports"],
            "an equality matcher names the group to pause, so it pauses whether or not that group holds anything yet");

        A.CallTo(() => driverDelegate.InsertPausedJobGroups(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<string>>.That.Matches(groups => groups.Count == 1 && groups.Contains("reports")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AGroupThatIsAlreadyPausedIsNotInsertedTwice()
    {
        A.CallTo(() => driverDelegate.SelectPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<string>>(["reports"]));

        await store.PauseJobs(GroupMatcher<JobKey>.GroupEquals("reports"));

        A.CallTo(() => driverDelegate.InsertPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task PausingAMatcherAsksAboutEveryGroupOnceAndWritesTheMissingRowsTogether()
    {
        A.CallTo(() => driverDelegate.SelectJobKeysInGroup(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<JobKey>>([new JobKey("a", "jga"), new JobKey("b", "jgb")]));

        await store.PauseJobs(GroupMatcher<JobKey>.GroupStartsWith("jg"));

        A.CallTo(() => driverDelegate.SelectPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.InsertPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.IsJobGroupPaused(
                A<ConnectionAndTransactionHolder>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.InsertPausedJobGroup(
                A<ConnectionAndTransactionHolder>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task APrefixPauseRecordsTheGroupsItMatchedAndNotThePattern()
    {
        A.CallTo(() => driverDelegate.SelectJobKeysInGroup(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<JobKey>>([new JobKey("a", "jga"), new JobKey("b", "jgb")]));

        List<string> paused = await store.PauseJobs(GroupMatcher<JobKey>.GroupStartsWith("jg"));

        paused.Should().BeEquivalentTo(["jga", "jgb"]);

        // A pattern is not a group, and no job could ever belong to one, so the matcher's own text
        // must never reach the table.
        A.CallTo(() => driverDelegate.InsertPausedJobGroups(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<string>>.That.Matches(groups => groups.Count == 2 && groups.Contains("jga") && groups.Contains("jgb")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ResumingByMatcherDeletesWithTheSameMatcher()
    {
        GroupMatcher<JobKey> matcher = GroupMatcher<JobKey>.GroupStartsWith("jg");

        await store.ResumeJobs(matcher);

        // A prefix pause recorded a row per matched group, so a resume that only understood equality
        // would leave every one of them paused forever.
        A.CallTo(() => driverDelegate.DeletePausedJobGroup(
                A<ConnectionAndTransactionHolder>._, matcher, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ResumeAllClearsEveryPausedJobGroup()
    {
        await store.ResumeAll();

        // Resume-all means everything, or a paused job group survives it with nothing left to
        // resume it: the loop above only visits groups the job table knows about.
        A.CallTo(() => driverDelegate.DeletePausedJobGroup(
                A<ConnectionAndTransactionHolder>._,
                A<GroupMatcher<JobKey>>.That.Matches(m => m.CompareWithOperator.Equals(StringOperator.Anything)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task PauseAllLeavesJobGroupsAlone()
    {
        await store.PauseAll();

        // Pause-all is a trigger operation, and the in-memory store agrees: it leaves its own set of
        // paused job groups untouched.
        A.CallTo(() => driverDelegate.InsertPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AFailedInsertIsReportedAsAPersistenceFailure()
    {
        A.CallTo(() => driverDelegate.InsertPausedJobGroups(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<string>>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("primary key violation"));

        Func<Task> act = async () => await store.PauseJobs(GroupMatcher<JobKey>.GroupEquals("reports"));

        (await act.Should().ThrowAsync<JobPersistenceException>(
                "callers catch JobPersistenceException, so a provider's own exception must not escape as itself"))
            .WithInnerException<InvalidOperationException>();
    }

    [Test]
    public async Task AFailedDeleteIsReportedAsAPersistenceFailure()
    {
        A.CallTo(() => driverDelegate.DeletePausedJobGroup(
                A<ConnectionAndTransactionHolder>._, A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("connection reset"));

        Func<Task> act = async () => await store.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("reports"));

        (await act.Should().ThrowAsync<JobPersistenceException>())
            .WithInnerException<InvalidOperationException>();
    }

    /// <summary>
    /// An <see cref="AdoJobStoreBase" /> whose lock runs its callback, over a connection holder that
    /// reaches no database.
    /// </summary>
    private sealed class RecordingJobStore : AdoJobStoreBase
    {
        public RecordingJobStore()
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
