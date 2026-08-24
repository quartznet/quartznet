using System.Net;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class TriggerEndpointsTest : WebApiTest
{
    private static readonly TriggerKey triggerKeyOne = new("trigger1", "group1");
    private static readonly TriggerKey triggerKeyTwo = new("trigger2", "group2");

    [Test]
    public async Task GetTriggerKeysShouldWork()
    {
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([HeaderFor(triggerKeyOne), HeaderFor(triggerKeyTwo)], HasMore: false));

        var triggerKeys = await HttpScheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
        using (new AssertionScope())
        {
            triggerKeys.Count.Should().Be(2);
            triggerKeys.Should().ContainSingle(x => x.Equals(triggerKeyOne));
            triggerKeys.Should().ContainSingle(x => x.Equals(triggerKeyTwo));
        }

        var matchers = new[]
        {
            GroupMatcher<TriggerKey>.AnyGroup(),
            GroupMatcher<TriggerKey>.GroupContains("contains"),
            GroupMatcher<TriggerKey>.GroupEquals("equals"),
            GroupMatcher<TriggerKey>.GroupEndsWith("ends"),
            GroupMatcher<TriggerKey>.GroupStartsWith("starts")
        };

        foreach (var matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            await HttpScheduler.GetTriggerKeys(matcher);
            A.CallTo(() => FakeScheduler.QueryTriggers(new TriggerQuery { Group = matcher, Take = int.MaxValue }, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task QueryTriggersShouldPassEveryFilterAndReturnHeaders()
    {
        var header = HeaderFor(triggerKeyOne);
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([header], HasMore: true, TotalCount: 12));

        var query = new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals("group1"),
            Job = new JobKey("job1", "jobgroup1"),
            CalendarName = "SomeCalendar",
            State = TriggerState.Error,
            Skip = 5,
            Take = 1,
            IncludeTotalCount = true
        };

        var result = await HttpScheduler.QueryTriggers(query);

        using (new AssertionScope())
        {
            result.Items.Should().ContainSingle().Which.Should().Be(header);
            result.HasMore.Should().BeTrue();
            result.TotalCount.Should().Be(12);
        }

        A.CallTo(() => FakeScheduler.QueryTriggers(query, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryTriggersShouldRejectJobNameWithoutJobGroup()
    {
        using var httpClient = WebApplicationFactory.CreateClient();

        var response = await httpClient.GetAsync($"schedulers/{HttpScheduler.SchedulerName}/triggers?jobName=job1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().ContainEquivalentOf("jobName and jobGroup");
    }

    [Test]
    public async Task FetchTriggersShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns([TestData.CronTrigger, TestData.SimpleTrigger]);

        var triggers = await HttpScheduler.GetTriggers([TestData.CronTrigger.Key, TestData.SimpleTrigger.Key, triggerKeyOne]);

        triggers.Count.Should().Be(2);
        triggers.Single(x => x.Key.Equals(TestData.CronTrigger.Key)).Should().BeEquivalentTo(TestData.CronTrigger);
        triggers.Single(x => x.Key.Equals(TestData.SimpleTrigger.Key)).Should().BeEquivalentTo(TestData.SimpleTrigger);

        A.CallTo(() => FakeScheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                keys.Count == 3 && keys.Contains(TestData.CronTrigger.Key) && keys.Contains(TestData.SimpleTrigger.Key) && keys.Contains(triggerKeyOne))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task FetchTriggersShouldNotCallSchedulerWithoutKeys()
    {
        var triggers = await HttpScheduler.GetTriggers([]);

        triggers.Should().BeEmpty();
        A.CallTo(() => FakeScheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task GetTriggerDetailsShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetTrigger(TestData.CalendarIntervalTrigger.Key, A<CancellationToken>._)).Returns(TestData.CalendarIntervalTrigger);
        A.CallTo(() => FakeScheduler.GetTrigger(TestData.CronTrigger.Key, A<CancellationToken>._)).Returns(TestData.CronTrigger);
        A.CallTo(() => FakeScheduler.GetTrigger(TestData.DailyTimeIntervalTrigger.Key, A<CancellationToken>._)).Returns(TestData.DailyTimeIntervalTrigger);
        A.CallTo(() => FakeScheduler.GetTrigger(TestData.SimpleTrigger.Key, A<CancellationToken>._)).Returns(TestData.SimpleTrigger);
        A.CallTo(() => FakeScheduler.GetTrigger(triggerKeyOne, A<CancellationToken>._)).Returns(null);

        var trigger = await HttpScheduler.GetTrigger(TestData.CalendarIntervalTrigger.Key);
        trigger.Should().BeEquivalentTo(TestData.CalendarIntervalTrigger);

        trigger = await HttpScheduler.GetTrigger(TestData.CronTrigger.Key);
        trigger.Should().BeEquivalentTo(TestData.CronTrigger);

        trigger = await HttpScheduler.GetTrigger(TestData.DailyTimeIntervalTrigger.Key);
        trigger.Should().BeEquivalentTo(TestData.DailyTimeIntervalTrigger);

        trigger = await HttpScheduler.GetTrigger(TestData.SimpleTrigger.Key);
        trigger.Should().BeEquivalentTo(TestData.SimpleTrigger);

        trigger = await HttpScheduler.GetTrigger(triggerKeyOne);
        trigger.Should().BeNull();
    }

    [Test]
    public async Task CheckTriggerExistsShouldWork()
    {
        A.CallTo(() => FakeScheduler.Exists(triggerKeyOne, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.Exists(triggerKeyTwo, A<CancellationToken>._)).Returns(false);

        var exists = await HttpScheduler.Exists(triggerKeyOne);
        exists.Should().BeTrue();

        exists = await HttpScheduler.Exists(triggerKeyTwo);
        exists.Should().BeFalse();
    }

    [Test]
    public async Task GetTriggerStateShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetTriggerState(triggerKeyOne, A<CancellationToken>._)).Returns(TriggerState.Normal);
        A.CallTo(() => FakeScheduler.GetTriggerState(triggerKeyTwo, A<CancellationToken>._)).Returns(TriggerState.Error);

        var exists = await HttpScheduler.GetTriggerState(triggerKeyOne);
        exists.Should().Be(TriggerState.Normal);

        exists = await HttpScheduler.GetTriggerState(triggerKeyTwo);
        exists.Should().Be(TriggerState.Error);
    }

    [Test]
    public async Task GetTriggerStateShouldWorkForExecuting()
    {
        A.CallTo(() => FakeScheduler.GetTriggerState(triggerKeyOne, A<CancellationToken>._)).Returns(TriggerState.Executing);

        var state = await HttpScheduler.GetTriggerState(triggerKeyOne);
        state.Should().Be(TriggerState.Executing);
    }

    /// <summary>
    /// The enum crosses the wire as its numeric value, so the ordinals are a contract with clients built
    /// against a different version. Both ends of the round trip above share the same enum, so only the
    /// literal values can catch a renumbering.
    /// </summary>
    [Test]
    public void TriggerStateOrdinalsAreTheWireContract()
    {
        ((int) TriggerState.Normal).Should().Be(0);
        ((int) TriggerState.Paused).Should().Be(1);
        ((int) TriggerState.Complete).Should().Be(2);
        ((int) TriggerState.Error).Should().Be(3);
        ((int) TriggerState.Blocked).Should().Be(4);
        ((int) TriggerState.None).Should().Be(5);
        ((int) TriggerState.Executing).Should().Be(6);
    }

    /// <summary>
    /// The state filter is sent by member name, so the names are a contract too.
    /// </summary>
    [Test]
    public void TriggerStateNamesAreTheWireContract()
    {
        Enum.GetNames<TriggerState>().Should().Equal(
            "Normal", "Paused", "Complete", "Error", "Blocked", "None", "Executing");
    }

    [Test]
    public async Task PauseTriggerShouldWork()
    {
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKeyOne, A<CancellationToken>._)).Returns(true);

        bool applied = await HttpScheduler.PauseTrigger(triggerKeyOne);

        applied.Should().BeTrue("the applied flag must round-trip over the wire");
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKeyOne, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task PauseTriggerShouldReportMissingTrigger()
    {
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKeyOne, A<CancellationToken>._)).Returns(false);

        bool applied = await HttpScheduler.PauseTrigger(triggerKeyOne);

        applied.Should().BeFalse("a no-op must not report as applied");
    }

    [Test]
    public async Task PauseTriggersShouldWork()
    {
        var matchers = new[]
        {
            GroupMatcher<TriggerKey>.AnyGroup(),
            GroupMatcher<TriggerKey>.GroupContains("contains"),
            GroupMatcher<TriggerKey>.GroupEquals("equals"),
            GroupMatcher<TriggerKey>.GroupEndsWith("ends"),
            GroupMatcher<TriggerKey>.GroupStartsWith("starts")
        };

        foreach (var matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            A.CallTo(() => FakeScheduler.PauseTriggers(matcher, A<CancellationToken>._)).Returns(new List<string> { "paused-group" });

            List<string> pausedGroups = await HttpScheduler.PauseTriggers(matcher);

            pausedGroups.Should().Equal("paused-group");
            A.CallTo(() => FakeScheduler.PauseTriggers(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task PauseTriggersByKeyShouldRoundTripTheAppliedKeys()
    {
        A.CallTo(() => FakeScheduler.PauseTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { triggerKeyOne });

        List<TriggerKey> paused = await HttpScheduler.PauseTriggers([triggerKeyOne, triggerKeyTwo]);

        paused.Should().Equal([triggerKeyOne],
            "the answer names the keys the pause applied to, and the key it did not move is absent");

        A.CallTo(() => FakeScheduler.PauseTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<TriggerKey> keys, CancellationToken _) => keys.SequenceEqual([triggerKeyOne, triggerKeyTwo]))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResumeTriggersByKeyShouldRoundTripTheAppliedKeys()
    {
        A.CallTo(() => FakeScheduler.ResumeTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { triggerKeyTwo });

        List<TriggerKey> resumed = await HttpScheduler.ResumeTriggers([triggerKeyOne, triggerKeyTwo]);

        resumed.Should().Equal([triggerKeyTwo]);

        A.CallTo(() => FakeScheduler.ResumeTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResetTriggersFromErrorStateByKeyShouldRoundTripTheAppliedKeys()
    {
        A.CallTo(() => FakeScheduler.ResetTriggersFromErrorState(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { triggerKeyOne, triggerKeyTwo });

        List<TriggerKey> reset = await HttpScheduler.ResetTriggersFromErrorState([triggerKeyOne, triggerKeyTwo]);

        reset.Should().Equal([triggerKeyOne, triggerKeyTwo]);

        A.CallTo(() => FakeScheduler.ResetTriggersFromErrorState(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task AKeySetThatAppliedToNothingComesBackEmpty()
    {
        A.CallTo(() => FakeScheduler.PauseTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey>());

        List<TriggerKey> paused = await HttpScheduler.PauseTriggers([triggerKeyOne]);

        paused.Should().BeEmpty("a key that moved nothing is absent from the answer, not an error");
    }

    [Test]
    public async Task ResumeTriggerShouldWork()
    {
        A.CallTo(() => FakeScheduler.ResumeTrigger(triggerKeyOne, A<CancellationToken>._)).Returns(true);

        bool applied = await HttpScheduler.ResumeTrigger(triggerKeyOne);

        applied.Should().BeTrue("the applied flag must round-trip over the wire");
        A.CallTo(() => FakeScheduler.ResumeTrigger(triggerKeyOne, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResumeTriggersShouldWork()
    {
        var matchers = new[]
        {
            GroupMatcher<TriggerKey>.AnyGroup(),
            GroupMatcher<TriggerKey>.GroupContains("contains"),
            GroupMatcher<TriggerKey>.GroupEquals("equals"),
            GroupMatcher<TriggerKey>.GroupEndsWith("ends"),
            GroupMatcher<TriggerKey>.GroupStartsWith("starts")
        };

        foreach (var matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            A.CallTo(() => FakeScheduler.ResumeTriggers(matcher, A<CancellationToken>._)).Returns(new List<string> { "resumed-group" });

            List<string> resumedGroups = await HttpScheduler.ResumeTriggers(matcher);

            resumedGroups.Should().Equal("resumed-group");
            A.CallTo(() => FakeScheduler.ResumeTriggers(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task GetTriggerGroupNamesShouldWork()
    {
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(A<TriggerGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroup>([new TriggerGroup("group1", Paused: false), new TriggerGroup("group2", Paused: true)], HasMore: false));

        var triggerGroupNames = await HttpScheduler.GetTriggerGroupNames();

        triggerGroupNames.Count.Should().Be(2);
        triggerGroupNames.Should().ContainSingle(x => x == "group1");
        triggerGroupNames.Should().ContainSingle(x => x == "group2");

        A.CallTo(() => FakeScheduler.QueryTriggerGroups(new TriggerGroupQuery { Take = int.MaxValue }, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task GetPausedTriggerGroupsShouldWork()
    {
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(A<TriggerGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroup>([new TriggerGroup("group1", Paused: true)], HasMore: false));

        var triggerGroupNames = await HttpScheduler.GetPausedTriggerGroups();

        triggerGroupNames.Count.Should().Be(1);
        triggerGroupNames.Should().ContainSingle(x => x == "group1");

        A.CallTo(() => FakeScheduler.QueryTriggerGroups(new TriggerGroupQuery { Paused = true, Take = int.MaxValue }, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryTriggerGroupsShouldPassPagingAndPausedFilter()
    {
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(A<TriggerGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroup>([new TriggerGroup("group2", Paused: false)], HasMore: true, TotalCount: 7));

        var query = new TriggerGroupQuery { Paused = false, Skip = 1, Take = 1, IncludeTotalCount = true };
        var result = await HttpScheduler.QueryTriggerGroups(query);

        using (new AssertionScope())
        {
            result.Items.Should().ContainSingle().Which.Should().Be(new TriggerGroup("group2", Paused: false));
            result.HasMore.Should().BeTrue();
            result.TotalCount.Should().Be(7);
        }

        A.CallTo(() => FakeScheduler.QueryTriggerGroups(query, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task IsTriggerGroupPausedShouldWork()
    {
        // the check asks the store for the one named group rather than listing every paused one
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(A<TriggerGroupQuery>.That.Matches(query => query.Name == "group1"), A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroup>([new TriggerGroup("group1", Paused: true)], HasMore: false));
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(A<TriggerGroupQuery>.That.Matches(query => query.Name != "group1"), A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroup>([], HasMore: false));

        bool paused = await HttpScheduler.IsTriggerGroupPaused("group1");
        paused.Should().BeTrue();

        paused = await HttpScheduler.IsTriggerGroupPaused("group2");
        paused.Should().BeFalse();

        A.CallTo(() => FakeScheduler.QueryTriggerGroups(new TriggerGroupQuery { Name = "group1", Paused = true, Take = 1 }, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(new TriggerGroupQuery { Name = "group2", Paused = true, Take = 1 }, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ScheduleJobShouldWork()
    {
        var firstFireTime = DateTimeOffset.Now;
        A.CallTo(() => FakeScheduler.ScheduleJob(A<ITrigger>._, A<CancellationToken>._)).Returns(firstFireTime);

        var response = await HttpScheduler.ScheduleJob(TestData.CronTrigger);
        response.Should().Be(firstFireTime);

        A.CallTo(() => FakeScheduler.ScheduleJob(A<ITrigger>._, A<CancellationToken>._))
            .WhenArgumentsMatch((ITrigger trigger, CancellationToken _) =>
            {
                trigger.Should().BeEquivalentTo(TestData.CronTrigger);
                return true;
            })
            .MustHaveHappened(1, Times.Exactly);

        firstFireTime = DateTimeOffset.Now.AddDays(1);
        A.CallTo(() => FakeScheduler.ScheduleJob(A<IJobDetail>._, A<ITrigger>._, A<CancellationToken>._)).Returns(firstFireTime);

        response = await HttpScheduler.ScheduleJob(TestData.JobDetail, TestData.DailyTimeIntervalTrigger);
        response.Should().Be(firstFireTime);

        A.CallTo(() => FakeScheduler.ScheduleJob(A<IJobDetail>._, A<ITrigger>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IJobDetail jobDetail, ITrigger trigger, CancellationToken _) =>
            {
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                trigger.Should().BeEquivalentTo(TestData.DailyTimeIntervalTrigger);
                return true;
            })
            .MustHaveHappened(1, Times.Exactly);

        // A job type the server cannot resolve is not rejected at request time - the server never resolves
        // a name that arrived with the request.
        Fake.ClearRecordedCalls(FakeScheduler);
        IJobDetail jobDetailWithUnresolvableType = TestData.JobDetail.GetJobBuilder()
            .OfType(TestData.UnresolvableJobTypeName)
            .Build();

        await HttpScheduler.ScheduleJob(jobDetailWithUnresolvableType, TestData.SimpleTrigger);

        A.CallTo(() => FakeScheduler.ScheduleJob(A<IJobDetail>._, A<ITrigger>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IJobDetail jobDetail, ITrigger trigger, CancellationToken _) =>
            {
                jobDetail.JobType.FullName.Should().Be(TestData.UnresolvableJobTypeName);
                return true;
            })
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public void ScheduleJobShouldRejectMalformedJobType()
    {
        // Shape is still checked, it is only resolution that is not done. An empty name has no shape.
        IJobDetail jobDetailWithEmptyType = TestData.JobDetail.GetJobBuilder()
            .OfType(" ")
            .Build();

        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.ScheduleJob(jobDetailWithEmptyType, TestData.SimpleTrigger).AsTask())!
            .Message.Should().ContainEquivalentOf("malformed job type");

        A.CallTo(() => FakeScheduler.ScheduleJob(A<IJobDetail>._, A<ITrigger>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ScheduleJobsShouldWork()
    {
        await HttpScheduler.ScheduleJob(TestData.JobDetail, [TestData.CronTrigger, TestData.SimpleTrigger], new ScheduleJobOptions { Replace = true });
        A.CallTo(() => FakeScheduler.ScheduleJobs(A<IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>>>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options, CancellationToken _) =>
            {
                // WhenArgumentsMatch is probably not intended for asserts, but this works so...
                triggersAndJobs.Count.Should().Be(1);

                var (jobDetail, triggersForJob) = triggersAndJobs.Single(x => x.Key.Key.Equals(TestData.JobDetail.Key));
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                triggersForJob.Count.Should().Be(2);
                triggersForJob.Single(x => x.Key.Equals(TestData.CronTrigger.Key)).Should().BeEquivalentTo(TestData.CronTrigger);
                triggersForJob.Single(x => x.Key.Equals(TestData.SimpleTrigger.Key)).Should().BeEquivalentTo(TestData.SimpleTrigger);

                return options.Replace;
            })
            .MustHaveHappened(1, Times.Exactly);

        Fake.ClearRecordedCalls(FakeScheduler);
        var requestJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            { TestData.JobDetail, [TestData.CronTrigger, TestData.SimpleTrigger] },
            { TestData.JobDetail2, [TestData.CalendarIntervalTrigger] }
        };

        await HttpScheduler.ScheduleJobs(requestJobs, new ScheduleJobOptions { Replace = false });
        A.CallTo(() => FakeScheduler.ScheduleJobs(A<IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>>>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options, CancellationToken _) =>
            {
                triggersAndJobs.Count.Should().Be(2);
                var (jobDetail, triggersForJob) = triggersAndJobs.Single(x => x.Key.Key.Equals(TestData.JobDetail.Key));
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                triggersForJob.Count.Should().Be(2);
                triggersForJob.Single(x => x.Key.Equals(TestData.CronTrigger.Key)).Should().BeEquivalentTo(TestData.CronTrigger);
                triggersForJob.Single(x => x.Key.Equals(TestData.SimpleTrigger.Key)).Should().BeEquivalentTo(TestData.SimpleTrigger);

                (jobDetail, triggersForJob) = triggersAndJobs.Single(x => x.Key.Key.Equals(TestData.JobDetail2.Key));
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail2);
                triggersForJob.Count.Should().Be(1);
                triggersForJob.ToArray()[0].Should().BeEquivalentTo(TestData.CalendarIntervalTrigger);

                return !options.Replace;
            })
            .MustHaveHappened(1, Times.Exactly);

        // A job type the server cannot resolve is not rejected at request time - the server never resolves
        // a name that arrived with the request.
        Fake.ClearRecordedCalls(FakeScheduler);
        IJobDetail jobDetailWithUnresolvableType = TestData.JobDetail.GetJobBuilder()
            .OfType(TestData.UnresolvableJobTypeName)
            .Build();

        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> requestWithUnresolvableType = new() { { jobDetailWithUnresolvableType, [TestData.CronTrigger] } };
        await HttpScheduler.ScheduleJobs(requestWithUnresolvableType, new ScheduleJobOptions { Replace = true });

        A.CallTo(() => FakeScheduler.ScheduleJobs(A<IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>>>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options, CancellationToken _) =>
            {
                triggersAndJobs.Single().Key.JobType.FullName.Should().Be(TestData.UnresolvableJobTypeName);
                return true;
            })
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public void ScheduleJobsShouldRejectMalformedJobType()
    {
        // Shape is still checked, it is only resolution that is not done. An empty name has no shape.
        IJobDetail jobDetailWithEmptyType = TestData.JobDetail.GetJobBuilder()
            .OfType(" ")
            .Build();

        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> request = new() { { jobDetailWithEmptyType, [TestData.CronTrigger] } };
        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.ScheduleJobs(request, new ScheduleJobOptions { Replace = true }).AsTask())!
            .Message.Should().ContainEquivalentOf("malformed job type");

        A.CallTo(() => FakeScheduler.ScheduleJobs(A<IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>>>._, A<ScheduleJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task UnscheduleJobShouldWork()
    {
        A.CallTo(() => FakeScheduler.UnscheduleJob(triggerKeyOne, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.UnscheduleJob(triggerKeyTwo, A<CancellationToken>._)).Returns(false);

        var response = await HttpScheduler.UnscheduleJob(triggerKeyOne);
        response.Should().BeTrue();

        response = await HttpScheduler.UnscheduleJob(triggerKeyTwo);
        response.Should().BeFalse();
    }

    [Test]
    public async Task UnscheduleJobsShouldWork()
    {
        A.CallTo(() => FakeScheduler.UnscheduleJobs(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { triggerKeyOne, triggerKeyTwo });

        var response = await HttpScheduler.UnscheduleJobs([triggerKeyOne, triggerKeyTwo]);
        response.Should().Equal([triggerKeyOne, triggerKeyTwo]);

        A.CallTo(() => FakeScheduler.UnscheduleJobs(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<TriggerKey> keys, CancellationToken _) => keys.Count == 2 && keys.Contains(triggerKeyOne) && keys.Contains(triggerKeyTwo))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task UnscheduleJobsShouldCarryThePartialHitBackToTheCaller()
    {
        A.CallTo(() => FakeScheduler.UnscheduleJobs(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { triggerKeyOne });

        var response = await HttpScheduler.UnscheduleJobs([triggerKeyOne, triggerKeyTwo]);

        response.Should().Equal([triggerKeyOne],
            "one of the two keys was found, and the answer says which — the old flag could only say "
            + "that not all of them were");
    }

    [Test]
    public async Task RescheduleJobShouldWork()
    {
        var firstFireTime = DateTimeOffset.Now;
        A.CallTo(() => FakeScheduler.RescheduleJob(triggerKeyOne, A<ITrigger>._, A<CancellationToken>._)).Returns(firstFireTime);

        var response = await HttpScheduler.RescheduleJob(triggerKeyOne, TestData.CronTrigger);
        response.Should().Be(firstFireTime);

        A.CallTo(() => FakeScheduler.RescheduleJob(A<TriggerKey>._, A<ITrigger>._, A<CancellationToken>._))
            .WhenArgumentsMatch((TriggerKey key, ITrigger trigger, CancellationToken _) =>
            {
                trigger.Should().BeEquivalentTo(TestData.CronTrigger);
                return key.Equals(triggerKeyOne);
            })
            .MustHaveHappened(1, Times.Exactly);

        Fake.ClearRecordedCalls(FakeScheduler);
        A.CallTo(() => FakeScheduler.RescheduleJob(triggerKeyTwo, A<ITrigger>._, A<CancellationToken>._)).Returns(null);

        response = await HttpScheduler.RescheduleJob(triggerKeyTwo, TestData.SimpleTrigger);
        response.Should().BeNull();

        A.CallTo(() => FakeScheduler.RescheduleJob(A<TriggerKey>._, A<ITrigger>._, A<CancellationToken>._))
            .WhenArgumentsMatch((TriggerKey key, ITrigger trigger, CancellationToken _) =>
            {
                trigger.Should().BeEquivalentTo(TestData.SimpleTrigger);
                return key.Equals(triggerKeyTwo);
            })
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResetTriggerFromErrorStateShouldWork()
    {
        A.CallTo(() => FakeScheduler.ResetTriggerFromErrorState(triggerKeyOne, A<CancellationToken>._)).Returns(true);

        bool applied = await HttpScheduler.ResetTriggerFromErrorState(triggerKeyOne);

        applied.Should().BeTrue("the applied flag must round-trip over the wire");
        A.CallTo(() => FakeScheduler.ResetTriggerFromErrorState(A<TriggerKey>._, A<CancellationToken>._))
            .WhenArgumentsMatch((TriggerKey key, CancellationToken _) => key.Equals(triggerKeyOne))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResetTriggerFromErrorStateShouldReportTriggerNotInErrorState()
    {
        A.CallTo(() => FakeScheduler.ResetTriggerFromErrorState(triggerKeyOne, A<CancellationToken>._)).Returns(false);

        bool applied = await HttpScheduler.ResetTriggerFromErrorState(triggerKeyOne);

        applied.Should().BeFalse("a no-op must not report as applied");
    }

    /// <summary>
    /// There is no endpoint behind <see cref="IScheduler.UpdateTriggerDetails" />, so the client says
    /// so in the same words it uses for the members a remote scheduler cannot have at all.
    /// </summary>
    [Test]
    public async Task UpdateTriggerDetailsIsNotSupportedRemotely()
    {
        Func<Task> update = async () => await HttpScheduler.UpdateTriggerDetails(triggerKeyOne, new TriggerDetailsUpdate());

        (await update.Should().ThrowAsync<NotSupportedException>("the HTTP API has no endpoint for it"))
            .WithMessage("*HttpScheduler.UpdateTriggerDetails*");
    }

    private static TriggerHeader HeaderFor(TriggerKey triggerKey) => new(
        triggerKey,
        JobKey: new JobKey("job_of_" + triggerKey.Name, triggerKey.Group),
        Description: "description of " + triggerKey,
        TriggerType: "SIMPLE",
        State: TriggerState.Normal,
        StartTimeUtc: new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
        EndTimeUtc: new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero),
        NextFireTimeUtc: new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero),
        PreviousFireTimeUtc: null,
        CalendarName: "SomeCalendar",
        Priority: 7,
        ExecutionGroup: "imports");
}