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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
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
[NonParallelizable]
public class RAMJobStoreTest
{
    private IJobStore fJobStore;
    private JobDetailImpl fJobDetail;
    private SampleSignaler fSignaler;

    [SetUp]
    public void SetUp()
    {
        fJobStore = new RAMJobStore();
        fSignaler = new SampleSignaler();
        fJobStore.Initialize(null, fSignaler);
        fJobStore.SchedulerStarted();

        fJobDetail = new JobDetailImpl("job1", "jobGroup1", typeof(NoOpJob));
        fJobDetail.Durable = true;
        fJobStore.StoreJob(fJobDetail, false);
    }

    [Test]
    public async Task TestAcquireNextTrigger()
    {
        DateTimeOffset d = DateBuilder.EvenMinuteDateAfterNow();
        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(200), d.AddSeconds(400), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(50), d.AddSeconds(250), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger1", "triggerGroup2", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(100), d.AddSeconds(300), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        trigger3.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(trigger1, false);
        await fJobStore.StoreTrigger(trigger2, false);
        await fJobStore.StoreTrigger(trigger3, false);

        DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc().Value;

        Assert.AreEqual(0, (await fJobStore.AcquireNextTriggers(d.AddMilliseconds(10), 1, TimeSpan.Zero)).Count);
        Assert.AreEqual(trigger2, (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).First());
        Assert.AreEqual(trigger3, (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).First());
        Assert.AreEqual(trigger1, (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).First());
        Assert.AreEqual(0, (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).Count);

        // release trigger3
        await fJobStore.ReleaseAcquiredTrigger(trigger3);
        Assert.AreEqual(trigger3, (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).First());
    }

    [Test]
    public async Task TestAcquireNextTriggerBatch()
    {
        DateTimeOffset d = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromSeconds(1));

