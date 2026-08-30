using System.Net;
using System.Net.Http.Json;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class JobEndpointsTest : WebApiTest
{
    private static readonly JobKey jobKeyOne = new("job1", "group1");
    private static readonly JobKey jobKeyTwo = new("job2", "group2");

    [Test]
    public async Task GetJobKeysShouldWork()
    {
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([HeaderFor(jobKeyOne), HeaderFor(jobKeyTwo)], HasMore: false));

        var jobKeys = await HttpScheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());

        using (new AssertionScope())
        {
            jobKeys.Count.Should().Be(2);
            jobKeys.Should().ContainSingle(x => x.Equals(jobKeyOne));
            jobKeys.Should().ContainSingle(x => x.Equals(jobKeyTwo));
        }

        var matchers = new[]
        {
            GroupMatcher<JobKey>.AnyGroup(),
            GroupMatcher<JobKey>.GroupContains("contains"),
            GroupMatcher<JobKey>.GroupEquals("equals"),
            GroupMatcher<JobKey>.GroupEndsWith("ends"),
            GroupMatcher<JobKey>.GroupStartsWith("starts")
        };

        foreach (var matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            await HttpScheduler.GetJobKeys(matcher);

            // the compat listing is deliberately unbounded
            A.CallTo(() => FakeScheduler.QueryJobs(new JobQuery { Group = matcher, Take = int.MaxValue }, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task QueryJobsShouldPassPagingAndReturnHeaders()
    {
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([HeaderFor(jobKeyOne)], HasMore: true, TotalCount: 5));

        var query = new JobQuery
        {
            Group = GroupMatcher<JobKey>.GroupEquals("group1"),
            Skip = 2,
            Take = 1,
            IncludeTotalCount = true
        };

        var result = await HttpScheduler.QueryJobs(query);

        using (new AssertionScope())
        {
            result.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(HeaderFor(jobKeyOne));
            result.HasMore.Should().BeTrue();
            result.TotalCount.Should().Be(5);
        }

        A.CallTo(() => FakeScheduler.QueryJobs(query, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryJobsWithoutTakeParameterAppliesTheServerDefault()
    {
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([], HasMore: false));

        // a raw request naming no take: the server must apply the query record's own default,
        // not "everything"
        using var httpClient = WebApplicationFactory.CreateClient();
        var response = await httpClient.GetAsync($"schedulers/{HttpScheduler.SchedulerName}/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>.That.Matches(query => query.Take == PagedQuery.DefaultTake), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryJobsAlwaysSendsTakeOnTheWire()
    {
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([], HasMore: false));

        // the default: the client sends take=250 explicitly rather than leaving the decision
        // to whatever the server's default happens to be
        await HttpScheduler.QueryJobs(new JobQuery());
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>.That.Matches(query => query.Take == PagedQuery.DefaultTake), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);

        // the explicit unbounded opt-in must survive the wire instead of being silently replaced
        // by the server default (the old behavior omitted the parameter for int.MaxValue)
        Fake.ClearRecordedCalls(FakeScheduler);
        await HttpScheduler.QueryJobs(new JobQuery { Take = int.MaxValue });
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>.That.Matches(query => query.Take == int.MaxValue), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task FetchJobsShouldWork()
    {
        var missing = new JobKey("missing", "missing_group");

        A.CallTo(() => FakeScheduler.GetJobDetails(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns([TestData.JobDetail, TestData.JobDetail2]);

        var jobDetails = await HttpScheduler.GetJobDetails([TestData.JobDetail.Key, TestData.JobDetail2.Key, missing]);

        jobDetails.Count.Should().Be(2);
        jobDetails.Single(x => x.Key.Equals(TestData.JobDetail.Key)).Should().BeEquivalentTo(TestData.JobDetail);
        jobDetails.Single(x => x.Key.Equals(TestData.JobDetail2.Key)).Should().BeEquivalentTo(TestData.JobDetail2);

        A.CallTo(() => FakeScheduler.GetJobDetails(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<JobKey> keys, CancellationToken _) =>
                keys.Count == 3 && keys.Contains(TestData.JobDetail.Key) && keys.Contains(TestData.JobDetail2.Key) && keys.Contains(missing))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task FetchJobsShouldNotCallSchedulerWithoutKeys()
    {
        var jobDetails = await HttpScheduler.GetJobDetails([]);

        jobDetails.Should().BeEmpty();
        A.CallTo(() => FakeScheduler.GetJobDetails(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task FetchJobsShouldRejectTooManyKeys()
    {
        using var httpClient = WebApplicationFactory.CreateClient();

        var keys = Enumerable.Range(0, 1001).Select(x => new { name = "job" + x, group = "group" }).ToArray();
        var response = await httpClient.PostAsJsonAsync($"schedulers/{HttpScheduler.SchedulerName}/jobs/fetch", keys);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().ContainEquivalentOf("at most 1000");
        A.CallTo(() => FakeScheduler.GetJobDetails(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task GetJobDetailsShouldWork()
    {
        var nonExistingJobKey = new JobKey("non_existing_name", "non_existing_group");

        A.CallTo(() => FakeScheduler.GetJobDetail(jobKeyOne, A<CancellationToken>._)).Returns(TestData.JobDetail);
        A.CallTo(() => FakeScheduler.GetJobDetail(jobKeyTwo, A<CancellationToken>._)).Returns(TestData.JobDetail2);
        A.CallTo(() => FakeScheduler.GetJobDetail(nonExistingJobKey, A<CancellationToken>._)).Returns(null);

        var jobDetails = await HttpScheduler.GetJobDetail(jobKeyOne);
        jobDetails.Should().BeEquivalentTo(TestData.JobDetail);

        jobDetails = await HttpScheduler.GetJobDetail(jobKeyTwo);
        jobDetails.Should().BeEquivalentTo(TestData.JobDetail2);

        jobDetails = await HttpScheduler.GetJobDetail(nonExistingJobKey);
        jobDetails.Should().BeNull();
    }

    [Test]
    public async Task CheckJobExistsShouldWork()
    {
        A.CallTo(() => FakeScheduler.Exists(jobKeyOne, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.Exists(jobKeyTwo, A<CancellationToken>._)).Returns(false);

        var exists = await HttpScheduler.Exists(jobKeyOne);
        exists.Should().BeTrue();

        exists = await HttpScheduler.Exists(jobKeyTwo);
        exists.Should().BeFalse();
    }

    [Test]
    public async Task GetJobTriggersShouldWork()
    {
        // GetTriggersOfJob is an extension over QueryTriggers + GetTriggers, so both ends go through those.
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>.That.Matches(query => jobKeyOne.Equals(query.Job)), A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([HeaderFor(TestData.SimpleTrigger), HeaderFor(TestData.CronTrigger)], HasMore: false));
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>.That.Matches(query => jobKeyTwo.Equals(query.Job)), A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));
        A.CallTo(() => FakeScheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<ITrigger> { TestData.SimpleTrigger, TestData.CronTrigger });

        var triggers = await HttpScheduler.GetTriggersOfJob(jobKeyOne);
        triggers.Count.Should().Be(2);

        var simpleTrigger = triggers.Single(x => x.Key.Equals(TestData.SimpleTrigger.Key));
        simpleTrigger.Should().BeEquivalentTo(TestData.SimpleTrigger);

        var cronTrigger = triggers.Single(x => x.Key.Equals(TestData.CronTrigger.Key));
        cronTrigger.Should().BeEquivalentTo(TestData.CronTrigger);

        triggers = await HttpScheduler.GetTriggersOfJob(jobKeyTwo);
        triggers.Should().BeEmpty();
    }

    [Test]
    public async Task GetJobTriggersEndpointShouldReturnTheJobsTriggers()
    {
        // The client reaches a job's triggers through the trigger query, so this is the only cover the
        // jobs/{group}/{name}/triggers route itself gets.
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>.That.Matches(query => jobKeyOne.Equals(query.Job)), A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([HeaderFor(TestData.SimpleTrigger), HeaderFor(TestData.CronTrigger)], HasMore: false));
        A.CallTo(() => FakeScheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<ITrigger> { TestData.SimpleTrigger, TestData.CronTrigger });

        using HttpClient httpClient = WebApplicationFactory.CreateClient();

        HttpResponseMessage response = await httpClient.GetAsync($"schedulers/{HttpScheduler.SchedulerName}/jobs/{jobKeyOne.Group}/{jobKeyOne.Name}/triggers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await response.Content.ReadAsStringAsync();
        using (new AssertionScope())
        {
            body.Should().Contain(TestData.SimpleTrigger.Key.Name);
            body.Should().Contain(TestData.CronTrigger.Key.Name);
        }

        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>.That.Matches(query => jobKeyOne.Equals(query.Job)), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task GetJobTriggersEndpointShouldReturnEmptyForAJobWithoutTriggers()
    {
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));

        using HttpClient httpClient = WebApplicationFactory.CreateClient();

        HttpResponseMessage response = await httpClient.GetAsync($"schedulers/{HttpScheduler.SchedulerName}/jobs/{jobKeyTwo.Group}/{jobKeyTwo.Name}/triggers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("[]");

        // no keys came back, so the bulk fetch must not have been asked for anything
        A.CallTo(() => FakeScheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task QueryFireInstancesShouldRoundTripEveryMember()
    {
        A.CallTo(() => FakeScheduler.QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstance>([TestData.ExecutingFireInstance, TestData.AcquiredFireInstance], HasMore: true, TotalCount: 7));

        PagedResult<FireInstance> result = await HttpScheduler.QueryFireInstances(new FireInstanceQuery());

        using (new AssertionScope())
        {
            result.Items.Should().BeEquivalentTo([TestData.ExecutingFireInstance, TestData.AcquiredFireInstance],
                "every member of a fire instance has to survive the wire, the nullable job key included");
            result.HasMore.Should().BeTrue();
            result.TotalCount.Should().Be(7);
        }
    }

    [Test]
    public async Task QueryFireInstancesShouldPassEveryFilter()
    {
        A.CallTo(() => FakeScheduler.QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstance>([], HasMore: false));

        FireInstanceQuery query = new()
        {
            TriggerGroup = GroupMatcher<TriggerKey>.GroupStartsWith("group"),
            TriggerName = NameMatcher<TriggerKey>.NameEquals("trigger1"),
            Job = new JobKey("job1", "jobs"),
            SchedulerInstanceId = "node-2",
            State = FireInstanceState.Acquired,
            Skip = 3,
            Take = 4,
            IncludeTotalCount = true
        };

        await HttpScheduler.QueryFireInstances(query);

        A.CallTo(() => FakeScheduler.QueryFireInstances(query, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryFireInstancesShouldDefaultToExecutingAndSayWhenItWantsEveryState()
    {
        FireInstanceQuery captured = null!;
        A.CallTo(() => FakeScheduler.QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._))
            .Invokes((FireInstanceQuery q, CancellationToken _) => captured = q)
            .Returns(new PagedResult<FireInstance>([], HasMore: false));

        // A request that names no state at all: the endpoint has to leave the record's own default alone.
        using var client = WebApplicationFactory.CreateClient();
        var response = await client.GetAsync($"schedulers/{HttpScheduler.SchedulerName}/jobs/fire-instances");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.State.Should().Be(FireInstanceState.Executing, "a bare request lists what is running");

        await HttpScheduler.QueryFireInstances(new FireInstanceQuery { State = null });
        captured.State.Should().BeNull("the client says 'any' out loud, so the server can tell it apart from silence");
    }

    [Test]
    public async Task QueryFireInstancesShouldRejectAnUnknownState()
    {
        using var client = WebApplicationFactory.CreateClient();
        var response = await client.GetAsync($"schedulers/{HttpScheduler.SchedulerName}/jobs/fire-instances?state=not-a-state");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a state the API cannot honour is a bad request, not a silently unfiltered listing");
    }

    [Test]
    public async Task PauseJobShouldWork()
    {
        A.CallTo(() => FakeScheduler.PauseJob(jobKeyOne, A<CancellationToken>._)).Returns(true);

        bool applied = await HttpScheduler.PauseJob(jobKeyOne);

        applied.Should().BeTrue("the applied flag must round-trip over the wire");
        A.CallTo(() => FakeScheduler.PauseJob(jobKeyOne, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task PauseJobShouldReportMissingJob()
    {
        A.CallTo(() => FakeScheduler.PauseJob(jobKeyOne, A<CancellationToken>._)).Returns(false);

        bool applied = await HttpScheduler.PauseJob(jobKeyOne);

        applied.Should().BeFalse("a no-op must not report as applied");
    }

    [Test]
    public async Task PauseJobsShouldWork()
    {
        var matchers = new[]
        {
            GroupMatcher<JobKey>.AnyGroup(),
            GroupMatcher<JobKey>.GroupContains("contains"),
            GroupMatcher<JobKey>.GroupEquals("equals"),
            GroupMatcher<JobKey>.GroupEndsWith("ends"),
            GroupMatcher<JobKey>.GroupStartsWith("starts")
        };

        foreach (var matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            A.CallTo(() => FakeScheduler.PauseJobs(matcher, A<CancellationToken>._)).Returns(new List<string> { "paused-group" });

            List<string> pausedGroups = await HttpScheduler.PauseJobs(matcher);

            pausedGroups.Should().Equal("paused-group");
            A.CallTo(() => FakeScheduler.PauseJobs(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task PauseJobsByKeyShouldRoundTripTheAppliedKeys()
    {
        A.CallTo(() => FakeScheduler.PauseJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { jobKeyOne });

        List<JobKey> paused = await HttpScheduler.PauseJobs([jobKeyOne, jobKeyTwo]);

        paused.Should().Equal([jobKeyOne],
            "the answer names the keys the pause applied to, and the key that named no job is absent");

        A.CallTo(() => FakeScheduler.PauseJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<JobKey> keys, CancellationToken _) => keys.SequenceEqual([jobKeyOne, jobKeyTwo]))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResumeJobsByKeyShouldRoundTripTheAppliedKeys()
    {
        A.CallTo(() => FakeScheduler.ResumeJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { jobKeyOne, jobKeyTwo });

        List<JobKey> resumed = await HttpScheduler.ResumeJobs([jobKeyOne, jobKeyTwo]);

        resumed.Should().Equal([jobKeyOne, jobKeyTwo]);

        A.CallTo(() => FakeScheduler.ResumeJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResumeJobShouldWork()
    {
        A.CallTo(() => FakeScheduler.ResumeJob(jobKeyOne, A<CancellationToken>._)).Returns(true);

        bool applied = await HttpScheduler.ResumeJob(jobKeyOne);

        applied.Should().BeTrue("the applied flag must round-trip over the wire");
        A.CallTo(() => FakeScheduler.ResumeJob(jobKeyOne, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResumeJobsShouldWork()
    {
        var matchers = new[]
        {
            GroupMatcher<JobKey>.AnyGroup(),
            GroupMatcher<JobKey>.GroupContains("contains"),
            GroupMatcher<JobKey>.GroupEquals("equals"),
            GroupMatcher<JobKey>.GroupEndsWith("ends"),
            GroupMatcher<JobKey>.GroupStartsWith("starts")
        };

        foreach (var matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            A.CallTo(() => FakeScheduler.ResumeJobs(matcher, A<CancellationToken>._)).Returns(new List<string> { "resumed-group" });

            List<string> resumedGroups = await HttpScheduler.ResumeJobs(matcher);

            resumedGroups.Should().Equal("resumed-group");
            A.CallTo(() => FakeScheduler.ResumeJobs(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task TriggerJobShouldWork()
    {
        await HttpScheduler.TriggerJob(jobKeyOne);
        A.CallTo(() => FakeScheduler.TriggerJob(jobKeyOne, null, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);

        await HttpScheduler.TriggerJob(jobKeyOne, new JobDataMap { { "TestKey", "TestValue" } });

        A.CallTo(() => FakeScheduler.TriggerJob(A<JobKey>._, A<JobDataMap>._, A<CancellationToken>._))
            .WhenArgumentsMatch((JobKey jobKey, JobDataMap? jobData, CancellationToken _) =>
                jobKey.Equals(jobKeyOne) && jobData is not null && jobData.Count == 1 && jobData.ContainsKey("TestKey") && jobData["TestKey"] is "TestValue"
            )
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task InterruptJobShouldWork()
    {
        A.CallTo(() => FakeScheduler.Interrupt(jobKeyOne, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.Interrupt(jobKeyTwo, A<CancellationToken>._)).Returns(false);

        var result = await HttpScheduler.Interrupt(jobKeyOne);
        result.Should().BeTrue();

        result = await HttpScheduler.Interrupt(jobKeyTwo);
        result.Should().BeFalse();
    }

    [Test]
    public async Task InterruptJobInstanceShouldWork()
    {
        A.CallTo(() => FakeScheduler.InterruptFireInstance("123", A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.InterruptFireInstance("234", A<CancellationToken>._)).Returns(false);

        var result = await HttpScheduler.InterruptFireInstance("123");
        result.Should().BeTrue();

        result = await HttpScheduler.InterruptFireInstance("234");
        result.Should().BeFalse();
    }

    [Test]
    public async Task DeleteJobShouldWork()
    {
        A.CallTo(() => FakeScheduler.DeleteJob(jobKeyOne, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.DeleteJob(jobKeyTwo, A<CancellationToken>._)).Returns(false);

        var result = await HttpScheduler.DeleteJob(jobKeyOne);
        result.Should().BeTrue();

        result = await HttpScheduler.DeleteJob(jobKeyTwo);
        result.Should().BeFalse();
    }

    [Test]
    public async Task DeleteJobsShouldWork()
    {
        A.CallTo(() => FakeScheduler.DeleteJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { jobKeyOne, jobKeyTwo });

        var result = await HttpScheduler.DeleteJobs([jobKeyOne, jobKeyTwo]);
        result.Should().Equal([jobKeyOne, jobKeyTwo]);

        A.CallTo(() => FakeScheduler.DeleteJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<JobKey> jobKeys, CancellationToken _) => jobKeys.Contains(jobKeyOne) && jobKeys.Contains(jobKeyTwo))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task DeleteJobsShouldCarryThePartialHitBackToTheCaller()
    {
        // The whole point of the key list: three of five deleted used to answer false, which a caller
        // could not tell from nothing having happened.
        A.CallTo(() => FakeScheduler.DeleteJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { jobKeyTwo });

        var result = await HttpScheduler.DeleteJobs([jobKeyOne, jobKeyTwo]);

        result.Should().Equal([jobKeyTwo],
            "the key the server did not find is absent from the answer, and the one it deleted is named");
    }

    [Test]
    public async Task DeleteJobsByGroupShouldCarryEveryMatcherAndAnswerWithTheKeys()
    {
        var matchers = new[]
        {
            GroupMatcher<JobKey>.AnyGroup(),
            GroupMatcher<JobKey>.GroupContains("contains"),
            GroupMatcher<JobKey>.GroupEquals("equals"),
            GroupMatcher<JobKey>.GroupEndsWith("ends"),
            GroupMatcher<JobKey>.GroupStartsWith("starts")
        };

        foreach (var matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            A.CallTo(() => FakeScheduler.DeleteJobs(matcher, A<CancellationToken>._))
                .Returns(new List<JobKey> { jobKeyOne, jobKeyTwo });

            List<JobKey> deleted = await HttpScheduler.DeleteJobs(matcher);

            deleted.Should().Equal([jobKeyOne, jobKeyTwo],
                "the group form answers with the keys it removed, not with the group names — there is "
                + "no deleted group left to name");
            A.CallTo(() => FakeScheduler.DeleteJobs(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task DeleteJobsByGroupShouldAnswerWithAnEmptyListWhenNothingMatched()
    {
        A.CallTo(() => FakeScheduler.DeleteJobs(A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey>());

        List<JobKey> deleted = await HttpScheduler.DeleteJobs(GroupMatcher<JobKey>.GroupEquals("nothing"));

        deleted.Should().BeEmpty("an empty group is not an error over the wire any more than it is in process");
    }

    [Test]
    public async Task AddJobShouldWork()
    {
        await HttpScheduler.AddJob(TestData.JobDetail, new AddJobOptions { Replace = true });
        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<AddJobOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IJobDetail jobDetail, AddJobOptions options, CancellationToken _) =>
            {
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                return options is { Replace: true, StoreNonDurableWhileAwaitingScheduling: false };
            })
            .MustHaveHappened(1, Times.Exactly);

        await HttpScheduler.AddJob(TestData.JobDetail, new AddJobOptions { Replace = true, StoreNonDurableWhileAwaitingScheduling = true });
        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<AddJobOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IJobDetail jobDetail, AddJobOptions options, CancellationToken _) =>
            {
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                return options is { Replace: true, StoreNonDurableWhileAwaitingScheduling: true };
            })
            .MustHaveHappened(1, Times.Exactly);

        // A job type the server cannot resolve is not rejected at request time - the server never resolves
        // a name that arrived with the request. The name reaches the scheduler as given, and only whatever
        // has to run the job resolves it.
        Fake.ClearRecordedCalls(FakeScheduler);
        IJobDetail jobDetailWithUnresolvableType = TestData.JobDetail.GetJobBuilder()
            .OfType(TestData.UnresolvableJobTypeName)
            .Build();

        await HttpScheduler.AddJob(jobDetailWithUnresolvableType);

        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<AddJobOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IJobDetail jobDetail, AddJobOptions options, CancellationToken _) =>
            {
                jobDetail.JobType.FullName.Should().Be(TestData.UnresolvableJobTypeName);
                return true;
            })
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public void AddJobShouldRejectMalformedJobType()
    {
        // Shape is still checked, it is only resolution that is not done. An empty name has no shape.
        IJobDetail jobDetailWithEmptyType = TestData.JobDetail.GetJobBuilder()
            .OfType(" ")
            .Build();

        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.AddJob(jobDetailWithEmptyType).AsTask())!
            .Message.Should().ContainEquivalentOf("malformed job type");

        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<AddJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task GetJobGroupNamesShouldWork()
    {
        A.CallTo(() => FakeScheduler.QueryJobGroups(A<JobGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobGroup>([new JobGroup("group1", Paused: false), new JobGroup("group2", Paused: true)], HasMore: false));

        var jobGroupNames = await HttpScheduler.GetJobGroupNames();

        jobGroupNames.Count.Should().Be(2);
        jobGroupNames.Should().ContainSingle(x => x == "group1");
        jobGroupNames.Should().ContainSingle(x => x == "group2");

        A.CallTo(() => FakeScheduler.QueryJobGroups(new JobGroupQuery { Take = int.MaxValue }, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryJobGroupsShouldPassPausedFilter()
    {
        A.CallTo(() => FakeScheduler.QueryJobGroups(A<JobGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobGroup>([new JobGroup("group2", Paused: true)], HasMore: false, TotalCount: 1));

        var result = await HttpScheduler.QueryJobGroups(new JobGroupQuery { Paused = true, IncludeTotalCount = true });

        using (new AssertionScope())
        {
            result.Items.Should().ContainSingle().Which.Should().Be(new JobGroup("group2", Paused: true));
            result.HasMore.Should().BeFalse();
            result.TotalCount.Should().Be(1);
        }

        A.CallTo(() => FakeScheduler.QueryJobGroups(new JobGroupQuery { Paused = true, IncludeTotalCount = true }, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task IsJobGroupPausedShouldWork()
    {
        // the check asks the store for the one named group rather than listing every paused one
        A.CallTo(() => FakeScheduler.QueryJobGroups(A<JobGroupQuery>.That.Matches(query => query.Name == "group1"), A<CancellationToken>._))
            .Returns(new PagedResult<JobGroup>([new JobGroup("group1", Paused: true)], HasMore: false));
        A.CallTo(() => FakeScheduler.QueryJobGroups(A<JobGroupQuery>.That.Matches(query => query.Name != "group1"), A<CancellationToken>._))
            .Returns(new PagedResult<JobGroup>([], HasMore: false));

        bool paused = await HttpScheduler.IsJobGroupPaused("group1");
        paused.Should().BeTrue();

        paused = await HttpScheduler.IsJobGroupPaused("group2");
        paused.Should().BeFalse();

        A.CallTo(() => FakeScheduler.QueryJobGroups(new JobGroupQuery { Name = "group1", Paused = true, Take = 1 }, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
        A.CallTo(() => FakeScheduler.QueryJobGroups(new JobGroupQuery { Name = "group2", Paused = true, Take = 1 }, A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    private static TriggerHeader HeaderFor(ITrigger trigger) => new(
        trigger.Key,
        JobKey: trigger.JobKey,
        Description: trigger.Description,
        TriggerType: "SIMPLE",
        State: TriggerState.Normal,
        StartTimeUtc: trigger.StartTimeUtc,
        EndTimeUtc: trigger.EndTimeUtc,
        NextFireTimeUtc: null,
        PreviousFireTimeUtc: null,
        CalendarName: trigger.CalendarName,
        Priority: trigger.Priority,
        ExecutionGroup: null);

    private static JobHeader HeaderFor(JobKey jobKey) => new(
        jobKey,
        Description: "description of " + jobKey,
        JobTypeName: typeof(DummyJob).FullName!,
        Durable: true,
        ConcurrentExecutionDisallowed: false,
        PersistJobDataAfterExecution: false,
        RequestsRecovery: true);
}