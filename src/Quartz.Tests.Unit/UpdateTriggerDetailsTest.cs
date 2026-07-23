using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

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
    private JobDetailImpl jobDetail;

    [SetUp]
    public void SetUp()
    {
        jobStore = new RAMJobStore();
        TestSignaler signaler = new TestSignaler();
        jobStore.Initialize(null, signaler);
        jobStore.SchedulerStarted();

        jobDetail = new JobDetailImpl("job1", "jobGroup1", typeof(NoOpJob))
        {
            Durable = true
        };
        jobStore.StoreJob(jobDetail, false);
    }

    [Test]
    public async Task UpdateDescription_PreservesFireTimes()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc().Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("Updated description");

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("Updated description", retrieved.Description);
        Assert.AreEqual(nextFireBefore, retrieved.GetNextFireTimeUtc());
    }

    [Test]
    public async Task UpdatePriority_PreservesFireTimes()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc().Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithPriority(10);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(10, retrieved.Priority);
        Assert.AreEqual(nextFireBefore, retrieved.GetNextFireTimeUtc());
    }

    [Test]
    public async Task UpdateJobDataMap_PreservesFireTimes()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc().Value;

        JobDataMap newData = new JobDataMap();
        newData.Put("key1", "value1");
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithJobDataMap(newData);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("value1", retrieved.JobDataMap.GetString("key1"));
        Assert.AreEqual(nextFireBefore, retrieved.GetNextFireTimeUtc());
    }

    [Test]
    public async Task TriggerNotFound_ReturnsFalse()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("something");

        bool result = await jobStore.UpdateTriggerDetails(new TriggerKey("nonexistent", "g1"), update);

        Assert.IsFalse(result);
    }

    [Test]
    public async Task PreservesState_WhenPaused()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);
        await jobStore.PauseTrigger(trigger.Key);

        TriggerState stateBefore = await jobStore.GetTriggerState(trigger.Key);
        Assert.AreEqual(TriggerState.Paused, stateBefore);

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("Updated while paused");

        await jobStore.UpdateTriggerDetails(trigger.Key, update);

        TriggerState stateAfter = await jobStore.GetTriggerState(trigger.Key);
        Assert.AreEqual(TriggerState.Paused, stateAfter);

        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.AreEqual("Updated while paused", retrieved.Description);
    }

    [Test]
    public async Task PriorityChange_AffectsAcquisitionOrder()
    {
        DateTimeOffset d = DateBuilder.EvenMinuteDateAfterNow();

        // Two triggers with same fire time, default priority (5)
        IOperableTrigger trigger1 = new SimpleTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, d.AddSeconds(100), d.AddSeconds(300), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("t2", "g1", jobDetail.Name, jobDetail.Group, d.AddSeconds(100), d.AddSeconds(300), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger1, false);
        await jobStore.StoreTrigger(trigger2, false);

        // Give trigger2 higher priority
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithPriority(10);
        await jobStore.UpdateTriggerDetails(trigger2.Key, update);

        DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc().Value;
        var acquired = await jobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 2, TimeSpan.FromMilliseconds(1));

        Assert.AreEqual(2, acquired.Count);
        // Higher priority (10) should be acquired first
        Assert.AreEqual(trigger2.Key, acquired.First().Key);
        Assert.AreEqual(trigger1.Key, acquired.Last().Key);
    }

    [Test]
    public async Task CalendarName_ValidatesExistence()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithCalendarName("nonexistentCalendar");

        Assert.ThrowsAsync<JobPersistenceException>(async () =>
            await jobStore.UpdateTriggerDetails(trigger.Key, update));
    }

    [Test]
    public async Task CalendarName_NullClearsCalendar()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.CalendarName = "myCal";
        trigger.ComputeFirstFireTimeUtc(null);

        await jobStore.StoreCalendar("myCal", new BaseCalendar(), false, false);
        await jobStore.StoreTrigger(trigger, false);

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithCalendarName(null);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.IsNull(retrieved.CalendarName);
    }

    [Test]
    public async Task CalendarName_SetsValidCalendar()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        await jobStore.StoreCalendar("myCal", new BaseCalendar(), false, false);

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithCalendarName("myCal");

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.AreEqual("myCal", retrieved.CalendarName);
    }

    [Test]
    public async Task EmptyUpdate_ReturnsTrueForExistingTrigger()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        TriggerDetailsUpdate update = new TriggerDetailsUpdate();

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
    }

    [Test]
    public async Task MultipleProperties_UpdatedAtOnce()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc().Value;

        JobDataMap newData = new JobDataMap();
        newData.Put("k", "v");
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("new desc")
            .WithPriority(7)
            .WithJobDataMap(newData);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("new desc", retrieved.Description);
        Assert.AreEqual(7, retrieved.Priority);
        Assert.AreEqual("v", retrieved.JobDataMap.GetString("k"));
        Assert.AreEqual(nextFireBefore, retrieved.GetNextFireTimeUtc());
    }

    [Test]
    public async Task MisfireInstruction_UpdatedWithoutReschedule()
    {
        DateTimeOffset start = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new CronTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, start, null, "0/30 * * * * ?");
        trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc().Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithMisfireInstruction(MisfireInstruction.CronTrigger.DoNothing);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        Assert.IsTrue(result);
        IOperableTrigger retrieved = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.AreEqual(MisfireInstruction.CronTrigger.DoNothing, retrieved.MisfireInstruction);
        Assert.AreEqual(nextFireBefore, retrieved.GetNextFireTimeUtc());
    }

    [Test]
    public async Task SimpleTrigger_PreservesFireStateAfterFiring()
    {
        DateTimeOffset d = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = new SimpleTriggerImpl("t1", "g1", jobDetail.Name, jobDetail.Group, d.AddSeconds(100), d.AddSeconds(500), 10, TimeSpan.FromSeconds(30));
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.StoreTrigger(trigger, false);

        // Simulate having fired by acquiring and completing
        DateTimeOffset firstFireTime = trigger.GetNextFireTimeUtc().Value;
        var acquired = await jobStore.AcquireNextTriggers(firstFireTime.AddSeconds(5), 1, TimeSpan.Zero);
        Assert.AreEqual(1, acquired.Count, "Should acquire the trigger");

        var firedResults = await jobStore.TriggersFired(acquired);
        Assert.AreEqual(1, firedResults.Count, "Should have one fired result");
        TriggerFiredResult firedResult = firedResults.First();
        await jobStore.TriggeredJobComplete(firedResult.TriggerFiredBundle.Trigger, firedResult.TriggerFiredBundle.JobDetail, SchedulerInstruction.NoInstruction);

        IOperableTrigger beforeUpdate = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.IsNotNull(beforeUpdate.GetPreviousFireTimeUtc(), "Should have a previous fire time after firing");
        Assert.IsNotNull(beforeUpdate.GetNextFireTimeUtc(), "Should have a next fire time");
        DateTimeOffset prevFireBefore = beforeUpdate.GetPreviousFireTimeUtc().Value;
        DateTimeOffset nextFireBefore = beforeUpdate.GetNextFireTimeUtc().Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("updated after firing");

        await jobStore.UpdateTriggerDetails(trigger.Key, update);

        IOperableTrigger afterUpdate = await jobStore.RetrieveTrigger(trigger.Key);
        Assert.AreEqual("updated after firing", afterUpdate.Description);
        Assert.AreEqual(prevFireBefore, afterUpdate.GetPreviousFireTimeUtc());
        Assert.AreEqual(nextFireBefore, afterUpdate.GetNextFireTimeUtc());
    }

    [Test]
    public async Task SchedulerExtensionMethod_WorksWithStdScheduler()
    {
        NameValueCollection config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "UpdateTriggerDetailsExtMethodTest",
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

            DateTimeOffset nextFireBefore = trigger.GetNextFireTimeUtc().Value;

            TriggerDetailsUpdate update = new TriggerDetailsUpdate()
                .WithDescription("via extension method")
                .WithPriority(8);

            bool result = await scheduler.UpdateTriggerDetails(trigger.Key, update);

            Assert.IsTrue(result);
            ITrigger retrieved = await scheduler.GetTrigger(trigger.Key);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("via extension method", retrieved.Description);
            Assert.AreEqual(8, retrieved.Priority);
            Assert.AreEqual(nextFireBefore, retrieved.GetNextFireTimeUtc());
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Test]
    public void ExtensionMethod_ThrowsForNonStdScheduler()
    {
        IScheduler fakeScheduler = A.Fake<IScheduler>();
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithDescription("test");

        Assert.ThrowsAsync<SchedulerException>(async () =>
            await fakeScheduler.UpdateTriggerDetails(new TriggerKey("t", "g"), update));
    }

    private sealed class TestSignaler : ISchedulerSignaler
    {
        public Task NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default)
        {
        }

        public Task NotifySchedulerListenersError(string message, SchedulerException jpe, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
