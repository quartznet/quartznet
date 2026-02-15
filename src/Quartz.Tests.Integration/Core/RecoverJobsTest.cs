using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Listener;
using Quartz.Diagnostics;
using Quartz.Simpl;
using Quartz.Tests.Integration.Utils;
using Quartz.Util;

namespace Quartz.Tests.Integration.Core;

[TestFixture(TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(TestConstants.PostgresProvider, Category = "db-postgres")]
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
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out var driverDelegateType);

        const string dataSourceName = "default";
        var jobStore = new JobStoreTX
        {
            DataSource = dataSourceName,
            InstanceId = "SINGLE_NODE_TEST",
            InstanceName = dataSourceName,
            MisfireThreshold = TimeSpan.FromSeconds(1)
        };

        var factory = DirectSchedulerFactory.Instance;

        await factory.CreateScheduler(new DefaultThreadPool(), jobStore);
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
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT TRIGGER_STATE from QRTZ_TRIGGERS WHERE SCHED_NAME = '{scheduler.SchedulerName}' AND TRIGGER_NAME='test'";
                var triggerState = command.ExecuteScalar().ToString();

                // check that trigger is blocked after fail over situation
                Assert.That(triggerState, Is.EqualTo("BLOCKED"));

                command.CommandText = $"SELECT count(*) from QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = '{scheduler.SchedulerName}' AND TRIGGER_NAME='test'";
                int count = Convert.ToInt32(command.ExecuteScalar());

                // check that fired trigger remains after fail over situation
                Assert.That(count, Is.EqualTo(1));
            }
        }

        // stop job executing to not as part of emulation fail over situation
        RecoverJobsTestJob.runForever = false;

        // emulate down time >> trigger interval - misfireThreshold
        await Task.Delay(TimeSpan.FromSeconds(4));

        var isJobRecovered = new ManualResetEventSlim(false);
        await factory.CreateScheduler(new DefaultThreadPool(), jobStore);
        IScheduler recovery = await factory.GetScheduler();
        recovery.ListenerManager.AddJobListener(new TestListener(isJobRecovered));
        await recovery.Start();

        // wait to be sure recovered job was executed
        await Task.Delay(TimeSpan.FromSeconds(2));

        // wait job
        await recovery.Shutdown(true);

        Assert.That(isJobRecovered.Wait(TimeSpan.FromSeconds(10)), Is.True);
    }

    private class TestListener : JobListenerSupport
    {
        private readonly ManualResetEventSlim isJobRecovered;

        public TestListener(ManualResetEventSlim isJobRecovered)
        {
            this.isJobRecovered = isJobRecovered;
        }

        public override string Name => typeof(RecoverJobsTest).Name;

        public override ValueTask JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = new CancellationToken())
        {
            isJobRecovered.Set();
            return default;
        }
    }

    [Test]
    public async Task TestRecoveryTriggersShouldNotExecuteAfterTriggerIsRemoved()
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out var driverDelegateType);

        const string dataSourceName = "default";
        var jobStore = new JobStoreTX
        {
            DataSource = dataSourceName,
            InstanceId = "SINGLE_NODE_TEST",
            InstanceName = dataSourceName,
            MisfireThreshold = TimeSpan.FromSeconds(1)
        };

        var factory = DirectSchedulerFactory.Instance;

        await factory.CreateScheduler(new DefaultThreadPool(), jobStore);
        var scheduler = await factory.GetScheduler();

        // Make job run forever to simulate a job that's executing when scheduler shuts down
        RecoverJobsTestJob.runForever = true;
        var jobExecutedEvent = new ManualResetEventSlim(false);

        await scheduler.Clear();

        var job = JobBuilder.Create<RecoverJobsTestJob>()
            .WithIdentity("test-recovery", "test-group")
            .RequestRecovery()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity("test-trigger", "test-group")
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        await scheduler.Start();

        // Wait for job to start executing
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Simulate scheduler crash (shutdown without waiting for jobs to complete)
        await scheduler.Shutdown(false);

        using (var connection = DBConnectionManager.Instance.GetConnection(dataSourceName))
        {
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT count(*) from QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName";
                var param1 = command.CreateParameter();
                param1.ParameterName = "@schedulerName";
                param1.Value = scheduler.SchedulerName;
                command.Parameters.Add(param1);
                var param2 = command.CreateParameter();
                param2.ParameterName = "@triggerName";
                param2.Value = "test-trigger";
                command.Parameters.Add(param2);
                
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());

                // Verify fired trigger record exists (simulating the job was executing when crashed)
                Assert.That(count, Is.EqualTo(1), "Fired trigger record should exist after unclean shutdown");
            }
        }

        // Stop job from running forever
        RecoverJobsTestJob.runForever = false;

        // Now create a new scheduler instance to unschedule the trigger before starting
        await factory.CreateScheduler(new DefaultThreadPool(), jobStore);
        var newScheduler = await factory.GetScheduler();

        // Get the trigger and unschedule it before starting the scheduler
        var triggers = await newScheduler.GetTriggersOfJob(job.Key);
        Assert.That(triggers, Has.Count.EqualTo(1), "Should have one trigger");

        // Unschedule the trigger
        bool removed = await newScheduler.UnscheduleJob(triggers[0].Key);
        Assert.That(removed, Is.True, "Trigger should be unscheduled successfully");

        // Verify trigger is removed from QRTZ_TRIGGERS table
        using (var connection = DBConnectionManager.Instance.GetConnection(dataSourceName))
        {
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT count(*) from QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName";
                var param1 = command.CreateParameter();
                param1.ParameterName = "@schedulerName";
                param1.Value = newScheduler.SchedulerName;
                command.Parameters.Add(param1);
                var param2 = command.CreateParameter();
                param2.ParameterName = "@triggerName";
                param2.Value = "test-trigger";
                command.Parameters.Add(param2);
                
                int triggerCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                Assert.That(triggerCount, Is.EqualTo(0), "Trigger should be removed from QRTZ_TRIGGERS");

                // With the fix, fired trigger records should be cleaned up when trigger is removed
                command.CommandText = "SELECT count(*) from QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName";
                // Reuse parameters - they should still be valid
                int firedTriggerCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                Assert.That(firedTriggerCount, Is.EqualTo(0), "Fired trigger record should be cleaned up when trigger is removed");
            }
        }

        // Add a listener to detect if recovery job executes
        var recoveryExecuted = new ManualResetEventSlim(false);
        newScheduler.ListenerManager.AddJobListener(new RecoveryDetectionListener(recoveryExecuted));

        // Start the scheduler - this should NOT execute the recovery trigger
        // because the original trigger was explicitly removed
        await newScheduler.Start();

        // Wait to see if job executes
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Shutdown
        await newScheduler.Shutdown(true);

        // With the fix, job should NOT execute because fired trigger record was cleaned up
        Assert.That(recoveryExecuted.IsSet, Is.False, 
            "Job should NOT execute with recovery=true after trigger was explicitly removed");
    }

    private class RecoveryDetectionListener : JobListenerSupport
    {
        private readonly ManualResetEventSlim recoveryExecuted;

        public RecoveryDetectionListener(ManualResetEventSlim recoveryExecuted)
        {
            this.recoveryExecuted = recoveryExecuted;
        }

        public override string Name => "RecoveryDetectionListener";

        public override ValueTask JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Recovering)
            {
                recoveryExecuted.Set();
            }
            return default;
        }
    }

    [DisallowConcurrentExecution]
    public class RecoverJobsTestJob : IJob
    {
        private static readonly ILogger<RecoverJobsTestJob> logger = LogProvider.CreateLogger<RecoverJobsTestJob>();

        internal static bool runForever = true;

        public async ValueTask Execute(IJobExecutionContext context)
        {
            long now = DateTime.UtcNow.Ticks;
            int tic = 0;
            logger.LogInformation("Started - {StartTime}", now);
            try
            {
                while (runForever)
                {
                    await Task.Delay(1000);
                    logger.LogInformation("Tic " + ++tic + "- " + now);
                }
                logger.LogInformation("Stopped - {StopTime}", now);
            }
            catch (ThreadInterruptedException)
            {
                logger.LogInformation("Interrupted - {InterruptionTime}", now);
            }
        }
    }
}