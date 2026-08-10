using System.Runtime.Serialization;
using System.Text.Json;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Serialization.Newtonsoft;
using Quartz.Extensibility;
using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Serialization.Newtonsoft.Triggers;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl;

public class SmokeTestPerformer
{
    public async Task Test(IScheduler scheduler, bool clearJobs, bool scheduleJobs)
    {
        try
        {
            if (clearJobs)
            {
                await scheduler.Clear();
            }

            if (scheduleJobs)
            {
                ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                ICalendar holidayCalendar = new HolidayCalendar();

                // QRTZNET-86
                ITrigger t = await scheduler.GetTrigger(new TriggerKey("NonExistingTrigger", "NonExistingGroup"));
                Assert.That(t, Is.Null);

                AnnualCalendar calendar = new AnnualCalendar();
                calendar.AddExcludedDay(new DateOnly(2018, 7, 4));
                await scheduler.AddCalendar("annualCalendar", calendar, new AddCalendarOptions { UpdateTriggers = true });

                IOperableTrigger calendarsTrigger = new SimpleTriggerImpl("calendarsTrigger", "test", 20, TimeSpan.FromHours(2));
                calendarsTrigger.CalendarName = "annualCalendar";

                var jd = JobBuilder.Create<NoOpJob>()
                    .WithIdentity(new JobKey("testJob", "test"))
                    .Build();
                await scheduler.ScheduleJob(jd, calendarsTrigger);

                // QRTZNET-93
                await scheduler.AddCalendar("annualCalendar", calendar, new AddCalendarOptions { Replace = true, UpdateTriggers = true });

                var annualCalendar = (AnnualCalendar) await scheduler.GetCalendar("annualCalendar");
                Assert.That(annualCalendar.Description, Is.EqualTo(calendar.Description));
                Assert.That(annualCalendar.DaysExcluded, Is.EquivalentTo(calendar.DaysExcluded));

                await scheduler.AddCalendar("baseCalendar", new BaseCalendar(), new AddCalendarOptions { UpdateTriggers = true });
                await scheduler.AddCalendar("cronCalendar", cronCalendar, new AddCalendarOptions { UpdateTriggers = true });
                await scheduler.AddCalendar("dailyCalendar", new DailyCalendar(TimeOnly.MinValue, new TimeOnly(23, 59, 59)), new AddCalendarOptions { UpdateTriggers = true });
                await scheduler.AddCalendar("holidayCalendar", holidayCalendar, new AddCalendarOptions { UpdateTriggers = true });
                await scheduler.AddCalendar("monthlyCalendar", new MonthlyCalendar(), new AddCalendarOptions { UpdateTriggers = true });
                await scheduler.AddCalendar("weeklyCalendar", new WeeklyCalendar(), new AddCalendarOptions { UpdateTriggers = true });

                await scheduler.AddCalendar("cronCalendar", cronCalendar, new AddCalendarOptions { Replace = true, UpdateTriggers = true });
                await scheduler.AddCalendar("holidayCalendar", holidayCalendar, new AddCalendarOptions { Replace = true, UpdateTriggers = true });

                await scheduler.AddCalendar("customCalendar", new CustomCalendar(), new AddCalendarOptions { Replace = true, UpdateTriggers = true });
                var customCalendar = (CustomCalendar) await scheduler.GetCalendar("customCalendar");
                Assert.That(customCalendar, Is.Not.Null);
                Assert.That(customCalendar.SomeCustomProperty, Is.True);

                Assert.That(await scheduler.GetCalendar("annualCalendar"), Is.Not.Null);

                var lonelyJob = JobBuilder.Create()
                    .OfType<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("lonelyJob", "lonelyGroup"))
                    .StoreDurably(true)
                    .RequestRecovery(true)
                    .Build();

                await scheduler.AddJob(lonelyJob);
                await scheduler.AddJob(lonelyJob, new AddJobOptions { Replace = true });

                string schedId = scheduler.SchedulerInstanceId;

                int count = 1;

                var job = JobBuilder.Create()
                    .OfType<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .RequestRecovery(true)
                    .Build();
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                IOperableTrigger trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));
                trigger.JobDataMap.Add("key", "value");
                trigger.EndTimeUtc = DateTime.UtcNow.AddYears(10);

                trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
                await scheduler.ScheduleJob(job, trigger);

                // check that trigger was stored
                ITrigger persisted = await scheduler.GetTrigger(new TriggerKey("trig_" + count, schedId));
                Assert.That(persisted, Is.Not.Null);
                Assert.That(persisted is SimpleTriggerImpl, Is.True);

                count++;
                job = JobBuilder.Create()
                    .OfType<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .RequestRecovery(true)
                    .Build();
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));
                trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(2000L);
                await scheduler.ScheduleJob(job, trigger);

                count++;
                job = JobBuilder.Create()
                    .OfType<SimpleRecoveryStatefulJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .RequestRecovery(true)
                    .Build();
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(3));
                trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
                await scheduler.ScheduleJob(job, trigger);

                count++;
                job = JobBuilder.Create()
                    .OfType<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .RequestRecovery(true)
                    .Build();
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(4));
                trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
                await scheduler.ScheduleJob(job, trigger);

                count++;
                job = JobBuilder.Create()
                    .OfType<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .RequestRecovery(true)
                    .Build();
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                await scheduler.ScheduleJob(job, trigger);

                count++;
                job = JobBuilder.Create()
                    .OfType<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .RequestRecovery(true)
                    .Build();
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                IOperableTrigger ct = new CronTriggerImpl("cron_trig_" + count, schedId, "0/10 * * * * ?");
                ct.JobDataMap.Add("key", "value");
                ct.StartTimeUtc = DateTime.Now.AddMilliseconds(1000);

                await scheduler.ScheduleJob(job, ct);

                count++;
                job = JobBuilder.Create()
                    .OfType<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .RequestRecovery(true)
                    .Build();
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...

                var timeZone1 = TimeZoneUtil.FindTimeZoneById("Central European Standard Time");
                var timeZone2 = TimeZoneUtil.FindTimeZoneById("Mountain Standard Time");

                DailyTimeIntervalTriggerImpl nt = new DailyTimeIntervalTriggerImpl("nth_trig_" + count, schedId, new TimeOnly(1, 1, 1), new TimeOnly(23, 30, 0), IntervalUnit.Hour, 1);
                nt.StartTimeUtc = DateTime.Now.Date.AddMilliseconds(1000);
                nt.TimeZone = timeZone1;

                await scheduler.ScheduleJob(job, nt);

                var loadedNt = (IDailyTimeIntervalTrigger) await scheduler.GetTrigger(nt.Key);
                Assert.That(loadedNt.TimeZone.Id, Is.EqualTo(timeZone1.Id));

                nt.TimeZone = timeZone2;
                await scheduler.RescheduleJob(nt.Key, nt);

                loadedNt = (IDailyTimeIntervalTrigger) await scheduler.GetTrigger(nt.Key);
                Assert.That(loadedNt.TimeZone.Id, Is.EqualTo(timeZone2.Id));

                DailyTimeIntervalTriggerImpl nt2 = new DailyTimeIntervalTriggerImpl();
                nt2.Key = new TriggerKey("nth_trig2_" + count, schedId);
                nt2.StartTimeUtc = DateTime.Now.Date.AddMilliseconds(1000);
                nt2.JobKey = job.Key;
                await scheduler.ScheduleJob(nt2);

                // GitHub issue #92
                await scheduler.GetTrigger(nt2.Key);

                // GitHub issue #98
                nt2.StartTimeOfDay = new TimeOnly(1, 2, 3);
                nt2.EndTimeOfDay = new TimeOnly(2, 3, 4);

                await scheduler.UnscheduleJob(nt2.Key);
                await scheduler.ScheduleJob(nt2);

                var triggerFromDb = (IDailyTimeIntervalTrigger) await scheduler.GetTrigger(nt2.Key);
                Assert.That(triggerFromDb.StartTimeOfDay.Hour, Is.EqualTo(1));
                Assert.That(triggerFromDb.StartTimeOfDay.Minute, Is.EqualTo(2));
                Assert.That(triggerFromDb.StartTimeOfDay.Second, Is.EqualTo(3));

                Assert.That(triggerFromDb.EndTimeOfDay.Hour, Is.EqualTo(2));
                Assert.That(triggerFromDb.EndTimeOfDay.Minute, Is.EqualTo(3));
                Assert.That(triggerFromDb.EndTimeOfDay.Second, Is.EqualTo(4));

                CalendarIntervalTriggerImpl intervalTrigger = new CalendarIntervalTriggerImpl(
                    "calint_trig_" + count,
                    schedId,
                    DateTime.UtcNow.AddMilliseconds(300),
                    DateTime.UtcNow.AddMinutes(1),
                    IntervalUnit.Second,
                    8);
                intervalTrigger.JobKey = job.Key;

                await scheduler.ScheduleJob(intervalTrigger);

                // custom time zone
                const string CustomTimeZoneId = "Custom TimeZone";
                var webTimezone = TimeZoneInfo.CreateCustomTimeZone(
                    CustomTimeZoneId,
                    TimeSpan.FromMinutes(22),
                    null,
                    null);

                TimeZoneUtil.CustomResolver = id =>
                {
                    if (id == CustomTimeZoneId)
                    {
                        return webTimezone;
                    }
                    return null;
                };

                var customTimeZoneTrigger = TriggerBuilder.Create()
                    .WithIdentity("customTimeZoneTrigger")
                    .WithCronSchedule("0/5 * * * * ?", x => x.InTimeZone(webTimezone))
                    .StartNow()
                    .ForJob(job)
                    .Build();

                await scheduler.ScheduleJob(customTimeZoneTrigger);
                var loadedCustomTimeZoneTrigger = (ICronTrigger) await scheduler.GetTrigger(customTimeZoneTrigger.Key);
                Assert.That(loadedCustomTimeZoneTrigger.TimeZone.BaseUtcOffset, Is.EqualTo(TimeSpan.FromMinutes(22)));

                // custom trigger blob serialization
                var customTrigger = new CustomTrigger
                {
                    Key = new TriggerKey("customTrigger"),
                    CronExpressionString = "30 45 18 * * ?",
                    StartTimeUtc = DateTimeOffset.UtcNow,
                    JobKey = job.Key
                };

                customTrigger.ComputeFirstFireTimeUtc(null);
                var nextFireTimeUtc = customTrigger.NextFireTimeUtc;

                await scheduler.ScheduleJob(customTrigger);
                var loadedCustomTrigger = (CustomTrigger) await scheduler.GetTrigger(customTrigger.Key);
                Assert.That(loadedCustomTrigger.NextFireTimeUtc, Is.EqualTo(nextFireTimeUtc));
                Assert.That(loadedCustomTrigger.CronExpressionString, Is.EqualTo(customTrigger.CronExpressionString));
                Assert.That(loadedCustomTrigger.SomeCustomProperty, Is.True);

                // bulk operations
                var info = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();
                IJobDetail detail = JobBuilder.Create<SimpleRecoveryJob>()
                    .WithIdentity(new JobKey("job_" + count, schedId))
                    .Build();
                ITrigger simple = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                var triggers = new List<ITrigger>();
                triggers.Add(simple);
                info[detail] = triggers;

                await scheduler.ScheduleJobs(info, true);

                Assert.That(await scheduler.CheckExists(detail.Key), Is.True);
                Assert.That(await scheduler.CheckExists(simple.Key), Is.True);

                // QRTZNET-243
                await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupContains("a"));
                await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEndsWith("a"));
                await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupStartsWith("a"));
                await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("a"));

                await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupContains("a"));
                await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEndsWith("a"));
                await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith("a"));
                await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("a"));

                await scheduler.Start();

                await Task.Delay(TimeSpan.FromSeconds(3));

                await scheduler.PauseAll();

                await scheduler.ResumeAll();

                await scheduler.PauseJob(new JobKey("job_1", schedId));

                await scheduler.ResumeJob(new JobKey("job_1", schedId));

                await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals(schedId));

                await Task.Delay(TimeSpan.FromSeconds(1));

                await scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals(schedId));

                await scheduler.PauseTrigger(new TriggerKey("trig_2", schedId));
                await scheduler.ResumeTrigger(new TriggerKey("trig_2", schedId));

                await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(schedId));

                var pausedTriggerGroups = await scheduler.GetPausedTriggerGroups();
                Assert.That(pausedTriggerGroups.Count, Is.EqualTo(1));

                await Task.Delay(TimeSpan.FromSeconds(3));
                await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(schedId));

                Assert.That(await scheduler.GetTrigger(new TriggerKey("trig_2", schedId)), Is.Not.Null);
                Assert.That(await scheduler.GetJobDetail(new JobKey("job_1", schedId)), Is.Not.Null);
                Assert.That(await scheduler.GetMetadata(), Is.Not.Null);
                Assert.That(await scheduler.GetCalendar("weeklyCalendar"), Is.Not.Null);

                var genericjobKey = new JobKey("genericJob", "genericGroup");
                GenericJobType.Reset();
                var genericJob = JobBuilder.Create<GenericJobType>()
                    .WithIdentity(genericjobKey)
                    .StoreDurably()
                    .Build();

                await scheduler.AddJob(genericJob);

                genericJob = await scheduler.GetJobDetail(genericjobKey);
                Assert.That(genericJob, Is.Not.Null);
                await scheduler.TriggerJob(genericjobKey);

                GenericJobType.WaitForTrigger(TimeSpan.FromSeconds(20));

                Assert.That(GenericJobType.TriggeredCount, Is.EqualTo(1));
                await scheduler.Standby();

                Assert.That(await scheduler.GetCalendarNames(), Is.Not.Empty);
                Assert.That(await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(schedId)), Is.Not.Empty);

                Assert.That(await scheduler.GetTriggersOfJob(new JobKey("job_2", schedId)), Is.Not.Empty);
