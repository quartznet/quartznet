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