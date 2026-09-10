using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Listener;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Tests.Integration.Utils;
using Quartz.Util;

namespace Quartz.Tests.Integration.Core;

[TestFixture(TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(TestConstants.PostgresProvider, Category = "db-postgres")]
[NonParallelizable]
public class RecoverJobsTest
{
    private readonly string provider;

    public RecoverJobsTest(string provider)
    {
        this.provider = provider;
    }

    [Test]
    public async Task TestRecoveringRepeatJobWhichIsFiredAndMisfiredAtTheSameTime()
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out var driverDelegateType, out var dataSourceName);

        var jobStore = new JobStoreTX
        {
            DataSource = dataSourceName,
            InstanceId = "SINGLE_NODE_TEST",
            InstanceName = dataSourceName,
            MisfireThreshold = TimeSpan.FromSeconds(1)
        };

        var factory = DirectSchedulerFactory.Instance;

        factory.CreateScheduler(new DefaultThreadPool(), jobStore);
        var scheduler = await factory.GetScheduler();

        // run forever up to the first fail over situation
        RecoverJobsTestJob.runForever = true;

        await scheduler.Clear();

        await scheduler.ScheduleJob(
            JobBuilder.Create<RecoverJobsTestJob>()
                .WithIdentity("test")
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("test")
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromSeconds(1))
                    .RepeatForever()
                ).Build()
        );

        await scheduler.Start();

        // wait to be sure job is executing
        await Task.Delay(TimeSpan.FromSeconds(2));

        // emulate fail over situation
        await scheduler.Shutdown(false);

        using (var connection = DBConnectionManager.Instance.GetConnection(dataSourceName))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT TRIGGER_STATE from QRTZ_TRIGGERS WHERE SCHED_NAME = '{scheduler.SchedulerName}' AND TRIGGER_NAME='test'";
                var triggerState = command.ExecuteScalar().ToString();

                // check that trigger is blocked after fail over situation
                Assert.AreEqual("BLOCKED", triggerState);

                command.CommandText = $"SELECT count(*) from QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = '{scheduler.SchedulerName}' AND TRIGGER_NAME='test'";
                int count = Convert.ToInt32(command.ExecuteScalar());

                // check that fired trigger remains after fail over situation
                Assert.AreEqual(1, count);
            }
        }

        // stop job executing to not as part of emulation fail over situation
        RecoverJobsTestJob.runForever = false;

        // emulate down time >> trigger interval - misfireThreshold
        await Task.Delay(TimeSpan.FromSeconds(4));

        var isJobRecovered = new ManualResetEventSlim(false);
        factory.CreateScheduler(new DefaultThreadPool(), jobStore);
        IScheduler recovery = await factory.GetScheduler();
        recovery.ListenerManager.AddJobListener(new TestListener(isJobRecovered));
        await recovery.Start();

        // wait to be sure recovered job was executed
        await Task.Delay(TimeSpan.FromSeconds(2));

        // wait job
        await recovery.Shutdown(true);

        Assert.True(isJobRecovered.Wait(TimeSpan.FromSeconds(10)));
    }

    private class TestListener : JobListenerSupport
    {
        private readonly ManualResetEventSlim isJobRecovered;

        public TestListener(ManualResetEventSlim isJobRecovered)
        {
            this.isJobRecovered = isJobRecovered;
        }

        public override string Name => typeof(RecoverJobsTest).Name;

        public override Task JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = new CancellationToken())
        {
            isJobRecovered.Set();
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task TestUnscheduleJobShouldCleanUpFiredTriggerRecords()
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out _, out var dataSourceName);

        var jobStore = new JobStoreTX
        {
            DataSource = dataSourceName,
            InstanceId = "SINGLE_NODE_TEST",
            InstanceName = dataSourceName,
            MisfireThreshold = TimeSpan.FromSeconds(1)
        };

        var factory = DirectSchedulerFactory.Instance;

        factory.CreateScheduler(new DefaultThreadPool(), jobStore);
        IScheduler scheduler = await factory.GetScheduler();

        RecoverJobsTestJob.runForever = true;

        await scheduler.Clear();

        var triggerKey = new TriggerKey("recoverTest", "testGroup");
        var jobKey = new JobKey("recoverTestJob", "testGroup");

        await scheduler.ScheduleJob(
            JobBuilder.Create<RecoverJobsTestJob>()
                .WithIdentity(jobKey)
                .RequestRecovery(true)
                .StoreDurably(true)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromSeconds(1))
                    .RepeatForever()
                ).Build()
        );

        await scheduler.Start();

        // wait to be sure job is executing (creates a QRTZ_FIRED_TRIGGERS record)
        await Task.Delay(TimeSpan.FromSeconds(2));

        // verify the fired trigger record exists while job is executing
        int firedCountBefore;
        using (var connection = DBConnectionManager.Instance.GetConnection(dataSourceName))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) from QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = '{scheduler.SchedulerName}' AND TRIGGER_NAME = '{triggerKey.Name}' AND TRIGGER_GROUP = '{triggerKey.Group}'";
            firedCountBefore = Convert.ToInt32(command.ExecuteScalar());
        }
        Assert.That(firedCountBefore, Is.GreaterThan(0), "Should have fired trigger records while job is executing");

        // unschedule the trigger while the job is still running
        // this should also clean up fired trigger records (#2083)
        bool unscheduled = await scheduler.UnscheduleJob(triggerKey);
        Assert.True(unscheduled, "Trigger should have been unscheduled");

        // verify fired trigger records were cleaned up
        int firedCountAfter;
        using (var connection = DBConnectionManager.Instance.GetConnection(dataSourceName))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) from QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = '{scheduler.SchedulerName}' AND TRIGGER_NAME = '{triggerKey.Name}' AND TRIGGER_GROUP = '{triggerKey.Group}'";
            firedCountAfter = Convert.ToInt32(command.ExecuteScalar());
        }
        Assert.AreEqual(0, firedCountAfter, "Fired trigger records should be cleaned up after trigger removal");

        RecoverJobsTestJob.runForever = false;
        await scheduler.Shutdown(true);
    }

    /// <summary>
    /// A trigger rescheduled before the restarted node starts — which is what re-applying an
    /// application's <c>AddTrigger</c> registrations on startup does — keeps the record of the execution
    /// the kill interrupted, so the job that asked to be recovered is (#3759).
    /// </summary>
    [Test]
    public async Task TestRecoveryStillHappensAfterTriggerIsRescheduledBeforeStart()
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out _, out var dataSourceName);

        // One store per process: the restarted node is a new process, and a store that has been shut
        // down refuses work until its scheduler starts, which is after the reschedule under test.
        JobStoreTX CreateJobStore()
        {
            // A recovery trigger carries a data map, and a store without a serializer cannot write one.
            var serializer = new JsonObjectSerializer();
            serializer.Initialize();

            return new JobStoreTX
            {
                DataSource = dataSourceName,
                InstanceId = "SINGLE_NODE_TEST",
                InstanceName = dataSourceName,
                MisfireThreshold = TimeSpan.FromSeconds(1),
                ObjectSerializer = serializer
            };
        }

        var factory = DirectSchedulerFactory.Instance;

        factory.CreateScheduler(new DefaultThreadPool(), CreateJobStore());
        var scheduler = await factory.GetScheduler();

        RecoverJobsTestJob.runForever = true;

        await scheduler.Clear();

        var jobKey = new JobKey("rescheduledRecovery", "testGroup");
        var triggerKey = new TriggerKey("rescheduledRecovery", "testGroup");

        await scheduler.ScheduleJob(
            JobBuilder.Create<RecoverJobsTestJob>()
                .WithIdentity(jobKey)
                .RequestRecovery(true)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromHours(1))
                    .RepeatForever()
                ).Build()
        );

        await scheduler.Start();

        // wait to be sure job is executing
        await Task.Delay(TimeSpan.FromSeconds(2));

        // emulate fail over situation
        await scheduler.Shutdown(false);

        CountFiredTriggers(dataSourceName, scheduler.SchedulerName, triggerKey).Should().Be(1,
            "the premise: the kill interrupted a firing the store knows about");

        // the restarted node re-applies its declared trigger before it starts
        factory.CreateScheduler(new DefaultThreadPool(), CreateJobStore());
        IScheduler restarted = await factory.GetScheduler();

        var declaredAgain = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromHours(1))
                .RepeatForever()
            ).Build();
        (await restarted.RescheduleJob(triggerKey, declaredAgain)).Should().NotBeNull("the trigger was there to replace");

        CountFiredTriggers(dataSourceName, restarted.SchedulerName, triggerKey).Should().Be(1,
            "a reschedule is not a removal: the interrupted execution's record stays for recovery to read");

        RecoverJobsTestJob.runForever = false;

        var recovered = new ManualResetEventSlim(false);
        restarted.ListenerManager.AddJobListener(new RecoveryDetectionListener(recovered));
        await restarted.Start();

        recovered.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue(
            "the job asked to be recovered and its execution was interrupted; rescheduling its trigger changes neither");

        await restarted.Shutdown(true);
    }

    private static int CountFiredTriggers(string dataSourceName, string schedulerName, TriggerKey triggerKey)
    {
        using var connection = DBConnectionManager.Instance.GetConnection(dataSourceName);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) from QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = '{schedulerName}' AND TRIGGER_NAME = '{triggerKey.Name}' AND TRIGGER_GROUP = '{triggerKey.Group}'";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private sealed class RecoveryDetectionListener : JobListenerSupport
    {
        private readonly ManualResetEventSlim recovered;

        public RecoveryDetectionListener(ManualResetEventSlim recovered)
        {
            this.recovered = recovered;
        }

        public override string Name => nameof(RecoveryDetectionListener);

        public override Task JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Recovering)
            {
                recovered.Set();
            }

            return Task.CompletedTask;
        }
    }

    [DisallowConcurrentExecution]
    public class RecoverJobsTestJob : IJob
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(RecoverJobsTestJob));

        internal static bool runForever = true;

        public async Task Execute(IJobExecutionContext context)
        {
            long now = DateTime.UtcNow.Ticks;
            int tic = 0;
            log.Info("Started - " + now);
            try
            {
                while (runForever)
                {
                    await Task.Delay(1000);
                    log.Info("Tic " + ++tic + "- " + now);
                }
                log.Info("Stopped - " + now);
            }
            catch (ThreadInterruptedException)
            {
                log.Info("Interrupted - " + now);
            }
        }
    }
}