#pragma warning disable NUnit2023
                Assert.That(scheduler.GetJobDetail(new JobKey("job_2", schedId)), Is.Not.Null);
#pragma warning restore NUnit2023

                await scheduler.DeleteCalendar("cronCalendar");
                await scheduler.DeleteCalendar("holidayCalendar");
                await scheduler.DeleteJob(new JobKey("lonelyJob", "lonelyGroup"));
                await scheduler.DeleteJob(job.Key);

                await scheduler.GetJobGroupNames();
                await scheduler.GetCalendarNames();
                await scheduler.GetTriggerGroupNames();

                await TestExecutionGroups(scheduler);
                await TestMatchers(scheduler);
                await TestGetTriggerStateExecutingWhileJobRuns(scheduler);
            }
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    private async Task TestExecutionGroups(IScheduler scheduler)
    {
        await scheduler.Clear();

        // Schedule a job with a trigger that has an execution group
        IJobDetail egJob = JobBuilder.Create<NoOpJob>()
            .WithIdentity("execGroupJob", "execGroupTest")
            .StoreDurably()
            .Build();
        await scheduler.AddJob(egJob, new AddJobOptions { Replace = true });

        ITrigger egTrigger = TriggerBuilder.Create()
            .WithIdentity("execGroupTrigger", "execGroupTest")
            .ForJob(egJob)
            .WithExecutionGroup("batch-jobs")
            .WithSimpleSchedule(s => s.WithRepeatCount(0))
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
        await scheduler.ScheduleJob(egTrigger);

        // Verify the execution group round-trips through store/retrieve
        ITrigger retrievedTrigger = await scheduler.GetTrigger(egTrigger.Key);
        Assert.That(retrievedTrigger, Is.Not.Null, "Trigger with execution group should be retrievable");
        Assert.That(retrievedTrigger.ExecutionGroup, Is.EqualTo("batch-jobs"), "Execution group should round-trip through job store");

        // Schedule a trigger without an execution group
        ITrigger noGroupTrigger = TriggerBuilder.Create()
            .WithIdentity("noGroupTrigger", "execGroupTest")
            .ForJob(egJob)
            .WithSimpleSchedule(s => s.WithRepeatCount(0))
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
        await scheduler.ScheduleJob(noGroupTrigger);

        ITrigger retrievedNoGroup = await scheduler.GetTrigger(noGroupTrigger.Key);
        Assert.That(retrievedNoGroup, Is.Not.Null);
        Assert.That(retrievedNoGroup.ExecutionGroup, Is.Null, "Trigger without execution group should have null");

        // Test execution limits API
        await scheduler.SetExecutionLimits(new ExecutionLimitsBuilder()
            .ForGroup("batch-jobs", 2)
            .ForOtherGroups(5)
            .Build()).ConfigureAwait(false);

        ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
        limits.Should().NotBeNull();
        limits.TryGetLimit("batch-jobs", out int? batchLimit).Should().BeTrue();
        batchLimit.Should().Be(2);
        limits.TryGetLimit(ExecutionLimits.OtherGroups, out int? otherLimit).Should().BeTrue();
        otherLimit.Should().Be(5);

        // Clear limits
        await scheduler.SetExecutionLimits(null).ConfigureAwait(false);
        Assert.That(await scheduler.GetExecutionLimits().ConfigureAwait(false), Is.Null);

        await scheduler.Clear();
    }

    private async Task TestMatchers(IScheduler scheduler)
    {
        await scheduler.Clear();

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "aaabbbccc").StoreDurably().Build();
        await scheduler.AddJob(job, new AddJobOptions { Replace = true });
        SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create();
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("trig1", "aaabbbccc").WithSchedule(schedule).ForJob(job).Build();
        await scheduler.ScheduleJob(trigger);

        job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "xxxyyyzzz").StoreDurably().Build();
        await scheduler.AddJob(job, new AddJobOptions { Replace = true });
        schedule = SimpleScheduleBuilder.Create();
        trigger = TriggerBuilder.Create().WithIdentity("trig1", "xxxyyyzzz").WithSchedule(schedule).ForJob(job).Build();
        await scheduler.ScheduleJob(trigger);

        job = JobBuilder.Create<NoOpJob>().WithIdentity("job2", "xxxyyyzzz").StoreDurably().Build();
        await scheduler.AddJob(job, new AddJobOptions { Replace = true });
        schedule = SimpleScheduleBuilder.Create();
        trigger = TriggerBuilder.Create().WithIdentity("trig2", "xxxyyyzzz").WithSchedule(schedule).ForJob(job).Build();
        await scheduler.ScheduleJob(trigger);

        var jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        Assert.That(jkeys.Count, Is.EqualTo(3), "Wrong number of jobs found by anything matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("xxxyyyzzz"));
        Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by equals matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("aaabbbccc"));
        Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by equals matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupStartsWith("aa"));
        Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by starts with matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupStartsWith("xx"));
        Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by starts with matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEndsWith("cc"));
        Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by ends with matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEndsWith("zzz"));
        Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by ends with matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupContains("bc"));
        Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by contains with matcher");

        jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupContains("yz"));
        Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by contains with matcher");

        var tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
        Assert.That(tkeys.Count, Is.EqualTo(3), "Wrong number of triggers found by anything matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("xxxyyyzzz"));
        Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by equals matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("aaabbbccc"));
        Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by equals matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith("aa"));
        Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by starts with matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith("xx"));
        Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by starts with matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEndsWith("cc"));
        Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by ends with matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEndsWith("zzz"));
        Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by ends with matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupContains("bc"));
        Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by contains with matcher");

        tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupContains("yz"));
        Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by contains with matcher");
    }

    /// <summary>
    /// Regression test for #2255 and #1416: GetTriggerState should report Executing (not Complete)
    /// while a trigger's last fire is still running.
    /// </summary>
    /// <remarks>
    /// #2255 was originally fixed by reporting Blocked here, because there was no state for "still
    /// executing". #1416 gave that case its own state, so the answer is now Executing.
    /// </remarks>
    private async Task TestGetTriggerStateExecutingWhileJobRuns(IScheduler scheduler)
    {
        await scheduler.Clear();
        await scheduler.Start();

        var jobStarted = new SemaphoreSlim(0, 1);
        var jobCanFinish = new SemaphoreSlim(0, 1);
        scheduler.Context["JobStarted_2255"] = jobStarted;
        scheduler.Context["JobCanFinish_2255"] = jobCanFinish;

        IJobDetail job = JobBuilder.Create<SignallingJob>()
            .WithIdentity("signalJob_2255", "testGroup")
            .Build();

        // Single-fire trigger — after it fires, the trigger has no next fire time
        // so the ADO store marks it COMPLETE in QRTZ_TRIGGERS while FIRED_TRIGGERS has EXECUTING
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("signalTrigger_2255", "testGroup")
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        try
        {
            // Wait for the job to start executing
            (await jobStarted.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("the job should have started within 10 seconds");

            // While the job is executing, GetTriggerState should return Executing, not Complete
            var state = await scheduler.GetTriggerState(trigger.Key);
            state.Should().Be(TriggerState.Executing,
                "GetTriggerState should return Executing while the trigger's job is running, not Complete (#2255, #1416)");

            // Let the job finish
            jobCanFinish.Release();

            // Wait for the scheduler to process completion by polling deterministically
            TriggerState finalState = TriggerState.Executing;
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                finalState = await scheduler.GetTriggerState(trigger.Key);
                if (finalState == TriggerState.None)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }

            // After job completes, single-fire trigger is removed (DeleteTrigger instruction)
            finalState.Should().Be(TriggerState.None,
                "Single-fire trigger should be removed after the job finishes executing (#2255)");
        }
        finally
        {
            // Release the job if it hasn't been released on the success path,
            // guarding against SemaphoreFullException on double-release (maxCount=1)
            if (jobCanFinish.CurrentCount == 0)
            {
                jobCanFinish.Release();
            }

            scheduler.Context.Remove("JobStarted_2255");
            scheduler.Context.Remove("JobCanFinish_2255");
            await scheduler.Standby();
        }
    }
}

