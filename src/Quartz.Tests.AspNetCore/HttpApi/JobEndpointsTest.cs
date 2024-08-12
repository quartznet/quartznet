using FakeItEasy;

using FluentAssertions;
using FluentAssertions.Execution;

using Quartz.HttpClient;
using Quartz.Impl.Matchers;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class JobEndpointsTest : WebApiTest
{
    private static readonly JobKey jobKeyOne = new("job1", "group1");
    private static readonly JobKey jobKeyTwo = new("job2", "group2");

    [Test]
    public async Task GetJobKeysShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetJobKeys(A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns([jobKeyOne, jobKeyTwo]);

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
            A.CallTo(() => FakeScheduler.GetJobKeys(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
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
        A.CallTo(() => FakeScheduler.CheckExists(jobKeyOne, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.CheckExists(jobKeyTwo, A<CancellationToken>._)).Returns(false);

        var exists = await HttpScheduler.CheckExists(jobKeyOne);
        exists.Should().BeTrue();

        exists = await HttpScheduler.CheckExists(jobKeyTwo);
        exists.Should().BeFalse();
    }

    [Test]
    public async Task GetJobTriggersShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetTriggersOfJob(jobKeyOne, A<CancellationToken>._)).Returns([TestData.SimpleTrigger, TestData.CronTrigger]);
        A.CallTo(() => FakeScheduler.GetTriggersOfJob(jobKeyTwo, A<CancellationToken>._)).Returns([]);

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
    public async Task CurrentlyExecutingJobsShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetCurrentlyExecutingJobs(A<CancellationToken>._)).Returns([TestData.ExecutingJobOne, TestData.ExecutingJobTwo]);

        var jobs = await HttpScheduler.GetCurrentlyExecutingJobs();
        jobs.Count.Should().Be(2);
        AssertJob(jobs, TestData.ExecutingJobOne);
        AssertJob(jobs, TestData.ExecutingJobTwo);

        static void AssertJob(IEnumerable<IJobExecutionContext> jobs, IJobExecutionContext expected)
        {
            var actual = jobs.Single(x => x.JobDetail.Key.Equals(expected.JobDetail.Key));
            actual.Should().BeEquivalentTo(expected, options => options
                .Using<TimeSpan>(x => x.Subject.Should().BeCloseTo(x.Expectation, TimeSpan.FromMilliseconds(100))).WhenTypeIs<TimeSpan>()
                .Excluding(y => y.Scheduler)
                .Excluding(y => y.RecoveringTriggerKey)
                .Excluding(y => y.JobInstance)
                .Excluding(y => y.CancellationToken)
            );
        }
    }

    [Test]
    public async Task PauseJobShouldWork()
    {
        await HttpScheduler.PauseJob(jobKeyOne);
        A.CallTo(() => FakeScheduler.PauseJob(jobKeyOne, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
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
            await HttpScheduler.PauseJobs(matcher);
            A.CallTo(() => FakeScheduler.PauseJobs(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task ResumeJobShouldWork()
    {
        await HttpScheduler.ResumeJob(jobKeyOne);
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
            await HttpScheduler.ResumeJobs(matcher);
            A.CallTo(() => FakeScheduler.ResumeJobs(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task TriggerJobShouldWork()
    {
        await HttpScheduler.TriggerJob(jobKeyOne);
        A.CallTo(() => FakeScheduler.TriggerJob(jobKeyOne, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);

        await HttpScheduler.TriggerJob(jobKeyOne, new JobDataMap { { "TestKey", "TestValue" } });

        A.CallTo(() => FakeScheduler.TriggerJob(A<JobKey>._, A<JobDataMap>._, A<CancellationToken>._))
            .WhenArgumentsMatch((JobKey jobKey, JobDataMap jobData, CancellationToken _) =>
                jobKey.Equals(jobKeyOne) && jobData.Count == 1 && jobData.ContainsKey("TestKey") && jobData["TestKey"] is "TestValue"
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
        A.CallTo(() => FakeScheduler.Interrupt("123", A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.Interrupt("234", A<CancellationToken>._)).Returns(false);

        var result = await HttpScheduler.Interrupt("123");
        result.Should().BeTrue();

        result = await HttpScheduler.Interrupt("234");
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
        A.CallTo(() => FakeScheduler.DeleteJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._)).Returns(true);

        var result = await HttpScheduler.DeleteJobs(new[] { jobKeyOne, jobKeyTwo });
        result.Should().BeTrue();

        A.CallTo(() => FakeScheduler.DeleteJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<JobKey> jobKeys, CancellationToken _) => jobKeys.Contains(jobKeyOne) && jobKeys.Contains(jobKeyTwo))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task AddJobShouldWork()
    {
        await HttpScheduler.AddJob(TestData.JobDetail, replace: true);
        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<bool>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IJobDetail jobDetail, bool replace, CancellationToken _) =>
            {
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                return replace;
            })
            .MustHaveHappened(1, Times.Exactly);

        await HttpScheduler.AddJob(TestData.JobDetail, replace: true, storeNonDurableWhileAwaitingScheduling: true);
        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<bool>._, A<bool>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling, CancellationToken _) =>
            {
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                return replace && storeNonDurableWhileAwaitingScheduling;
            })
            .MustHaveHappened(1, Times.Exactly);

        var jobDetailsForUnknownJob = TestData.JobDetail.GetJobBuilder()
            .OfType("Quartz.Tests.AspNetCore.Support.DummyJob2, Quartz.Tests.AspNetCore")
            .Build();

        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.AddJob(jobDetailsForUnknownJob, replace: false).AsTask())!
            .Message.Should().ContainEquivalentOf("unknown job type");
    }

    [Test]
    public async Task GetJobGroupNamesShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetJobGroupNames(A<CancellationToken>._)).Returns(["group1", "group2"]);

        var jobGroupNames = await HttpScheduler.GetJobGroupNames();

        jobGroupNames.Count.Should().Be(2);
        jobGroupNames.Should().ContainSingle(x => x == "group1");
        jobGroupNames.Should().ContainSingle(x => x == "group2");
    }

    [Test]
    public async Task IsJobGroupPausedShouldWork()
    {
        A.CallTo(() => FakeScheduler.IsJobGroupPaused("group1", A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.IsJobGroupPaused("group2", A<CancellationToken>._)).Returns(false);

        var paused = await HttpScheduler.IsJobGroupPaused("group1");
        paused.Should().BeTrue();

        paused = await HttpScheduler.IsJobGroupPaused("group2");
        paused.Should().BeFalse();
    }
}