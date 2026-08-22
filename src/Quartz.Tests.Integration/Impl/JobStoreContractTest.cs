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

using AwesomeAssertions.Execution;

using Quartz.Extensibility;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// The <see cref="IJobStore" /> contract, asserted against every store that implements it.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory store and the ADO.NET store implement one interface twice over, and nothing held the
/// two to the same answers: each had its own tests, written against whatever the store in front of the
/// author happened to do. This fixture is the shared half — one set of assertions, run once per store,
/// so a store that quietly disagrees with the other fails here rather than in an application.
/// </para>
/// <para>
/// Where the two genuinely disagree the difference is a hook, not a missing assertion: each store says
/// which way it behaves and both branches are asserted, so the divergence is written down and can be
/// found by anyone deciding whether to close it. One hook is left,
/// <see cref="ReportsJobGroupPauseState" />: the ADO schema has a paused-groups table for triggers and
/// none for jobs, so closing it is a schema change rather than a code change.
/// </para>
/// </remarks>
public abstract class JobStoreContractTest
{
    protected const string JobGroupA = "jga";
    protected const string JobGroupB = "jgb";
    protected const string TriggerGroupA = "tga";
    protected const string TriggerGroupB = "tgb";
    protected const string OtherGroup = "other";

    private static readonly JobKey AnchorJobKey = new JobKey("anchor", JobGroupA);
    private static readonly TriggerKey AnchorTriggerKey = new TriggerKey("anchor", TriggerGroupA);
    private static readonly JobKey MissingJobKey = new JobKey("no-such-job", "no-such-group");
    private static readonly TriggerKey MissingTriggerKey = new TriggerKey("no-such-trigger", "no-such-group");

    private const string MissingCalendarName = "no-such-calendar";

    /// <summary>
    /// The store under test, built fresh for each test and shut down after it.
    /// </summary>
    protected IJobStore Store { get; private set; }

    /// <summary>
    /// Builds a store that is initialized and ready for work.
    /// </summary>
    /// <remarks>
    /// The scheduler is deliberately never started: <see cref="IJobStore.SchedulerStarted" /> spawns
    /// background loops that would move trigger state underneath the tests that drive it by hand.
    /// </remarks>
    protected abstract ValueTask<IJobStore> CreateStore();

    /// <summary>
    /// Releases whatever <see cref="CreateStore" /> allocated around the store itself, after the store
    /// has been shut down.
    /// </summary>
    protected virtual ValueTask DisposeStore() => default;

    /// <summary>
    /// Whether the store can report that a job group is paused through
    /// <see cref="IJobStore.QueryJobGroups" />.
    /// </summary>
    protected abstract bool ReportsJobGroupPauseState { get; }

    [SetUp]
    public async Task BuildStoreUnderTest()
    {
        Store = await CreateStore();
    }

    [TearDown]
    public async Task ShutDownStoreUnderTest()
    {
        if (Store is not null)
        {
            // Every store this fixture builds is shut down, the ADO one included: its background
            // handlers are foreground threads, so a store left running leaks one per test.
            await Store.Shutdown();
            Store = null;
        }

        await DisposeStore();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Pausing and resuming
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task PausingAndResumingOneTriggerWalksItThroughTheStates()
    {
        IOperableTrigger trigger = await ScheduleJobWithTrigger("one", JobGroupA, TriggerGroupA);

        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
            "a trigger that has just been scheduled is waiting to fire");

        (await Store.PauseTrigger(trigger.Key)).Should().BeTrue("pausing a waiting trigger moves it");
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);

