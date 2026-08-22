using System.Text.Json;

using Quartz.Impl.Triggers;
using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Triggers;
using Quartz.Extensibility;

namespace Quartz.Tests.Integration;

public abstract class AbstractSchedulerTest
{
    protected readonly string provider;
    protected readonly string serializerType;

    private const string Barrier = "BARRIER";
    private const string DateStamps = "DATE_STAMPS";

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private static readonly TimeSpan testTimeout = TimeSpan.FromSeconds(125);

    protected AbstractSchedulerTest(string provider, string serializerType)
    {
        this.provider = provider;
        this.serializerType = serializerType;
    }

    public class TestJobWithSync : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                List<DateTime> jobExecTimestamps = (List<DateTime>) context.Scheduler.Context[DateStamps];
                Barrier barrier = (Barrier) context.Scheduler.Context[Barrier];

                jobExecTimestamps.Add(DateTime.UtcNow);

                barrier.SignalAndWait(testTimeout);
                return default;
            }
            catch (Exception e)
            {
                Console.Write(e);
                Assert.Fail("Await on barrier was interrupted: " + e);
            }
            return default;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    protected abstract ValueTask<IScheduler> CreateScheduler(string name, int threadPoolSize);

    [Test]
    public async Task TestBasicStorageFunctions()
    {
        IScheduler scheduler = await CreateScheduler("testBasicStorageFunctions", 2);
        await scheduler.Clear();

        // test basic storage functions of scheduler...
        IJobDetail job = JobBuilder.Create<TestJob>()
            .WithIdentity("j1")
            .StoreDurably()
            .Build();

        Assert.That(await scheduler.Exists(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

        await scheduler.AddJob(job);

        Assert.That(await scheduler.Exists(new JobKey("j1")), "Expected existence of job named 'j1' but checkExists return false.");

        job = await scheduler.GetJobDetail(new JobKey("j1"));

        Assert.That(job, Is.Not.Null, "Stored job not found!");

        await scheduler.DeleteJob(new JobKey("j1"));

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        Assert.That(await scheduler.Exists(new TriggerKey("t1")), Is.False, "Unexpected existence of trigger named '11'.");

        await scheduler.ScheduleJob(job, trigger);

        Assert.That(await scheduler.Exists(new TriggerKey("t1")), "Expected existence of trigger named 't1' but checkExists return false.");

        job = await scheduler.GetJobDetail(new JobKey("j1"));

        Assert.That(job, Is.Not.Null, "Stored job not found!");

        trigger = await scheduler.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null, "Stored trigger not found!");

        job = JobBuilder.Create<TestJob>()
            .WithIdentity("j2", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t2", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        job = JobBuilder.Create<TestJob>()
            .WithIdentity("j3", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t3", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        var jobGroups = await scheduler.GetJobGroupNames();
        var triggerGroups = await scheduler.GetTriggerGroupNames();

        Assert.That(jobGroups.Count, Is.EqualTo(2), "Job group list size expected to be = 2 ");
        Assert.That(triggerGroups.Count, Is.EqualTo(2), "Trigger group list size expected to be = 2 ");

        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
        var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

        Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 ");
        Assert.That(triggerKeys.Count, Is.EqualTo(1), "Number of triggers expected in default group was 1 ");

        jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
        triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 2 ");
        Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 2 ");

        TriggerState s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        await scheduler.PauseTrigger(new TriggerKey("t2", "g1"));
        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

        await scheduler.ResumeTrigger(new TriggerKey("t2", "g1"));
        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        var pausedGroups = await scheduler.GetPausedTriggerGroups();
        Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        // test that adding a trigger to a paused group causes the new trigger to be paused also...
        job = JobBuilder.Create<TestJob>()
            .WithIdentity("j4", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t4", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        pausedGroups = await scheduler.GetPausedTriggerGroups();
        Assert.That(pausedGroups.Count, Is.EqualTo(1), "Size of paused trigger groups list expected to be 1 ");

        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

        s = await scheduler.GetTriggerState(new TriggerKey("t4", "g1"));
        Assert.That(s.Equals(TriggerState.Paused), "State of trigger t4 expected to be PAUSED ");

        await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));
        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");
        s = await scheduler.GetTriggerState(new TriggerKey("t4", "g1"));
        Assert.That(s.Equals(TriggerState.Normal), "State of trigger t4 expected to be NORMAL ");
        pausedGroups = await scheduler.GetPausedTriggerGroups();
        Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

        Assert.That(await scheduler.UnscheduleJob(new TriggerKey("foasldfksajdflk")), Is.False, "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");

        Assert.That(await scheduler.UnscheduleJob(new TriggerKey("t3", "g1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

        jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
        triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
        Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 1 ");

        Assert.That(await scheduler.UnscheduleJob(new TriggerKey("t1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

        jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
        triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

        Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
        Assert.That(triggerKeys, Is.Empty, "Number of triggers expected in default group was 0 ");

        await scheduler.Shutdown();
    }

    [Test]
    public async Task TestUpdatingTriggerTypes()
    {
        var scheduler = await CreateScheduler("testUpdatingTriggerTypes", 2);
        await scheduler.Clear();

        // test basic storage functions of scheduler...
        var job = JobBuilder.Create<TestJob>()
            .WithIdentity("j1")
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x
                .RepeatForever()
                .WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        trigger = await scheduler.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger, Is.InstanceOf<SimpleTriggerImpl>());
        var simpleTrigger = (SimpleTriggerImpl) trigger;
        Assert.That(simpleTrigger.RepeatCount, Is.EqualTo(-1));
        Assert.That(simpleTrigger.RepeatInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));


        trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .WithCronSchedule("0/5 * * * * ?")
            .Build();

        await scheduler.ScheduleJob(job, [trigger], new ScheduleJobOptions { Replace = true });

        trigger = await scheduler.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger, Is.InstanceOf<CronTriggerImpl>());
        var cronTrigger = (CronTriggerImpl) trigger;
        Assert.That(cronTrigger.CronExpressionString, Is.EqualTo("0/5 * * * * ?"));

        var blobTrigger = new TestBlobCronTriggerImpl
        {
            StartTimeUtc = DateTimeOffset.UtcNow,
            Key = new TriggerKey("t1"),
            CronExpression = new CronExpression("0/10 * * * * ?")
        };

        await scheduler.ScheduleJob(job, [blobTrigger], new ScheduleJobOptions { Replace = true });

        trigger = await scheduler.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger, Is.InstanceOf<TestBlobCronTriggerImpl>());
        blobTrigger = (TestBlobCronTriggerImpl) trigger;
        Assert.That(blobTrigger.CronExpressionString, Is.EqualTo("0/10 * * * * ?"));

        trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .WithCalendarIntervalSchedule(x =>
                x.WithInterval(5, IntervalUnit.Day))
            .Build();

        await scheduler.ScheduleJob(job, [trigger], new ScheduleJobOptions { Replace = true });

        trigger = await scheduler.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger, Is.InstanceOf<CalendarIntervalTriggerImpl>());
        var calendarTrigger = (CalendarIntervalTriggerImpl) trigger;
        Assert.That(calendarTrigger.RepeatInterval, Is.EqualTo(5));
        Assert.That(calendarTrigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Day));


        trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .WithDailyTimeIntervalSchedule(x =>
                x.WithInterval(30, IntervalUnit.Minute))
            .Build();

        await scheduler.ScheduleJob(job, [trigger], new ScheduleJobOptions { Replace = true });

        trigger = await scheduler.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger, Is.InstanceOf<DailyTimeIntervalTriggerImpl>());
        var dailyTimeIntervalTrigger = (DailyTimeIntervalTriggerImpl) trigger;
        Assert.That(dailyTimeIntervalTrigger.RepeatInterval, Is.EqualTo(30));
        Assert.That(dailyTimeIntervalTrigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Minute));

        await scheduler.Shutdown();
    }

    [Test]
    public async Task TestAbilityToFireImmediatelyWhenStartedBefore()
    {
        List<DateTime> jobExecTimestamps = [];
        Barrier barrier = new Barrier(2);

        IScheduler scheduler = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedBefore", 5);
        scheduler.Context[Barrier] = barrier;
        scheduler.Context[DateStamps] = jobExecTimestamps;
        await scheduler.Start();

        IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
            .WithIdentity("job1")
            .Build();

        ITrigger trigger1 = TriggerBuilder.Create()
            .ForJob(job1)
            .Build();

        DateTime sTime = DateTime.UtcNow;

        await scheduler.ScheduleJob(job1, trigger1);

        barrier.SignalAndWait(testTimeout);

        await scheduler.Shutdown(false);

        DateTime fTime = jobExecTimestamps[0];

        Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time.");
    }

    [Test]
    public async Task TestAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob()
    {
        List<DateTime> jobExecTimestamps = [];
        Barrier barrier = new Barrier(2);

        IScheduler scheduler = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob", 5);
        await scheduler.Clear();

        scheduler.Context[Barrier] = barrier;
        scheduler.Context[DateStamps] = jobExecTimestamps;

        await scheduler.Start();

        IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
            .WithIdentity("job1").
            StoreDurably().Build();
        await scheduler.AddJob(job1);

        DateTime sTime = DateTime.UtcNow;

        await scheduler.TriggerJob(job1.Key);

        barrier.SignalAndWait(testTimeout);

        await scheduler.Shutdown(false);

        DateTime fTime = jobExecTimestamps[0];

        Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
    }

    [Test]
    public async Task TestAbilityToFireImmediatelyWhenStartedAfter()
    {
        List<DateTime> jobExecTimestamps = [];

        Barrier barrier = new Barrier(2);

        IScheduler scheduler = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedAfter", 5);
        await scheduler.Clear();
        scheduler.Context[Barrier] = barrier;
        scheduler.Context[DateStamps] = jobExecTimestamps;

        IJobDetail job1 = JobBuilder.Create<TestJobWithSync>().WithIdentity("job1").Build();
        ITrigger trigger1 = TriggerBuilder.Create().ForJob(job1).Build();

        DateTime sTime = DateTime.UtcNow;

        await scheduler.ScheduleJob(job1, trigger1);
        await scheduler.Start();

        barrier.SignalAndWait(testTimeout);

        await scheduler.Shutdown(false);

        DateTime fTime = jobExecTimestamps[0];

        Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
    }

    [Test]
    public async Task TestScheduleMultipleTriggersForAJob()
    {
        IJobDetail job = JobBuilder.Create<TestJob>().WithIdentity("job1", "group1").Build();
        ITrigger trigger1 = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
            .Build();
        ITrigger trigger2 = TriggerBuilder.Create()
            .WithIdentity("trigger2", "group1")
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
            .Build();

        var triggersForJob = new List<ITrigger>();
        triggersForJob.Add(trigger1);
        triggersForJob.Add(trigger2);

        IScheduler scheduler = await CreateScheduler("testScheduleMultipleTriggersForAJob", 5);
        await scheduler.ScheduleJob(job, triggersForJob, new ScheduleJobOptions { Replace = true });

        var triggersOfJob = await scheduler.GetTriggersOfJob(job.Key);
        Assert.That(triggersOfJob.Count, Is.EqualTo(2));
        Assert.That(triggersOfJob.Contains(trigger1));
        Assert.That(triggersOfJob.Contains(trigger2));

        await scheduler.Shutdown(false);
    }

    [Test]
    public async Task TestDurableStorageFunctions()
    {
        IScheduler scheduler = await CreateScheduler("testDurableStorageFunctions", 2);
        await scheduler.Clear();

        // test basic storage functions of scheduler...

        IJobDetail job = JobBuilder.Create<TestJob>()
            .WithIdentity("j1")
            .StoreDurably()
            .Build();

        Assert.That(await scheduler.Exists(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

        await scheduler.AddJob(job);

        Assert.That(await scheduler.Exists(new JobKey("j1")), "Unexpected non-existence of job named 'j1'.");

        IJobDetail nonDurableJob = JobBuilder.Create<TestJob>()
            .WithIdentity("j2")
            .Build();

        try
        {
            await scheduler.AddJob(nonDurableJob);
            Assert.Fail("Storage of non-durable job should not have succeeded.");
        }
        catch (SchedulerException)
        {
            Assert.That(await scheduler.Exists(new JobKey("j2")), Is.False, "Unexpected existence of job named 'j2'.");
        }

        await scheduler.AddJob(nonDurableJob, new AddJobOptions { StoreNonDurableWhileAwaitingScheduling = true });

        Assert.That(await scheduler.Exists(new JobKey("j2")), "Unexpected non-existence of job named 'j2'.");
    }

    [Test]
    public async Task TestShutdownWithoutWaitIsUnclean()
    {
        List<DateTime> jobExecTimestamps = [];
        Barrier barrier = new Barrier(2);
        IScheduler scheduler = await CreateScheduler("testShutdownWithoutWaitIsUnclean", 8);
        try
        {
            scheduler.Context[Barrier] = barrier;
            scheduler.Context[DateStamps] = jobExecTimestamps;
            await scheduler.Start();
            string jobName = Guid.NewGuid().ToString();
            await scheduler.AddJob(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build());
            await scheduler.ScheduleJob(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
            while ((await scheduler.QueryFireInstances(new FireInstanceQuery())).Items.Count == 0)
            {
                await Task.Delay(50);
            }
        }
        finally
        {
            await scheduler.Shutdown(false);
        }

        barrier.SignalAndWait(testTimeout);
    }

    [Test]
    public async Task TestShutdownWithWaitIsClean()
    {
        bool shutdown = false;
        List<DateTime> jobExecTimestamps = [];
        Barrier barrier = new Barrier(2);
        IScheduler scheduler = await CreateScheduler("testShutdownWithoutWaitIsUnclean", 8);
        try
        {
            scheduler.Context[Barrier] = barrier;
            scheduler.Context[DateStamps] = jobExecTimestamps;
            await scheduler.Start();
            string jobName = Guid.NewGuid().ToString();
            await scheduler.AddJob(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build());
            await scheduler.ScheduleJob(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
            while ((await scheduler.QueryFireInstances(new FireInstanceQuery())).Items.Count == 0)
            {
                await Task.Delay(50);
            }
        }
        finally
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    await scheduler.Shutdown(true);
                    shutdown = true;
                }
                catch (SchedulerException ex)
                {
                    throw new Exception("exception: " + ex.Message, ex);
                }
            });
            await Task.Delay(1000);
            Assert.That(shutdown, Is.False);
            barrier.SignalAndWait(testTimeout);
            await task;
            Assert.That(shutdown, Is.True);
        }
    }

    protected string CreateSchedulerName(string name)
    {
        return $"{name}_Scheduler_{provider}_{serializerType}";
    }

    public class TestBlobCronTriggerImpl : CronTriggerImpl
    {
        public override bool HasAdditionalProperties => true;

        public sealed class SystemTextJsonSerializer : TriggerSerializer<TestBlobCronTriggerImpl>
        {
            public override string TriggerTypeName => "TestBlobCronTrigger";

            public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
            {
                var cronExpressionString = jsonElement.GetProperty("CronExpressionString").GetString()!;
                var timeZone = jsonElement.GetProperty("TimeZone").GetTimeZone();

                var trigger = new TestBlobCronTriggerImpl
                {
                    CronExpression = new CronExpression(cronExpressionString),
                    TimeZone = timeZone,
                    MisfireInstructionCode = Quartz.MisfireInstruction.SmartPolicy
                };

                return new StaticScheduleBuilder(trigger);
            }

            protected override void SerializeFields(Utf8JsonWriter writer, TestBlobCronTriggerImpl trigger, JsonSerializerOptions options)
            {
                writer.WriteString("CronExpressionString", trigger.CronExpressionString);
                writer.WriteTimeZoneInfo("TimeZone", trigger.TimeZone);
            }

            private sealed class StaticScheduleBuilder(IMutableTrigger trigger) : IScheduleBuilder
            {
                public IMutableTrigger Build() => trigger;
            }
        }
    }
}