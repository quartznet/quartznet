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

using FakeItEasy;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The key-set members <see cref="IJobStore" /> supplies a default body for, exercised as a store
/// that has not overridden them.
/// </summary>
/// <remarks>
/// <para>
/// Every shipped store overrides all seven to walk the set in one lock or one transaction, so no other
/// test ever runs the defaults — and the interface promises that <em>the answer must not change when
/// a store overrides it</em>. That promise is only worth the paper it is written on if both sides of
/// it are asserted, and <c>JobStoreContractTest</c> asserts only the overriding side.
/// </para>
/// <para>
/// The store here is a fake whose plural member calls its base — the default implementation — while
/// its singular member answers from the table below it. That is exactly the shape of a third-party
/// store that implements only the single-key members.
/// </para>
/// </remarks>
public class JobStoreKeySetDefaultsTest
{
    private static readonly JobKey FirstJob = new JobKey("first", "jobs");
    private static readonly JobKey SecondJob = new JobKey("second", "jobs");
    private static readonly JobKey MissingJob = new JobKey("missing", "jobs");

    private static readonly TriggerKey FirstTrigger = new TriggerKey("first", "triggers");
    private static readonly TriggerKey SecondTrigger = new TriggerKey("second", "triggers");
    private static readonly TriggerKey MissingTrigger = new TriggerKey("missing", "triggers");

    private IJobStore store;