        IOperableTrigger early = new SimpleTriggerImpl("early", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d, d.AddMilliseconds(220000), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(190000), d.AddMilliseconds(570000), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(229000), d.AddMilliseconds(610050), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger3", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(240000), d.AddMilliseconds(620050), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger4 = new SimpleTriggerImpl("trigger4", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(240000), d.AddMilliseconds(630050), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger10 = new SimpleTriggerImpl("trigger10", "triggerGroup2", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(5000000), d.AddMilliseconds(7000000), 2, TimeSpan.FromSeconds(2));

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
        DateTimeOffset firstFireTime2 = early.GetNextFireTimeUtc().Value;
        List<IOperableTrigger> acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 4, TimeSpan.FromSeconds(1))).ToList();
        Assert.AreEqual(1, acquiredTriggers.Count);
        Assert.AreEqual(early.Key, acquiredTriggers[0].Key);
        await fJobStore.ReleaseAcquiredTrigger(early);
            

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 4, TimeSpan.FromMilliseconds(200000))).ToList();
        Assert.AreEqual(2, acquiredTriggers.Count);
        Assert.AreEqual(early.Key, acquiredTriggers[0].Key);
        Assert.AreEqual(trigger1.Key, acquiredTriggers[1].Key);
        await fJobStore.ReleaseAcquiredTrigger(early);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);

        await fJobStore.RemoveTrigger(early.Key);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 5, TimeSpan.FromMilliseconds(300000))).ToList();
        Assert.AreEqual(4, acquiredTriggers.Count);
        Assert.AreEqual(trigger1.Key, acquiredTriggers[0].Key);
        Assert.AreEqual(trigger2.Key, acquiredTriggers[1].Key);
        Assert.AreEqual(trigger3.Key, acquiredTriggers[2].Key);
        Assert.AreEqual(trigger4.Key, acquiredTriggers[3].Key);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);
        await fJobStore.ReleaseAcquiredTrigger(trigger2);
        await fJobStore.ReleaseAcquiredTrigger(trigger3);
        await fJobStore.ReleaseAcquiredTrigger(trigger4);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 6, TimeSpan.FromMilliseconds(300000))).ToList();

        Assert.AreEqual(4, acquiredTriggers.Count);
        Assert.AreEqual(trigger1.Key, acquiredTriggers[0].Key);
        Assert.AreEqual(trigger2.Key, acquiredTriggers[1].Key);
        Assert.AreEqual(trigger3.Key, acquiredTriggers[2].Key);
        Assert.AreEqual(trigger4.Key, acquiredTriggers[3].Key);

        await fJobStore.ReleaseAcquiredTrigger(trigger1);
        await fJobStore.ReleaseAcquiredTrigger(trigger2);
        await fJobStore.ReleaseAcquiredTrigger(trigger3);
        await fJobStore.ReleaseAcquiredTrigger(trigger4);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(1), 5, TimeSpan.Zero)).ToList();
        Assert.AreEqual(1, acquiredTriggers.Count);
        Assert.AreEqual(trigger1.Key, acquiredTriggers[0].Key);

        await fJobStore.ReleaseAcquiredTrigger(trigger1);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(250), 5, TimeSpan.FromMilliseconds(40000))).ToList();
        Assert.AreEqual(2, acquiredTriggers.Count);
        Assert.AreEqual(trigger1.Key, acquiredTriggers[0].Key);
        Assert.AreEqual(trigger2.Key, acquiredTriggers[1].Key);

        await fJobStore.ReleaseAcquiredTrigger(early);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);
        await fJobStore.ReleaseAcquiredTrigger(trigger2);
        await fJobStore.ReleaseAcquiredTrigger(trigger3);

        acquiredTriggers = (await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(150), 5, TimeSpan.FromMilliseconds(5000L))).ToList();
        Assert.AreEqual(1, acquiredTriggers.Count);
        Assert.AreEqual(trigger1.Key, acquiredTriggers[0].Key);
        await fJobStore.ReleaseAcquiredTrigger(trigger1);
    }

    [Test]
    public async Task TestTriggerStates()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        trigger.ComputeFirstFireTimeUtc(null);
        Assert.AreEqual(TriggerState.None, await fJobStore.GetTriggerState(trigger.Key));
        await fJobStore.StoreTrigger(trigger, false);
        Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerState(trigger.Key));

        await fJobStore.PauseTrigger(trigger.Key);
        Assert.AreEqual(TriggerState.Paused, await fJobStore.GetTriggerState(trigger.Key));

        await fJobStore.ResumeTrigger(trigger.Key);
        Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerState(trigger.Key));

        trigger = (await fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).First();
        Assert.IsNotNull(trigger);
        await fJobStore.ReleaseAcquiredTrigger(trigger);
        trigger = (await fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).First();
        Assert.IsNotNull(trigger);
        Assert.AreEqual(0, (await fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).Count);
    }

    [Test]
    public void TestRemoveCalendarWhenTriggersPresent()
    {
        // QRTZNET-29

        IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
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
        JobDetailImpl detail = new JobDetailImpl(jobName, jobGroup, typeof(NoOpJob));
        await fJobStore.StoreJob(detail, false);

        string trName = "StoreTriggerReplacesTrigger";
        string trGroup = "StoreTriggerReplacesTriggerGroup";
        IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.Now);
        tr.JobKey = new JobKey(jobName, jobGroup);
        tr.CalendarName = null;

        await fJobStore.StoreTrigger(tr, false);
        Assert.AreEqual(tr, await fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup)));

        tr.CalendarName = "NonExistingCalendar";
        await fJobStore.StoreTrigger(tr, true);
        Assert.AreEqual(tr, await fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup)));
        var trigger = await fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup));
        Assert.AreEqual(tr.CalendarName, trigger.CalendarName, "StoreJob doesn't replace triggers");

        bool exceptionRaised = false;
        try
        {
            await fJobStore.StoreTrigger(tr, false);
        }
        catch (ObjectAlreadyExistsException)
        {
            exceptionRaised = true;
        }
        Assert.IsTrue(exceptionRaised, "an attempt to store duplicate trigger succeeded");
    }

    [Test]
    public async Task PauseJobGroupPausesNewJob()
    {
        string jobName1 = "PauseJobGroupPausesNewJob";
        string jobName2 = "PauseJobGroupPausesNewJob2";
        string jobGroup = "PauseJobGroupPausesNewJobGroup";
        JobDetailImpl detail = new JobDetailImpl(jobName1, jobGroup, typeof(NoOpJob));
        detail.Durable = true;
        await fJobStore.StoreJob(detail, false);
        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

        detail = new JobDetailImpl(jobName2, jobGroup, typeof(NoOpJob));
        detail.Durable = true;
        await fJobStore.StoreJob(detail, false);

        string trName = "PauseJobGroupPausesNewJobTrigger";
        string trGroup = "PauseJobGroupPausesNewJobTriggerGroup";
        IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.UtcNow);
        tr.JobKey = new JobKey(jobName2, jobGroup);
        await fJobStore.StoreTrigger(tr, false);
        Assert.AreEqual(TriggerState.Paused, await fJobStore.GetTriggerState(tr.Key));
    }

    [Test]
    public async Task ResumeJob_WhenGroupPaused_NewTriggerShouldNotBePaused()
    {
        string jobGroup = "ResumeJobGroupTest";
        JobDetailImpl job = new JobDetailImpl("job1", jobGroup, typeof(NoOpJob)) { Durable = true };
        await fJobStore.StoreJob(job, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));
        await fJobStore.ResumeJob(job.Key);

        IOperableTrigger tr = new SimpleTriggerImpl("newTrigger", "triggerGroup", DateTimeOffset.UtcNow);
        tr.JobKey = job.Key;
        await fJobStore.StoreTrigger(tr, false);

        Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerState(tr.Key));
    }

    [Test]
    public async Task ResumeJob_WhenGroupPaused_OtherJobsStillPaused()
    {
        string jobGroup = "ResumeJobGroupTest2";
        JobDetailImpl job1 = new JobDetailImpl("job1", jobGroup, typeof(NoOpJob)) { Durable = true };
        JobDetailImpl job2 = new JobDetailImpl("job2", jobGroup, typeof(NoOpJob)) { Durable = true };
        await fJobStore.StoreJob(job1, false);
        await fJobStore.StoreJob(job2, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));
        await fJobStore.ResumeJob(job1.Key);

        IOperableTrigger tr1 = new SimpleTriggerImpl("trigger1", "triggerGroup", DateTimeOffset.UtcNow);
        tr1.JobKey = job1.Key;
        await fJobStore.StoreTrigger(tr1, false);

        IOperableTrigger tr2 = new SimpleTriggerImpl("trigger2", "triggerGroup", DateTimeOffset.UtcNow);
        tr2.JobKey = job2.Key;
        await fJobStore.StoreTrigger(tr2, false);

        Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerState(tr1.Key));
        Assert.AreEqual(TriggerState.Paused, await fJobStore.GetTriggerState(tr2.Key));
    }

    [Test]
    public async Task ResumeJob_ThenRePauseGroup_ExemptionCleared()
    {
        string jobGroup = "ResumeJobGroupTest3";
        JobDetailImpl job = new JobDetailImpl("job1", jobGroup, typeof(NoOpJob)) { Durable = true };
        await fJobStore.StoreJob(job, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));
        await fJobStore.ResumeJob(job.Key);
        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

        IOperableTrigger tr = new SimpleTriggerImpl("newTrigger", "triggerGroup", DateTimeOffset.UtcNow);
        tr.JobKey = job.Key;
        await fJobStore.StoreTrigger(tr, false);

        Assert.AreEqual(TriggerState.Paused, await fJobStore.GetTriggerState(tr.Key));
    }

    [Test]
    public async Task ResumeJob_NonexistentJob_DoesNotCreateExemption()
    {
        string jobGroup = "ResumeJobGroupTest4";
        JobDetailImpl job = new JobDetailImpl("job1", jobGroup, typeof(NoOpJob)) { Durable = true };
        await fJobStore.StoreJob(job, false);

        await fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

        // resume a nonexistent job — should not create an exemption for that key
        await fJobStore.ResumeJob(new JobKey("nonexistent", jobGroup));

        // now store the previously-nonexistent job and a trigger for it
        JobDetailImpl laterJob = new JobDetailImpl("nonexistent", jobGroup, typeof(NoOpJob)) { Durable = true };
        await fJobStore.StoreJob(laterJob, false);

        IOperableTrigger tr = new SimpleTriggerImpl("newTrigger", "triggerGroup", DateTimeOffset.UtcNow);
        tr.JobKey = laterJob.Key;
        await fJobStore.StoreTrigger(tr, false);

        Assert.AreEqual(TriggerState.Paused, await fJobStore.GetTriggerState(tr.Key));
    }

    [Test]
    public async Task TestRetrieveJob_NoJobFound()
    {
        RAMJobStore store = new RAMJobStore();
        IJobDetail job = await store.RetrieveJob(new JobKey("not", "existing"));
        Assert.IsNull(job);
    }

    [Test]
    public async Task TestRetrieveTrigger_NoTriggerFound()
    {
        RAMJobStore store = new RAMJobStore();
        IOperableTrigger trigger = await store.RetrieveTrigger(new TriggerKey("not", "existing"));
        Assert.IsNull(trigger);
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
            Assert.AreEqual(jobKey, storedJob.Key);
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
            await store.StoreTrigger((IOperableTrigger)trigger, true);
        }
        // Retrieve job and trigger.
        for (int i = 0; i < 10; i++)
        {
            JobKey jobKey = JobKey.Create("job" + i);
            IJobDetail storedJob = await store.RetrieveJob(jobKey);
            Assert.AreEqual(jobKey, storedJob.Key);

            TriggerKey triggerKey = new TriggerKey("job" + i);
            ITrigger storedTrigger = await store.RetrieveTrigger(triggerKey);
            Assert.AreEqual(triggerKey, storedTrigger.Key);
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
            DateTime startTime = startTime0.AddMinutes(i*1); // a min apart
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.RepeatMinutelyForever(2);
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).StartAt(startTime).Build();

            // Manually trigger the first fire time computation that scheduler would do. Otherwise
            // the store.acquireNextTriggers() will not work properly.
            DateTimeOffset? fireTime = trigger.ComputeFirstFireTimeUtc(null);
            Assert.AreEqual(true, fireTime != null);

            await store.StoreJobAndTrigger(job, trigger);
        }

        // Test acquire one trigger at a time
        for (int i = 0; i < 10; i++)
        {
            DateTimeOffset noLaterThan = startTime0.AddMinutes(i + 2);
            int maxCount = 1;
            TimeSpan timeWindow = TimeSpan.Zero;
            var triggers = await store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow);
            Assert.AreEqual(1, triggers.Count);
            var trigger = triggers.First();
            Assert.AreEqual("job" + i, trigger.Key.Name);

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
            Assert.AreEqual(true, fireTime != null);

            await store.StoreJobAndTrigger(job, trigger);
        }

        // Test acquire batch of triggers at a time
        DateTimeOffset noLaterThan = startTime0.AddMinutes(10);
        int maxCount = 7;
        TimeSpan timeWindow = TimeSpan.FromMinutes(8);
        var triggers = (await store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow)).ToList();
        Assert.AreEqual(7, triggers.Count);
        for (int i = 0; i < 7; i++)
        {
            Assert.AreEqual("job" + i, triggers[i].Key.Name);
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
            fJobDetail.Name,
            fJobDetail.Group,
            baseFireTimeDate,
            baseFireTimeDate.AddMilliseconds(200000),
            2,
            TimeSpan.FromMilliseconds(2000));

        trigger1.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(trigger1, false);

        var firstFireTime = trigger1.GetNextFireTimeUtc().Value;

        // pretend to fire it
        var aqTs = await fJobStore.AcquireNextTriggers(firstFireTime.AddMilliseconds(10000), 1, TimeSpan.Zero);
        Assert.AreEqual(trigger1.Key, aqTs.First().Key);

        var fTs = await fJobStore.TriggersFired(aqTs);
        var ft = fTs.First();

        // get the trigger into error state
        await fJobStore.TriggeredJobComplete(ft.TriggerFiredBundle.Trigger, ft.TriggerFiredBundle.JobDetail, SchedulerInstruction.SetTriggerError);

        var state = await fJobStore.GetTriggerState(trigger1.Key);
        Assert.AreEqual(TriggerState.Error, state);

        // test reset
        await fJobStore.ResetTriggerFromErrorState(trigger1.Key);
        state = await fJobStore.GetTriggerState(trigger1.Key);
        Assert.AreEqual(TriggerState.Normal, state);
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
        Assert.IsTrue(deleteSuccess, "Expected RemoveJob to return True when deleting an existing job");

        deleteSuccess = await store.RemoveJob(new JobKey("job0"));
        Assert.IsFalse(deleteSuccess, "Expected RemoveJob to return False when deleting an non-existing job");
    }

    [Test]
    public async Task TestTriggeredJobComplete_UnblocksTriggersForDisallowConcurrentExecutionJob()
    {
        // Store a DisallowConcurrentExecution job with two triggers
        var job = new JobDetailImpl("blockedJob", "group1", typeof(DisallowConcurrentNoOpJob))
        {
            Durable = true
        };
        await fJobStore.StoreJob(job, true);

        var d = DateBuilder.EvenMinuteDateAfterNow();
        var trigger1 = new SimpleTriggerImpl("trigger1", "group1", job.Name, job.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));
        var trigger2 = new SimpleTriggerImpl("trigger2", "group1", job.Name, job.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(trigger1, false);
        await fJobStore.StoreTrigger(trigger2, false);

        // Acquire and fire one trigger
        var acquiredTriggers = await fJobStore.AcquireNextTriggers(d.AddSeconds(10), 1, TimeSpan.Zero);
        Assert.AreEqual(1, acquiredTriggers.Count);

        var firedResults = await fJobStore.TriggersFired(acquiredTriggers);
        Assert.AreEqual(1, firedResults.Count);

        // Both triggers should be blocked now (DisallowConcurrentExecution)
        Assert.AreEqual(TriggerState.Blocked, await fJobStore.GetTriggerState(trigger1.Key));
        Assert.AreEqual(TriggerState.Blocked, await fJobStore.GetTriggerState(trigger2.Key));

        // Simulate job completion with NoInstruction (graceful shutdown scenario)
        var firedResult = firedResults.First();
        await fJobStore.TriggeredJobComplete(
            firedResult.TriggerFiredBundle.Trigger,
            firedResult.TriggerFiredBundle.JobDetail,
            SchedulerInstruction.NoInstruction);

        // Both triggers should be unblocked (Normal = Waiting)
        Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerState(trigger1.Key));
        Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerState(trigger2.Key));
    }

    [Test]
    public async Task TestReleaseAcquiredTrigger_DoesNotUnblockOtherTriggersForDisallowConcurrentExecutionJob()
    {
        // This test documents the reason we must use TriggeredJobComplete
        // (not ReleaseAcquiredTrigger) after TriggersFired for DisallowConcurrentExecution jobs

        var job = new JobDetailImpl("blockedJob", "group1", typeof(DisallowConcurrentNoOpJob))
        {
            Durable = true
        };
        await fJobStore.StoreJob(job, true);

        var d = DateBuilder.EvenMinuteDateAfterNow();
        var trigger1 = new SimpleTriggerImpl("trigger1", "group1", job.Name, job.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));
        var trigger2 = new SimpleTriggerImpl("trigger2", "group1", job.Name, job.Group,
            d.AddSeconds(1), d.AddSeconds(200), 10, TimeSpan.FromSeconds(5));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(trigger1, false);
        await fJobStore.StoreTrigger(trigger2, false);

        // Acquire and fire one trigger
        var acquiredTriggers = await fJobStore.AcquireNextTriggers(d.AddSeconds(10), 1, TimeSpan.Zero);
        Assert.AreEqual(1, acquiredTriggers.Count);
        var firedTrigger = acquiredTriggers.First();

        var firedResults = await fJobStore.TriggersFired(acquiredTriggers);
        Assert.AreEqual(1, firedResults.Count);

        // Both triggers should be blocked
        Assert.AreEqual(TriggerState.Blocked, await fJobStore.GetTriggerState(trigger1.Key));
        Assert.AreEqual(TriggerState.Blocked, await fJobStore.GetTriggerState(trigger2.Key));

        // ReleaseAcquiredTrigger only handles the specific trigger's Acquired state,
        // it does NOT unblock other triggers since it doesn't know about job concurrency
        await fJobStore.ReleaseAcquiredTrigger(firedTrigger);

        // The other trigger remains blocked - this is the bug scenario
        // that was fixed by using TriggeredJobComplete instead
        var trigger1State = await fJobStore.GetTriggerState(trigger1.Key);
        var trigger2State = await fJobStore.GetTriggerState(trigger2.Key);
        
        // At least one trigger should still be blocked since ReleaseAcquiredTrigger
        // does not handle the unblocking of all triggers for the job
        Assert.IsTrue(
            trigger1State == TriggerState.Blocked || trigger2State == TriggerState.Blocked,
            "ReleaseAcquiredTrigger should not unblock all triggers for DisallowConcurrentExecution jobs");
    }

    [Test]
    public async Task TestScheduledFireTimeUtc_CronTrigger_WithMisfire_ReturnsOriginalScheduledTime()
    {
        // Arrange: create a cron trigger that fires every minute, starting in the past
        var now = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var originalScheduledTime = new DateTimeOffset(2025, 6, 15, 10, 29, 0, TimeSpan.Zero);
        var previousFireTime = new DateTimeOffset(2025, 6, 15, 10, 28, 0, TimeSpan.Zero);

        var originalUtcNow = SystemTime.UtcNow;
        try
        {
            SystemTime.UtcNow = () => now;

            var store = new RAMJobStore { MisfireThreshold = TimeSpan.FromSeconds(5) };
            var signaler = new SampleSignaler();
            await store.Initialize(null, signaler);
            await store.SchedulerStarted();

            var job = new JobDetailImpl("testJob", "testGroup", typeof(NoOpJob)) { Durable = true };
            await store.StoreJob(job, false);

            // Build a cron trigger that fires every minute with FireAndProceed (FireOnceNow) misfire policy
            var trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .ForJob(job)
                .WithCronSchedule("0 * * * * ?", x => x.WithMisfireHandlingInstructionFireAndProceed())
                .Build();

            // Manually set trigger state to simulate a missed fire at 10:29:00
            trigger.SetPreviousFireTimeUtc(previousFireTime);
            trigger.SetNextFireTimeUtc(originalScheduledTime);

            await store.StoreTrigger(trigger, false);

            // Act: acquire triggers - this should detect the misfire (10:29:00 is > 5s in the past)
            // and call UpdateAfterMisfire which sets nextFireTimeUtc to ~now
            var acquired = await store.AcquireNextTriggers(now.AddMinutes(1), 1, TimeSpan.Zero);
            Assert.AreEqual(1, acquired.Count, "Should acquire the misfired trigger");

            // Fire the trigger
            var firedResults = await store.TriggersFired(acquired);
            Assert.AreEqual(1, firedResults.Count, "Should have one fired result");

            var bundle = firedResults.First().TriggerFiredBundle;

            // Assert: ScheduledFireTimeUtc should be the ORIGINAL scheduled time (10:29:00),
            // not the misfire-adjusted time (~10:30:00)
            Assert.IsNotNull(bundle);
            Assert.AreEqual(originalScheduledTime, bundle.ScheduledFireTimeUtc,
                "ScheduledFireTimeUtc should reflect the original scheduled time, not the misfire-adjusted time");
            Assert.AreEqual(previousFireTime, bundle.PrevFireTimeUtc,
                "PrevFireTimeUtc should be the previous fire time before this execution");
        }
        finally
        {
            SystemTime.UtcNow = originalUtcNow;
        }
    }

    [Test]
    public async Task TestScheduledFireTimeUtc_SimpleTrigger_WithMisfire_ReturnsOriginalScheduledTime()
    {
        // Arrange: create a simple trigger with FireNow misfire policy
        var now = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var originalScheduledTime = new DateTimeOffset(2025, 6, 15, 10, 29, 0, TimeSpan.Zero);

        var originalUtcNow = SystemTime.UtcNow;
        try
        {
            SystemTime.UtcNow = () => now;

            var store = new RAMJobStore { MisfireThreshold = TimeSpan.FromSeconds(5) };
            var signaler = new SampleSignaler();
            await store.Initialize(null, signaler);
            await store.SchedulerStarted();

            var job = new JobDetailImpl("testJob", "testGroup", typeof(NoOpJob)) { Durable = true };
            await store.StoreJob(job, false);

            // Build a one-shot simple trigger (RepeatCount = 0 -> FireNow misfire policy via SmartPolicy)
            var trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .ForJob(job)
                .WithSimpleSchedule(x => x.WithRepeatCount(0))
                .StartAt(originalScheduledTime)
                .Build();

            trigger.ComputeFirstFireTimeUtc(null);
            // nextFireTimeUtc should now be originalScheduledTime
            Assert.AreEqual(originalScheduledTime, trigger.GetNextFireTimeUtc());

            await store.StoreTrigger(trigger, false);

            // Act: acquire triggers - should detect misfire and apply FireNow
            var acquired = await store.AcquireNextTriggers(now.AddMinutes(1), 1, TimeSpan.Zero);
            Assert.AreEqual(1, acquired.Count, "Should acquire the misfired trigger");

            var firedResults = await store.TriggersFired(acquired);
            Assert.AreEqual(1, firedResults.Count, "Should have one fired result");

            var bundle = firedResults.First().TriggerFiredBundle;

            // Assert: ScheduledFireTimeUtc should be the ORIGINAL scheduled time
            Assert.IsNotNull(bundle);
            Assert.AreEqual(originalScheduledTime, bundle.ScheduledFireTimeUtc,
                "ScheduledFireTimeUtc should reflect the original scheduled time, not the misfire-adjusted time");
        }
        finally
        {
            SystemTime.UtcNow = originalUtcNow;
        }
    }

    [Test]
    public async Task TestScheduledFireTimeUtc_NoMisfire_ReturnsScheduledTime()
    {
        // Arrange: trigger fires on time (no misfire)
        var scheduledTime = DateTimeOffset.UtcNow.AddMinutes(1);

        var trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group,
            scheduledTime, scheduledTime.AddHours(1), 2, TimeSpan.FromMinutes(30));
        trigger.ComputeFirstFireTimeUtc(null);
        await fJobStore.StoreTrigger(trigger, false);

        // Act: acquire at the scheduled time (no misfire)
        var acquired = await fJobStore.AcquireNextTriggers(scheduledTime.AddSeconds(1), 1, TimeSpan.Zero);
        Assert.AreEqual(1, acquired.Count);

        var firedResults = await fJobStore.TriggersFired(acquired);
        Assert.AreEqual(1, firedResults.Count);

        var bundle = firedResults.First().TriggerFiredBundle;

        // Assert: ScheduledFireTimeUtc should be the scheduled time
        Assert.IsNotNull(bundle);
        Assert.AreEqual(scheduledTime, bundle.ScheduledFireTimeUtc,
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

        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger3", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        trigger3.ComputeFirstFireTimeUtc(null);

        await fJobStore.StoreTrigger(trigger1, false);
        await fJobStore.StoreTrigger(trigger2, false);
        await fJobStore.StoreTrigger(trigger3, false);

        // Acquire all three triggers
        DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc()!.Value;
        var acquired = await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 3, TimeSpan.Zero);
        Assert.AreEqual(3, acquired.Count, "Should acquire all 3 triggers");

        // Delete trigger2 between acquire and fire — simulates the race condition
        Assert.IsTrue(await fJobStore.RemoveTrigger(trigger2.Key), "trigger2 should be removed");

        // Fire all acquired triggers
        var results = await fJobStore.TriggersFired(acquired.ToList());

        // Result count must match input count for correct index correlation
        Assert.AreEqual(3, results.Count,
            "TriggersFired must return one result per input trigger to maintain index alignment with QuartzSchedulerThread");

        var resultList = results.ToList();

        // trigger1 and trigger3 should have non-null bundles
        Assert.IsNotNull(resultList[0].TriggerFiredBundle, "trigger1 should have fired successfully");
        Assert.IsNotNull(resultList[2].TriggerFiredBundle, "trigger3 should have fired successfully");

        // trigger2 (deleted) should have a null bundle
        Assert.IsNull(resultList[1].TriggerFiredBundle,
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

        IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(1), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);

        await fJobStore.StoreTrigger(trigger1, false);
        await fJobStore.StoreTrigger(trigger2, false);

        // Acquire both triggers
        DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc()!.Value;
        var acquired = await fJobStore.AcquireNextTriggers(firstFireTime.AddSeconds(10), 2, TimeSpan.Zero);
        Assert.AreEqual(2, acquired.Count);

        // Pause trigger2's group between acquire and fire
        await fJobStore.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("triggerGroup1"));

        // Fire all acquired triggers
        var results = await fJobStore.TriggersFired(acquired.ToList());

        // Both triggers should have entries — paused ones get null bundles
        Assert.AreEqual(2, results.Count,
            "TriggersFired must return one result per input trigger even when some are paused");

        var resultList = results.ToList();
        Assert.IsNull(resultList[0].TriggerFiredBundle, "paused trigger1 should have null bundle");
        Assert.IsNull(resultList[1].TriggerFiredBundle, "paused trigger2 should have null bundle");
    }

    [DisallowConcurrentExecution]
    private class DisallowConcurrentNoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }

    public class SampleSignaler : ISchedulerSignaler
    {
        internal int fMisfireCount;

        public Task NotifyTriggerListenersMisfired(
            ITrigger trigger, 
            CancellationToken cancellationToken = default)
        {
            fMisfireCount++;
            return Task.FromResult(true);
        }

        public Task NotifySchedulerListenersFinalized(
            ITrigger trigger, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void SignalSchedulingChange(
            DateTimeOffset? candidateNewNextFireTimeUtc, 
            CancellationToken cancellationToken = default)
        {
        }

        public Task NotifySchedulerListenersError(
            string message,
            SchedulerException jpe, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task NotifySchedulerListenersJobDeleted(
            JobKey jobKey, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}