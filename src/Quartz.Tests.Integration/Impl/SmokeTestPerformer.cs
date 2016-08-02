using System;
using System.Collections.Generic;
using System.Threading;
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
                    await scheduler.Clear();
                }

                if (scheduleJobs)
                {
                    ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                    ICalendar holidayCalendar = new HolidayCalendar();

                    // QRTZNET-86
                    ITrigger t = await scheduler.GetTrigger(new TriggerKey("NonExistingTrigger", "NonExistingGroup"));
                    Assert.IsNull(t);

                    AnnualCalendar cal = new AnnualCalendar();
                    await scheduler.AddCalendar("annualCalendar", cal, false, true);

                    IOperableTrigger calendarsTrigger = new SimpleTriggerImpl("calendarsTrigger", "test", 20, TimeSpan.FromMilliseconds(5));
                    calendarsTrigger.CalendarName = "annualCalendar";

                    JobDetailImpl jd = new JobDetailImpl("testJob", "test", typeof (NoOpJob));
                    await scheduler.ScheduleJob(jd, calendarsTrigger);

                    // QRTZNET-93
                    await scheduler.AddCalendar("annualCalendar", cal, true, true);

                    await scheduler.AddCalendar("baseCalendar", new BaseCalendar(), false, true);
                    await scheduler.AddCalendar("cronCalendar", cronCalendar, false, true);
                    await scheduler.AddCalendar("dailyCalendar", new DailyCalendar(DateTime.Now.Date, DateTime.Now.AddMinutes(1)), false, true);
                    await scheduler.AddCalendar("holidayCalendar", holidayCalendar, false, true);
                    await scheduler.AddCalendar("monthlyCalendar", new MonthlyCalendar(), false, true);
                    await scheduler.AddCalendar("weeklyCalendar", new WeeklyCalendar(), false, true);

                    await scheduler.AddCalendar("cronCalendar", cronCalendar, true, true);
                    await scheduler.AddCalendar("holidayCalendar", holidayCalendar, true, true);

                    Assert.IsNotNull(scheduler.GetCalendar("annualCalendar"));

                    JobDetailImpl lonelyJob = new JobDetailImpl("lonelyJob", "lonelyGroup", typeof (SimpleRecoveryJob));
                    lonelyJob.Durable = true;
                    lonelyJob.RequestsRecovery = true;
                    await scheduler.AddJob(lonelyJob, false);
                    await scheduler.AddJob(lonelyJob, true);

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
                    await scheduler.ScheduleJob(job, trigger);

                    // check that trigger was stored
                    ITrigger persisted = await scheduler.GetTrigger(new TriggerKey("trig_" + count, schedId));
                    Assert.IsNotNull(persisted);
                    Assert.IsTrue(persisted is SimpleTriggerImpl);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(2000L));
                    await scheduler.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryStatefulJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(3));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    await scheduler.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(4));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    await scheduler.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                    await scheduler.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    IOperableTrigger ct = new CronTriggerImpl("cron_trig_" + count, schedId, "0/10 * * * * ?");
                    ct.JobDataMap.Add("key", "value");
                    ct.StartTimeUtc = DateTime.Now.AddMilliseconds(1000);

                    await scheduler.ScheduleJob(job, ct);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    DailyTimeIntervalTriggerImpl nt = new DailyTimeIntervalTriggerImpl("nth_trig_" + count, schedId, new TimeOfDay(1, 1, 1), new TimeOfDay(23, 30, 0), IntervalUnit.Hour, 1);
                    nt.StartTimeUtc = DateTime.Now.Date.AddMilliseconds(1000);

                    await scheduler.ScheduleJob(job, nt);

                    DailyTimeIntervalTriggerImpl nt2 = new DailyTimeIntervalTriggerImpl();
                    nt2.Key = new TriggerKey("nth_trig2_" + count, schedId);
                    nt2.StartTimeUtc = DateTime.Now.Date.AddMilliseconds(1000);
                    nt2.JobKey = job.Key;
                    await scheduler.ScheduleJob(nt2);

                    // GitHub issue #92
                    await scheduler.GetTrigger(nt2.Key);

                    // GitHub issue #98
                    nt2.StartTimeOfDay = new TimeOfDay(1, 2, 3);
                    nt2.EndTimeOfDay = new TimeOfDay(2, 3, 4);

                    await scheduler.UnscheduleJob(nt2.Key);
                    await scheduler.ScheduleJob(nt2);

                    var triggerFromDb = (IDailyTimeIntervalTrigger) await scheduler.GetTrigger(nt2.Key);
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

                    await scheduler.ScheduleJob(intervalTrigger);

                    // bulk operations
                    var info = new Dictionary<IJobDetail, ISet<ITrigger>>();
                    IJobDetail detail = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    ITrigger simple = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                    var triggers = new HashSet<ITrigger>();
                    triggers.Add(simple);
                    info[detail] = triggers;

                    await scheduler.ScheduleJobs(info, true);

                    Assert.IsTrue(await scheduler.CheckExists(detail.Key));
                    Assert.IsTrue(await scheduler.CheckExists(simple.Key));

                    // QRTZNET-243
                    await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupContains("a").DeepClone());
                    await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEndsWith("a").DeepClone());
                    await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupStartsWith("a").DeepClone());
                    await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("a").DeepClone());

                    await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupContains("a").DeepClone());
                    await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEndsWith("a").DeepClone());
                    await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith("a").DeepClone());
                    await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("a").DeepClone());

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
                    Assert.AreEqual(1, pausedTriggerGroups.Count);

                    await Task.Delay(TimeSpan.FromSeconds(3));
                    await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(schedId));

                    Assert.IsNotNull(scheduler.GetTrigger(new TriggerKey("trig_2", schedId)));
                    Assert.IsNotNull(scheduler.GetJobDetail(new JobKey("job_1", schedId)));
                    Assert.IsNotNull(scheduler.GetMetaData());
                    Assert.IsNotNull(scheduler.GetCalendar("weeklyCalendar"));

                    var genericjobKey = new JobKey("genericJob", "genericGroup");
                    GenericJobType.Reset();
                    var genericJob = JobBuilder.Create<GenericJobType>()
                        .WithIdentity(genericjobKey)
                        .StoreDurably()
                        .Build();

                    await scheduler.AddJob(genericJob, false);

                    genericJob = await scheduler.GetJobDetail(genericjobKey);
                    Assert.That(genericJob, Is.Not.Null);
                    await scheduler.TriggerJob(genericjobKey);

                    GenericJobType.WaitForTrigger(TimeSpan.FromSeconds(20));

                    Assert.That(GenericJobType.TriggeredCount, Is.EqualTo(1));
                    await scheduler.Standby();

                    CollectionAssert.IsNotEmpty(await scheduler.GetCalendarNames());
                    CollectionAssert.IsNotEmpty(await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(schedId)));

                    CollectionAssert.IsNotEmpty(await scheduler.GetTriggersOfJob(new JobKey("job_2", schedId)));
                    Assert.IsNotNull(scheduler.GetJobDetail(new JobKey("job_2", schedId)));

                    await scheduler.DeleteCalendar("cronCalendar");
                    await scheduler.DeleteCalendar("holidayCalendar");
                    await scheduler.DeleteJob(new JobKey("lonelyJob", "lonelyGroup"));
                    await scheduler.DeleteJob(job.Key);

                    await scheduler.GetJobGroupNames();
                    await scheduler.GetCalendarNames();
                    await scheduler.GetTriggerGroupNames();

                    await TestMatchers(scheduler);
                }
            }
            finally
            {
                await scheduler.Shutdown(false);
            }
        }

        private async Task TestMatchers(IScheduler scheduler)
        {
            await scheduler.Clear();

            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "aaabbbccc").StoreDurably().Build();
            await scheduler.AddJob(job, true);
            SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create();
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("trig1", "aaabbbccc").WithSchedule(schedule).ForJob(job).Build();
            await scheduler.ScheduleJob(trigger);

            job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "xxxyyyzzz").StoreDurably().Build();
            await scheduler.AddJob(job, true);
            schedule = SimpleScheduleBuilder.Create();
            trigger = TriggerBuilder.Create().WithIdentity("trig1", "xxxyyyzzz").WithSchedule(schedule).ForJob(job).Build();
            await scheduler.ScheduleJob(trigger);

            job = JobBuilder.Create<NoOpJob>().WithIdentity("job2", "xxxyyyzzz").StoreDurably().Build();
            await scheduler.AddJob(job, true);
            schedule = SimpleScheduleBuilder.Create();
            trigger = TriggerBuilder.Create().WithIdentity("trig2", "xxxyyyzzz").WithSchedule(schedule).ForJob(job).Build();
            await scheduler.ScheduleJob(trigger);

            ISet<JobKey> jkeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
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

            ISet<TriggerKey> tkeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
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
    }

    public class GenericJobType : IJob
    {
        private static readonly ManualResetEventSlim triggered = new ManualResetEventSlim();

        public Task Execute(IJobExecutionContext context)
        {
            TriggeredCount++;
            triggered.Set();
            return Task.FromResult(0);
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
}