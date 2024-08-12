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

using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
///  Unit test for RAMJobStore.  These tests were submitted by Johannes Zillmann
/// as part of issue QUARTZ-306.
/// </summary>
public class RAMJobStoreTest
{
    private IJobStore fJobStore;
    private IJobDetail fJobDetail;
    private SampleSignaler fSignaler;

    [SetUp]
    public void SetUp()
    {
        fJobStore = new RAMJobStore();
        fSignaler = new SampleSignaler();
        fJobStore.Initialize(null, fSignaler);
        fJobStore.SchedulerStarted();

        fJobDetail = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(new JobKey("job1", "jobGroup1"))
            .StoreDurably(true)
            .Build();

        fJobStore.StoreJob(fJobDetail, false);
    }

    [Test]
    public async Task TestAcquireNextTrigger()
    {
        DateTimeOffset d = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(200), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(50), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger1", "triggerGroup2", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(100), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        trigger3.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(trigger1, false);
        await fJobStore.StoreTrigger(trigger2, false);
        await fJobStore.StoreTrigger(trigger3, false);

        DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc().Value;

        await Assert.MultipleAsync(async () =>
        {
            Assert.That((await fJobStore.AcquireNextTriggers(d.AddMilliseconds(10), 1, TimeSpan.Zero)), Is.Empty);
            Assert.That((await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).First(), Is.EqualTo(trigger2));
            Assert.That((await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).First(), Is.EqualTo(trigger3));
            Assert.That((await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).First(), Is.EqualTo(trigger1));
            Assert.That((await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)), Is.Empty);
        });