        (await Store.PauseTrigger(trigger.Key)).Should().BeFalse(
            "a trigger that is already paused is not moved by pausing it again");
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);

        (await Store.ResumeTrigger(trigger.Key)).Should().BeTrue("resuming a paused trigger moves it");
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);

        (await Store.ResumeTrigger(trigger.Key)).Should().BeFalse(
            "a trigger that is not paused is not moved by resuming it");
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task PausingAJobPausesEveryTriggerItHas()
    {
        IJobDetail job = CreateJob("job", JobGroupA);
        IOperableTrigger first = CreateTrigger("first", TriggerGroupA, job.Key);
        IOperableTrigger second = CreateTrigger("second", TriggerGroupB, job.Key);

        await Store.ScheduleJob(job, first);
        await Store.AddTrigger(second, replace: false);

        (await Store.PauseJob(job.Key)).Should().BeTrue();

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Paused,
            "pausing a job reaches its triggers whatever group they are in");

        (await Store.ResumeJob(job.Key)).Should().BeTrue();

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task PausingAJobThatHasNoTriggersStillReportsTheJobWasFound()
    {
        IJobDetail job = JobBuilder.Create<ContractTestJob>()
            .WithIdentity("durable", JobGroupA)
            .StoreDurably()
            .Build();

        await Store.AddJob(job, replace: false);

        (await Store.PauseJob(job.Key)).Should().BeTrue(
            "the answer says whether the job was found, not how many triggers it happened to have");
        (await Store.ResumeJob(job.Key)).Should().BeTrue();
    }

    [Test]
    public async Task PausingAndResumingASetOfTriggersReportsOnlyTheKeysItMoved()
    {
        IOperableTrigger first = await ScheduleJobWithTrigger("first", JobGroupA, TriggerGroupA);
        IOperableTrigger second = await ScheduleJobWithTrigger("second", JobGroupA, TriggerGroupB);
        IOperableTrigger alreadyPaused = await ScheduleJobWithTrigger("already", JobGroupA, TriggerGroupA);

        (await Store.PauseTrigger(alreadyPaused.Key)).Should().BeTrue();

        List<TriggerKey> paused = await Store.PauseTriggers(
            [first.Key, MissingTriggerKey, alreadyPaused.Key, second.Key]);

        paused.Should().Equal([first.Key, second.Key],
            "the answer names the keys the pause moved, in the order they were given — a missing key "
            + "and an already-paused one are absent rather than a throw");

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Paused);

        List<TriggerKey> resumed = await Store.ResumeTriggers([second.Key, MissingTriggerKey, first.Key]);

        resumed.Should().Equal([second.Key, first.Key], "the resume answers in the order it was asked");
        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(alreadyPaused.Key)).Should().Be(TriggerState.Paused,
            "a key that was not asked for is not touched");
    }

    [Test]
    public async Task PausingAndResumingASetOfJobsReportsOnlyTheKeysItFound()
    {
        IOperableTrigger first = await ScheduleJobWithTrigger("first", JobGroupA, TriggerGroupA);
        IOperableTrigger second = await ScheduleJobWithTrigger("second", JobGroupB, TriggerGroupB);

        IJobDetail durable = JobBuilder.Create<ContractTestJob>()
            .WithIdentity("durable", JobGroupA)
            .StoreDurably()
            .Build();
        await Store.AddJob(durable, replace: false);

        JobKey firstJob = new JobKey("first", JobGroupA);
        JobKey secondJob = new JobKey("second", JobGroupB);

        List<JobKey> paused = await Store.PauseJobs([firstJob, MissingJobKey, durable.Key, secondJob]);

        paused.Should().Equal([firstJob, durable.Key, secondJob],
            "a job with no triggers was still found, and only the key that names no job is absent");

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Paused);

        List<JobKey> resumed = await Store.ResumeJobs([firstJob, MissingJobKey, secondJob]);

        resumed.Should().Equal([firstJob, secondJob]);
        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public void TheKeySetMembersAreImplementedRatherThanLeftToTheLoopingDefault()
    {
        // The defaults on IJobStore walk the set one key at a time, which is correct but costs a lock
        // or a round trip per key. Every shipped store does the walk in one pass instead, and the
        // answers are identical either way — so this is the only place the difference is visible.
        string[] keySetMembers =
        [
            nameof(IJobStore.PauseTriggers),
            nameof(IJobStore.ResumeTriggers),
            nameof(IJobStore.PauseJobs),
            nameof(IJobStore.ResumeJobs),
            nameof(IJobStore.ResetTriggersFromErrorState)
        ];

        System.Reflection.InterfaceMapping map = Store.GetType().GetInterfaceMap(typeof(IJobStore));

        for (int i = 0; i < map.InterfaceMethods.Length; i++)
        {
            System.Reflection.MethodInfo declared = map.InterfaceMethods[i];
            if (!keySetMembers.Contains(declared.Name)
                || declared.GetParameters()[0].ParameterType.GetGenericTypeDefinition() != typeof(IReadOnlyCollection<>))
            {
                continue;
            }

            map.TargetMethods[i].DeclaringType.Should().NotBe(typeof(IJobStore),
                $"{Store.GetType().Name}.{declared.Name} must coalesce the key set into one pass "
                + "rather than inherit the per-key default");
        }
    }

    [Test]
    public async Task PausingAnEmptySetOfKeysIsANoOp()
    {
        IOperableTrigger trigger = await ScheduleJobWithTrigger("untouched", JobGroupA, TriggerGroupA);

        (await Store.PauseTriggers([])).Should().BeEmpty();
        (await Store.ResumeTriggers([])).Should().BeEmpty();
        (await Store.PauseJobs([])).Should().BeEmpty();
        (await Store.ResumeJobs([])).Should().BeEmpty();
        (await Store.ResetTriggersFromErrorState([])).Should().BeEmpty();

        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task PausingAJobGroupPausesTheTriggersOfEveryJobInIt()
    {
        IOperableTrigger inGroup = await ScheduleJobWithTrigger("in-group", JobGroupA, TriggerGroupA);
        IOperableTrigger elsewhere = await ScheduleJobWithTrigger("elsewhere", JobGroupB, TriggerGroupB);

        List<string> paused = await Store.PauseJobs(GroupMatcher<JobKey>.GroupEquals(JobGroupA));

        paused.Should().Equal([JobGroupA], "the group that was asked for is the group that paused");
        (await Store.GetTriggerState(inGroup.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(elsewhere.Key)).Should().Be(TriggerState.Normal,
            "a job group pause stops at the group's edge");

        List<string> resumed = await Store.ResumeJobs(GroupMatcher<JobKey>.GroupEquals(JobGroupA));

        resumed.Should().Equal([JobGroupA]);
        (await Store.GetTriggerState(inGroup.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task PausingJobGroupsByPrefixReachesEveryGroupThatMatches()
    {
        IOperableTrigger first = await ScheduleJobWithTrigger("first", JobGroupA, TriggerGroupA);
        IOperableTrigger second = await ScheduleJobWithTrigger("second", JobGroupB, TriggerGroupB);
        IOperableTrigger untouched = await ScheduleJobWithTrigger("untouched", OtherGroup, OtherGroup);

        List<string> paused = await Store.PauseJobs(GroupMatcher<JobKey>.GroupStartsWith("jg"));

        paused.Should().BeEquivalentTo([JobGroupA, JobGroupB],
            "a prefix matcher pauses every group whose name starts with it");
        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(untouched.Key)).Should().Be(TriggerState.Normal,
            "a group the prefix does not match is never touched");

        await Store.ResumeJobs(GroupMatcher<JobKey>.GroupStartsWith("jg"));

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task PausingATriggerGroupPausesEveryTriggerInIt()
    {
        IOperableTrigger inGroup = await ScheduleJobWithTrigger("in-group", JobGroupA, TriggerGroupA);
        IOperableTrigger elsewhere = await ScheduleJobWithTrigger("elsewhere", JobGroupA, OtherGroup);

        List<string> paused = await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(TriggerGroupA));

        paused.Should().Equal([TriggerGroupA]);
        (await Store.GetTriggerState(inGroup.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(elsewhere.Key)).Should().Be(TriggerState.Normal);

        PagedResult<TriggerGroup> pausedGroups = await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true });
        pausedGroups.Items.Select(x => x.Name).Should().Equal([TriggerGroupA],
            "a store has to remember which trigger groups are paused, or the pause is forgotten");

        await Store.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(TriggerGroupA));

        (await Store.GetTriggerState(inGroup.Key)).Should().Be(TriggerState.Normal);
        (await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true })).Items.Should().BeEmpty(
            "resuming the group takes it off the paused list");
    }

    [Test]
    public async Task PausingTriggerGroupsByPrefixReachesEveryGroupThatMatches()
    {
        IOperableTrigger first = await ScheduleJobWithTrigger("first", JobGroupA, TriggerGroupA);
        IOperableTrigger second = await ScheduleJobWithTrigger("second", JobGroupA, TriggerGroupB);
        IOperableTrigger untouched = await ScheduleJobWithTrigger("untouched", JobGroupA, OtherGroup);

        List<string> paused = await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupStartsWith("tg"));

        paused.Should().BeEquivalentTo([TriggerGroupA, TriggerGroupB],
            "a prefix matcher pauses every group whose name starts with it");
        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Paused);

        (await Store.GetTriggerState(untouched.Key)).Should().Be(TriggerState.Normal,
            "a group the prefix does not match is never touched");

        (await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true })).Items
            .Select(x => x.Name).Should().BeEquivalentTo([TriggerGroupA, TriggerGroupB],
                "what a prefix pause records is the groups it matched, never the pattern itself");

        // A group that would have matched the pattern but held no triggers when the pause ran was
        // never one of the matched groups, so nothing imposes the pause on it afterwards. Pausing a
        // group that does not exist yet is what the equality matcher is for.
        IJobDetail lateJob = CreateJob("late", JobGroupA);
        IOperableTrigger late = CreateTrigger("late", "tg-late", lateJob.Key);
        await Store.ScheduleJob(lateJob, late);

        (await Store.GetTriggerState(late.Key)).Should().Be(TriggerState.Normal,
            "the prefix pause matched groups, not a pattern the store keeps applying");

        // A trigger joining a group that *was* matched is born paused, because that group is recorded.
        IJobDetail joiningJob = CreateJob("joining", JobGroupA);
        IOperableTrigger joining = CreateTrigger("joining", TriggerGroupA, joiningJob.Key);
        await Store.ScheduleJob(joiningJob, joining);

        (await Store.GetTriggerState(joining.Key)).Should().Be(TriggerState.Paused,
            "a group the prefix pause matched is a paused group like any other");

        await Store.ResumeTriggers(GroupMatcher<TriggerKey>.GroupStartsWith("tg"));

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Normal);
        (await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true })).Items.Should().BeEmpty(
            "the same prefix that paused the groups takes the pause off them again");
    }

    [Test]
    public async Task PauseAllAndResumeAllReachEveryGroup()
    {
        IOperableTrigger first = await ScheduleJobWithTrigger("first", JobGroupA, TriggerGroupA);
        IOperableTrigger second = await ScheduleJobWithTrigger("second", JobGroupB, TriggerGroupB);

        await Store.PauseAll();

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Paused);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Paused);

        PagedResult<TriggerGroup> pausedGroups = await Store.QueryTriggerGroups(
            new TriggerGroupQuery { Paused = true, IncludeTotalCount = true });

        // Whatever a store records to mean "everything is paused" stays its own business: a listing
        // reports groups, and a caller must never be handed a name no trigger can belong to.
        pausedGroups.Items.Select(x => x.Name).Should().BeEquivalentTo([TriggerGroupA, TriggerGroupB],
            "only real groups are paused");
        pausedGroups.TotalCount.Should().Be(2, "the count matches the listing it counts");

        await Store.ResumeAll();

        (await Store.GetTriggerState(first.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(second.Key)).Should().Be(TriggerState.Normal);
        (await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true })).Items.Should().BeEmpty(
            "resuming everything leaves nothing paused, marker included");
    }

    [Test]
    public async Task ATriggerAddedToAPausedGroupIsBornPaused()
    {
        IOperableTrigger existing = await ScheduleJobWithTrigger("existing", JobGroupA, TriggerGroupA);

        await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(TriggerGroupA));

        IJobDetail job = CreateJob("late", JobGroupA);
        IOperableTrigger late = CreateTrigger("late", TriggerGroupA, job.Key);
        await Store.ScheduleJob(job, late);

        (await Store.GetTriggerState(late.Key)).Should().Be(TriggerState.Paused,
            "a paused group imposes the pause on what is added to it, or the pause leaks");
        (await Store.GetTriggerState(existing.Key)).Should().Be(TriggerState.Paused);
    }

    [Test]
    public async Task PausingAGroupThatHasNoTriggersStillRemembersThePause()
    {
        List<string> paused = await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("empty-group"));

        paused.Should().Equal(["empty-group"],
            "pausing a group that holds nothing yet is how a caller pauses what is about to be added to it");

        (await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true })).Items
            .Select(x => x.Name).Should().Equal(["empty-group"],
                "a paused group with no triggers is still a paused group");

        IJobDetail job = CreateJob("late", JobGroupA);
        IOperableTrigger late = CreateTrigger("late", "empty-group", job.Key);
        await Store.ScheduleJob(job, late);

        (await Store.GetTriggerState(late.Key)).Should().Be(TriggerState.Paused);
    }

    [Test]
    public async Task ResumeAllAndAGroupThatIsPausedButEmpty()
    {
        await ScheduleJobWithTrigger("elsewhere", JobGroupA, TriggerGroupA);
        await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("empty-group"));

        await Store.ResumeAll();

        IJobDetail job = CreateJob("late", JobGroupA);
        IOperableTrigger late = CreateTrigger("late", "empty-group", job.Key);
        await Store.ScheduleJob(job, late);

        // A resume-all reaches a group that is paused but holds no triggers, which the group listing
        // could otherwise never show a caller a way to resume.
        (await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true })).Items.Should().BeEmpty();
        (await Store.GetTriggerState(late.Key)).Should().Be(TriggerState.Normal,
            "resume-all resumed everything, so there is no pause left to impose");
    }

    [Test]
    public async Task PausedJobGroupsAreReportedAsFarAsTheStoreCanReportThem()
    {
        IOperableTrigger trigger = await ScheduleJobWithTrigger("job", JobGroupA, TriggerGroupA);

        await Store.PauseJobs(GroupMatcher<JobKey>.GroupEquals(JobGroupA));

        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused,
            "whatever the store can report about the group, pausing it has to stop the triggers");

        PagedResult<JobGroup> paused = await Store.QueryJobGroups(new JobGroupQuery { Paused = true });
        PagedResult<JobGroup> listed = await Store.QueryJobGroups(new JobGroupQuery { Name = JobGroupA });

        if (ReportsJobGroupPauseState)
        {
            paused.Items.Select(x => x.Name).Should().Equal([JobGroupA]);
            listed.Items.Should().ContainSingle().Which.Paused.Should().BeTrue();
        }
        else
        {
            // A deliberate asymmetry, documented on JobGroupQuery.Paused: the ADO schema has a paused
            // groups table for triggers and none for jobs, so a paused job group is not something the
            // store can report. Pausing the group still pauses the triggers of the jobs in it, which
            // is the part callers depend on.
            paused.Items.Should().BeEmpty("the ADO store has nowhere to record a paused job group");
            listed.Items.Should().ContainSingle().Which.Paused.Should().BeFalse();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Error state
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ResetTriggerFromErrorStateBringsAnErroredTriggerBackToWaiting()
    {
        IOperableTrigger trigger = await GivenATriggerInErrorState();

        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Error);

        (await Store.ResetTriggerFromErrorState(trigger.Key)).Should().BeTrue(
            "the trigger was in error, so the reset had something to do");
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
            "a trigger out of error waits to fire again");

        (await Store.ResetTriggerFromErrorState(trigger.Key)).Should().BeFalse(
            "the second reset found nothing left to reset");
    }

    [Test]
    public async Task ResettingASetOfTriggersFromErrorStateReportsOnlyTheKeysItReset()
    {
        IOperableTrigger errored = await GivenATriggerInErrorState("errored");
        IOperableTrigger fine = await ScheduleJobWithTrigger("fine", JobGroupA, TriggerGroupA);

        List<TriggerKey> reset = await Store.ResetTriggersFromErrorState(
            [errored.Key, fine.Key, MissingTriggerKey]);

        reset.Should().Equal([errored.Key],
            "only the trigger that was in error had anything to reset; the healthy one and the "
            + "missing one are absent rather than a throw");

        (await Store.GetTriggerState(errored.Key)).Should().Be(TriggerState.Normal);
        (await Store.GetTriggerState(fine.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task ResetTriggerFromErrorStateIsANoOpForATriggerThatIsNotInError()
    {
        IOperableTrigger trigger = await ScheduleJobWithTrigger("fine", JobGroupA, TriggerGroupA);

        (await Store.ResetTriggerFromErrorState(trigger.Key)).Should().BeFalse(
            "there was no error to reset");
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);

        await Store.PauseTrigger(trigger.Key);

        (await Store.ResetTriggerFromErrorState(trigger.Key)).Should().BeFalse();
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused,
            "a reset that found no error must not resume a trigger somebody paused on purpose");
    }

    [Test]
    public async Task PausingTheGroupOfAnErroredTrigger()
    {
        IOperableTrigger trigger = await GivenATriggerInErrorState();

        await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(trigger.Key.Group));

        // Only WAITING, ACQUIRED and BLOCKED are pausable, so the error survives the pause and can
        // still be reset — into the pause, rather than past it.
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Error,
            "a trigger in error is not a trigger a group pause may quietly clear");

        (await Store.PauseTrigger(trigger.Key)).Should().BeFalse(
            "there was no pausable trigger to move, so the pause reports it moved nothing");

        (await Store.ResetTriggerFromErrorState(trigger.Key)).Should().BeTrue();
        (await Store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused,
            "the group is paused, so the trigger comes out of error into the pause");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Storing over something that is already there
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task StoringOverAJobOrTriggerWithoutReplacingRaisesTheSpecificException()
    {
        IJobDetail job = CreateJob("taken", JobGroupA);
        IOperableTrigger trigger = CreateTrigger("taken", TriggerGroupA, job.Key);
        await Store.ScheduleJob(job, trigger);

        // ObjectAlreadyExistsException derives from JobPersistenceException, so a store that wraps it
        // still satisfies a catch of the base type — and silently costs the caller the only thing that
        // told "already there" apart from "the store broke".
        Func<Task> addingTheJobAgain = async () => await Store.AddJob(CreateJob("taken", JobGroupA), replace: false);
        await addingTheJobAgain.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "storing over a job without asking to replace it names the mistake");

        Func<Task> addingTheTriggerAgain = async () => await Store.AddTrigger(
            CreateTrigger("taken", TriggerGroupA, job.Key), replace: false);
        await addingTheTriggerAgain.Should().ThrowAsync<ObjectAlreadyExistsException>();

        Func<Task> replacing = async () =>
        {
            await Store.AddJob(CreateJob("taken", JobGroupA), replace: true);
            await Store.AddTrigger(CreateTrigger("taken", TriggerGroupA, job.Key), replace: true);
        };

        await replacing.Should().NotThrowAsync("replacing is what the flag asks for");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Calendars
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task CalendarsAreStoredRetrievedReplacedAndDeleted()
    {
        MonthDay christmasEve = new MonthDay(12, 24);
        MonthDay christmas = new MonthDay(12, 25);

        AnnualCalendar calendar = new AnnualCalendar();
        calendar.AddExcludedDay(christmasEve);

        await Store.AddCalendar("holidays", calendar);

        ICalendar stored = await Store.GetCalendar("holidays");
        stored.Should().BeOfType<AnnualCalendar>()
            .Which.IsDayExcluded(christmasEve).Should().BeTrue(
                "a calendar has to come back out the way it went in");

        AnnualCalendar replacement = new AnnualCalendar();
        replacement.AddExcludedDay(christmas);

        Func<Task> addingAgain = async () => await Store.AddCalendar("holidays", replacement);

        // The specific type, on every store: "there is already one of those" is an answer a caller
        // catches by type, and a store that re-wrapped it would make that catch store-dependent.
        await addingAgain.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "adding over a calendar without asking to replace it is a mistake, not an update");

        await Store.AddCalendar("holidays", replacement, new AddCalendarOptions { Replace = true });

        AnnualCalendar updated = (AnnualCalendar) await Store.GetCalendar("holidays");
        updated.IsDayExcluded(christmas).Should().BeTrue();
        updated.IsDayExcluded(christmasEve).Should().BeFalse("the replacement replaced");

        (await Store.DeleteCalendar("holidays")).Should().BeTrue();
        (await Store.GetCalendar("holidays")).Should().BeNull();
    }

    [Test]
    public async Task ACalendarATriggerReferencesCannotBeDeleted()
    {
        await Store.AddCalendar("in-use", new AnnualCalendar());

        IJobDetail job = CreateJob("job", JobGroupA);
        IOperableTrigger trigger = CreateTrigger("trigger", TriggerGroupA, job.Key, calendarName: "in-use");
        await Store.ScheduleJob(job, trigger);

        Func<Task> deleting = async () => await Store.DeleteCalendar("in-use");
        await deleting.Should().ThrowAsync<JobPersistenceException>(
            "deleting a calendar out from under a trigger would leave the trigger pointing at nothing");

        (await Store.GetCalendar("in-use")).Should().NotBeNull("the refused delete changed nothing");

        (await Store.DeleteTrigger(trigger.Key)).Should().BeTrue();
        (await Store.DeleteCalendar("in-use")).Should().BeTrue("nothing references the calendar any more");
    }

    [Test]
    public async Task CalendarNamesArePagedInNameOrder()
    {
        for (int i = 0; i < 5; i++)
        {
            await Store.AddCalendar($"cal-{i}", new AnnualCalendar());
        }

        PagedResult<string> all = await Store.QueryCalendarNames(new CalendarQuery { IncludeTotalCount = true });
        all.Items.Should().Equal(["cal-0", "cal-1", "cal-2", "cal-3", "cal-4"]);
        all.HasMore.Should().BeFalse();
        all.TotalCount.Should().Be(5);

        PagedResult<string> page = await Store.QueryCalendarNames(new CalendarQuery
        {
            Skip = 1,
            Take = 2,
            IncludeTotalCount = true
        });

        page.Items.Should().Equal(["cal-1", "cal-2"], "a page is a window on the same ordering");
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(5, "the total ignores paging");

        PagedResult<string> pastEnd = await Store.QueryCalendarNames(new CalendarQuery { Skip = 5, Take = 5 });
        pastEnd.Items.Should().BeEmpty();
        pastEnd.HasMore.Should().BeFalse();
    }

    [Test]
    public async Task CalendarNamesAreFilteredByNameAndStillPaged()
    {
        foreach (string name in new[] { "holiday-easter", "holiday-xmas", "workday", "50%off" })
        {
            await Store.AddCalendar(name, new AnnualCalendar());
        }

        PagedResult<string> exact = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameEquals("workday")
        });
        exact.Items.Should().Equal(["workday"]);

        PagedResult<string> prefixed = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameStartsWith("holiday-"),
            IncludeTotalCount = true
        });
        prefixed.Items.Should().Equal(["holiday-easter", "holiday-xmas"]);
        prefixed.TotalCount.Should().Be(2, "the total counts what the filter selects, not every calendar");

        PagedResult<string> suffixed = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameEndsWith("xmas")
        });
        suffixed.Items.Should().Equal(["holiday-xmas"]);

        PagedResult<string> contained = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameContains("day")
        });
        contained.Items.Should().Equal(["holiday-easter", "holiday-xmas", "workday"],
            "a filtered listing keeps the ordering of an unfiltered one");

        PagedResult<string> filteredPage = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameContains("day"),
            Skip = 1,
            Take = 1,
            IncludeTotalCount = true
        });
        filteredPage.Items.Should().Equal(["holiday-xmas"], "paging windows the filtered set");
        filteredPage.HasMore.Should().BeTrue();
        filteredPage.TotalCount.Should().Be(3);

        PagedResult<string> counted = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameStartsWith("holiday-"),
            Take = 0,
            IncludeTotalCount = true
        });
        counted.Items.Should().BeEmpty("Take = 0 is the count idiom");
        counted.TotalCount.Should().Be(2);

        // The matcher's own text is a literal: '%' is not a wildcard, and the ADO store has to escape
        // it out of the LIKE it builds.
        PagedResult<string> literalWildcard = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameStartsWith("50%")
        });
        literalWildcard.Items.Should().Equal(["50%off"]);

        PagedResult<string> notAPattern = await Store.QueryCalendarNames(new CalendarQuery
        {
            Name = CalendarNameMatcher.NameStartsWith("50%o_f")
        });
        notAPattern.Items.Should().BeEmpty("'_' is a literal too, so it does not match the 'f' in '50%off'");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Paged listings
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task JobsArePagedInGroupThenNameOrder()
    {
        List<JobKey> seeded = await SeedForPaging();

        PagedResult<JobHeader> everything = await Store.QueryJobs(new JobQuery { Take = int.MaxValue });
        everything.Items.Select(x => x.Key).Should().Equal(seeded,
            "a listing is ordered by group and then name on every store");
        everything.HasMore.Should().BeFalse();
        everything.TotalCount.Should().BeNull("a total is only computed when it is asked for");
        everything.Items.Should().OnlyContain(x => x.JobTypeName.Contains(nameof(ContractTestJob)),
            "the listing carries the recorded job type name");
        everything.Items.Should().OnlyContain(x => x.Description.StartsWith("job ", StringComparison.Ordinal));

        PagedResult<JobHeader> first = await Store.QueryJobs(new JobQuery { Take = 5, IncludeTotalCount = true });
        first.Items.Select(x => x.Key).Should().Equal(seeded.Take(5));
        first.HasMore.Should().BeTrue();
        first.TotalCount.Should().Be(seeded.Count);

        PagedResult<JobHeader> second = await Store.QueryJobs(new JobQuery { Skip = 5, Take = 5, IncludeTotalCount = true });
        second.Items.Select(x => x.Key).Should().Equal(seeded.Skip(5).Take(5));
        second.HasMore.Should().BeTrue();

        PagedResult<JobHeader> last = await Store.QueryJobs(new JobQuery { Skip = 10, Take = 5 });
        last.Items.Select(x => x.Key).Should().Equal(seeded.Skip(10));
        last.HasMore.Should().BeFalse("nothing follows the last page");

        PagedResult<JobHeader> pastEnd = await Store.QueryJobs(new JobQuery { Skip = seeded.Count, Take = 5, IncludeTotalCount = true });
        pastEnd.Items.Should().BeEmpty();
        pastEnd.HasMore.Should().BeFalse();
        pastEnd.TotalCount.Should().Be(seeded.Count);

        PagedResult<JobHeader> countOnly = await Store.QueryJobs(new JobQuery { Take = 0, IncludeTotalCount = true });
        countOnly.Items.Should().BeEmpty("Take = 0 turns the query into a count");
        countOnly.TotalCount.Should().Be(seeded.Count);

        PagedResult<JobHeader> byGroup = await Store.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupEquals("pg-b"),
            IncludeTotalCount = true
        });
        byGroup.Items.Select(x => x.Key.Group).Should().AllBe("pg-b");
        byGroup.TotalCount.Should().Be(4);

        PagedResult<JobHeader> byPrefix = await Store.QueryJobs(new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupStartsWith("pg-"),
            IncludeTotalCount = true
        });
        byPrefix.TotalCount.Should().Be(seeded.Count, "every seeded group starts with the prefix");
    }

    [Test]
    public async Task TriggersArePagedInGroupThenNameOrder()
    {
        List<JobKey> seeded = await SeedForPaging();
        List<TriggerKey> expected = seeded.Select(x => new TriggerKey(x.Name, x.Group)).ToList();

        PagedResult<TriggerHeader> everything = await Store.QueryTriggers(new TriggerQuery { Take = int.MaxValue });
        everything.Items.Select(x => x.Key).Should().Equal(expected);
        everything.Items.Should().OnlyContain(x => x.NextFireTimeUtc != null,
            "a scheduled trigger knows when it fires next");

        PagedResult<TriggerHeader> page = await Store.QueryTriggers(new TriggerQuery
        {
            Skip = 4,
            Take = 4,
            IncludeTotalCount = true
        });

        page.Items.Select(x => x.Key).Should().Equal(expected.Skip(4).Take(4));
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(expected.Count);

        PagedResult<TriggerHeader> countOnly = await Store.QueryTriggers(new TriggerQuery { Take = 0, IncludeTotalCount = true });
        countOnly.Items.Should().BeEmpty();
        countOnly.TotalCount.Should().Be(expected.Count);

        PagedResult<TriggerHeader> forOneJob = await Store.QueryTriggers(new TriggerQuery
        {
            Job = seeded[0],
            IncludeTotalCount = true
        });
        forOneJob.TotalCount.Should().Be(1);
        forOneJob.Items.Should().ContainSingle().Which.JobKey.Should().Be(seeded[0]);

        PagedResult<TriggerHeader> byState = await Store.QueryTriggers(new TriggerQuery
        {
            State = TriggerState.Normal,
            IncludeTotalCount = true
        });
        byState.TotalCount.Should().Be(expected.Count, "nothing has been paused yet");

        await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("pg-b"));

        PagedResult<TriggerHeader> pausedOnly = await Store.QueryTriggers(new TriggerQuery
        {
            State = TriggerState.Paused,
            IncludeTotalCount = true
        });
        pausedOnly.TotalCount.Should().Be(4);
        pausedOnly.Items.Select(x => x.Key.Group).Should().AllBe("pg-b");
    }

    [Test]
    public async Task JobGroupsArePagedInNameOrder()
    {
        await SeedForPaging();

        PagedResult<JobGroup> all = await Store.QueryJobGroups(new JobGroupQuery { IncludeTotalCount = true });
        all.Items.Select(x => x.Name).Should().Equal(["pg-a", "pg-b", "pg-c"]);
        all.HasMore.Should().BeFalse();
        all.TotalCount.Should().Be(3);

        PagedResult<JobGroup> page = await Store.QueryJobGroups(new JobGroupQuery { Skip = 1, Take = 1, IncludeTotalCount = true });
        page.Items.Select(x => x.Name).Should().Equal(["pg-b"]);
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(3);

        PagedResult<JobGroup> named = await Store.QueryJobGroups(new JobGroupQuery { Name = "pg-c" });
        named.Items.Select(x => x.Name).Should().Equal(["pg-c"],
            "the name filter is an exact match, not a pattern");

        PagedResult<JobGroup> unknown = await Store.QueryJobGroups(new JobGroupQuery { Name = "nope" });
        unknown.Items.Should().BeEmpty();
    }

    [Test]
    public async Task TriggerGroupsArePagedInNameOrder()
    {
        await SeedForPaging();

        PagedResult<TriggerGroup> all = await Store.QueryTriggerGroups(new TriggerGroupQuery { IncludeTotalCount = true });
        all.Items.Select(x => x.Name).Should().Equal(["pg-a", "pg-b", "pg-c"]);
        all.Items.Should().OnlyContain(x => !x.Paused);
        all.TotalCount.Should().Be(3);

        PagedResult<TriggerGroup> page = await Store.QueryTriggerGroups(new TriggerGroupQuery { Skip = 2, Take = 5, IncludeTotalCount = true });
        page.Items.Select(x => x.Name).Should().Equal(["pg-c"]);
        page.HasMore.Should().BeFalse();
        page.TotalCount.Should().Be(3);

        await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("pg-b"));

        PagedResult<TriggerGroup> paused = await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = true, IncludeTotalCount = true });
        paused.Items.Select(x => x.Name).Should().Equal(["pg-b"]);
        paused.TotalCount.Should().Be(1);

        PagedResult<TriggerGroup> unpaused = await Store.QueryTriggerGroups(new TriggerGroupQuery { Paused = false, IncludeTotalCount = true });
        unpaused.Items.Select(x => x.Name).Should().Equal(["pg-a", "pg-c"]);
        unpaused.TotalCount.Should().Be(2);

        PagedResult<TriggerGroup> named = await Store.QueryTriggerGroups(new TriggerGroupQuery { Name = "pg-b" });
        named.Items.Should().ContainSingle().Which.Paused.Should().BeTrue(
            "a group listing carries the group's paused state");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Entities that are not there
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// One store member that answers <see cref="bool" />, and the call that asks it about an entity
    /// the store does not have.
    /// </summary>
    public sealed record MissingEntityCase(string Member, Func<IJobStore, ValueTask<bool>> Invoke)
    {
        public override string ToString() => Member;
    }

    public static IEnumerable<MissingEntityCase> MissingEntityCases()
    {
        yield return new MissingEntityCase(nameof(IJobStore.DeleteJob), store => store.DeleteJob(MissingJobKey));
        yield return new MissingEntityCase(nameof(IJobStore.DeleteJobs), store => store.DeleteJobs([MissingJobKey]));
        yield return new MissingEntityCase(nameof(IJobStore.DeleteTrigger), store => store.DeleteTrigger(MissingTriggerKey));
        yield return new MissingEntityCase(nameof(IJobStore.DeleteTriggers), store => store.DeleteTriggers([MissingTriggerKey]));
        yield return new MissingEntityCase(nameof(IJobStore.ReplaceTrigger), store => store.ReplaceTrigger(
            MissingTriggerKey,
            CreateTrigger("replacement", TriggerGroupA, AnchorJobKey)));
        yield return new MissingEntityCase(nameof(IJobStore.UpdateTriggerDetails), store => store.UpdateTriggerDetails(
            MissingTriggerKey,
            new TriggerDetailsUpdate().WithDescription("does not matter")));
        yield return new MissingEntityCase($"{nameof(IJobStore.Exists)}(JobKey)", store => store.Exists(MissingJobKey));
        yield return new MissingEntityCase($"{nameof(IJobStore.Exists)}(TriggerKey)", store => store.Exists(MissingTriggerKey));
        yield return new MissingEntityCase(nameof(IJobStore.DeleteCalendar), store => store.DeleteCalendar(MissingCalendarName));
        yield return new MissingEntityCase(nameof(IJobStore.ResetTriggerFromErrorState), store => store.ResetTriggerFromErrorState(MissingTriggerKey));
        yield return new MissingEntityCase(nameof(IJobStore.PauseTrigger), store => store.PauseTrigger(MissingTriggerKey));
        yield return new MissingEntityCase(nameof(IJobStore.PauseJob), store => store.PauseJob(MissingJobKey));
        yield return new MissingEntityCase(nameof(IJobStore.ResumeTrigger), store => store.ResumeTrigger(MissingTriggerKey));
        yield return new MissingEntityCase(nameof(IJobStore.ResumeJob), store => store.ResumeJob(MissingJobKey));
    }

    /// <summary>
    /// Guards the table itself: a member that starts answering <see cref="bool" /> without a row here
    /// would otherwise go untested on both stores.
    /// </summary>
    [Test]
    public void EveryBooleanMemberHasAMissingEntityCase()
    {
        List<string> members = typeof(IJobStore).GetMethods()
            .Where(x => x.ReturnType == typeof(ValueTask<bool>))
            .Select(x => x.Name)
            .Distinct()
            .ToList();

        List<string> covered = MissingEntityCases()
            .Select(x => x.Member.Split('(')[0])
            .Distinct()
            .ToList();

        covered.Should().BeEquivalentTo(members,
            "the matrix is only a matrix while it covers every store member that answers true or false");
    }

    [TestCaseSource(nameof(MissingEntityCases))]
    public async Task MutatingMembersAnswerFalseForAnEntityThatIsNotThere(MissingEntityCase testCase)
    {
        await SeedAnchor();

        bool result = await testCase.Invoke(Store);

        result.Should().BeFalse(
            "{0} was asked about an entity the store does not have, which is an answer rather than an error",
            testCase.Member);
    }

    [Test]
    public async Task ReadsAnswerNothingForAnEntityThatIsNotThere()
    {
        await SeedAnchor();

        (await Store.GetJob(MissingJobKey)).Should().BeNull();
        (await Store.GetTrigger(MissingTriggerKey)).Should().BeNull();
        (await Store.GetCalendar(MissingCalendarName)).Should().BeNull();
        (await Store.GetTriggerState(MissingTriggerKey)).Should().Be(TriggerState.None,
            "a trigger the store does not have is in no state at all");
        (await Store.GetTriggersForJob(MissingJobKey)).Should().BeEmpty();
        (await Store.GetJobs([MissingJobKey])).Should().BeEmpty();
        (await Store.GetTriggers([MissingTriggerKey])).Should().BeEmpty();
    }

    [Test]
    public async Task DeletingNothingIsVacuouslySuccessful()
    {
        await SeedAnchor();

        (await Store.DeleteJobs([])).Should().BeTrue("nothing was asked for, so nothing is missing");
        (await Store.DeleteTriggers([])).Should().BeTrue();

        (await Store.Exists(AnchorJobKey)).Should().BeTrue("an empty batch deletes nothing");
    }

    [Test]
    public async Task GroupMembersAnswerNothingForAMatcherThatMatchesNothing()
    {
        await SeedAnchor();

        (await Store.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("no-such-group"))).Should().BeEmpty(
            "nothing matched, so nothing was resumed");
        (await Store.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("no-such-group"))).Should().BeEmpty();

        // A prefix matcher rather than an equality one: pausing a group by exact name is how a caller
        // pauses a group before it exists, so that call deliberately does report a group.
        (await Store.PauseJobs(GroupMatcher<JobKey>.GroupStartsWith("no-such-prefix"))).Should().BeEmpty();
        (await Store.PauseTriggers(GroupMatcher<TriggerKey>.GroupStartsWith("no-such-prefix"))).Should().BeEmpty();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Fire instances
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AFiringIsListedWithEverythingTheStoreKnowsAboutIt()
    {
        IOperableTrigger trigger = await GivenAFiringInFlight("listed", executionGroup: "reports");

        PagedResult<FireInstance> page = await Store.QueryFireInstances(new FireInstanceQuery());

        FireInstance firing = page.Items.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            firing.FireInstanceId.Should().Be(trigger.FireInstanceId);
            firing.TriggerKey.Should().Be(trigger.Key);
            firing.JobKey.Should().Be(trigger.JobKey, "an execution that has started knows its job");
            firing.State.Should().Be(FireInstanceState.Executing);
            firing.SchedulerInstanceId.Should().Be(StoreInstanceId,
                "the firing is owned by the node whose identity the store was initialized with");
            firing.ExecutionGroup.Should().Be("reports",
                "the execution group is recorded with the firing, not looked up from the trigger afterwards");
            firing.FireTimeUtc.Should().NotBe(default(DateTimeOffset));
            page.HasMore.Should().BeFalse();
        }
    }

    [Test]
    public async Task ConcurrentFiringsOfOneTriggerAreListedSeparately()
    {
        // The point of a fire instance: a trigger whose job allows concurrent execution can have several
        // executions in flight, and a listing that keyed on the trigger would collapse them into one.
        IOperableTrigger first = await GivenAFiringInFlight("multiple");
        IOperableTrigger second = await FireAgain(first);

        PagedResult<FireInstance> page = await Store.QueryFireInstances(new FireInstanceQuery());

        using (new AssertionScope())
        {
            first.FireInstanceId.Should().NotBe(second.FireInstanceId, "each firing is its own");
            page.Items.Should().HaveCount(2, "two executions of the same trigger are two firings");
            page.Items.Select(x => x.FireInstanceId).Should().BeEquivalentTo([first.FireInstanceId, second.FireInstanceId]);
            page.Items.Select(x => x.TriggerKey).Should().AllBeEquivalentTo(first.Key);
        }

        // ...and completing one of them leaves the other running.
        await Store.TriggeredJobComplete(first, await Store.GetJob(first.JobKey), SchedulerInstruction.NoInstruction);

        (await Store.QueryFireInstances(new FireInstanceQuery())).Items
            .Should().ContainSingle().Which.FireInstanceId.Should().Be(second.FireInstanceId);
    }

    [Test]
    public async Task AReservedFiringIsListedOnlyWhenItIsAskedFor()
    {
        IOperableTrigger acquired = await GivenAnAcquiredTrigger("reserved");

        using (new AssertionScope())
        {
            (await Store.QueryFireInstances(new FireInstanceQuery())).Items.Should().BeEmpty(
                "the default listing is what is running, and nothing has started");

            FireInstance reserved = (await Store.QueryFireInstances(new FireInstanceQuery { State = FireInstanceState.Acquired }))
                .Items.Should().ContainSingle().Subject;
            reserved.FireInstanceId.Should().Be(acquired.FireInstanceId);
            reserved.TriggerKey.Should().Be(acquired.Key);
            reserved.State.Should().Be(FireInstanceState.Acquired);
            reserved.JobKey.Should().BeNull("a reservation is written before the job is loaded");
            reserved.SchedulerInstanceId.Should().Be(StoreInstanceId);

            (await Store.QueryFireInstances(new FireInstanceQuery { State = null })).Items
                .Should().ContainSingle("a null state filter is every state");
        }

        // Once it fires the same firing moves state rather than becoming a second one.
        await Store.TriggersFired([acquired]);

        using (new AssertionScope())
        {
            (await Store.QueryFireInstances(new FireInstanceQuery { State = FireInstanceState.Acquired })).Items
                .Should().BeEmpty("a firing that started is no longer merely reserved");
            (await Store.QueryFireInstances(new FireInstanceQuery())).Items
                .Should().ContainSingle().Which.FireInstanceId.Should().Be(acquired.FireInstanceId);
        }
    }

    [Test]
    public async Task FiringsPageInAStableOrderWithTheFireInstanceIdAsTheTiebreaker()
    {
        // Two firings of one trigger and one of another: group and name alone cannot order the first
        // two, so a page boundary between them is exactly where an unstable order shows up.
        IOperableTrigger first = await GivenAFiringInFlight("paged-a");
        await FireAgain(first);
        await GivenAFiringInFlight("paged-b");

        PagedResult<FireInstance> all = await Store.QueryFireInstances(new FireInstanceQuery { IncludeTotalCount = true });
        all.Items.Should().HaveCount(3);
        all.TotalCount.Should().Be(3);

        List<string> paged = [];
        for (int skip = 0; skip < 3; skip++)
        {
            PagedResult<FireInstance> page = await Store.QueryFireInstances(new FireInstanceQuery { Skip = skip, Take = 1 });
            page.Items.Should().ContainSingle();
            page.HasMore.Should().Be(skip < 2, "the last page is the one with nothing after it");
            paged.Add(page.Items[0].FireInstanceId);
        }

        paged.Should().Equal(all.Items.Select(x => x.FireInstanceId),
            "paging one at a time has to walk the same order, with no firing seen twice and none missed");

        PagedResult<FireInstance> counted = await Store.QueryFireInstances(new FireInstanceQuery { Take = 0, IncludeTotalCount = true });
        counted.Items.Should().BeEmpty("the count idiom reads no page");
        counted.TotalCount.Should().Be(3);
    }

    [Test]
    public async Task FiringsFilterByTriggerJobAndOwningNode()
    {
        IOperableTrigger wanted = await GivenAFiringInFlight("wanted");
        await GivenAFiringInFlight("other");

        using (new AssertionScope())
        {
            (await Store.QueryFireInstances(new FireInstanceQuery { TriggerName = NameMatcher<TriggerKey>.NameEquals(wanted.Key.Name) }))
                .Items.Should().ContainSingle().Which.TriggerKey.Should().Be(wanted.Key);

            (await Store.QueryFireInstances(new FireInstanceQuery { TriggerGroup = GroupMatcher<TriggerKey>.GroupEquals(TriggerGroupA) }))
                .Items.Should().HaveCount(2, "both firings are in that trigger group");

            (await Store.QueryFireInstances(new FireInstanceQuery { TriggerGroup = GroupMatcher<TriggerKey>.GroupEquals(OtherGroup) }))
                .Items.Should().BeEmpty();

            (await Store.QueryFireInstances(new FireInstanceQuery { Job = wanted.JobKey }))
                .Items.Should().ContainSingle().Which.JobKey.Should().Be(wanted.JobKey);

            (await Store.QueryFireInstances(new FireInstanceQuery { SchedulerInstanceId = StoreInstanceId }))
                .Items.Should().HaveCount(2, "both firings are owned by this node");

            (await Store.QueryFireInstances(new FireInstanceQuery { SchedulerInstanceId = "some-other-node" }))
                .Items.Should().BeEmpty("a firing owned by this node must not answer for another one");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Execution limits
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ATriggerGroupIsNotAnExecutionGroupUnlessTheLimitsSayItIs()
    {
        await GivenTwoDueTriggersInOneGroup();

        ExecutionLimits plain = ExecutionLimitsBuilder.Create().ForGroup(TriggerGroupA, 1).Build();

        (await AcquireWith(plain)).Should().HaveCount(2,
            "neither trigger carries an execution group, so a limit on a group of that name catches nothing");
    }

    [Test]
    public async Task WithTheOptionOnAGroupPartitionedScheduleIsCappedByItsTriggerGroup()
    {
        await GivenTwoDueTriggersInOneGroup();

        ExecutionLimits derived = ExecutionLimitsBuilder.Create()
            .ForGroup(TriggerGroupA, 1)
            .UseTriggerGroupWhenUnset()
            .Build();

        List<IOperableTrigger> acquired = await AcquireWith(derived);

        acquired.Should().ContainSingle("the trigger group stands in for the execution group the triggers do not carry");
        acquired[0].ExecutionGroup.Should().BeNull(
            "the derivation is evaluated, never stored: what the trigger carries is unchanged");
    }

    [Test]
    public async Task AnExplicitExecutionGroupStillWinsOverTheDerivedOne()
    {
        IJobDetail job = CreateJob("explicit", JobGroupA);
        IOperableTrigger trigger = CreateTrigger("explicit", TriggerGroupA, job.Key,
            startAt: DateTimeOffset.UtcNow.AddSeconds(5), executionGroup: "cpu");
        await Store.ScheduleJob(job, trigger);

        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup(TriggerGroupA, 0)
            .ForGroup("cpu", 1)
            .UseTriggerGroupWhenUnset()
            .Build();

        (await AcquireWith(limits)).Should().ContainSingle(
            "the trigger names its execution group, so the group it is in does not decide for it")
            .Which.Key.Should().Be(trigger.Key);
    }

    private async Task GivenTwoDueTriggersInOneGroup()
    {
        // Two jobs rather than one, so that DisallowConcurrentExecution is not what limits the batch.
        foreach (string name in (string[]) ["partitioned-one", "partitioned-two"])
        {
            IJobDetail job = CreateJob(name, JobGroupA);
            await Store.ScheduleJob(job, CreateTrigger(name, TriggerGroupA, job.Key,
                startAt: DateTimeOffset.UtcNow.AddSeconds(5)));
        }
    }

    private ValueTask<List<IOperableTrigger>> AcquireWith(ExecutionLimits limits)
    {
        return Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 5,
            // Wide enough that the batch does not close on the first trigger's fire time: two triggers
            // scheduled milliseconds apart have to be in the same batch for a limit to be what excludes
            // one of them.
            TimeWindow = TimeSpan.FromMinutes(1),
            ExecutionLimits = limits
        });
    }

    /// <summary>
    /// Acquires a trigger due right away and hands the acquired copy back, without firing it.
    /// </summary>
    private async Task<IOperableTrigger> GivenAnAcquiredTrigger(string name, string executionGroup = null)
    {
        IJobDetail job = CreateJob(name, JobGroupA);
        IOperableTrigger trigger = CreateTrigger(name, TriggerGroupA, job.Key,
            startAt: DateTimeOffset.UtcNow.AddSeconds(5), executionGroup: executionGroup);
        await Store.ScheduleJob(job, trigger);

        List<IOperableTrigger> acquired = await Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1
        });

        return acquired.Should().ContainSingle(x => x.Key.Equals(trigger.Key),
            "the trigger due next is the one under test").Subject;
    }

    /// <summary>
    /// Acquires and fires a trigger, leaving the execution in flight for the test to observe.
    /// </summary>
    private async Task<IOperableTrigger> GivenAFiringInFlight(string name, string executionGroup = null)
    {
        IOperableTrigger acquired = await GivenAnAcquiredTrigger(name, executionGroup);

        List<TriggerFiredResult> fired = await Store.TriggersFired([acquired]);
        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the firing has to be committed before it can be listed");

        return acquired;
    }

    /// <summary>
    /// Fires the same trigger a second time while the first execution is still in flight, by reaching
    /// far enough ahead to acquire its next scheduled fire.
    /// </summary>
    private async Task<IOperableTrigger> FireAgain(IOperableTrigger trigger)
    {
        List<IOperableTrigger> acquired = await Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddHours(2),
            MaxCount = 1
        });

        IOperableTrigger again = acquired.Should().ContainSingle(x => x.Key.Equals(trigger.Key)).Subject;
        await Store.TriggersFired([again]);
        return again;
    }

    /// <summary>
    /// The instance id this fixture's store was initialized with, which is what every
    /// <see cref="FireInstance.SchedulerInstanceId" /> it reports has to say.
    /// </summary>
    protected abstract string StoreInstanceId { get; }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Stores a job, a trigger and a calendar, so that a test asking about something missing asks a
    /// store with content in it rather than an empty one.
    /// </summary>
    private async Task SeedAnchor()
    {
        IJobDetail job = CreateJob(AnchorJobKey.Name, AnchorJobKey.Group);
        IOperableTrigger trigger = CreateTrigger(AnchorTriggerKey.Name, AnchorTriggerKey.Group, job.Key);

        await Store.ScheduleJob(job, trigger);
        await Store.AddCalendar("anchor", new AnnualCalendar());
    }

    /// <summary>
    /// Seeds twelve jobs, each with one trigger of the same name and group, spread over three groups.
    /// Names are zero padded and lower case ASCII, so ordinal ordering and database collation agree.
    /// </summary>
    private async Task<List<JobKey>> SeedForPaging()
    {
        string[] groups = ["pg-a", "pg-b", "pg-c"];
        List<JobKey> keys = [];

        for (int i = 0; i < 12; i++)
        {
            string group = groups[i % groups.Length];
            string name = $"item-{i:00}";

            IJobDetail job = CreateJob(name, group);
            IOperableTrigger trigger = CreateTrigger(name, group, job.Key);
            await Store.ScheduleJob(job, trigger);

            keys.Add(job.Key);
        }

        keys.Sort((left, right) =>
        {
            int byGroup = string.CompareOrdinal(left.Group, right.Group);
            return byGroup != 0 ? byGroup : string.CompareOrdinal(left.Name, right.Name);
        });

        return keys;
    }

    private async Task<IOperableTrigger> ScheduleJobWithTrigger(string name, string jobGroup, string triggerGroup)
    {
        IJobDetail job = CreateJob(name, jobGroup);
        IOperableTrigger trigger = CreateTrigger(name, triggerGroup, job.Key);
        await Store.ScheduleJob(job, trigger);
        return trigger;
    }

    /// <summary>
    /// Drives a trigger through a firing that ends in failure, which is how a trigger legitimately
    /// reaches <see cref="TriggerState.Error" />.
    /// </summary>
    private async Task<IOperableTrigger> GivenATriggerInErrorState(string name = "failing")
    {
        IJobDetail job = CreateJob(name, JobGroupA);
        IOperableTrigger trigger = CreateTrigger(name, TriggerGroupA, job.Key, startAt: DateTimeOffset.UtcNow.AddSeconds(5));
        await Store.ScheduleJob(job, trigger);

        List<IOperableTrigger> acquired = await Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1
        });

        acquired.Select(x => x.Key).Should().Equal([trigger.Key], "the trigger due next is the one under test");

        List<TriggerFiredResult> fired = await Store.TriggersFired(acquired);
        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the firing has to be committed before completing it says anything");

        await Store.TriggeredJobComplete(acquired[0], job, SchedulerInstruction.SetTriggerError);

        return trigger;
    }

    private static IJobDetail CreateJob(string name, string group)
    {
        return JobBuilder.Create<ContractTestJob>()
            .WithIdentity(name, group)
            .WithDescription("job " + name)
            .Build();
    }

    private static IOperableTrigger CreateTrigger(
        string name,
        string group,
        JobKey jobKey,
        DateTimeOffset? startAt = null,
        string calendarName = null,
        string executionGroup = null)
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob(jobKey)
            // Far enough out that nothing fires and nothing misfires while a test runs.
            .StartAt(startAt ?? DateTimeOffset.UtcNow.AddYears(1))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());

        if (calendarName is not null)
        {
            builder = builder.WithCalendarName(calendarName);
        }

        if (executionGroup is not null)
        {
            builder = builder.WithExecutionGroup(executionGroup);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    public sealed class ContractTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
