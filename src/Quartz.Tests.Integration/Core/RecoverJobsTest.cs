using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Listener;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Integration.Core
{
    /// <summary>
    /// </summary>
    /// <author>https://github.com/eugene-goroschenya</author>
    public class RecoverJobsTest
    {
        [Test]
        public async Task TestRecoveringRepeatJobWhichIsFiredAndMisfiredAtTheSameTime()
        {
            const string DsName = "recoverJobsTest";
            DBConnectionManager.Instance.AddConnectionProvider(DsName, new DbProvider(TestConstants.DefaultSqlServerProvider, TestConstants.SqlServerConnectionString));

            var jobStore = new JobStoreTX
            {
                DataSource = DsName,
                InstanceId = "SINGLE_NODE_TEST",
                InstanceName = DsName,
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

            using (var connection = DBConnectionManager.Instance.GetConnection(DsName))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT TRIGGER_STATE from QRTZ_TRIGGERS WHERE SCHED_NAME = '{scheduler.SchedulerName}' AND TRIGGER_NAME='test'";
                    var triggerState = command.ExecuteScalar().ToString();

                    // check that trigger is blocked after fail over situation
                    Assert.AreEqual("BLOCKED", triggerState);

                    command.CommandText = "SELECT count(*) from QRTZ_FIRED_TRIGGERS";
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
                return TaskUtil.CompletedTask;
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
}