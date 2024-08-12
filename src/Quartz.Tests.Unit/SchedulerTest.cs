using System.Collections.Specialized;
using System.Diagnostics;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Job;
using Quartz.Spi;


namespace Quartz.Tests.Unit;

[TestFixture]
public class SchedulerTest
{
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    public class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    public class TestJobWithDelay : IJob
    {
        public const string ExecutingWaitHandleKey = "ExecutingWaitHandle";
        public const string CompletedWaitHandleKey = "CompletedWaitHandle";

        public static TimeSpan Delay = TimeSpan.FromMilliseconds(200);

        public static JobDataMap CreateJobDataMap(ManualResetEvent executing, ManualResetEvent completed)
        {
            return new JobDataMap
            {
                { ExecutingWaitHandleKey, executing },
                { CompletedWaitHandleKey, completed }
            };
        }

        public ValueTask Execute(IJobExecutionContext context)
        {
            if (!context.JobDetail.JobDataMap.TryGetValue(ExecutingWaitHandleKey, out var executing))
            {
                throw new Exception($"Expected job data '{ExecutingWaitHandleKey}' not set.");
            }

            var signalExecuting = (ManualResetEvent) executing;
            signalExecuting.Set();

            Thread.Sleep(Delay);

            if (!context.JobDetail.JobDataMap.TryGetValue(CompletedWaitHandleKey, out var completed))
            {
                throw new Exception($"Expected job data '{CompletedWaitHandleKey}' not set.");
            }

            var signalCompleted = (ManualResetEvent) completed;
            signalCompleted.Set();

            return default;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    [SetUp]
    protected async Task SetUp()
    {
        string input = "0 0 03-07 ? * MON-FRI | 0 35/15 07 ? * MON-FRI | 0 05/15 08-14 ? * MON-FRI | 0 0/10 15-16 ? * MON-FRI | 0 05/15 17-23 ? * MON-FRI";

        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        ISchedulerFactory factory = new StdSchedulerFactory(properties);

        IScheduler scheduler = await factory.GetScheduler();
        var job = JobBuilder.Create<NoOpJob>().Build();
        var crontTriggers = input.Split('|').Select(x => x.Trim()).Select(cronExpression => TriggerBuilder.Create().WithCronSchedule(cronExpression).Build());
        await scheduler.ScheduleJob(job, new List<ITrigger>(crontTriggers), replace: false);
    }

    [Test]
    public async Task TestBasicStorageFunctions()
    {
        NameValueCollection config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "SchedulerTest_Scheduler",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.threadPool.threadCount"] = "2",
            ["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool, Quartz",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        IScheduler sched = await new StdSchedulerFactory(config).GetScheduler();

        // test basic storage functions of scheduler...

        IJobDetail job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j1")
            .StoreDurably()
            .Build();

        var exists = await sched.CheckExists(new JobKey("j1"));
        Assert.That(exists, Is.False, "Unexpected existence of job named 'j1'.");

        await sched.AddJob(job, false);

        exists = await sched.CheckExists(new JobKey("j1"));
        Assert.That(exists, Is.True, "Expected existence of job named 'j1' but checkExists return false.");

        job = await sched.GetJobDetail(new JobKey("j1"));

        Assert.That(job, Is.Not.Null, "Stored job not found!");

        await sched.DeleteJob(new JobKey("j1"));

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
            .Build();

        exists = await sched.CheckExists(new TriggerKey("t1"));
        Assert.That(exists, Is.False, "Unexpected existence of trigger named '11'.");

        await sched.ScheduleJob(job, trigger);

        exists = await sched.CheckExists(new TriggerKey("t1"));
        Assert.That(exists, Is.True, "Expected existence of trigger named 't1' but checkExists return false.");

        job = await sched.GetJobDetail(new JobKey("j1"));

        Assert.That(job, Is.Not.Null, "Stored job not found!");

        trigger = await sched.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null, "Stored trigger not found!");

        job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j2", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t2", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
            .Build();

        await sched.ScheduleJob(job, trigger);

        job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j3", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t3", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
            .Build();

        await sched.ScheduleJob(job, trigger);

        var jobGroups = await sched.GetJobGroupNames();
        var triggerGroups = await sched.GetTriggerGroupNames();

        Assert.Multiple(() =>
        {
            Assert.That(jobGroups, Has.Count.EqualTo(2), "Job group list size expected to be = 2 ");
            Assert.That(triggerGroups, Has.Count.EqualTo(2), "Trigger group list size expected to be = 2 ");
        });

        var jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
        var triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

        Assert.Multiple(() =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(1), "Number of jobs expected in default group was 1 ");
            Assert.That(triggerKeys, Has.Count.EqualTo(1), "Number of triggers expected in default group was 1 ");
        });

        jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
        triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        Assert.Multiple(() =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(2), "Number of jobs expected in 'g1' group was 2 ");
            Assert.That(triggerKeys, Has.Count.EqualTo(2), "Number of triggers expected in 'g1' group was 2 ");
        });

        TriggerState s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        await sched.PauseTrigger(new TriggerKey("t2", "g1"));
        s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

        await sched.ResumeTrigger(new TriggerKey("t2", "g1"));
        s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        var pausedGroups = await sched.GetPausedTriggerGroups();
        Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

        await sched.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        // test that adding a trigger to a paused group causes the new trigger to be paused also...
        job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j4", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t4", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
            .Build();

        await sched.ScheduleJob(job, trigger);

        pausedGroups = await sched.GetPausedTriggerGroups();
        Assert.That(pausedGroups, Has.Count.EqualTo(1), "Size of paused trigger groups list expected to be 1 ");

        s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

        s = await sched.GetTriggerState(new TriggerKey("t4", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Paused), "State of trigger t4 expected to be PAUSED");

        await sched.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        s = await sched.GetTriggerState(new TriggerKey("t4", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        pausedGroups = await sched.GetPausedTriggerGroups();
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");
            Assert.That(await sched.UnscheduleJob(new TriggerKey("foasldfksajdflk")), Is.False, "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");
            Assert.That(await sched.UnscheduleJob(new TriggerKey("t3", "g1")), Is.True, "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");
        });

        jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
        triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(2), "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
            Assert.That(triggerKeys, Has.Count.EqualTo(2), "Number of triggers expected in 'g1' group was 1 ");
            Assert.That(await sched.UnscheduleJob(new TriggerKey("t1")), Is.True, "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");
        });

        jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
        triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

        Assert.Multiple(() =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(1), "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
            Assert.That(triggerKeys, Is.Empty, "Number of triggers expected in default group was 0 ");
        });

        await sched.Shutdown();
    }

    [Test]
    public async Task TestShutdownWithWaitShouldBlockUntilAllTasksHaveCompleted()
    {
        var schedulerName = Guid.NewGuid().ToString();
        var executing = new ManualResetEvent(false);
        var completed = new ManualResetEvent(false);
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.threadPool.threadCount"] = "2"
        };

        var factory = new StdSchedulerFactory(properties);
        var scheduler = await factory.GetScheduler();
        await scheduler.Start();

        var job = JobBuilder.Create<TestJobWithDelay>()
            .UsingJobData(TestJobWithDelay.CreateJobDataMap(executing, completed))
            .Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(x => x.WithRepeatCount(0))
            .ForJob(job)
            .StartNow()
            .Build();
        await scheduler.ScheduleJob(job, trigger);

        // Wait for job to start executing
        executing.WaitOne();

        var stopwatch = Stopwatch.StartNew();

        // There was a deadlock on shutdown, the test should cancel and fail instead of running forever.
        CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await scheduler.Shutdown(true, timeout.Token);

        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(TestJobWithDelay.Delay.TotalMilliseconds).Within(5));
            Assert.That(completed.WaitOne(0), Is.True);
        });
    }

    [Test]
    public void TestShutdownWithoutWaitShouldNotBlockUntilAllTasksHaveCompleted()
    {
        var schedulerName = Guid.NewGuid().ToString();
        var executing = new ManualResetEvent(false);
        var completed = new ManualResetEvent(false);
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.threadPool.threadCount"] = "2"
        };

        var factory = new StdSchedulerFactory(properties);
        var scheduler = factory.GetScheduler().GetAwaiter().GetResult();
        scheduler.Start().GetAwaiter().GetResult();

        var job = JobBuilder.Create<TestJobWithDelay>()
            .UsingJobData(TestJobWithDelay.CreateJobDataMap(executing, completed))
            .Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(x => x.WithRepeatCount(0))
            .ForJob(job)
            .StartNow()
            .Build();
        scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();

        // Wait for job to start executing
        executing.WaitOne();

        var stopwatch = Stopwatch.StartNew();

        scheduler.Shutdown(false).GetAwaiter().GetResult();

        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            // Shutdown should be fast since we're not waiting for tasks to complete
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(TestJobWithDelay.Delay.TotalMilliseconds - 50));
            // The task should still be executing
            Assert.That(completed.WaitOne(0), Is.False);
        });
    }

    [Test]
    public void SerializationExceptionTest()
    {
        SchedulerException before;
        SchedulerException after;

        try
        {
            try
            {
                throw new Exception("INNER");
            }
            catch (Exception ex)
            {
                throw new SchedulerException("OUTER", ex);
            }
        }
        catch (SchedulerException ex)
        {
            before = ex;
        }

        using (var stream = new MemoryStream())
        {
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            formatter.Serialize(stream, before);
            stream.Seek(0, SeekOrigin.Begin);
            after = (SchedulerException) formatter.Deserialize(stream);
        }

        Assert.Multiple(() =>
        {
            Assert.That(before.InnerException, Is.Not.Null);
            Assert.That(after.InnerException, Is.Not.Null);
            Assert.That(after.ToString(), Is.EqualTo(before.ToString()));
        });
    }

    [Test]
    public async Task ReschedulingTriggerShouldKeepOriginalNextFireTime()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        ISchedulerFactory factory = new StdSchedulerFactory(properties);
        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Start();

        // Delay starting the trigger by a second as we do not want it to get triggered
        var triggerStartTime = DateTimeOffset.UtcNow.AddSeconds(1);

        var job = JobBuilder.Create<NoOpJob>().Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
            .ForJob(job)
            .StartAt(triggerStartTime)
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        trigger = (IOperableTrigger) await scheduler.GetTrigger(trigger.Key);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(triggerStartTime));
            Assert.That(trigger.GetNextFireTimeUtc(), Is.EqualTo(triggerStartTime));
            Assert.That(trigger.GetPreviousFireTimeUtc(), Is.EqualTo(null));
        });

        var previousFireTimeUtc = triggerStartTime.AddDays(1);
        trigger.SetPreviousFireTimeUtc(previousFireTimeUtc);
        trigger.SetNextFireTimeUtc(trigger.GetFireTimeAfter(previousFireTimeUtc));

        await scheduler.RescheduleJob(trigger.Key, trigger);

        trigger = (IOperableTrigger) await scheduler.GetTrigger(trigger.Key);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.GetNextFireTimeUtc(), Is.Not.Null);
            Assert.That(trigger.GetNextFireTimeUtc(), Is.EqualTo(previousFireTimeUtc.AddHours(1)));
        });

        await scheduler.Shutdown(true);
    }
}