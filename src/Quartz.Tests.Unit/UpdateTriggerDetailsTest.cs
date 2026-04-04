using System.Collections.Specialized;

using FakeItEasy;

using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit;

public class UpdateTriggerDetailsTest
{
    private RAMJobStore jobStore;
    private IJobDetail jobDetail;

    [SetUp]
    public void SetUp()
    {
        jobStore = new RAMJobStore();
        TestSignaler signaler = new TestSignaler();
        jobStore.Initialize(null!, signaler);
        jobStore.SchedulerStarted();

        jobDetail = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(new JobKey("job1", "jobGroup1"))
            .StoreDurably(true)
            .Build();

        jobStore.StoreJob(jobDetail, false);
    }

    [Test]
    public async Task UpdateDescription_PreservesFireTimes()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc()!.Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("Updated description");

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.RetrieveTrigger(trigger.Key))!;
        retrieved.Should().NotBeNull();
        retrieved.Description.Should().Be("Updated description");
        retrieved.GetNextFireTimeUtc().Should().Be(nextFireBefore);
    }

    [Test]
    public async Task UpdatePriority_PreservesFireTimes()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc()!.Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithPriority(10);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.RetrieveTrigger(trigger.Key))!;
        retrieved.Priority.Should().Be(10);
        retrieved.GetNextFireTimeUtc().Should().Be(nextFireBefore);
    }

    [Test]
    public async Task UpdateJobDataMap_PreservesFireTimes()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc()!.Value;

        JobDataMap newData = new JobDataMap { { "key1", "value1" } };
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithJobDataMap(newData);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.RetrieveTrigger(trigger.Key))!;
        retrieved.JobDataMap.GetString("key1").Should().Be("value1");
        retrieved.GetNextFireTimeUtc().Should().Be(nextFireBefore);
    }

    [Test]
    public async Task TriggerNotFound_ReturnsFalse()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("something");

        bool result = await jobStore.UpdateTriggerDetails(new TriggerKey("nonexistent", "g1"), update);

        result.Should().BeFalse();
    }

    [Test]
    public async Task PreservesState_WhenPaused()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);
        await jobStore.PauseTrigger(trigger.Key);

        (await jobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);

        await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithDescription("Updated while paused"));

        (await jobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);
        (await jobStore.RetrieveTrigger(trigger.Key))!.Description.Should().Be("Updated while paused");
    }

    [Test]
    public async Task PriorityChange_AffectsAcquisitionOrder()
    {
        DateTimeOffset d = DateBuilder.EvenMinuteDateAfterNow();

        IOperableTrigger trigger1 = new SimpleTriggerImpl("t1", "g1", jobDetail.Key.Name, jobDetail.Key.Group, d.AddSeconds(100), d.AddSeconds(300), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("t2", "g1", jobDetail.Key.Name, jobDetail.Key.Group, d.AddSeconds(100), d.AddSeconds(300), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger1, false);
        await jobStore.StoreTrigger(trigger2, false);

        await jobStore.UpdateTriggerDetails(trigger2.Key, new TriggerDetailsUpdate().WithPriority(10));

        DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc()!.Value;
        var acquired = await jobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 2, TimeSpan.FromMilliseconds(1));

        acquired.Should().HaveCount(2);
        acquired.First().Key.Should().Be(trigger2.Key);
        acquired.Last().Key.Should().Be(trigger1.Key);
    }

    [Test]
    public async Task CalendarName_ValidatesExistence()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        Assert.ThrowsAsync<JobPersistenceException>(async () =>
            await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithCalendarName("nonexistent")));
    }

    [Test]
    public async Task CalendarName_NullClearsCalendar()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.CalendarName = "myCal";
        trigger.ComputeFirstFireTimeUtc(null);

        await jobStore.StoreCalendar("myCal", new BaseCalendar(), false, false);
        await jobStore.StoreTrigger(trigger, false);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithCalendarName(null));

        result.Should().BeTrue();
        (await jobStore.RetrieveTrigger(trigger.Key))!.CalendarName.Should().BeNull();
    }

    [Test]
    public async Task EmptyUpdate_ReturnsTrueForExistingTrigger()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        (await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate())).Should().BeTrue();
    }

    [Test]
    public async Task MultipleProperties_UpdatedAtOnce()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc()!.Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("new desc")
            .WithPriority(7)
            .WithJobDataMap(new JobDataMap { { "k", "v" } });

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.RetrieveTrigger(trigger.Key))!;
        retrieved.Description.Should().Be("new desc");
        retrieved.Priority.Should().Be(7);
        retrieved.JobDataMap.GetString("k").Should().Be("v");
        retrieved.GetNextFireTimeUtc().Should().Be(nextFireBefore);
    }

    [Test]
    public async Task MisfireInstruction_UpdatedWithoutReschedule()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc()!.Value;

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithMisfireInstruction(MisfireInstruction.CronTrigger.DoNothing));

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.RetrieveTrigger(trigger.Key))!;
        retrieved.MisfireInstruction.Should().Be(MisfireInstruction.CronTrigger.DoNothing);
        retrieved.GetNextFireTimeUtc().Should().Be(nextFireBefore);
    }

    [Test]
    public async Task SchedulerLevel_UpdateTriggerDetails()
    {
        NameValueCollection config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "UpdateTriggerDetailsTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        IScheduler scheduler = await new StdSchedulerFactory(config).GetScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("extJob", "extGroup")
                .StoreDurably()
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("extTrigger", "extGroup")
                .ForJob(job)
                .WithCronSchedule("0/30 * * * * ?")
                .Build();

            await scheduler.AddJob(job, true);
            await scheduler.ScheduleJob(trigger);

            DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc()!.Value;

            TriggerDetailsUpdate update = new TriggerDetailsUpdate()
                .WithDescription("via scheduler")
                .WithPriority(8);

            bool result = await scheduler.UpdateTriggerDetails(trigger.Key, update);

            result.Should().BeTrue();
            ITrigger retrieved = (await scheduler.GetTrigger(trigger.Key))!;
            retrieved.Description.Should().Be("via scheduler");
            retrieved.Priority.Should().Be(8);
            retrieved.GetNextFireTimeUtc().Should().Be(nextFireBefore);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    private IOperableTrigger CreateCronTrigger(string name, string group, string cronExpression, DateTimeOffset startTime)
    {
        return (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob(jobDetail)
            .StartAt(startTime)
            .WithCronSchedule(cronExpression)
            .Build();
    }

    private sealed class TestSignaler : ISchedulerSignaler
    {
        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersError(string message, SchedulerException jpe, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;
    }
}
