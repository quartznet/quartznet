using System;
using System.Threading;

using Common.Logging;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Listener;
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
        public void TestRecoveringRepeatJobWhichIsFiredAndMisfiredAtTheSameTime()
        {
            const string DsName = "recoverJobsTest";
            var connectionString = "Server=(local);Database=quartz;User Id=quartznet;Password=quartznet;";
            DBConnectionManager.Instance.AddConnectionProvider(DsName, new DbProvider("SqlServer-20", connectionString));

            var jobStore = new JobStoreTX
            {
                DataSource = DsName,
                InstanceId = "SINGLE_NODE_TEST",
                InstanceName = DsName,
                MisfireThreshold = TimeSpan.FromSeconds(1)
            };

            var factory = DirectSchedulerFactory.Instance;

            factory.CreateScheduler(new SimpleThreadPool(1, ThreadPriority.Normal), jobStore);
            var scheduler = factory.GetScheduler();

            // run forever up to the first fail over situation
            RecoverJobsTestJob.runForever = true;

            /*
            scheduler.Clear();

            scheduler.ScheduleJob(
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
            */
            scheduler.Start();

            // wait to be sure job is executing
            Thread.Sleep(2000);

            // emulate fail over situation
            scheduler.Shutdown(false);

            using (var connection = DBConnectionManager.Instance.GetConnection(DsName))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT TRIGGER_STATE from QRTZ_TRIGGERS";
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
            Thread.Sleep(TimeSpan.FromSeconds(4));

            ManualResetEvent isJobRecovered = new ManualResetEvent(false);
            factory.CreateScheduler(new SimpleThreadPool(1, ThreadPriority.Normal), jobStore);
            IScheduler recovery = factory.GetScheduler();
            recovery.ListenerManager.AddJobListener(new TestListener(isJobRecovered));
            recovery.Start();

            // wait to be sure recovered job was executed
            Thread.Sleep(TimeSpan.FromSeconds(2));

            // wait job
            recovery.Shutdown(true);

            Assert.True(isJobRecovered.WaitOne(TimeSpan.FromSeconds(10)));
        }

        private class TestListener : JobListenerSupport
        {
            private readonly ManualResetEvent isJobRecovered;

            public TestListener(ManualResetEvent isJobRecovered)
            {
                this.isJobRecovered = isJobRecovered;
            }

            public override string Name
            {
                get { return typeof(RecoverJobsTest).Name; }
            }

            public override void JobToBeExecuted(IJobExecutionContext context)
            {
                isJobRecovered.Set();
            }
        }

        [DisallowConcurrentExecution]
        public class RecoverJobsTestJob : IJob
        {
            private static readonly ILog log = LogManager.GetLogger<RecoverJobsTestJob>();

            internal static bool runForever = true;

            public void Execute(IJobExecutionContext context)
            {
                long now = DateTime.UtcNow.Ticks;
                int tic = 0;
                log.Info("Started - " + now);
                try
                {
                    while (runForever)
                    {
                        Thread.Sleep(1000);
                        log.Info("Tic " + (++tic) + "- " + now);
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