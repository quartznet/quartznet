using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Spi;
using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl
{
    public class SmokeTestPerformer
    {
        public async Task Test(IScheduler scheduler, bool clearJobs, bool scheduleJobs)
        {
            try
            {
                if (clearJobs)
                {
                    await scheduler.ClearAsync();
                }

                if (scheduleJobs)
                {
                    ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                    ICalendar holidayCalendar = new HolidayCalendar();

                    // QRTZNET-86
                    ITrigger t = await scheduler.GetTriggerAsync(new TriggerKey("NonExistingTrigger", "NonExistingGroup"));
                    Assert.IsNull(t);

                    AnnualCalendar cal = new AnnualCalendar();
                    await scheduler.AddCalendarAsync("annualCalendar", cal, false, true);

                    IOperableTrigger calendarsTrigger = new SimpleTriggerImpl("calendarsTrigger", "test", 20, TimeSpan.FromMilliseconds(5));
                    calendarsTrigger.CalendarName = "annualCalendar";

                    JobDetailImpl jd = new JobDetailImpl("testJob", "test", typeof (NoOpJob));
                    await scheduler.ScheduleJobAsync(jd, calendarsTrigger);

                    // QRTZNET-93
                    await scheduler.AddCalendarAsync("annualCalendar", cal, true, true);

                    await scheduler.AddCalendarAsync("baseCalendar", new BaseCalendar(), false, true);
                    await scheduler.AddCalendarAsync("cronCalendar", cronCalendar, false, true);
                    await scheduler.AddCalendarAsync("dailyCalendar", new DailyCalendar(DateTime.Now.Date, DateTime.Now.AddMinutes(1)), false, true);
                    await scheduler.AddCalendarAsync("holidayCalendar", holidayCalendar, false, true);
                    await scheduler.AddCalendarAsync("monthlyCalendar", new MonthlyCalendar(), false, true);
                    await scheduler.AddCalendarAsync("weeklyCalendar", new WeeklyCalendar(), false, true);

                    await scheduler.AddCalendarAsync("cronCalendar", cronCalendar, true, true);
                    await scheduler.AddCalendarAsync("holidayCalendar", holidayCalendar, true, true);

                    Assert.IsNotNull(scheduler.GetCalendarAsync("annualCalendar"));

                    JobDetailImpl lonelyJob = new JobDetailImpl("lonelyJob", "lonelyGroup", typeof (SimpleRecoveryJob));
                    lonelyJob.Durable = true;
                    lonelyJob.RequestsRecovery = true;
                    await scheduler.AddJobAsync(lonelyJob, false);
                    await scheduler.AddJobAsync(lonelyJob, true);

                    string schedId = scheduler.SchedulerInstanceId;

                    int count = 1;

                    JobDetailImpl job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));

                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = true;
                    IOperableTrigger trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));
                    trigger.JobDataMap.Add("key", "value");
                    trigger.EndTimeUtc = DateTime.UtcNow.AddYears(10);

                    trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
                    await scheduler.ScheduleJobAsync(job, trigger);

                    // check that trigger was stored
                    ITrigger persisted = await scheduler.GetTriggerAsync(new TriggerKey("trig_" + count, schedId));
                    Assert.IsNotNull(persisted);
                    Assert.IsTrue(persisted is SimpleTriggerImpl);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(2000L));
                    await scheduler.ScheduleJobAsync(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryStatefulJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(3));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    await scheduler.ScheduleJobAsync(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(4));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    await scheduler.ScheduleJobAsync(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                    await scheduler.ScheduleJobAsync(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    IOperableTrigger ct = new CronTriggerImpl("cron_trig_" + count, schedId, "0/10 * * * * ?");
                    ct.JobDataMap.Add("key", "value");
                    ct.StartTimeUtc = DateTime.Now.AddMilliseconds(1000);

                    await scheduler.ScheduleJobAsync(job, ct);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    DailyTimeIntervalTriggerImpl nt = new DailyTimeIntervalTriggerImpl("nth_trig_" + count, schedId, new TimeOfDay(1, 1, 1), new TimeOfDay(23, 30, 0), IntervalUnit.Hour, 1);
                    nt.StartTimeUtc = DateTime.Now.Date.AddMilliseconds(1000);

                    await scheduler.ScheduleJobAsync(job, nt);

                    DailyTimeIntervalTriggerImpl nt2 = new DailyTimeIntervalTriggerImpl();
                    nt2.Key = new TriggerKey("nth_trig2_" + count, schedId);
                    nt2.StartTimeUtc = DateTime.Now.Date.AddMilliseconds(1000);
                    nt2.JobKey = job.Key;
                    await scheduler.ScheduleJobAsync(nt2);

                    // GitHub issue #92
                    await scheduler.GetTriggerAsync(nt2.Key);

                    // GitHub issue #98
                    nt2.StartTimeOfDay = new TimeOfDay(1, 2, 3);
                    nt2.EndTimeOfDay = new TimeOfDay(2, 3, 4);

                    await scheduler.UnscheduleJobAsync(nt2.Key);
                    await scheduler.ScheduleJobAsync(nt2);

                    var triggerFromDb = (IDailyTimeIntervalTrigger) await scheduler.GetTriggerAsync(nt2.Key);
                    Assert.That(triggerFromDb.StartTimeOfDay.Hour, Is.EqualTo(1));
                    Assert.That(triggerFromDb.StartTimeOfDay.Minute, Is.EqualTo(2));
                    Assert.That(triggerFromDb.StartTimeOfDay.Second, Is.EqualTo(3));

                    Assert.That(triggerFromDb.EndTimeOfDay.Hour, Is.EqualTo(2));
                    Assert.That(triggerFromDb.EndTimeOfDay.Minute, Is.EqualTo(3));
                    Assert.That(triggerFromDb.EndTimeOfDay.Second, Is.EqualTo(4));

                    job.RequestsRecovery = (true);
                    CalendarIntervalTriggerImpl intervalTrigger = new CalendarIntervalTriggerImpl(
                        "calint_trig_" + count,
                        schedId,
                        DateTime.UtcNow.AddMilliseconds(300),
                        DateTime.UtcNow.AddMinutes(1),
                        IntervalUnit.Second,
                        8);
                    intervalTrigger.JobKey = job.Key;

                    await scheduler.ScheduleJobAsync(intervalTrigger);

                    // bulk operations
                    var info = new Dictionary<IJobDetail, ISet<ITrigger>>();
                    IJobDetail detail = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    ITrigger simple = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                    var triggers = new HashSet<ITrigger>();
                    triggers.Add(simple);
                    info[detail] = triggers;

                    await scheduler.ScheduleJobsAsync(info, true);

                    Assert.IsTrue(await scheduler.CheckExistsAsync(detail.Key));
                    Assert.IsTrue(await scheduler.CheckExistsAsync(simple.Key));

                    // QRTZNET-243
                    await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupContains("a").DeepClone());
                    await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEndsWith("a").DeepClone());
                    await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupStartsWith("a").DeepClone());
                    await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals("a").DeepClone());

                    await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupContains("a").DeepClone());
                    await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEndsWith("a").DeepClone());
                    await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupStartsWith("a").DeepClone());
                    await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals("a").DeepClone());

                    await scheduler.StartAsync();

                    await Task.Delay(TimeSpan.FromSeconds(3));

                    await scheduler.PauseAllAsync();

                    await scheduler.ResumeAllAsync();

                    await scheduler.PauseJobAsync(new JobKey("job_1", schedId));

                    await scheduler.ResumeJobAsync(new JobKey("job_1", schedId));

                    await scheduler.PauseJobsAsync(GroupMatcher<JobKey>.GroupEquals(schedId));

                    await Task.Delay(TimeSpan.FromSeconds(1));

                    await scheduler.ResumeJobsAsync(GroupMatcher<JobKey>.GroupEquals(schedId));

                    await scheduler.PauseTriggerAsync(new TriggerKey("trig_2", schedId));
                    await scheduler.ResumeTriggerAsync(new TriggerKey("trig_2", schedId));

                    await scheduler.PauseTriggersAsync(GroupMatcher<TriggerKey>.GroupEquals(schedId));

                    var pausedTriggerGroups = await scheduler.GetPausedTriggerGroupsAsync();
                    Assert.AreEqual(1, pausedTriggerGroups.Count);

                    await Task.Delay(TimeSpan.FromSeconds(3));
                    await scheduler.ResumeTriggersAsync(GroupMatcher<TriggerKey>.GroupEquals(schedId));

                    Assert.IsNotNull(scheduler.GetTriggerAsync(new TriggerKey("trig_2", schedId)));
                    Assert.IsNotNull(scheduler.GetJobDetailAsync(new JobKey("job_1", schedId)));
                    Assert.IsNotNull(scheduler.GetMetaDataAsync());
                    Assert.IsNotNull(scheduler.GetCalendarAsync("weeklyCalendar"));

                    var genericjobKey = new JobKey("genericJob", "genericGroup");
                    var genericJob = JobBuilder.Create<GenericJobType<string>>()
                        .WithIdentity(genericjobKey)
                        .StoreDurably()
                        .Build();

                    await scheduler.AddJobAsync(genericJob, false);

                    genericJob = await scheduler.GetJobDetailAsync(genericjobKey);
                    Assert.That(genericJob, Is.Not.Null);
                    await scheduler.TriggerJobAsync(genericjobKey);

                    await Task.Delay(TimeSpan.FromSeconds(20));

                    Assert.That(GenericJobType<string>.TriggeredCount, Is.EqualTo(1));
                    await scheduler.StandbyAsync();

                    CollectionAssert.IsNotEmpty(await scheduler.GetCalendarNamesAsync());
                    CollectionAssert.IsNotEmpty(await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals(schedId)));

                    CollectionAssert.IsNotEmpty(await scheduler.GetTriggersOfJobAsync(new JobKey("job_2", schedId)));
                    Assert.IsNotNull(scheduler.GetJobDetailAsync(new JobKey("job_2", schedId)));

                    await scheduler.DeleteCalendarAsync("cronCalendar");
                    await scheduler.DeleteCalendarAsync("holidayCalendar");
                    await scheduler.DeleteJobAsync(new JobKey("lonelyJob", "lonelyGroup"));
                    await scheduler.DeleteJobAsync(job.Key);

                    await scheduler.GetJobGroupNamesAsync();
                    await scheduler.GetCalendarNamesAsync();
                    await scheduler.GetTriggerGroupNamesAsync();

                    await TestMatchers(scheduler);
                }
            }
            finally
            {
                await scheduler.ShutdownAsync(false);
            }
        }

        private async Task TestMatchers(IScheduler scheduler)
        {
            await scheduler.ClearAsync();

            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "aaabbbccc").StoreDurably().Build();
            await scheduler.AddJobAsync(job, true);
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create();
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("trig1", "aaabbbccc").WithSchedule(schedule).ForJob(job).Build();
            await scheduler.ScheduleJobAsync(trigger);

            job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "xxxyyyzzz").StoreDurably().Build();
            await scheduler.AddJobAsync(job, true);
            schedule = SimpleScheduleBuilder.Create();
            trigger = TriggerBuilder.Create().WithIdentity("trig1", "xxxyyyzzz").WithSchedule(schedule).ForJob(job).Build();
            await scheduler.ScheduleJobAsync(trigger);

            job = JobBuilder.Create<NoOpJob>().WithIdentity("job2", "xxxyyyzzz").StoreDurably().Build();
            await scheduler.AddJobAsync(job, true);
            schedule = SimpleScheduleBuilder.Create();
            trigger = TriggerBuilder.Create().WithIdentity("trig2", "xxxyyyzzz").WithSchedule(schedule).ForJob(job).Build();
            await scheduler.ScheduleJobAsync(trigger);

            ISet<JobKey> jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.AnyGroup());
            Assert.That(jkeys.Count, Is.EqualTo(3), "Wrong number of jobs found by anything matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals("xxxyyyzzz"));
            Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by equals matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals("aaabbbccc"));
            Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by equals matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupStartsWith("aa"));
            Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by starts with matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupStartsWith("xx"));
            Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by starts with matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEndsWith("cc"));
            Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by ends with matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEndsWith("zzz"));
            Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by ends with matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupContains("bc"));
            Assert.That(jkeys.Count, Is.EqualTo(1), "Wrong number of jobs found by contains with matcher");

            jkeys = await scheduler.GetJobKeysAsync(GroupMatcher<JobKey>.GroupContains("yz"));
            Assert.That(jkeys.Count, Is.EqualTo(2), "Wrong number of jobs found by contains with matcher");

            ISet<TriggerKey> tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.AnyGroup());
            Assert.That(tkeys.Count, Is.EqualTo(3), "Wrong number of triggers found by anything matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals("xxxyyyzzz"));
            Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by equals matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals("aaabbbccc"));
            Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by equals matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupStartsWith("aa"));
            Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by starts with matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupStartsWith("xx"));
            Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by starts with matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEndsWith("cc"));
            Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by ends with matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEndsWith("zzz"));
            Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by ends with matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupContains("bc"));
            Assert.That(tkeys.Count, Is.EqualTo(1), "Wrong number of triggers found by contains with matcher");

            tkeys = await scheduler.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupContains("yz"));
            Assert.That(tkeys.Count, Is.EqualTo(2), "Wrong number of triggers found by contains with matcher");
        }
    }

    public class GenericJobType<T> : IJob
    {
        public void Execute(IJobExecutionContext context)
        {
            TriggeredCount++;
        }

        public static int TriggeredCount { get; private set; }
    }
}