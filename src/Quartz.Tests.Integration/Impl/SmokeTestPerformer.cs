using System.Runtime.Serialization;
using System.Text.Json;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Serialization.Newtonsoft;
using Quartz.Spi;
using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Triggers;
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

                AnnualCalendar cal = new AnnualCalendar();
                cal.SetDayExcluded(new DateTime(2018, 7, 4), true);
                await scheduler.AddCalendar("annualCalendar", cal, false, true);

                IOperableTrigger calendarsTrigger = new SimpleTriggerImpl("calendarsTrigger", "test", 20, TimeSpan.FromHours(2));
                calendarsTrigger.CalendarName = "annualCalendar";

                var jd = JobBuilder.Create<NoOpJob>()
                    .WithIdentity(new JobKey("testJob", "test"))
                    .Build();
                await scheduler.ScheduleJob(jd, calendarsTrigger);

                // QRTZNET-93
                await scheduler.AddCalendar("annualCalendar", cal, true, true);

                var annualCalendar = (AnnualCalendar) await scheduler.GetCalendar("annualCalendar");
                Assert.That(annualCalendar.Description, Is.EqualTo(cal.Description));
                Assert.That(annualCalendar.DaysExcluded, Is.EquivalentTo(cal.DaysExcluded));

                await scheduler.AddCalendar("baseCalendar", new BaseCalendar(), false, true);
                await scheduler.AddCalendar("cronCalendar", cronCalendar, false, true);
                await scheduler.AddCalendar("dailyCalendar", new DailyCalendar(DateTime.Now.Date, DateTime.Now.AddMinutes(1)), false, true);
                await scheduler.AddCalendar("holidayCalendar", holidayCalendar, false, true);
                await scheduler.AddCalendar("monthlyCalendar", new MonthlyCalendar(), false, true);
                await scheduler.AddCalendar("weeklyCalendar", new WeeklyCalendar(), false, true);

                await scheduler.AddCalendar("cronCalendar", cronCalendar, true, true);
                await scheduler.AddCalendar("holidayCalendar", holidayCalendar, true, true);

                await scheduler.AddCalendar("customCalendar", new CustomCalendar(), true, true);
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

                await scheduler.AddJob(lonelyJob, false);
                await scheduler.AddJob(lonelyJob, true);

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

                DailyTimeIntervalTriggerImpl nt = new DailyTimeIntervalTriggerImpl("nth_trig_" + count, schedId, new TimeOfDay(1, 1, 1), new TimeOfDay(23, 30, 0), IntervalUnit.Hour, 1);
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
                var nextFireTimeUtc = customTrigger.GetNextFireTimeUtc();

                await scheduler.ScheduleJob(customTrigger);
                var loadedCustomTrigger = (CustomTrigger) await scheduler.GetTrigger(customTrigger.Key);
                Assert.That(loadedCustomTrigger.GetNextFireTimeUtc(), Is.EqualTo(nextFireTimeUtc));
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
                Assert.That(await scheduler.GetMetaData(), Is.Not.Null);
                Assert.That(await scheduler.GetCalendar("weeklyCalendar"), Is.Not.Null);

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
}

public class GenericJobType : IJob
{
    private static readonly ManualResetEventSlim triggered = new ManualResetEventSlim();

    public ValueTask Execute(IJobExecutionContext context)
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

internal class CustomNewtonsoftTriggerSerializer : CronTriggerSerializer
{
    public override string TriggerTypeForJson => "CustomTrigger";

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

    private class CustomTriggerScheduleBuilder : ScheduleBuilder<CustomTrigger>
    {
        public override IMutableTrigger Build()
        {
            return new CustomTrigger();
        }
    }
}

internal class CustomSystemTextJsonTriggerSerializer : Serialization.Json.Triggers.CronTriggerSerializer
{
    public override string TriggerTypeForJson => "CustomTrigger";

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

    private class CustomTriggerScheduleBuilder : ScheduleBuilder<CustomTrigger>
    {
        public override IMutableTrigger Build()
        {
            return new CustomTrigger();
        }
    }
}