    [SetUp]
    public void BuildStoreThatOverridesNothing()
    {
        store = A.Fake<IJobStore>();

        A.CallTo(() => store.DeleteJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.DeleteTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.PauseJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.PauseTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.ResumeJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.ResumeTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.ResetTriggersFromErrorState(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.DeleteJobs(A<GroupMatcher<JobKey>>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.DeleteTriggers(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._)).CallsBaseMethod();
    }

    [Test]
    public async Task TheDefaultDeleteWalksTheSetAndNamesTheKeysTheSingleKeyMemberFound()
    {
        GivenTheseJobsExist(FirstJob, SecondJob);

        List<JobKey> deleted = await store.DeleteJobs([FirstJob, MissingJob, SecondJob]);

        deleted.Should().Equal([FirstJob, SecondJob],
            "the default is the plural of DeleteJob: it keeps the keys that answered true, in the "
            + "order they were given");

        A.CallTo(() => store.DeleteJob(A<JobKey>._, A<CancellationToken>._)).MustHaveHappened(3, Times.Exactly);
    }

    [Test]
    public async Task TheDefaultUnscheduleWalksTheSetAndNamesTheKeysTheSingleKeyMemberFound()
    {
        GivenTheseTriggersExist(FirstTrigger, SecondTrigger);

        List<TriggerKey> deleted = await store.DeleteTriggers([FirstTrigger, MissingTrigger, SecondTrigger]);

        deleted.Should().Equal([FirstTrigger, SecondTrigger]);
    }

    [Test]
    public async Task ADefaultAnswersNothingForAnEmptySet()
    {
        (await store.DeleteJobs([])).Should().BeEmpty();
        (await store.DeleteTriggers([])).Should().BeEmpty();

        A.CallTo(() => store.DeleteJob(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => store.DeleteTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The other five, so the whole family is exercised the same way rather than only the two this
    /// test file was written for.
    /// </summary>
    [Test]
    public async Task EveryOtherKeySetDefaultIsThePluralOfItsSingleKeyMember()
    {
        GivenTheseJobsExist(FirstJob);
        GivenTheseTriggersExist(FirstTrigger);

        (await store.PauseJobs([FirstJob, MissingJob])).Should().Equal([FirstJob]);
        (await store.ResumeJobs([FirstJob, MissingJob])).Should().Equal([FirstJob]);
        (await store.PauseTriggers([FirstTrigger, MissingTrigger])).Should().Equal([FirstTrigger]);
        (await store.ResumeTriggers([FirstTrigger, MissingTrigger])).Should().Equal([FirstTrigger]);
        (await store.ResetTriggersFromErrorState([FirstTrigger, MissingTrigger])).Should().Equal([FirstTrigger]);
    }

    /// <summary>
    /// The group form's default: list what matches, then delete it by key.
    /// </summary>
    /// <remarks>
    /// A store that overrides nothing still answers a group delete correctly, which is the point of
    /// giving the member a body at all. What it cannot be is atomic - the listing and the deletion
    /// are two operations - so the shipped stores override it; this is the other side of that
    /// promise, and the only place the default runs.
    /// </remarks>
    [Test]
    public async Task TheDefaultGroupDeleteListsTheGroupAndThenDeletesTheKeysItNamed()
    {
        GivenTheseJobsExist(FirstJob, SecondJob);
        GivenTheseJobsAreInTheStore(FirstJob, SecondJob);

        List<JobKey> deleted = await store.DeleteJobs(GroupMatcher<JobKey>.GroupEquals("jobs"));

        deleted.Should().Equal([FirstJob, SecondJob], "the default answers with what the delete deleted");

        A.CallTo(() => store.DeleteJobs(
                A<IReadOnlyCollection<JobKey>>.That.IsSameSequenceAs(FirstJob, SecondJob),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TheDefaultGroupUnscheduleListsTheGroupAndThenDeletesTheKeysItNamed()
    {
        GivenTheseTriggersExist(FirstTrigger, SecondTrigger);
        GivenTheseTriggersAreInTheStore(FirstTrigger, SecondTrigger);

        List<TriggerKey> deleted = await store.DeleteTriggers(GroupMatcher<TriggerKey>.GroupEquals("triggers"));

        deleted.Should().Equal([FirstTrigger, SecondTrigger]);

        A.CallTo(() => store.DeleteTriggers(
                A<IReadOnlyCollection<TriggerKey>>.That.IsSameSequenceAs(FirstTrigger, SecondTrigger),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The listing the default performs asks for the whole group, not a page of it.
    /// </summary>
    /// <remarks>
    /// <see cref="PagedQuery.Take" /> defaults to 250 precisely so that an unpaged call cannot
    /// materialize an unbounded result by accident. A delete that inherited that default would empty
    /// the first 250 of a group and report success, which is the one thing worse than being slow.
    /// </remarks>
    [Test]
    public async Task TheDefaultGroupDeleteAsksForEveryMatchRatherThanAPage()
    {
        GivenTheseJobsAreInTheStore(FirstJob);
        GivenTheseTriggersAreInTheStore(FirstTrigger);

        GroupMatcher<JobKey> jobs = GroupMatcher<JobKey>.GroupStartsWith("jo");
        GroupMatcher<TriggerKey> triggers = GroupMatcher<TriggerKey>.GroupStartsWith("tri");

        await store.DeleteJobs(jobs);
        await store.DeleteTriggers(triggers);

        A.CallTo(() => store.QueryJobs(
                A<JobQuery>.That.Matches(query => query.Group == jobs && query.Take == int.MaxValue),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => store.QueryTriggers(
                A<TriggerQuery>.That.Matches(query => query.Group == triggers && query.Take == int.MaxValue),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ADefaultAnswersNothingForAGroupThatMatchesNothing()
    {
        GivenTheseJobsAreInTheStore();
        GivenTheseTriggersAreInTheStore();

        (await store.DeleteJobs(GroupMatcher<JobKey>.GroupEquals("empty"))).Should().BeEmpty();
        (await store.DeleteTriggers(GroupMatcher<TriggerKey>.GroupEquals("empty"))).Should().BeEmpty();

        A.CallTo(() => store.DeleteJob(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => store.DeleteTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AGroupMatcherIsRequiredEvenOfADefault()
    {
        Func<Task> deletingNothing = async () => await store.DeleteJobs((GroupMatcher<JobKey>) null!);
        await deletingNothing.Should().ThrowAsync<ArgumentNullException>();

        Func<Task> unschedulingNothing = async () => await store.DeleteTriggers((GroupMatcher<TriggerKey>) null!);
        await unschedulingNothing.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// What the listing the group default runs would answer.
    /// </summary>
    private void GivenTheseJobsAreInTheStore(params JobKey[] present)
    {
        List<JobHeader> headers = [.. present.Select(key => new JobHeader(
            key,
            Description: null,
            JobTypeName: "Quartz.Simpl.NoOpJob, Quartz",
            Durable: false,
            ConcurrentExecutionDisallowed: false,
            PersistJobDataAfterExecution: false,
            RequestsRecovery: false))];

        A.CallTo(() => store.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>(headers, HasMore: false));
    }

    /// <inheritdoc cref="GivenTheseJobsAreInTheStore" />
    private void GivenTheseTriggersAreInTheStore(params TriggerKey[] present)
    {
        List<TriggerHeader> headers = [.. present.Select(key => new TriggerHeader(
            key,
            JobKey: FirstJob,
            Description: null,
            TriggerType: "SIMPLE",
            State: TriggerState.Normal,
            StartTimeUtc: DateTimeOffset.UnixEpoch,
            EndTimeUtc: null,
            NextFireTimeUtc: null,
            PreviousFireTimeUtc: null,
            CalendarName: null,
            Priority: 5,
            ExecutionGroup: null))];

        A.CallTo(() => store.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>(headers, HasMore: false));
    }

    private void GivenTheseJobsExist(params JobKey[] existing)
    {
        A.CallTo(() => store.DeleteJob(A<JobKey>._, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => store.PauseJob(A<JobKey>._, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => store.ResumeJob(A<JobKey>._, A<CancellationToken>._)).Returns(false);

        foreach (JobKey key in existing)
        {
            A.CallTo(() => store.DeleteJob(key, A<CancellationToken>._)).Returns(true);
            A.CallTo(() => store.PauseJob(key, A<CancellationToken>._)).Returns(true);
            A.CallTo(() => store.ResumeJob(key, A<CancellationToken>._)).Returns(true);
        }
    }

    private void GivenTheseTriggersExist(params TriggerKey[] existing)
    {
        A.CallTo(() => store.DeleteTrigger(A<TriggerKey>._, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => store.PauseTrigger(A<TriggerKey>._, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => store.ResumeTrigger(A<TriggerKey>._, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => store.ResetTriggerFromErrorState(A<TriggerKey>._, A<CancellationToken>._)).Returns(false);

        foreach (TriggerKey key in existing)
        {
            A.CallTo(() => store.DeleteTrigger(key, A<CancellationToken>._)).Returns(true);
            A.CallTo(() => store.PauseTrigger(key, A<CancellationToken>._)).Returns(true);
            A.CallTo(() => store.ResumeTrigger(key, A<CancellationToken>._)).Returns(true);
            A.CallTo(() => store.ResetTriggerFromErrorState(key, A<CancellationToken>._)).Returns(true);
        }
    }
}
