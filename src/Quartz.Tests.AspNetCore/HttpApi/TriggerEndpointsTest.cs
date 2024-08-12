using FakeItEasy;

using FluentAssertions;
using FluentAssertions.Execution;

using Quartz.HttpClient;
using Quartz.Impl.Matchers;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class TriggerEndpointsTest : WebApiTest
{
    private static readonly TriggerKey triggerKeyOne = new("trigger1", "group1");
    private static readonly TriggerKey triggerKeyTwo = new("trigger2", "group2");

    [Test]
    public async Task GetTriggerKeysShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetTriggerKeys(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._)).Returns([triggerKeyOne, triggerKeyTwo]);

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
            A.CallTo(() => FakeScheduler.GetTriggerKeys(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
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
        A.CallTo(() => FakeScheduler.CheckExists(triggerKeyOne, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.CheckExists(triggerKeyTwo, A<CancellationToken>._)).Returns(false);

        var exists = await HttpScheduler.CheckExists(triggerKeyOne);
        exists.Should().BeTrue();

        exists = await HttpScheduler.CheckExists(triggerKeyTwo);
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
    public async Task PauseTriggerShouldWork()
    {
        await HttpScheduler.PauseTrigger(triggerKeyOne);
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKeyOne, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
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
            await HttpScheduler.PauseTriggers(matcher);
            A.CallTo(() => FakeScheduler.PauseTriggers(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task ResumeTriggerShouldWork()
    {
        await HttpScheduler.ResumeTrigger(triggerKeyOne);
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
            await HttpScheduler.ResumeTriggers(matcher);
            A.CallTo(() => FakeScheduler.ResumeTriggers(matcher, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task GetTriggerGroupNamesShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetTriggerGroupNames(A<CancellationToken>._)).Returns(["group1", "group2"]);

        var triggerGroupNames = await HttpScheduler.GetTriggerGroupNames();

        triggerGroupNames.Count.Should().Be(2);
        triggerGroupNames.Should().ContainSingle(x => x == "group1");
        triggerGroupNames.Should().ContainSingle(x => x == "group2");
    }

    [Test]
    public async Task GetPausedTriggerGroupsShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetPausedTriggerGroups(A<CancellationToken>._)).Returns(["group1"]);

        var triggerGroupNames = await HttpScheduler.GetPausedTriggerGroups();

        triggerGroupNames.Count.Should().Be(1);
        triggerGroupNames.Should().ContainSingle(x => x == "group1");
    }

    [Test]
    public async Task IsTriggerGroupPausedShouldWork()
    {
        A.CallTo(() => FakeScheduler.IsTriggerGroupPaused("group1", A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.IsTriggerGroupPaused("group2", A<CancellationToken>._)).Returns(false);

        var paused = await HttpScheduler.IsTriggerGroupPaused("group1");
        paused.Should().BeTrue();

        paused = await HttpScheduler.IsTriggerGroupPaused("group2");
        paused.Should().BeFalse();
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

        var jobDetailsForUnknownJob = TestData.JobDetail.GetJobBuilder()
            .OfType("Quartz.Tests.AspNetCore.Support.DummyJob2, Quartz.Tests.AspNetCore")
            .Build();

        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.ScheduleJob(jobDetailsForUnknownJob, TestData.SimpleTrigger).AsTask())!
            .Message.Should().ContainEquivalentOf("unknown job type");
    }

    [Test]
    public async Task ScheduleJobsShouldWork()
    {
        await HttpScheduler.ScheduleJob(TestData.JobDetail, new[] { TestData.CronTrigger, TestData.SimpleTrigger }, replace: true);
        A.CallTo(() => FakeScheduler.ScheduleJobs(A<IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>>>._, A<bool>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken _) =>
            {
                // WhenArgumentsMatch is probably not intended for asserts, but this works so...
                triggersAndJobs.Count.Should().Be(1);

                var (jobDetail, triggersForJob) = triggersAndJobs.Single(x => x.Key.Key.Equals(TestData.JobDetail.Key));
                jobDetail.Should().BeEquivalentTo(TestData.JobDetail);
                triggersForJob.Count.Should().Be(2);
                triggersForJob.Single(x => x.Key.Equals(TestData.CronTrigger.Key)).Should().BeEquivalentTo(TestData.CronTrigger);
                triggersForJob.Single(x => x.Key.Equals(TestData.SimpleTrigger.Key)).Should().BeEquivalentTo(TestData.SimpleTrigger);

                return replace;
            })
            .MustHaveHappened(1, Times.Exactly);

        Fake.ClearRecordedCalls(FakeScheduler);
        var requestJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            { TestData.JobDetail, new[] { TestData.CronTrigger, TestData.SimpleTrigger } },
            { TestData.JobDetail2, new[] { TestData.CalendarIntervalTrigger } }
        };

        await HttpScheduler.ScheduleJobs(requestJobs, replace: false);
        A.CallTo(() => FakeScheduler.ScheduleJobs(A<IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>>>._, A<bool>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken _) =>
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

                return !replace;
            })
            .MustHaveHappened(1, Times.Exactly);

        var jobDetailsForUnknownJob = TestData.JobDetail.GetJobBuilder()
            .OfType("Quartz.Tests.AspNetCore.Support.DummyJob2, Quartz.Tests.AspNetCore")
            .Build();

        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.ScheduleJob(jobDetailsForUnknownJob, new[] { TestData.CronTrigger, TestData.SimpleTrigger }, replace: true).AsTask())!
            .Message.Should().ContainEquivalentOf("unknown job type");

        var request = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { { jobDetailsForUnknownJob, new[] { TestData.CronTrigger } } };
        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.ScheduleJobs(request, replace: true).AsTask())!.Message.Should().ContainEquivalentOf("unknown job type");
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
        A.CallTo(() => FakeScheduler.UnscheduleJobs(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).Returns(true);

        var response = await HttpScheduler.UnscheduleJobs(new[] { triggerKeyOne, triggerKeyTwo });
        response.Should().BeTrue();

        A.CallTo(() => FakeScheduler.UnscheduleJobs(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .WhenArgumentsMatch((IReadOnlyCollection<TriggerKey> keys, CancellationToken _) => keys.Count == 2 && keys.Contains(triggerKeyOne) && keys.Contains(triggerKeyTwo))
            .MustHaveHappened(1, Times.Exactly);
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
        await HttpScheduler.ResetTriggerFromErrorState(triggerKeyOne);

        A.CallTo(() => FakeScheduler.ResetTriggerFromErrorState(A<TriggerKey>._, A<CancellationToken>._))
            .WhenArgumentsMatch((TriggerKey key, CancellationToken _) => key.Equals(triggerKeyOne))
            .MustHaveHappened(1, Times.Exactly);
    }
}