public class SignallingJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var jobStarted = (SemaphoreSlim) context.Scheduler.Context["JobStarted_2255"];
        var jobCanFinish = (SemaphoreSlim) context.Scheduler.Context["JobCanFinish_2255"];

        jobStarted.Release();
        bool acquired = await jobCanFinish.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (!acquired)
        {
            throw new JobExecutionException("Job did not receive completion signal within 30 seconds");
        }
    }
}

public class GenericJobType : IJob
{
    private static readonly ManualResetEventSlim triggered = new ManualResetEventSlim();

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        TriggeredCount++;
        triggered.Set();
        return default;
    }

    public static int TriggeredCount { get; private set; }

    public static void Reset()
    {
        TriggeredCount = 0;
        triggered.Reset();
    }

    public static void WaitForTrigger(TimeSpan timeout)
    {
        triggered.Wait(timeout);
    }
}

[Serializable]
internal sealed class CustomCalendar : BaseCalendar
{
    public bool SomeCustomProperty { get; set; } = true;

    public CustomCalendar()
    {
    }

    public CustomCalendar(ICalendar baseCalendar) : base(baseCalendar)
    {
    }

    public CustomCalendar(TimeZoneInfo timeZone) : base(timeZone)
    {
    }

    public CustomCalendar(ICalendar baseCalendar, TimeZoneInfo timeZone) : base(baseCalendar, timeZone)
    {
    }