        // release trigger3
        await fJobStore.ReleaseAcquiredTrigger(trigger3);
        Assert.That((await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).First(), Is.EqualTo(trigger3));
    }

    [Test]
    public async Task TestAcquireNextTriggerBatch()
    {
        DateTimeOffset d = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromSeconds(1));

        IOperableTrigger early = new SimpleTriggerImpl("early", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d, d.AddMilliseconds(5), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddMilliseconds(200000), d.AddMilliseconds(200005), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddMilliseconds(210000), d.AddMilliseconds(210005), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger3", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddMilliseconds(220000), d.AddMilliseconds(220005), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger4 = new SimpleTriggerImpl("trigger4", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddMilliseconds(230000), d.AddMilliseconds(230005), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger10 = new SimpleTriggerImpl("trigger10", "triggerGroup2", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddMilliseconds(500000), d.AddMilliseconds(700000), 2, TimeSpan.FromSeconds(2));

        early.ComputeFirstFireTimeUtc(null);
        early.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        trigger3.ComputeFirstFireTimeUtc(null);
        trigger4.ComputeFirstFireTimeUtc(null);
        trigger10.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(early, false);
        await fJobStore.StoreTrigger(trigger1, false);
        await fJobStore.StoreTrigger(trigger2, false);
        await fJobStore.StoreTrigger(trigger3, false);
        await fJobStore.StoreTrigger(trigger4, false);
        await fJobStore.StoreTrigger(trigger10, false);

        DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc().Value;

        List<IOperableTrigger> acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 4, TimeSpan.FromSeconds(1))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(acquiredTriggers, Has.Count.EqualTo(1));
            Assert.That(acquiredTriggers[0].Key, Is.EqualTo(early.Key));
        });
        await fJobStore.ReleaseAcquiredTrigger(early);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 4, TimeSpan.FromMilliseconds(205000))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(acquiredTriggers, Has.Count.EqualTo(2));
            Assert.That(acquiredTriggers[0].Key, Is.EqualTo(early.Key));
            Assert.That(acquiredTriggers[1].Key, Is.EqualTo(trigger1.Key));
        });
        await fJobStore.ReleaseAcquiredTrigger(early);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);

        await fJobStore.RemoveTrigger(early.Key);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 5, TimeSpan.FromMilliseconds(100000))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(acquiredTriggers, Has.Count.EqualTo(4));
            Assert.That(acquiredTriggers[0].Key, Is.EqualTo(trigger1.Key));
            Assert.That(acquiredTriggers[1].Key, Is.EqualTo(trigger2.Key));
            Assert.That(acquiredTriggers[2].Key, Is.EqualTo(trigger3.Key));
            Assert.That(acquiredTriggers[3].Key, Is.EqualTo(trigger4.Key));
        });
        await fJobStore.ReleaseAcquiredTrigger(trigger1);
        await fJobStore.ReleaseAcquiredTrigger(trigger2);
        await fJobStore.ReleaseAcquiredTrigger(trigger3);
        await fJobStore.ReleaseAcquiredTrigger(trigger4);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 6, TimeSpan.FromMilliseconds(100000))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(acquiredTriggers, Has.Count.EqualTo(4));
            Assert.That(acquiredTriggers[0].Key, Is.EqualTo(trigger1.Key));
            Assert.That(acquiredTriggers[1].Key, Is.EqualTo(trigger2.Key));
            Assert.That(acquiredTriggers[2].Key, Is.EqualTo(trigger3.Key));
            Assert.That(acquiredTriggers[3].Key, Is.EqualTo(trigger4.Key));
        });

        await fJobStore.ReleaseAcquiredTrigger(trigger1);
        await fJobStore.ReleaseAcquiredTrigger(trigger2);
        await fJobStore.ReleaseAcquiredTrigger(trigger3);
        await fJobStore.ReleaseAcquiredTrigger(trigger4);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(1), 5, TimeSpan.Zero)).ToList();
        Assert.Multiple(() =>{
        Assert.That(acquiredTriggers, Has.Count.EqualTo(1));
        Assert.That(acquiredTriggers[0].Key, Is.EqualTo(trigger1.Key));
        });

        await fJobStore.ReleaseAcquiredTrigger(trigger1);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(250), 5, TimeSpan.FromMilliseconds(19999L))).ToList();
        Assert.Multiple(() =>{
        Assert.That(acquiredTriggers, Has.Count.EqualTo(2));
        Assert.That(acquiredTriggers[0].Key, Is.EqualTo(trigger1.Key));
        Assert.That(acquiredTriggers[1].Key, Is.EqualTo(trigger2.Key));
        });

        await fJobStore.ReleaseAcquiredTrigger(early);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);
        await fJobStore.ReleaseAcquiredTrigger(trigger2);
        await fJobStore.ReleaseAcquiredTrigger(trigger3);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(150), 5, TimeSpan.FromMilliseconds(5000L))).ToList();
        Assert.Multiple(() =>{
        Assert.That(acquiredTriggers, Has.Count.EqualTo(1));
        Assert.That(acquiredTriggers[0].Key, Is.EqualTo(trigger1.Key));
        });
        await fJobStore.ReleaseAcquiredTrigger(trigger1);
    }

    [Test]
    public async Task TestTriggerStates()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        trigger.ComputeFirstFireTimeUtc(null);
        Assert.That(await fJobStore.GetTriggerState(trigger.Key), Is.EqualTo(TriggerState.None));
        await fJobStore.StoreTrigger(trigger, false);
        Assert.That(await fJobStore.GetTriggerState(trigger.Key), Is.EqualTo(TriggerState.Normal));

        await fJobStore.PauseTrigger(trigger.Key);
        Assert.That(await fJobStore.GetTriggerState(trigger.Key), Is.EqualTo(TriggerState.Paused));

        await fJobStore.ResumeTrigger(trigger.Key);
        Assert.That(await fJobStore.GetTriggerState(trigger.Key), Is.EqualTo(TriggerState.Normal));

        trigger = (await fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).First();
        Assert.That(trigger, Is.Not.Null);
        await fJobStore.ReleaseAcquiredTrigger(trigger);
        trigger = (await fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).First();
        Assert.Multiple(async () =>
        {
            Assert.That(trigger, Is.Not.Null);
            Assert.That((await fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))), Is.Empty);
        });
    }

    [Test]
    public void TestRemoveCalendarWhenTriggersPresent()
    {
        // QRTZNET-29

        IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        trigger.ComputeFirstFireTimeUtc(null);
        ICalendar cal = new MonthlyCalendar();
        fJobStore.StoreTrigger(trigger, false);
        fJobStore.StoreCalendar("cal", cal, false, true);

        fJobStore.RemoveCalendar("cal");
    }

    [Test]
    public async Task TestStoreTriggerReplacesTrigger()
    {
        string jobName = "StoreTriggerReplacesTrigger";
        string jobGroup = "StoreTriggerReplacesTriggerGroup";
        var detail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey(jobName, jobGroup))
            .Build();
        await fJobStore.StoreJob(detail, false);

        string trName = "StoreTriggerReplacesTrigger";
        string trGroup = "StoreTriggerReplacesTriggerGroup";
        IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.Now);
        tr.JobKey = new JobKey(jobName, jobGroup);
        tr.CalendarName = null;

        await fJobStore.StoreTrigger(tr, false);
        Assert.That(await fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup)), Is.EqualTo(tr));

        tr.CalendarName = "NonExistingCalendar";
        await fJobStore.StoreTrigger(tr, true);
        Assert.That(await fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup)), Is.EqualTo(tr));
        var trigger = await fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup));
        Assert.That(trigger.CalendarName, Is.EqualTo(tr.CalendarName), "StoreJob doesn't replace triggers");

        bool exceptionRaised = false;
        try
        {
            await fJobStore.StoreTrigger(tr, false);
        }
        catch (ObjectAlreadyExistsException)
        {
            exceptionRaised = true;
        }
        Assert.That(exceptionRaised, Is.True, "an attempt to store duplicate trigger succeeded");
    }

    [Test]
    public async Task PauseJobGroupPausesNewJob()
    {
        string jobName1 = "PauseJobGroupPausesNewJob";
        string jobName2 = "PauseJobGroupPausesNewJob2";
        string jobGroup = "PauseJobGroupPausesNewJobGroup";

        var detail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey(jobName1, jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.StoreJob(detail, false);
        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

        detail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey(jobName2, jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.StoreJob(detail, false);

        string trName = "PauseJobGroupPausesNewJobTrigger";
        string trGroup = "PauseJobGroupPausesNewJobTriggerGroup";
        IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.UtcNow);
        tr.JobKey = new JobKey(jobName2, jobGroup);
        await fJobStore.StoreTrigger(tr, false);
        Assert.That(await fJobStore.GetTriggerState(tr.Key), Is.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task TestRetrieveJob_NoJobFound()
    {
        RAMJobStore store = new RAMJobStore();
        IJobDetail job = await store.RetrieveJob(new JobKey("not", "existing"));
        Assert.That(job, Is.Null);
    }

    [Test]
    public async Task TestRetrieveTrigger_NoTriggerFound()
    {
        RAMJobStore store = new RAMJobStore();
        IOperableTrigger trigger = await store.RetrieveTrigger(new TriggerKey("not", "existing"));
        Assert.That(trigger, Is.Null);
    }

    [Test]
    public async Task testStoreAndRetrieveJobs()
    {
        RAMJobStore store = new RAMJobStore();

        // Store jobs.
        for (int i = 0; i < 10; i++)
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            await store.StoreJob(job, false);
        }
        // Retrieve jobs.
        for (int i = 0; i < 10; i++)
        {
            JobKey jobKey = JobKey.Create("job" + i);
            IJobDetail storedJob = await store.RetrieveJob(jobKey);
            Assert.That(storedJob.Key, Is.EqualTo(jobKey));
        }
    }

    [Test]
    public async Task TestStoreAndRetrieveTriggers()
    {
        RAMJobStore store = new RAMJobStore();

        // Store jobs and triggers.
        for (int i = 0; i < 10; i++)
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            await store.StoreJob(job, true);
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create();
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).Build();
            await store.StoreTrigger((IOperableTrigger) trigger, true);
        }
        // Retrieve job and trigger.
        for (int i = 0; i < 10; i++)
        {
            JobKey jobKey = JobKey.Create("job" + i);
            IJobDetail storedJob = await store.RetrieveJob(jobKey);
            Assert.That(storedJob.Key, Is.EqualTo(jobKey));

            TriggerKey triggerKey = new TriggerKey("job" + i);
            ITrigger storedTrigger = await store.RetrieveTrigger(triggerKey);
            Assert.That(storedTrigger.Key, Is.EqualTo(triggerKey));
        }
    }

    [Test]
    public async Task TestAcquireTriggers()
    {
        ISchedulerSignaler schedSignaler = new SampleSignaler();
        ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
        loadHelper.Initialize();

        RAMJobStore store = new RAMJobStore();
        await store.Initialize(loadHelper, schedSignaler);

        // Setup: Store jobs and triggers.
        DateTime startTime0 = DateTime.UtcNow.AddMinutes(1).ToUniversalTime(); // a min from now.
        for (int i = 0; i < 10; i++)
        {
            DateTime startTime = startTime0.AddMinutes(i * 1); // a min apart
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.RepeatMinutelyForever(2);
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).StartAt(startTime).Build();

            // Manually trigger the first fire time computation that scheduler would do. Otherwise
            // the store.acquireNextTriggers() will not work properly.
            DateTimeOffset? fireTime = trigger.ComputeFirstFireTimeUtc(null);
            Assert.That(fireTime is not null, Is.EqualTo(true));

            await store.StoreJobAndTrigger(job, trigger);
        }

        // Test acquire one trigger at a time
        for (int i = 0; i < 10; i++)
        {
            DateTimeOffset noLaterThan = startTime0.AddMinutes(i);
            int maxCount = 1;
            TimeSpan timeWindow = TimeSpan.Zero;
            var triggers = await store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow);
            Assert.That(triggers, Has.Count.EqualTo(1));
            var trigger = triggers.First();
            Assert.That(trigger.Key.Name, Is.EqualTo("job" + i));

            // Let's remove the trigger now.
            await store.RemoveJob(trigger.JobKey);
        }
    }

    [Test]
    public async Task TestAcquireTriggersInBatch()
    {
        ISchedulerSignaler schedSignaler = new SampleSignaler();
        ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
        loadHelper.Initialize();

        RAMJobStore store = new RAMJobStore();
        await store.Initialize(loadHelper, schedSignaler);

        // Setup: Store jobs and triggers.
        DateTimeOffset startTime0 = DateTimeOffset.UtcNow.AddMinutes(1); // a min from now.
        for (int i = 0; i < 10; i++)
        {
            DateTimeOffset startTime = startTime0.AddMinutes(i); // a min apart
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.RepeatMinutelyForever(2);
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).StartAt(startTime).Build();

            // Manually trigger the first fire time computation that scheduler would do. Otherwise
            // the store.acquireNextTriggers() will not work properly.
            DateTimeOffset? fireTime = trigger.ComputeFirstFireTimeUtc(null);
            Assert.That(fireTime is not null, Is.EqualTo(true));

            await store.StoreJobAndTrigger(job, trigger);
        }

        // Test acquire batch of triggers at a time
        DateTimeOffset noLaterThan = startTime0.AddMinutes(10);
        int maxCount = 7;
        TimeSpan timeWindow = TimeSpan.FromMinutes(8);
        var triggers = (await store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow)).ToList();
        Assert.That(triggers, Has.Count.EqualTo(7));
        for (int i = 0; i < 7; i++)
        {
            Assert.That(triggers[i].Key.Name, Is.EqualTo("job" + i));
        }
    }

    [Test]
    public async Task TestResetErrorTrigger()
    {
        var baseFireTimeDate = DateBuilder.EvenMinuteDateAfterNow();

        // create and store a trigger
        IOperableTrigger trigger1 = new SimpleTriggerImpl(
            "trigger1",
            "triggerGroup1",
            fJobDetail.Key.Name,
            fJobDetail.Key.Group,
            baseFireTimeDate.AddMilliseconds(200000),
            baseFireTimeDate.AddMilliseconds(200000),
            2,
            TimeSpan.FromMilliseconds(2000));

        trigger1.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(trigger1, false);

        var firstFireTime = trigger1.GetNextFireTimeUtc().Value;

        // pretend to fire it
        var aqTs = await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(10000), 1, TimeSpan.Zero);
        Assert.That(aqTs.First().Key, Is.EqualTo(trigger1.Key));

        var fTs = await fJobStore.TriggersFired(aqTs);
        var ft = fTs.First();

        // get the trigger into error state
        await fJobStore.TriggeredJobComplete(ft.TriggerFiredBundle.Trigger, ft.TriggerFiredBundle.JobDetail, SchedulerInstruction.SetTriggerError);

        var state = await fJobStore.GetTriggerState(trigger1.Key);
        Assert.That(state, Is.EqualTo(TriggerState.Error));

        // test reset
        await fJobStore.ResetTriggerFromErrorState(trigger1.Key);
        state = await fJobStore.GetTriggerState(trigger1.Key);
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
    }

    [Test]
    public async Task TestJobDeleteReturnValue()
    {
        var job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("job0")
            .StoreDurably()
            .Build();

        var store = new RAMJobStore();
        await store.StoreJob(job, false);

        var deleteSuccess = await store.RemoveJob(new JobKey("job0"));
        Assert.That(deleteSuccess, Is.True, "Expected RemoveJob to return True when deleting an existing job");

        deleteSuccess = await store.RemoveJob(new JobKey("job0"));
        Assert.That(deleteSuccess, Is.False, "Expected RemoveJob to return False when deleting an non-existing job");
    }

    public class SampleSignaler : ISchedulerSignaler
    {
        internal int fMisfireCount;

        public ValueTask NotifyTriggerListenersMisfired(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            fMisfireCount++;
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask SignalSchedulingChange(
            DateTimeOffset? candidateNewNextFireTimeUtc,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask NotifySchedulerListenersError(
            string message,
            SchedulerException jpe,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask NotifySchedulerListenersJobDeleted(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}