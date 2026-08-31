using System.Collections.Specialized;
using System.Diagnostics;

using Quartz.Jobs;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

[NonParallelizable]
public class SchedulerTest
{
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

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
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
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
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
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();

        IScheduler scheduler = await factory.GetScheduler();
        var job = JobBuilder.Create<NoOpJob>().Build();
        var crontTriggers = input.Split('|').Select(x => x.Trim()).Select(cronExpression => TriggerBuilder.Create().WithCronSchedule(cronExpression).Build());
        await scheduler.ScheduleJob(job, new List<ITrigger>(crontTriggers));
    }

    [Test]
    public async Task TestBasicStorageFunctions()
    {
        NameValueCollection config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "SchedulerTest_Scheduler",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.threadPool.threadCount"] = "2",
            ["quartz.threadPool.type"] = "Quartz.Impl.DefaultThreadPool, Quartz",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();

        // test basic storage functions of scheduler...

        IJobDetail job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j1")
            .StoreDurably()
            .Build();

        var exists = await scheduler.Exists(new JobKey("j1"));
        Assert.That(exists, Is.False, "Unexpected existence of job named 'j1'.");

        await scheduler.AddJob(job);

        exists = await scheduler.Exists(new JobKey("j1"));
        Assert.That(exists, Is.True, "Expected existence of job named 'j1' but checkExists return false.");

        job = await scheduler.GetJobDetail(new JobKey("j1"));

        Assert.That(job, Is.Not.Null, "Stored job not found!");

        await scheduler.DeleteJob(new JobKey("j1"));

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        exists = await scheduler.Exists(new TriggerKey("t1"));
        Assert.That(exists, Is.False, "Unexpected existence of trigger named '11'.");

        await scheduler.ScheduleJob(job, trigger);

        exists = await scheduler.Exists(new TriggerKey("t1"));
        Assert.That(exists, Is.True, "Expected existence of trigger named 't1' but checkExists return false.");

        job = await scheduler.GetJobDetail(new JobKey("j1"));

        Assert.That(job, Is.Not.Null, "Stored job not found!");

        trigger = await scheduler.GetTrigger(new TriggerKey("t1"));

        Assert.That(trigger, Is.Not.Null, "Stored trigger not found!");

        job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j2", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t2", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j3", "g1")
            .Build();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t3", "g1")
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(TimeSpan.FromSeconds(5)))
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        var jobGroups = await scheduler.GetJobGroupNames();
        var triggerGroups = await scheduler.GetTriggerGroupNames();

        Assert.Multiple(() =>
        {
            Assert.That(jobGroups, Has.Count.EqualTo(2), "Job group list size expected to be = 2 ");
            Assert.That(triggerGroups, Has.Count.EqualTo(2), "Trigger group list size expected to be = 2 ");
        });

        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
        var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

        Assert.Multiple(() =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(1), "Number of jobs expected in default group was 1 ");
            Assert.That(triggerKeys, Has.Count.EqualTo(1), "Number of triggers expected in default group was 1 ");
        });

        jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
        triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        Assert.Multiple(() =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(2), "Number of jobs expected in 'g1' group was 2 ");
            Assert.That(triggerKeys, Has.Count.EqualTo(2), "Number of triggers expected in 'g1' group was 2 ");
        });

        TriggerState s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        await scheduler.PauseTrigger(new TriggerKey("t2", "g1"));
        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

        await scheduler.ResumeTrigger(new TriggerKey("t2", "g1"));
        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        var pausedGroups = await scheduler.GetPausedTriggerGroups();
        Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        // test that adding a trigger to a paused group causes the new trigger to be paused also...
        job = JobBuilder.Create()
            .OfType<TestJob>()
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
        Assert.That(pausedGroups, Has.Count.EqualTo(1), "Size of paused trigger groups list expected to be 1 ");

        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

        s = await scheduler.GetTriggerState(new TriggerKey("t4", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Paused), "State of trigger t4 expected to be PAUSED");

        await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        s = await scheduler.GetTriggerState(new TriggerKey("t2", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        s = await scheduler.GetTriggerState(new TriggerKey("t4", "g1"));
        Assert.That(s, Is.EqualTo(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

        pausedGroups = await scheduler.GetPausedTriggerGroups();
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");
            Assert.That(await scheduler.UnscheduleJob(new TriggerKey("foasldfksajdflk")), Is.False, "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");
            Assert.That(await scheduler.UnscheduleJob(new TriggerKey("t3", "g1")), Is.True, "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");
        });

        jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
        triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(2), "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
            Assert.That(triggerKeys, Has.Count.EqualTo(2), "Number of triggers expected in 'g1' group was 1 ");
            Assert.That(await scheduler.UnscheduleJob(new TriggerKey("t1")), Is.True, "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");
        });

        jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
        triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

        Assert.Multiple(() =>
        {
            Assert.That(jobKeys, Has.Count.EqualTo(1), "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
            Assert.That(triggerKeys, Is.Empty, "Number of triggers expected in default group was 0 ");
        });

        await scheduler.Shutdown();
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

        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
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

        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
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
    public async Task ReschedulingTriggerShouldKeepOriginalNextFireTime()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Start();

        // Delay starting the trigger by a second as we do not want it to get triggered
        var triggerStartTime = DateTimeOffset.UtcNow.AddSeconds(1);

        var job = JobBuilder.Create<NoOpJob>().Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .ForJob(job)
            .StartAt(triggerStartTime)
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        trigger = (IOperableTrigger) await scheduler.GetTrigger(trigger.Key);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(triggerStartTime));
            Assert.That(trigger.NextFireTimeUtc, Is.EqualTo(triggerStartTime));
            Assert.That(trigger.PreviousFireTimeUtc, Is.EqualTo(null));
        });

        var previousFireTimeUtc = triggerStartTime.AddDays(1);
        trigger.PreviousFireTimeUtc = previousFireTimeUtc;
        trigger.NextFireTimeUtc = trigger.GetFireTimeAfter(previousFireTimeUtc);

        await scheduler.RescheduleJob(trigger.Key, trigger);

        trigger = (IOperableTrigger) await scheduler.GetTrigger(trigger.Key);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.NextFireTimeUtc, Is.Not.Null);
            Assert.That(trigger.NextFireTimeUtc, Is.EqualTo(previousFireTimeUtc.AddHours(1)));
        });

        await scheduler.Shutdown(true);
    }

    [Test]
    public async Task ScheduleJobsWithReplace_SwitchFromSimpleToCronTrigger()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "SchedulerTest_TriggerSwitch",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await factory.GetScheduler();

        var jobKey = new JobKey("switchJob", "switchGroup");
        var triggerKey = new TriggerKey("switchTrigger", "switchGroup");

        // Schedule job with a SimpleTrigger
        IJobDetail job = JobBuilder.Create<TestJob>()
            .WithIdentity(jobKey)
            .StoreDurably()
            .Build();

        ITrigger simpleTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, simpleTrigger);

        ITrigger storedTrigger = await scheduler.GetTrigger(triggerKey);
        Assert.That(storedTrigger, Is.InstanceOf<ISimpleTrigger>(), "Initial trigger should be a SimpleTrigger");

        // Now replace with a CronTrigger using the same trigger key
        ITrigger cronTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule("0 0 12 * * ?")
            .Build();

        IJobDetail updatedJob = JobBuilder.Create<TestJob>()
            .WithIdentity(jobKey)
            .StoreDurably()
            .Build();

        var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            [updatedJob] = new[] { cronTrigger }
        };

        await scheduler.ScheduleJobs(triggersAndJobs, new ScheduleJobOptions { Replace = true });

        ITrigger updatedTrigger = await scheduler.GetTrigger(triggerKey);
        Assert.That(updatedTrigger, Is.InstanceOf<ICronTrigger>(), "Trigger should have been replaced with a CronTrigger");
        Assert.That(((ICronTrigger) updatedTrigger).CronExpressionString, Is.EqualTo("0 0 12 * * ?"));

        await scheduler.Shutdown(false);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scheduling over what is already there, in one call
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task SchedulingATriggerOverAnExistingOneIsOneCall()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(SchedulingATriggerOverAnExistingOneIsOneCall));

        JobKey jobKey = new("upsert", "upsert");
        TriggerKey triggerKey = new("upsert", "upsert");

        await scheduler.ScheduleJob(
            JobBuilder.Create<TestJob>().WithIdentity(jobKey).StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartAt(FarFuture).Build());

        ITrigger replacement = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(FarFuture.AddDays(1))
            .Build();

        Func<Task> withoutReplace = async () => await scheduler.ScheduleJob(replacement, new ScheduleJobOptions());
        await withoutReplace.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "the default is still the conservative one, so a caller who did not ask to replace is told");

        await scheduler.ScheduleJob(replacement, new ScheduleJobOptions { Replace = true });

        ITrigger stored = await scheduler.GetTrigger(triggerKey);
        stored.StartTimeUtc.Should().Be(FarFuture.AddDays(1),
            "replacing is the store's own operation, so an upsert needs no unschedule-then-schedule of the caller's");
    }

    [Test]
    public async Task SchedulingAJobAndTriggerOverExistingOnesIsOneCall()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(SchedulingAJobAndTriggerOverExistingOnesIsOneCall));

        JobKey jobKey = new("upsert", "upsert");
        TriggerKey triggerKey = new("upsert", "upsert");

        await scheduler.ScheduleJob(
            JobBuilder.Create<TestJob>().WithIdentity(jobKey).UsingJobData("version", 1).Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartAt(FarFuture).Build());

        await scheduler.ScheduleJob(
            JobBuilder.Create<TestJob>().WithIdentity(jobKey).UsingJobData("version", 2).Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartAt(FarFuture.AddDays(1)).Build(),
            new ScheduleJobOptions { Replace = true });

        IJobDetail storedJob = await scheduler.GetJobDetail(jobKey);
        storedJob.JobDataMap.GetInt("version").Should().Be(2, "the job and its trigger are replaced together");

        ITrigger storedTrigger = await scheduler.GetTrigger(triggerKey);
        storedTrigger.StartTimeUtc.Should().Be(FarFuture.AddDays(1));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scheduling one firing of a typed job in one call
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task TheOneLinerStoresOneDurableJobAndOneTriggerPerCall()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheOneLinerStoresOneDurableJobAndOneTriggerPerCall));

        ScheduledOneOffJob first = await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("first"), FarFuture);
        ScheduledOneOffJob second = await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("second"), FarFuture);

        second.TriggerKey.Should().NotBe(first.TriggerKey, "a call with no name of its own gets a generated one, so two calls never collide");

        first.TriggerKey.Group.Should().Be(nameof(ReminderJob),
            "the trigger group defaults to the job type, and is the axis a caller correlates firings on");

        IJobDetail job = await scheduler.GetJobDetail(new JobKey(nameof(ReminderJob), SchedulerConstants.ScheduledJobGroup));
        job.Should().NotBeNull("one durable job per job type is what every trigger hangs off");
        job.Durable.Should().BeTrue("it has to outlive the firings that point at it");

        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(job.Key);
        triggers.Should().HaveCount(2, "each call adds a trigger; there is no job churn to pay for");

        ITrigger stored = await scheduler.GetTrigger(second.TriggerKey);
        stored.StartTimeUtc.Should().Be(FarFuture);
        stored.JobDataMap[SchedulerConstants.JobInput].Should().Be("""{"Note":"second"}""",
            "the payload rides on the trigger, so one job serves every firing");
    }

    [Test]
    public async Task TheOneLinerReplacesAFiringOfTheSameName()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheOneLinerReplacesAFiringOfTheSameName));

        OneOffJobOptions options = new() { Name = "order-42", Group = "orders", Replace = true };

        await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("first"), FarFuture, options);
        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("second"), FarFuture.AddDays(1), options);

        scheduled.TriggerKey.Should().Be(new TriggerKey("order-42", "orders"));

        IJobDetail job = await scheduler.GetJobDetail(new JobKey(nameof(ReminderJob), SchedulerConstants.ScheduledJobGroup));
        (await scheduler.GetTriggersOfJob(job.Key)).Should().ContainSingle(
            "naming the firing and asking to replace is how a caller upserts it, in one call rather than three");

        ITrigger stored = await scheduler.GetTrigger(scheduled.TriggerKey);
        stored.StartTimeUtc.Should().Be(FarFuture.AddDays(1));
        stored.JobDataMap[SchedulerConstants.JobInput].Should().Be("""{"Note":"second"}""");
    }

    [Test]
    public async Task TheOneLinerRefusesToReplaceUnlessAsked()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheOneLinerRefusesToReplaceUnlessAsked));

        OneOffJobOptions options = new() { Name = "order-42", Group = "orders" };
        await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("first"), FarFuture, options);

        Func<Task> again = async () => await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("second"), FarFuture, options);
        await again.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "Replace is opt-in here too, so a name reused by accident is reported rather than silently overwritten");
    }

    [Test]
    public async Task TheOneLinerCanBeToldADelayInsteadOfATime()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheOneLinerCanBeToldADelayInsteadOfATime));

        DateTimeOffset before = DateTimeOffset.UtcNow;
        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("later"), TimeSpan.FromHours(2));
        DateTimeOffset after = DateTimeOffset.UtcNow;

        ITrigger stored = await scheduler.GetTrigger(scheduled.TriggerKey);
        stored.StartTimeUtc.Should().BeOnOrAfter(before.AddHours(2)).And.BeOnOrBefore(after.AddHours(2),
            "a delay is measured from the moment the call was made");
    }

    [Test]
    public async Task TheOneLinerPutsTheDurableJobBackIfItWentMissing()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheOneLinerPutsTheDurableJobBackIfItWentMissing));

        await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("first"), FarFuture);

        // What a cluster restore, an operator or another node's Clear() does: the job the memo says is
        // there is gone.
        JobKey jobKey = new(nameof(ReminderJob), SchedulerConstants.ScheduledJobGroup);
        (await scheduler.DeleteJob(jobKey)).Should().BeTrue();

        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("second"), FarFuture);

        (await scheduler.GetJobDetail(jobKey)).Should().NotBeNull(
            "the memo is only an optimization: a store that says the job is missing gets it back and the firing is stored");
        (await scheduler.GetTrigger(scheduled.TriggerKey)).Should().NotBeNull();
    }

    [Test]
    public async Task TheOneLinerAnswersWithTheKeyItStoredAndTheTimeItWillFire()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheOneLinerAnswersWithTheKeyItStoredAndTheTimeItWillFire));

        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("first"), FarFuture);

        ITrigger stored = await scheduler.GetTrigger(scheduled.TriggerKey);
        stored.Should().NotBeNull("the key it answers with is the key it stored, so the handle needs no lookup to be trusted");

        scheduled.FirstFireTimeUtc.Should().Be(stored.NextFireTimeUtc,
            "the fire time is the store's own answer, which is what makes 'scheduled for' something a caller can log rather than infer from the time it asked for");
    }

    [Test]
    public async Task TheEnsuredJobIsTheOneScheduledJobKeyNames()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheEnsuredJobIsTheOneScheduledJobKeyNames));

        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("first"), FarFuture);

        JobKey key = SchedulerJobExtensions.ScheduledJobKey<ReminderJob>();
        key.Group.Should().Be(SchedulerConstants.ScheduledJobGroup, "the group is reserved, which is why the key is named rather than re-derived");

        (await scheduler.GetJobDetail(key)).Should().NotBeNull(
            "the key the extension answers with is the key the durable job was actually stored under");

        (await scheduler.GetTrigger(scheduled.TriggerKey)).JobKey.Should().Be(key,
            "an integration pointing a schedule of its own at that job needs the same key the firings hang off");
    }

    [Test]
    public async Task TheEnsuredJobRequestsRecoveryOnlyWhenAsked()
    {
        await using IScheduler quiet = await NewScheduler(nameof(TheEnsuredJobRequestsRecoveryOnlyWhenAsked) + "_quiet");
        await quiet.ScheduleJob<ReminderJob, Reminder>(new Reminder("first"), FarFuture);

        (await quiet.GetJobDetail(SchedulerJobExtensions.ScheduledJobKey<ReminderJob>())).RequestsRecovery
            .Should().BeFalse("the default is the builder's own, so nothing changes for a caller who says nothing");

        await using IScheduler recovering = await NewScheduler(nameof(TheEnsuredJobRequestsRecoveryOnlyWhenAsked) + "_recovering");
        await recovering.ScheduleJob<ReminderJob, Reminder>(
            new Reminder("first"),
            FarFuture,
            new OneOffJobOptions { RequestRecovery = true });

        (await recovering.GetJobDetail(SchedulerJobExtensions.ScheduledJobKey<ReminderJob>())).RequestsRecovery
            .Should().BeTrue("the job every firing hangs off is the one thing the caller cannot otherwise reach, and recovery is a property of it");
    }

    [Test]
    public async Task TheFirstCallDecidesWhetherTheEnsuredJobRequestsRecovery()
    {
        await using IScheduler scheduler = await NewScheduler(nameof(TheFirstCallDecidesWhetherTheEnsuredJobRequestsRecovery));

        await scheduler.ScheduleJob<ReminderJob, Reminder>(
            new Reminder("first"),
            FarFuture,
            new OneOffJobOptions { RequestRecovery = true });

        await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("second"), FarFuture.AddDays(1));

        (await scheduler.GetJobDetail(SchedulerJobExtensions.ScheduledJobKey<ReminderJob>())).RequestsRecovery
            .Should().BeTrue(
                "the job is ensured once per scheduler instance, so the second call does not store it again and cannot quietly undo what the first asked for");
    }

    [Test]
    public async Task TheOneLinerRunsTheJobWithItsPayload()
    {
        ReminderJob.Reset();
        await using IScheduler scheduler = await NewScheduler(nameof(TheOneLinerRunsTheJobWithItsPayload));
        await scheduler.Start();

        await scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("run me"), TimeSpan.Zero);

        ReminderJob.Fired.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("the scheduled firing has to actually run");
        ReminderJob.Received.Should().Be(new Reminder("run me"));
    }

    /// <summary>
    /// Far enough out that nothing fires while a storage test runs, and rounded to whole seconds so a
    /// store that keeps milliseconds and one that does not agree.
    /// </summary>
    private static readonly DateTimeOffset FarFuture = new(2126, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<IScheduler> NewScheduler(string name)
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "SchedulerTest_" + name,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };

        return await QuartzSchedulerBuilder.Create().UseProperties(properties).BuildScheduler();
    }

    public sealed record Reminder(string Note);

    public sealed class ReminderJob : IJob<Reminder>
    {
        public static readonly ManualResetEventSlim Fired = new();

        public static Reminder Received { get; private set; }

        public static void Reset()
        {
            Fired.Reset();
            Received = null;
        }

        public ValueTask Execute(IJobExecutionContext context, Reminder input, CancellationToken cancellationToken = default)
        {
            Received = input;
            Fired.Set();
            return default;
        }
    }
}