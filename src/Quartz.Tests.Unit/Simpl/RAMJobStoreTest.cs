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

using Microsoft.Extensions.Time.Testing;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
///  Unit test for RAMJobStore.  These tests were submitted by Johannes Zillmann
/// as part of issue QUARTZ-306.
/// </summary>
[NonParallelizable]
public class RAMJobStoreTest
{
    private IJobStore fJobStore;
    private IJobDetail fJobDetail;
    private SampleSignaler fSignaler;

    [SetUp]
    public void SetUp()
    {
        fJobStore = TestJobStores.Ram();
        fSignaler = new SampleSignaler();
        fJobStore.Initialize();
        fJobStore.SchedulerStarted();

        fJobDetail = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(new JobKey("job1", "jobGroup1"))
            .StoreDurably(true)
            .Build();

        fJobStore.AddJob(fJobDetail, false);
    }

    [Test]
    public async Task TestAcquireNextTrigger()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(200), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(50), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger1", "triggerGroup2", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(100), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        trigger3.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(trigger1, false);
        await fJobStore.AddTrigger(trigger2, false);
        await fJobStore.AddTrigger(trigger3, false);

        DateTimeOffset firstFireTime = trigger1.NextFireTimeUtc.Value;

        await Assert.MultipleAsync(async () =>
        {
            Assert.That((await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddMilliseconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero })), Is.Empty);
            Assert.That((await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero })).First(), Is.EqualTo(trigger2));
            Assert.That((await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero })).First(), Is.EqualTo(trigger3));
            Assert.That((await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero })).First(), Is.EqualTo(trigger1));
            Assert.That((await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero })), Is.Empty);
        });

        // release trigger3
        await fJobStore.ReleaseAcquiredTrigger(trigger3);
        Assert.That((await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.FromMilliseconds(1) })).First(), Is.EqualTo(trigger3));
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
        early.MisfireInstructionCode = MisfireInstruction.IgnoreMisfirePolicy;

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        trigger3.ComputeFirstFireTimeUtc(null);
        trigger4.ComputeFirstFireTimeUtc(null);
        trigger10.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(early, false);
        await fJobStore.AddTrigger(trigger1, false);
        await fJobStore.AddTrigger(trigger2, false);
        await fJobStore.AddTrigger(trigger3, false);
        await fJobStore.AddTrigger(trigger4, false);
        await fJobStore.AddTrigger(trigger10, false);

        DateTimeOffset firstFireTime = trigger1.NextFireTimeUtc.Value;

        List<IOperableTrigger> acquiredTriggers = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 4, TimeWindow = TimeSpan.FromSeconds(1) })).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(acquiredTriggers, Has.Count.EqualTo(1));
            Assert.That(acquiredTriggers[0].Key, Is.EqualTo(early.Key));
        });
        await fJobStore.ReleaseAcquiredTrigger(early);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 4, TimeWindow = TimeSpan.FromMilliseconds(205000) })).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(acquiredTriggers, Has.Count.EqualTo(2));
            Assert.That(acquiredTriggers[0].Key, Is.EqualTo(early.Key));
            Assert.That(acquiredTriggers[1].Key, Is.EqualTo(trigger1.Key));
        });
        await fJobStore.ReleaseAcquiredTrigger(early);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);

        await fJobStore.DeleteTrigger(early.Key);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 5, TimeWindow = TimeSpan.FromMilliseconds(100000) })).ToList();
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

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 6, TimeWindow = TimeSpan.FromMilliseconds(100000) })).ToList();
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

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddMilliseconds(1), MaxCount = 5, TimeWindow = TimeSpan.Zero })).ToList();
        Assert.Multiple(() =>{
        Assert.That(acquiredTriggers, Has.Count.EqualTo(1));
        Assert.That(acquiredTriggers[0].Key, Is.EqualTo(trigger1.Key));
        });

        await fJobStore.ReleaseAcquiredTrigger(trigger1);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddMilliseconds(250), MaxCount = 5, TimeWindow = TimeSpan.FromMilliseconds(19999L) })).ToList();
        Assert.Multiple(() =>{
        Assert.That(acquiredTriggers, Has.Count.EqualTo(2));
        Assert.That(acquiredTriggers[0].Key, Is.EqualTo(trigger1.Key));
        Assert.That(acquiredTriggers[1].Key, Is.EqualTo(trigger2.Key));
        });

        await fJobStore.ReleaseAcquiredTrigger(early);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);
        await fJobStore.ReleaseAcquiredTrigger(trigger2);
        await fJobStore.ReleaseAcquiredTrigger(trigger3);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddMilliseconds(150), MaxCount = 5, TimeWindow = TimeSpan.FromMilliseconds(5000L) })).ToList();
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
        await fJobStore.AddTrigger(trigger, false);
        Assert.That(await fJobStore.GetTriggerState(trigger.Key), Is.EqualTo(TriggerState.Normal));

        await fJobStore.PauseTrigger(trigger.Key);
        Assert.That(await fJobStore.GetTriggerState(trigger.Key), Is.EqualTo(TriggerState.Paused));

        await fJobStore.ResumeTrigger(trigger.Key);
        Assert.That(await fJobStore.GetTriggerState(trigger.Key), Is.EqualTo(TriggerState.Normal));

        trigger = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = trigger.NextFireTimeUtc.Value.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.FromMilliseconds(1) })).First();
        Assert.That(trigger, Is.Not.Null);
        await fJobStore.ReleaseAcquiredTrigger(trigger);
        trigger = (await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = trigger.NextFireTimeUtc.Value.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.FromMilliseconds(1) })).First();
        Assert.Multiple(async () =>
        {
            Assert.That(trigger, Is.Not.Null);
            Assert.That((await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = trigger.NextFireTimeUtc.Value.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.FromMilliseconds(1) })), Is.Empty);
        });
    }

    [Test]
    public void TestRemoveCalendarWhenTriggersPresent()
    {
        // QRTZNET-29

        IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        trigger.ComputeFirstFireTimeUtc(null);
        ICalendar calendar = new MonthlyCalendar();
        fJobStore.AddTrigger(trigger, false);
        fJobStore.AddCalendar("cal", calendar, new AddCalendarOptions { UpdateTriggers = true });

        fJobStore.DeleteCalendar("cal");
    }

    [Test]
    public async Task TestStoreTriggerReplacesTrigger()
    {
        string jobName = "StoreTriggerReplacesTrigger";
        string jobGroup = "StoreTriggerReplacesTriggerGroup";
        var detail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey(jobName, jobGroup))
            .Build();
        await fJobStore.AddJob(detail, false);

        string trName = "StoreTriggerReplacesTrigger";
        string trGroup = "StoreTriggerReplacesTriggerGroup";
        IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.Now);
        tr.JobKey = new JobKey(jobName, jobGroup);
        tr.CalendarName = null;

        await fJobStore.AddTrigger(tr, false);
        Assert.That(await fJobStore.GetTrigger(new TriggerKey(trName, trGroup)), Is.EqualTo(tr));

        tr.CalendarName = "NonExistingCalendar";
        await fJobStore.AddTrigger(tr, true);
        Assert.That(await fJobStore.GetTrigger(new TriggerKey(trName, trGroup)), Is.EqualTo(tr));
        var trigger = await fJobStore.GetTrigger(new TriggerKey(trName, trGroup));
        Assert.That(trigger.CalendarName, Is.EqualTo(tr.CalendarName), "AddJob doesn't replace triggers");

        bool exceptionRaised = false;
        try
        {
            await fJobStore.AddTrigger(tr, false);
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
        await fJobStore.AddJob(detail, false);
        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

        detail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey(jobName2, jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(detail, false);

        string trName = "PauseJobGroupPausesNewJobTrigger";
        string trGroup = "PauseJobGroupPausesNewJobTriggerGroup";
        IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.UtcNow);
        tr.JobKey = new JobKey(jobName2, jobGroup);
        await fJobStore.AddTrigger(tr, false);
        Assert.That(await fJobStore.GetTriggerState(tr.Key), Is.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task ResumeJob_WhenGroupPaused_NewTriggerShouldNotBePaused()
    {
        string jobGroup = "ResumeJobGroupTest";
        var job = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("job1", jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(job, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));
        await fJobStore.ResumeJob(job.Key);

        IOperableTrigger tr = new SimpleTriggerImpl("newTrigger", "triggerGroup", DateTimeOffset.UtcNow);
        tr.JobKey = job.Key;
        await fJobStore.AddTrigger(tr, false);

        Assert.That(await fJobStore.GetTriggerState(tr.Key), Is.EqualTo(TriggerState.Normal));
    }

    [Test]
    public async Task ResumeJob_WhenGroupPaused_OtherJobsStillPaused()
    {
        string jobGroup = "ResumeJobGroupTest2";
        var job1 = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("job1", jobGroup))
            .StoreDurably(true)
            .Build();
        var job2 = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("job2", jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(job1, false);
        await fJobStore.AddJob(job2, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));
        await fJobStore.ResumeJob(job1.Key);

        IOperableTrigger tr1 = new SimpleTriggerImpl("trigger1", "triggerGroup", DateTimeOffset.UtcNow);
        tr1.JobKey = job1.Key;
        await fJobStore.AddTrigger(tr1, false);

        IOperableTrigger tr2 = new SimpleTriggerImpl("trigger2", "triggerGroup", DateTimeOffset.UtcNow);
        tr2.JobKey = job2.Key;
        await fJobStore.AddTrigger(tr2, false);

        Assert.That(await fJobStore.GetTriggerState(tr1.Key), Is.EqualTo(TriggerState.Normal));
        Assert.That(await fJobStore.GetTriggerState(tr2.Key), Is.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task ResumeJob_ThenRePauseGroup_ExemptionCleared()
    {
        string jobGroup = "ResumeJobGroupTest3";
        var job = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("job1", jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(job, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));
        await fJobStore.ResumeJob(job.Key);
        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

        IOperableTrigger tr = new SimpleTriggerImpl("newTrigger", "triggerGroup", DateTimeOffset.UtcNow);
        tr.JobKey = job.Key;
        await fJobStore.AddTrigger(tr, false);

        Assert.That(await fJobStore.GetTriggerState(tr.Key), Is.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task ResumeJob_NonexistentJob_DoesNotCreateExemption()
    {
        string jobGroup = "ResumeJobGroupTest4";
        var job = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("job1", jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(job, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

        // resume a nonexistent job — should not create an exemption for that key
        await fJobStore.ResumeJob(new JobKey("nonexistent", jobGroup));

        // now store the previously-nonexistent job and a trigger for it
        var laterJob = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("nonexistent", jobGroup))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(laterJob, false);

        IOperableTrigger tr = new SimpleTriggerImpl("newTrigger", "triggerGroup", DateTimeOffset.UtcNow);
        tr.JobKey = laterJob.Key;
        await fJobStore.AddTrigger(tr, false);

        Assert.That(await fJobStore.GetTriggerState(tr.Key), Is.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task TestRetrieveJob_NoJobFound()
    {
        RAMJobStore store = TestJobStores.Ram();
        IJobDetail job = await store.GetJob(new JobKey("not", "existing"));
        Assert.That(job, Is.Null);
    }

    [Test]
    public async Task TestRetrieveTrigger_NoTriggerFound()
    {
        RAMJobStore store = TestJobStores.Ram();
        IOperableTrigger trigger = await store.GetTrigger(new TriggerKey("not", "existing"));
        Assert.That(trigger, Is.Null);
    }

    [Test]
    public async Task testStoreAndRetrieveJobs()
    {
        RAMJobStore store = TestJobStores.Ram();

        // Store jobs.
        for (int i = 0; i < 10; i++)
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            await store.AddJob(job, false);
        }
        // Retrieve jobs.
        for (int i = 0; i < 10; i++)
        {
            JobKey jobKey = new JobKey("job" + i);
            IJobDetail storedJob = await store.GetJob(jobKey);
            Assert.That(storedJob.Key, Is.EqualTo(jobKey));
        }
    }

    [Test]
    public async Task TestStoreAndRetrieveTriggers()
    {
        RAMJobStore store = TestJobStores.Ram();

        // Store jobs and triggers.
        for (int i = 0; i < 10; i++)
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            await store.AddJob(job, true);
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create();
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).Build();
            await store.AddTrigger((IOperableTrigger) trigger, true);
        }
        // Retrieve job and trigger.
        for (int i = 0; i < 10; i++)
        {
            JobKey jobKey = new JobKey("job" + i);
            IJobDetail storedJob = await store.GetJob(jobKey);
            Assert.That(storedJob.Key, Is.EqualTo(jobKey));

            TriggerKey triggerKey = new TriggerKey("job" + i);
            ITrigger storedTrigger = await store.GetTrigger(triggerKey);
            Assert.That(storedTrigger.Key, Is.EqualTo(triggerKey));
        }
    }

    [Test]
    public async Task TestAcquireTriggers()
    {
        ISchedulerSignaler schedSignaler = new SampleSignaler();
        ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();

        RAMJobStore store = TestJobStores.Ram();
        await store.Initialize();

        // Setup: Store jobs and triggers.
        DateTime startTime0 = DateTime.UtcNow.AddMinutes(1).ToUniversalTime(); // a min from now.
        for (int i = 0; i < 10; i++)
        {
            DateTime startTime = startTime0.AddMinutes(i * 1); // a min apart
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(2)).RepeatForever();
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).StartAt(startTime).Build();

            // Manually trigger the first fire time computation that scheduler would do. Otherwise
            // the store.acquireNextTriggers() will not work properly.
            DateTimeOffset? fireTime = trigger.ComputeFirstFireTimeUtc(null);
            Assert.That(fireTime is not null, Is.EqualTo(true));

            await store.ScheduleJob(job, trigger);
        }

        // Test acquire one trigger at a time
        for (int i = 0; i < 10; i++)
        {
            DateTimeOffset noLaterThan = startTime0.AddMinutes(i);
            int maxCount = 1;
            TimeSpan timeWindow = TimeSpan.Zero;
            var triggers = await store.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = noLaterThan, MaxCount = maxCount, TimeWindow = timeWindow });
            Assert.That(triggers, Has.Count.EqualTo(1));
            var trigger = triggers.First();
            Assert.That(trigger.Key.Name, Is.EqualTo("job" + i));

            // Let's remove the trigger now.
            await store.DeleteJob(trigger.JobKey);
        }
    }

    [Test]
    public async Task TestAcquireTriggersInBatch()
    {
        ISchedulerSignaler schedSignaler = new SampleSignaler();
        ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();

        RAMJobStore store = TestJobStores.Ram();
        await store.Initialize();

        // Setup: Store jobs and triggers.
        DateTimeOffset startTime0 = DateTimeOffset.UtcNow.AddMinutes(1); // a min from now.
        for (int i = 0; i < 10; i++)
        {
            DateTimeOffset startTime = startTime0.AddMinutes(i); // a min apart
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(2)).RepeatForever();
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).StartAt(startTime).Build();

            // Manually trigger the first fire time computation that scheduler would do. Otherwise
            // the store.acquireNextTriggers() will not work properly.
            DateTimeOffset? fireTime = trigger.ComputeFirstFireTimeUtc(null);
            Assert.That(fireTime is not null, Is.EqualTo(true));

            await store.ScheduleJob(job, trigger);
        }

        // Test acquire batch of triggers at a time
        DateTimeOffset noLaterThan = startTime0.AddMinutes(10);
        int maxCount = 7;
        TimeSpan timeWindow = TimeSpan.FromMinutes(8);
        var triggers = (await store.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = noLaterThan, MaxCount = maxCount, TimeWindow = timeWindow })).ToList();
        Assert.That(triggers, Has.Count.EqualTo(7));
        for (int i = 0; i < 7; i++)
        {
            Assert.That(triggers[i].Key.Name, Is.EqualTo("job" + i));
        }
    }

    [Test]
    public async Task TestResetErrorTrigger()
    {
        var baseFireTimeDate = TestDates.EvenMinuteDateAfterNow();

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
        await fJobStore.AddTrigger(trigger1, false);

        var firstFireTime = trigger1.NextFireTimeUtc.Value;

        // pretend to fire it
        var aqTs = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddMilliseconds(10000), MaxCount = 1, TimeWindow = TimeSpan.Zero });
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

        var store = TestJobStores.Ram();
        await store.AddJob(job, false);

        var deleteSuccess = await store.DeleteJob(new JobKey("job0"));
        Assert.That(deleteSuccess, Is.True, "Expected DeleteJob to return True when deleting an existing job");

        deleteSuccess = await store.DeleteJob(new JobKey("job0"));
        Assert.That(deleteSuccess, Is.False, "Expected DeleteJob to return False when deleting an non-existing job");
    }

    [Test]
    public async Task TestTriggeredJobComplete_UnblocksTriggersForDisallowConcurrentExecutionJob()
    {
        // Store a DisallowConcurrentExecution job with two triggers
        var job = JobBuilder.Create<DisallowConcurrentNoOpJob>()
            .WithIdentity(new JobKey("blockedJob", "group1"))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(job, true);

        var d = TestDates.EvenMinuteDateAfterNow();
        var trigger1 = new SimpleTriggerImpl("trigger1", "group1", job.Key.Name, job.Key.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));
        var trigger2 = new SimpleTriggerImpl("trigger2", "group1", job.Key.Name, job.Key.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(trigger1, false);
        await fJobStore.AddTrigger(trigger2, false);

        // Acquire and fire one trigger
        var acquiredTriggers = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        Assert.That(acquiredTriggers, Has.Count.EqualTo(1));

        var firedResults = await fJobStore.TriggersFired(acquiredTriggers);
        Assert.That(firedResults, Has.Count.EqualTo(1));

        // The trigger that fired is running the job, so it reports Executing. Its sibling is merely
        // gated behind the DisallowConcurrentExecution job, which is what Blocked means.
        var firedResult = firedResults.First();
        TriggerKey firedKey = firedResult.TriggerFiredBundle.Trigger.Key;
        TriggerKey siblingKey = firedKey.Equals(trigger1.Key) ? trigger2.Key : trigger1.Key;

        (await fJobStore.GetTriggerState(firedKey)).Should().Be(TriggerState.Executing);
        (await fJobStore.GetTriggerState(siblingKey)).Should().Be(TriggerState.Blocked);

        // Simulate job completion with NoInstruction (graceful shutdown scenario)
        await fJobStore.TriggeredJobComplete(
            firedResult.TriggerFiredBundle.Trigger,
            firedResult.TriggerFiredBundle.JobDetail,
            SchedulerInstruction.NoInstruction);

        // Both triggers should be unblocked (Normal = Waiting)
        (await fJobStore.GetTriggerState(trigger1.Key)).Should().Be(TriggerState.Normal);
        (await fJobStore.GetTriggerState(trigger2.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task TestReleaseAcquiredTrigger_DoesNotUnblockOtherTriggersForDisallowConcurrentExecutionJob()
    {
        // This test documents the reason we must use TriggeredJobComplete
        // (not ReleaseAcquiredTrigger) after TriggersFired for DisallowConcurrentExecution jobs

        var job = JobBuilder.Create<DisallowConcurrentNoOpJob>()
            .WithIdentity(new JobKey("blockedJob", "group1"))
            .StoreDurably(true)
            .Build();
        await fJobStore.AddJob(job, true);

        var d = TestDates.EvenMinuteDateAfterNow();
        var trigger1 = new SimpleTriggerImpl("trigger1", "group1", job.Key.Name, job.Key.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));
        var trigger2 = new SimpleTriggerImpl("trigger2", "group1", job.Key.Name, job.Key.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(trigger1, false);
        await fJobStore.AddTrigger(trigger2, false);

        // Acquire and fire one trigger
        var acquiredTriggers = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        Assert.That(acquiredTriggers, Has.Count.EqualTo(1));
        var firedTrigger = acquiredTriggers.First();

        var firedResults = await fJobStore.TriggersFired(acquiredTriggers);
        Assert.That(firedResults, Has.Count.EqualTo(1));

        // The trigger that fired is running the job; its sibling is gated behind it.
        TriggerKey siblingKey = firedTrigger.Key.Equals(trigger1.Key) ? trigger2.Key : trigger1.Key;

        (await fJobStore.GetTriggerState(firedTrigger.Key)).Should().Be(TriggerState.Executing);
        (await fJobStore.GetTriggerState(siblingKey)).Should().Be(TriggerState.Blocked);

        // ReleaseAcquiredTrigger only handles the specific trigger's Acquired state,
        // it does NOT unblock other triggers since it doesn't know about job concurrency
        await fJobStore.ReleaseAcquiredTrigger(firedTrigger);

        // Releasing means the fire is not going to run after all, so the execution is dropped and the
        // trigger is no longer executing - but it stays blocked, along with its sibling, because
        // ReleaseAcquiredTrigger knows nothing about job concurrency. That is the bug scenario this
        // documents: only TriggeredJobComplete unblocks the job's triggers.
        (await fJobStore.GetTriggerState(firedTrigger.Key)).Should().Be(TriggerState.Blocked,
            "releasing drops the execution but does not unblock the job");
        (await fJobStore.GetTriggerState(siblingKey)).Should().Be(TriggerState.Blocked,
            "ReleaseAcquiredTrigger should not unblock all triggers for DisallowConcurrentExecution jobs");
    }

    /// <summary>
    /// Builds a repeating trigger for the concurrency-allowed job the fixture stores.
    /// </summary>
    private static SimpleTriggerImpl ExecutingTestTrigger(string name, DateTimeOffset d)
    {
        var trigger = new SimpleTriggerImpl(name, "triggerGroup1", "job1", "jobGroup1",
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    [Test]
    public async Task GetTriggerState_ReturnsExecuting_WhileConcurrencyAllowedJobRuns()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("executingTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        acquired.Should().HaveCount(1);

        // Nothing is running yet, so an acquired trigger still reads as normal.
        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);

        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Executing);

        var bundle = fired[0].TriggerFiredBundle;
        await fJobStore.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task GetTriggerState_ReturnsExecuting_UntilLastOfSeveralConcurrentFiresCompletes()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("concurrentTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        // The job allows concurrent execution, so the trigger is re-armed as soon as it fires and can be
        // acquired again while the first execution is still in flight.
        var firstAcquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        firstAcquired.Should().HaveCount(1);
        var firstFired = await fJobStore.TriggersFired(firstAcquired);
        firstFired.Should().HaveCount(1);

        var secondAcquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        secondAcquired.Should().HaveCount(1);
        var secondFired = await fJobStore.TriggersFired(secondAcquired);
        secondFired.Should().HaveCount(1);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Executing);

        var firstBundle = firstFired[0].TriggerFiredBundle;
        await fJobStore.TriggeredJobComplete(firstBundle.Trigger, firstBundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Executing,
            "one of the two executions is still running");

        var secondBundle = secondFired[0].TriggerFiredBundle;
        await fJobStore.TriggeredJobComplete(secondBundle.Trigger, secondBundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task GetTriggerState_ReturnsPaused_WhenTriggerPausedWhileJobExecuting()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("pausedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);

        await fJobStore.PauseTrigger(trigger.Key);

        // The pause is the actionable fact, so it outranks the execution still in flight.
        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);

        var bundle = fired[0].TriggerFiredBundle;
        await fJobStore.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);
    }

    [Test]
    public async Task GetTriggerState_ReturnsNone_WhenTriggerRemovedWhileExecuting()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("removedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);

        (await fJobStore.DeleteTrigger(trigger.Key)).Should().BeTrue();
        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.None);

        // The completion arrives for a trigger that no longer exists and must not throw.
        var bundle = fired[0].TriggerFiredBundle;
        await fJobStore.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        // Once the execution has been accounted for, a trigger re-stored under the same key is idle.
        var replacement = ExecutingTestTrigger("removedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(replacement, false);

        (await fJobStore.GetTriggerState(replacement.Key)).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// The other half of the rule the reschedule test pins: deleting a trigger takes its executions with
    /// it, so a trigger later created under the same key is a different trigger and starts idle. Asserted
    /// while the original execution is still in flight — after its completion the entry would have been
    /// released anyway, and the test would pass whether or not removal forgets it.
    /// </summary>
    [Test]
    public async Task GetTriggerState_ForgetsExecutions_WhenTriggerIsRemovedAndRecreatedMidExecution()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("recreatedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);

        (await fJobStore.DeleteTrigger(trigger.Key)).Should().BeTrue();

        var replacement = ExecutingTestTrigger("recreatedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(replacement, false);

        (await fJobStore.GetTriggerState(replacement.Key)).Should().Be(TriggerState.Normal,
            "the new trigger never fired, so it cannot inherit the deleted trigger's execution");

        // The orphaned completion still arrives and must not disturb the new trigger.
        var bundle = fired[0].TriggerFiredBundle;
        await fJobStore.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await fJobStore.GetTriggerState(replacement.Key)).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// ReplaceTrigger deletes rather than updates, so unlike AddTrigger(replace: true) it does
    /// not carry the executions over — matching the ADO store, where ReplaceTrigger removes the
    /// fired-trigger rows and an in-place update leaves them.
    /// </summary>
    [Test]
    public async Task GetTriggerState_ForgetsExecutions_WhenTriggerIsReplacedMidExecution()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("replacedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);

        var replacement = ExecutingTestTrigger("replacedWhileExecutingTrigger", d);
        (await fJobStore.ReplaceTrigger(trigger.Key, replacement)).Should().BeTrue();

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
            "the replaced trigger was deleted, so its execution went with it");
    }

    /// <summary>
    /// Clearing the store forgets everything, executions included.
    /// </summary>
    [Test]
    public async Task GetTriggerState_ForgetsExecutions_WhenSchedulingDataIsClearedMidExecution()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("clearedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        (await fJobStore.TriggersFired(acquired)).Should().HaveCount(1);

        await fJobStore.Clear();

        await fJobStore.AddJob(fJobDetail, true);
        var replacement = ExecutingTestTrigger("clearedWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(replacement, false);

        (await fJobStore.GetTriggerState(replacement.Key)).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// A rejected replacement must leave the store exactly as it was, rather than half-deleting the
    /// trigger it refused to replace.
    /// </summary>
    [Test]
    public async Task ReplaceTrigger_LeavesTriggerIntact_WhenReplacementNamesADifferentJob()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("mismatchedReplacementTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var otherJob = JobBuilder.Create<NoOpJob>().WithIdentity("otherJob", "jobGroup1").StoreDurably().Build();
        await fJobStore.AddJob(otherJob, true);

        var replacement = new SimpleTriggerImpl("mismatchedReplacementTrigger", "triggerGroup1",
            otherJob.Key.Name, otherJob.Key.Group, d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));
        replacement.ComputeFirstFireTimeUtc(null);

        Func<Task> act = async () => await fJobStore.ReplaceTrigger(trigger.Key, replacement);
        await act.Should().ThrowAsync<JobPersistenceException>();

        (await fJobStore.Exists(trigger.Key)).Should().BeTrue("a rejected replacement must not remove the trigger");
        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
        (await fJobStore.GetTrigger(trigger.Key)).Should().NotBeNull();
        (await fJobStore.DeleteTrigger(trigger.Key)).Should().BeTrue("the trigger must still be removable");
    }

    [Test]
    public async Task GetTriggerState_KeepsReportingExecuting_WhenTriggerIsRescheduledMidExecution()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("rescheduledWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);

        // Replacing the trigger builds a new wrapper; the execution it already started is unaffected and
        // has to stay visible, which is also what the ADO store reports since its fired-trigger row
        // survives the update.
        var replacement = ExecutingTestTrigger("rescheduledWhileExecutingTrigger", d);
        await fJobStore.AddTrigger(replacement, replace: true);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Executing);

        var bundle = fired[0].TriggerFiredBundle;
        await fJobStore.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// A fire that bails out records nothing, so the trigger must not be left looking like it is running
    /// with no completion coming to clear it.
    /// </summary>
    [Test]
    public async Task GetTriggerState_RecordsNoExecution_WhenFiringBailsOut()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("missingCalendarTrigger", d);
        trigger.CalendarName = "noSuchCalendar";
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        acquired.Should().HaveCount(1);

        // The calendar the trigger names does not exist, so no bundle is produced.
        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);
        fired[0].TriggerFiredBundle.Should().BeNull();

        (await fJobStore.GetTriggerState(trigger.Key)).Should().NotBe(TriggerState.Executing,
            "nothing started, so nothing may be recorded as running");
    }

    /// <summary>
    /// The other side of that coin, and a regression test for #3294: the dashboard's reschedule
    /// wrote CALENDAR_NAME='' instead of NULL. The store gates its calendar lookup on a non-null
    /// name, so the empty string passed the gate, resolved to no calendar, and the trigger silently
    /// never fired again.
    /// </summary>
    [Test]
    public async Task TriggersFired_StillProducesABundle_WhenTheCalendarNameIsBlank()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("blankCalendarTrigger", d);
        trigger.CalendarName = "";
        await fJobStore.AddTrigger(trigger, false);

        (await fJobStore.GetTrigger(trigger.Key))!.CalendarName.Should().BeNull(
            "a blank name is stored as no calendar, so nothing is ever looked up for it");

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        acquired.Should().HaveCount(1);

        var fired = await fJobStore.TriggersFired(acquired);

        fired.Should().HaveCount(1);
        fired[0].TriggerFiredBundle.Should().NotBeNull(
            "a trigger with no calendar has to fire; naming the empty string is naming no calendar");

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Executing);
    }

    /// <summary>
    /// Releasing after the fire was recorded has to drop the record: the scheduler releases the whole
    /// batch when <c>TriggersFired</c> fails part-way, and no completion will arrive for the fires it had
    /// already recorded. Uses a concurrency-allowed job so the answer is not masked by the blocking
    /// fan-out.
    /// </summary>
    [Test]
    public async Task GetTriggerState_DropsExecution_WhenTriggerIsReleasedAfterFiring()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("releasedAfterFiringTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        var fired = await fJobStore.TriggersFired(acquired);
        fired.Should().HaveCount(1);
        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Executing);

        await fJobStore.ReleaseAcquiredTrigger(fired[0].TriggerFiredBundle.Trigger);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
            "releasing means the fire is not going to run, so nothing may still be recorded for it");
    }

    [Test]
    public async Task GetTriggerState_ReturnsNormal_WhenAcquiredTriggerReleasedWithoutFiring()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();
        var trigger = ExecutingTestTrigger("releasedTrigger", d);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = d.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        acquired.Should().HaveCount(1);

        // Released before it ever fired, so no execution was ever counted.
        await fJobStore.ReleaseAcquiredTrigger(acquired[0]);

        (await fJobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task TestScheduledFireTimeUtc_CronTrigger_WithMisfire_ReturnsOriginalScheduledTime()
    {
        var now = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var originalScheduledTime = new DateTimeOffset(2025, 6, 15, 10, 29, 0, TimeSpan.Zero);
        var previousFireTime = new DateTimeOffset(2025, 6, 15, 10, 28, 0, TimeSpan.Zero);

        var fakeTime = new FakeTimeProvider(now);
        var store = TestJobStores.Ram(timeProvider: fakeTime);
        store.MisfireThreshold = TimeSpan.FromSeconds(5);
        var signaler = new SampleSignaler();
        await store.Initialize();
        await store.SchedulerStarted();

        var job = JobBuilder.Create().OfType<NoOpJob>()
            .WithIdentity("testJob", "testGroup").StoreDurably(true).Build();
        await store.AddJob(job, false);

        var trigger = new CronTriggerImpl("testTrigger", "testGroup", "0 * * * * ?", fakeTime)
        {
            JobKey = job.Key,
            MisfireInstructionCode = MisfireInstruction.CronTrigger.FireOnceNow
        };
        trigger.PreviousFireTimeUtc = previousFireTime;
        trigger.NextFireTimeUtc = originalScheduledTime;
        await store.AddTrigger(trigger, false);

        var acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = now.AddMinutes(1), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        Assert.That(acquired, Has.Count.EqualTo(1));

        var firedResults = await store.TriggersFired(acquired);
        Assert.That(firedResults, Has.Count.EqualTo(1));

        var bundle = firedResults[0].TriggerFiredBundle;
        Assert.That(bundle, Is.Not.Null);
        Assert.That(bundle!.ScheduledFireTimeUtc, Is.EqualTo(originalScheduledTime),
            "ScheduledFireTimeUtc should reflect the original scheduled time, not the misfire-adjusted time");
        Assert.That(bundle.PreviousFireTimeUtc, Is.EqualTo(previousFireTime));
    }

    [Test]
    public async Task RescheduleNextWithExistingCount_PastStartTime_DoesNotFireImmediately()
    {
        // Regression test for #3096: a trigger with a start time in the past and the
        // RescheduleNextWithExistingCount misfire policy must not fire immediately on
        // scheduler start, even when 'now' is just after one of the scheduled fire
        // times (within the misfire threshold window). Misfire handling must
        // reschedule to the next scheduled time strictly after 'now'.
        var startTime = new DateTimeOffset(2025, 6, 15, 9, 0, 0, TimeSpan.Zero);
        // 30 seconds after the 10:30:00 occurrence, within the 60s misfire threshold
        var now = new DateTimeOffset(2025, 6, 15, 10, 30, 30, TimeSpan.Zero);

        var fakeTime = new FakeTimeProvider(now);
        var store = TestJobStores.Ram(timeProvider: fakeTime);
        store.MisfireThreshold = TimeSpan.FromSeconds(60);
        var signaler = new SampleSignaler();
        await store.Initialize();
        await store.SchedulerStarted();

        var job = JobBuilder.Create().OfType<NoOpJob>()
            .WithIdentity("testJob", "testGroup").StoreDurably(true).Build();
        await store.AddJob(job, false);

        var trigger = new SimpleTriggerImpl(fakeTime)
        {
            Key = new TriggerKey("testTrigger", "testGroup"),
            JobKey = job.Key,
            StartTimeUtc = startTime,
            RepeatInterval = TimeSpan.FromMinutes(5),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount
        };
        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger, false);

        // Act: acquiring triggers due by 'now' applies the misfire handling
        var acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = now, MaxCount = 1, TimeWindow = TimeSpan.Zero });

        // Assert: nothing is due now; the trigger was rescheduled to 10:35:00
        Assert.That(acquired, Is.Empty,
            "Trigger must not fire immediately after misfire handling (#3096)");

        var stored = await store.GetTrigger(trigger.Key);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.NextFireTimeUtc, Is.EqualTo(new DateTimeOffset(2025, 6, 15, 10, 35, 0, TimeSpan.Zero)),
            "Trigger should be rescheduled to the next scheduled time strictly after now");
    }

    [Test]
    public async Task TestScheduledFireTimeUtc_NoMisfire_ReturnsScheduledTime()
    {
        var scheduledTime = DateTimeOffset.UtcNow.AddMinutes(1);

        var trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group,
            scheduledTime, scheduledTime.AddHours(1), 2, TimeSpan.FromMinutes(30));
        trigger.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(trigger, false);

        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = scheduledTime.AddSeconds(1), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        Assert.That(acquired, Has.Count.EqualTo(1));

        var firedResults = await fJobStore.TriggersFired(acquired);
        Assert.That(firedResults, Has.Count.EqualTo(1));

        var bundle = firedResults[0].TriggerFiredBundle;
        Assert.That(bundle, Is.Not.Null);
        Assert.That(bundle!.ScheduledFireTimeUtc, Is.EqualTo(scheduledTime),
            "ScheduledFireTimeUtc should match the trigger's scheduled fire time when no misfire occurred");
    }

    /// <summary>
    /// Regression test for #1386: TriggersFired must return a result entry for every input trigger,
    /// using a null bundle for skipped triggers, so that QuartzSchedulerThread can correlate
    /// results by index position.
    /// </summary>
    [Test]
    public async Task TriggersFired_DeletedTrigger_ReturnsNullBundleInsteadOfSkipping()
    {
        DateTimeOffset d = DateTimeOffset.UtcNow;

        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger3", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        trigger3.ComputeFirstFireTimeUtc(null);

        await fJobStore.AddTrigger(trigger1, false);
        await fJobStore.AddTrigger(trigger2, false);
        await fJobStore.AddTrigger(trigger3, false);

        // Acquire all three triggers
        DateTimeOffset firstFireTime = trigger1.NextFireTimeUtc!.Value;
        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 3, TimeWindow = TimeSpan.Zero });
        Assert.That(acquired, Has.Count.EqualTo(3), "Should acquire all 3 triggers");

        // Delete trigger2 between acquire and fire — simulates the race condition
        Assert.That(await fJobStore.DeleteTrigger(trigger2.Key), Is.True, "trigger2 should be removed");

        // Fire all acquired triggers
        var results = await fJobStore.TriggersFired(acquired);

        // Result count must match input count for correct index correlation
        Assert.That(results, Has.Count.EqualTo(3),
            "TriggersFired must return one result per input trigger to maintain index alignment with QuartzSchedulerThread");

        // trigger1 and trigger3 should have non-null bundles
        Assert.That(results[0].TriggerFiredBundle, Is.Not.Null, "trigger1 should have fired successfully");
        Assert.That(results[2].TriggerFiredBundle, Is.Not.Null, "trigger3 should have fired successfully");

        // trigger2 (deleted) should have a null bundle
        Assert.That(results[1].TriggerFiredBundle, Is.Null,
            "Deleted trigger should produce a null bundle, not be omitted from results");
    }

    /// <summary>
    /// Regression test for #1386: TriggersFired returns null bundle for triggers
    /// that changed state (e.g., paused) between acquire and fire.
    /// </summary>
    [Test]
    public async Task TriggersFired_PausedTrigger_ReturnsNullBundleInsteadOfSkipping()
    {
        DateTimeOffset d = DateTimeOffset.UtcNow;

        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);

        await fJobStore.AddTrigger(trigger1, false);
        await fJobStore.AddTrigger(trigger2, false);

        // Acquire both triggers
        DateTimeOffset firstFireTime = trigger1.NextFireTimeUtc!.Value;
        var acquired = await fJobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 2, TimeWindow = TimeSpan.Zero });
        Assert.That(acquired, Has.Count.EqualTo(2));

        // Pause trigger group between acquire and fire
        await fJobStore.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("triggerGroup1"));

        // Fire all acquired triggers
        var results = await fJobStore.TriggersFired(acquired);

        // Both triggers should have entries — paused ones get null bundles
        Assert.That(results, Has.Count.EqualTo(2),
            "TriggersFired must return one result per input trigger even when some are paused");
        Assert.That(results[0].TriggerFiredBundle, Is.Null, "paused trigger1 should have null bundle");
        Assert.That(results[1].TriggerFiredBundle, Is.Null, "paused trigger2 should have null bundle");
    }

    [Test]
    public async Task TestStoreJobsAndTriggersReplace_SwitchFromSimpleToCronTrigger()
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("job-switch", "group1")
            .StoreDurably(true)
            .Build();

        IOperableTrigger simpleTrigger = new SimpleTriggerImpl("trigger-switch", "group1", job.Key.Name, job.Key.Group, DateTimeOffset.UtcNow.AddSeconds(30), null, -1, TimeSpan.FromSeconds(30));
        simpleTrigger.ComputeFirstFireTimeUtc(null);

        await fJobStore.ScheduleJob(job, simpleTrigger);

        var stored = await fJobStore.GetTrigger(new TriggerKey("trigger-switch", "group1"));
        Assert.That(stored, Is.InstanceOf<ISimpleTrigger>(), "Initial trigger should be a SimpleTrigger");

        // Now replace with a cron trigger using the same trigger key
        var cronTrigger = new CronTriggerImpl("trigger-switch", "group1", job.Key.Name, job.Key.Group, "0 0 * * * ?");
        cronTrigger.ComputeFirstFireTimeUtc(null);

        var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>>
        {
            [job] = new IOperableTrigger[] { cronTrigger }
        };

        await fJobStore.ScheduleJobs(triggersAndJobs, replace: true);

        var updated = await fJobStore.GetTrigger(new TriggerKey("trigger-switch", "group1"));
        Assert.That(updated, Is.InstanceOf<ICronTrigger>(), "Trigger should have been replaced with a CronTrigger");
    }

    [Test]
    public async Task PauseTriggerFollowsMissingKeyRule()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl("missing-key-pause", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, DateTimeOffset.UtcNow.AddMinutes(5), null, 2, TimeSpan.FromSeconds(2));
        trigger.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(trigger, false);

        (await fJobStore.PauseTrigger(new TriggerKey("no-such-trigger"))).Should().BeFalse(
            "pausing a missing trigger is a no-op");
        (await fJobStore.PauseTrigger(trigger.Key)).Should().BeTrue(
            "the trigger existed and was paused");
        (await fJobStore.PauseTrigger(trigger.Key)).Should().BeFalse(
            "the trigger was already paused, so nothing changed");
    }

    [Test]
    public async Task ResumeTriggerFollowsMissingKeyRule()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl("missing-key-resume", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, DateTimeOffset.UtcNow.AddMinutes(5), null, 2, TimeSpan.FromSeconds(2));
        trigger.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(trigger, false);

        (await fJobStore.ResumeTrigger(new TriggerKey("no-such-trigger"))).Should().BeFalse(
            "resuming a missing trigger is a no-op");
        (await fJobStore.ResumeTrigger(trigger.Key)).Should().BeFalse(
            "the trigger was not paused, so nothing changed");

        await fJobStore.PauseTrigger(trigger.Key);
        (await fJobStore.ResumeTrigger(trigger.Key)).Should().BeTrue(
            "the trigger was paused and was resumed");
    }

    [Test]
    public async Task PauseJobAndResumeJobFollowMissingKeyRule()
    {
        (await fJobStore.PauseJob(new JobKey("no-such-job"))).Should().BeFalse(
            "pausing a missing job is a no-op");
        (await fJobStore.ResumeJob(new JobKey("no-such-job"))).Should().BeFalse(
            "resuming a missing job is a no-op");

        // fJobDetail is durable and has no triggers: the defined edge case is true
        (await fJobStore.PauseJob(fJobDetail.Key)).Should().BeTrue(
            "the job exists even though it has no triggers");
        (await fJobStore.ResumeJob(fJobDetail.Key)).Should().BeTrue(
            "the job exists even though it has no triggers");
    }

    [Test]
    public async Task ResetTriggerFromErrorStateFollowsMissingKeyRule()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl("missing-key-reset", "triggerGroup1", fJobDetail.Key.Name, fJobDetail.Key.Group, DateTimeOffset.UtcNow.AddMinutes(5), null, 2, TimeSpan.FromSeconds(2));
        trigger.ComputeFirstFireTimeUtc(null);
        await fJobStore.AddTrigger(trigger, false);

        (await fJobStore.ResetTriggerFromErrorState(new TriggerKey("no-such-trigger"))).Should().BeFalse(
            "resetting a missing trigger is a no-op");
        (await fJobStore.ResetTriggerFromErrorState(trigger.Key)).Should().BeFalse(
            "the trigger is not in the error state, so nothing changed");
    }

    [DisallowConcurrentExecution]
    private sealed class DisallowConcurrentNoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
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