    private CustomCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        SomeCustomProperty = info?.GetBoolean("SomeCustomProperty") ?? true;
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info?.AddValue("SomeCustomProperty", SomeCustomProperty);
    }
}

internal sealed class CustomNewtonsoftCalendarSerializer : CalendarSerializer<CustomCalendar>
{
    protected override CustomCalendar Create(JObject source)
    {
        return new CustomCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, CustomCalendar calendar)
    {
        writer.WritePropertyName("SomeCustomProperty");
        writer.WriteValue(calendar.SomeCustomProperty);
    }

    protected override void DeserializeFields(CustomCalendar calendar, JObject source)
    {
        calendar.SomeCustomProperty = source["SomeCustomProperty"]!.Value<bool>();
    }
}

internal sealed class CustomSystemTextJsonCalendarSerializer : Serialization.Json.Calendars.CalendarSerializer<CustomCalendar>
{
    public override string CalendarTypeName => "Custom";

    protected override CustomCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new CustomCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, CustomCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteBoolean("SomeCustomProperty", calendar.SomeCustomProperty);
    }

    protected override void DeserializeFields(CustomCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.SomeCustomProperty = jsonElement.GetProperty("SomeCustomProperty").GetBoolean();
    }
}

[Serializable]
internal sealed class CustomTrigger : CronTriggerImpl
{
    public override bool HasAdditionalProperties => true;

    public bool SomeCustomProperty { get; set; } = true;
}

internal sealed class CustomNewtonsoftTriggerSerializer : CronTriggerSerializer
{
    public override string TriggerTypeName => "CustomTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        return new CustomTriggerScheduleBuilder();
    }

    protected override void SerializeFields(JsonWriter writer, ICronTrigger trigger)
    {
        base.SerializeFields(writer, trigger);
        writer.WritePropertyName("SomeCustomProperty");
        writer.WriteValue(((CustomTrigger) trigger).SomeCustomProperty);
    }

    protected override void DeserializeFields(ICronTrigger trigger, JObject source)
    {
        base.DeserializeFields(trigger, source);
        ((CustomTrigger) trigger).CronExpressionString = source.Value<string>("CronExpressionString");
        ((CustomTrigger) trigger).TimeZone = TimeZoneUtil.FindTimeZoneById(source.Value<string>("TimeZone")!);
        ((CustomTrigger) trigger).SomeCustomProperty = source.Value<bool>("SomeCustomProperty");
    }

    private sealed class CustomTriggerScheduleBuilder : IScheduleBuilder
    {
        public IMutableTrigger Build()
        {
            return new CustomTrigger();
        }
    }
}

internal sealed class CustomSystemTextJsonTriggerSerializer : Serialization.Json.Triggers.CronTriggerSerializer
{
    public override string TriggerTypeName => "CustomTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new CustomTriggerScheduleBuilder();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ICronTrigger trigger, JsonSerializerOptions options)
    {
        base.SerializeFields(writer, trigger, options);
        writer.WriteBoolean("SomeCustomProperty", ((CustomTrigger) trigger).SomeCustomProperty);
    }

    protected override void DeserializeFields(ICronTrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        base.DeserializeFields(trigger, jsonElement, options);
        ((CustomTrigger) trigger).CronExpressionString = jsonElement.GetProperty("CronExpressionString").GetString();
        ((CustomTrigger) trigger).TimeZone = TimeZoneUtil.FindTimeZoneById(jsonElement.GetProperty("TimeZone").GetString());
        ((CustomTrigger) trigger).SomeCustomProperty = jsonElement.GetProperty("SomeCustomProperty").GetBoolean();
    }

    private sealed class CustomTriggerScheduleBuilder : IScheduleBuilder
    {
        public IMutableTrigger Build()
        {
            return new CustomTrigger();
        }
    }
}