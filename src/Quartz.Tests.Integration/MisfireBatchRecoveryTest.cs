using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Listener;
using Quartz.Util;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class MisfireBatchRecoveryTest
    {
        private static readonly Dictionary<string, string> dbConnectionStrings = new Dictionary<string, string>();
        private ILogProvider oldProvider;

        static MisfireBatchRecoveryTest()
        {
            dbConnectionStrings["Oracle"] = "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=system;Password=oracle;";
            dbConnectionStrings["SQLServer"] = TestConstants.SqlServerConnectionString;
            dbConnectionStrings["MySQL"] = "Server = localhost; Database = quartznet; Uid = quartznet; Pwd = quartznet";
            dbConnectionStrings["PostgreSQL"] = "Server=127.0.0.1;Port=5432;Userid=quartznet;Password=quartznet;Pooling=true;MinPoolSize=1;MaxPoolSize=20;Timeout=15;SslMode=Disable;Database=quartznet";
        }

        [OneTimeSetUp]
        public void FixtureSetUp()
        {
            // set Adapter to report problems
            oldProvider = (ILogProvider)typeof(LogProvider).GetField("s_currentLogProvider", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            LogProvider.SetCurrentLogProvider(new FailFastLoggerFactoryAdapter());
        }

        [OneTimeTearDown]
        public void FixtureTearDown()
        {
            // default back to old
            LogProvider.SetCurrentLogProvider(oldProvider);
        }

        [Test]
        [Category("sqlserver")]
        public Task TestSqlServer()
        {
            var properties = new NameValueCollection
            {
                ["quartz.jobStore.driverDelegateType"] = typeof(Quartz.Impl.AdoJobStore.SqlServerDelegate).AssemblyQualifiedNameWithoutVersion()
            };
            return RunTests(TestConstants.DefaultSqlServerProvider, "SQLServer", properties);
        }

        [Test]
        [Category("postgresql")]
        public Task TestPostgreSql()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";
            return RunTests("Npgsql", "PostgreSQL", properties);
        }

        [Test]
        [Category("mysql")]
        public Task TestMySql()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            return RunTests("MySql", "MySQL", properties);
        }

        [Test]
        [Category("oracle")]
        public Task TestOracleODPManaged()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
            return RunTests("OracleODPManaged", "Oracle", properties);
        }

        private static readonly TimeSpan MisfireThreshold = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan MisfireHandlerFrequency = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan TimeToPass = TimeSpan.FromMilliseconds(1000);
        private static readonly TimeSpan TriggerInterval = TimeSpan.FromMilliseconds(1000);
        private const int MaxRetryCount = 15;

        private async Task RunTests(
            string dbProvider,
            string connectionStringId,
            NameValueCollection extraProperties = null)
        {
            var tests = new Tuple<int, int>[]
            {
                new Tuple<int, int>(10, 3),
                new Tuple<int, int>(23, 7),
                new Tuple<int, int>(21, 10),
                new Tuple<int, int>(7, 10),
            };

            IScheduler sched;
            foreach (var test in tests)
            {
                var jobsCount = test.Item1;
                var batchSize = test.Item2;

                await TestContext.Progress.WriteLineAsync($"{DateTime.Now.ToString("HH:mm:ss.fff")} - Test for misfires with {jobsCount} jobs and batch size {batchSize}...");
                sched = await CreateScheduler(dbProvider, connectionStringId, "json", extraProperties, batchSize);
                await sched.Clear();
                await RunMisfireDuringJobExecutionTest(sched, jobsCount);
                await sched.Shutdown(true);
            }

            // Now run a recovery test
            await TestContext.Progress.WriteLineAsync($"{DateTime.Now.ToString("HH:mm:ss.fff")} - Test for recovery of misfired triggers.");
            sched = await CreateScheduler(dbProvider, connectionStringId, "json", extraProperties, 3);

            await RunMisfireFromRecoveryTest(sched);

            await sched.Clear();

            Assert.IsEmpty(FailFastLoggerFactoryAdapter.Errors, "Found error from logging output");
        }

        private async Task RunMisfireFromRecoveryTest(IScheduler sched)
        {
            var jobsKeys = new HashSet<JobKey>((await sched.GetJobKeys(GroupMatcher<JobKey>.AnyGroup())).Where(x => IsTestJob(x)));
            Assert.IsTrue(jobsKeys.Any());

            var triggersListner = new TriggersListner();
            triggersListner.ShouldRecordFiredTriggers = true;
            sched.ListenerManager.AddTriggerListener(triggersListner, GroupMatcher<TriggerKey>.AnyGroup());

            await sched.Start();

            await sched.Standby();
            // Wait before starting the scheduler so all triggers are misfired.
            await Task.Delay(TriggerInterval.Add(TriggerInterval));

            await sched.Start();

            // Do some retries as this is timing sensitive.
            var retry = 0;
            while (retry++ < MaxRetryCount && jobsKeys.Count > triggersListner.FiredTriggers.Count)
            {
                WriteLog($"Waiting {TimeToPass}ms for recovery - retry: {retry}");
                await Task.Delay(TimeToPass);
            }

            Assert.AreEqual(jobsKeys.Count, triggersListner.MisfiredTriggers.Count, $"Recovery: Expected {jobsKeys.Count} misfired triggers, got {triggersListner.MisfiredTriggers.Count} {triggersListner.MisfiredTriggers}");
            Assert.IsTrue(jobsKeys.SetEquals(triggersListner.MisfiredTriggers.Keys), "Recovery: Expected all triggers to be misfired.");

            Assert.AreEqual(jobsKeys.Count, triggersListner.MisfiredTriggers.Count, $"Recovery: Expected {jobsKeys.Count} fired triggers, got {triggersListner.FiredTriggers.Count}");
            Assert.IsTrue(jobsKeys.SetEquals(triggersListner.FiredTriggers.Keys), "Recovery: Expected all triggers to have fired.");
        }

        private const string SemaphoreKey = "BlockingSemKey";
        private const string BlockingJobGroup = "BlockingJob";
        private const string TestJobPrefix = "Test_";

        private async Task RunMisfireDuringJobExecutionTest(IScheduler sched, int jobsToCreate)
        {
            var triggersListner = new TriggersListner();
            sched.ListenerManager.AddTriggerListener(triggersListner, GroupMatcher<TriggerKey>.AnyGroup());

            var sem = new SemaphoreSlim(0);
            sched.Context.Put(SemaphoreKey, sem);

            IJobDetail blockingJob = JobBuilder.Create<BlockingJob>()
                .WithIdentity("blockingJob", BlockingJobGroup)
                .StoreDurably()
                .Build();

            var jobsKeys = new HashSet<JobKey>();
            for (int i = 0; i < jobsToCreate; i++)
            {
                ITrigger t = TriggerBuilder.Create()
                    .WithIdentity($"Test_t{i}")
                    .WithSimpleSchedule(x => x
                        .WithInterval(TriggerInterval)
                        .RepeatForever()
                        .WithMisfireHandlingInstructionFireNow())
                    .Build();

                if (i % 2 == 0)
                {
                    IJobDetail nakedJob = JobBuilder.Create<TestJob>()
                        .WithIdentity($"{TestJobPrefix}nakedJob{i}")
                        .Build();

                    await sched.ScheduleJob(nakedJob, t);
                    jobsKeys.Add(nakedJob.Key);
                }
                else
                {
                    IJobDetail jobWithData = JobBuilder.Create<TestJob2>()
                        .WithIdentity($"{TestJobPrefix}jobWithData{i}", "datagroup")
                        .WithDescription("job with data")
                        .UsingJobData("some", "data")
                        .UsingJobData("other", true)
                        .Build();

                    await sched.ScheduleJob(jobWithData, t);
                    jobsKeys.Add(jobWithData.Key);
                }
            }

            // Get the jobs to run a bit.
            await sched.Start();
            await Task.Delay(TimeToPass);
            // This job will cause the others to misfire.
            await sched.AddJob(blockingJob, replace: false);
            await sched.TriggerJob(blockingJob.Key);

            // After we block the scheduler we should have misfired triggers reported.
            // Do some retries as this is timing sensitive.
            var retry = 0;
            while (retry++ < MaxRetryCount && jobsKeys.Count > triggersListner.MisfiredTriggers.Count)
            {
                WriteLog($"Waiting {TimeToPass}ms for misfires - retry: {retry}");
                await Task.Delay(TimeToPass);
            }

            Assert.AreEqual(jobsKeys.Count, triggersListner.MisfiredTriggers.Count, $"Expected {jobsKeys.Count} misfired triggers, got {triggersListner.MisfiredTriggers.Count}");
            Assert.IsTrue(jobsKeys.SetEquals(triggersListner.MisfiredTriggers.Keys), "Expected all triggers to be misfired.");

            // Start recording jobs that will run after releasing the blocking job.
            triggersListner.ShouldRecordFiredTriggers = true;
            sem.Release();
            WriteLog($"Sem for blocking job {blockingJob.Key} released.");
            // Wait a bit more to give the misfire handler time to process the batches.
            // Do some retries as this is timing sensitive.
            retry = 0;
            while (retry++ < MaxRetryCount && jobsKeys.Count > triggersListner.FiredTriggers.Count)
            {
                WriteLog($"Waiting {TimeToPass}ms for fires - retry: {retry}");
                await Task.Delay(TimeToPass);
            }

            Assert.AreEqual(jobsKeys.Count, triggersListner.FiredTriggers.Count, $"Expected {jobsKeys.Count} fired triggers, got {triggersListner.FiredTriggers.Count}");
            Assert.IsTrue(jobsKeys.SetEquals(triggersListner.FiredTriggers.Keys), "Expected all triggers to have fired.");

            await TestContext.Out.FlushAsync();

            sem.Dispose();
        }

        private static bool IsTestJob(JobKey key)
        {
            return key.Name.StartsWith(TestJobPrefix);
        }

        public class TestJob: IJob
        {
            public virtual Task Execute(IJobExecutionContext context)
            {
                JobKey key = context.JobDetail.Key;
                JobDataMap dataMap = context.JobDetail.JobDataMap;

                WriteLog($"Executing job {key}");
                if (GroupMatcher<JobKey>.GroupContains("datagroup").IsMatch(key))
                {
                    Assert.AreEqual("data", dataMap.GetString("some"));
                    Assert.IsTrue(dataMap.GetBooleanValue("other"));
                }

                return TaskUtil.CompletedTask;
            }
        }

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestJob2 : TestJob
        {
        }

        public class BlockingJob: IJob
        {
            public async Task Execute(IJobExecutionContext context)
            {
                var sem = context.Scheduler.Context.Get(SemaphoreKey) as SemaphoreSlim;
                Assert.IsNotNull(sem);
                JobKey key = context.JobDetail.Key;

                WriteLog($"Executing blocking job {key}");
                // Long running job to cause misfires.
                var sw = Stopwatch.StartNew();
                await sem.WaitAsync();
                sw.Stop();
                WriteLog($"Completed blocking job {key} - elapsed time = {sw.Elapsed}");
            }
        }

        public class TriggersListner : TriggerListenerSupport
        {
            public ConcurrentDictionary<JobKey, int> MisfiredTriggers { get; } = new ConcurrentDictionary<JobKey, int>();
            public ConcurrentDictionary<JobKey, int> FiredTriggers{ get; } = new ConcurrentDictionary<JobKey, int>();
            public bool ShouldRecordFiredTriggers = false;


            public override string Name => "execution and misfire listner";

            public override Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                if (ShouldRecordFiredTriggers && IsTestJob(trigger.JobKey))
                {
                    FiredTriggers[trigger.JobKey] = 1;
                }
                return base.TriggerFired(trigger, context, cancellationToken);
            }

            public override Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
            {
                if (IsTestJob(trigger.JobKey))
                {
                    WriteLog($"Misfire reported for {trigger.Key}");
                    MisfiredTriggers[trigger.JobKey] = 1;
                }
                return base.TriggerMisfired(trigger, cancellationToken);
            }
        }

        private static void WriteLog(string message)
        {
            TestContext.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} - {Thread.CurrentThread.ManagedThreadId}: {message}");
        }

        private static async Task<IScheduler> CreateScheduler(
            string dbProvider, 
            string connectionStringId, 
            string serializerType, 
            NameValueCollection extraProperties,
            int maxMisfiresToHandleAtTime
        )
        {
            var properties = new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = "MisfireTestScheduler",
                ["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz",
                ["quartz.threadPool.threadCount"] = "1",
                ["quartz.jobStore.misfireThreshold"] = MisfireThreshold.Milliseconds.ToString(),
                ["quartz.jobStore.misfireHandlerFrequency"] = MisfireHandlerFrequency.Milliseconds.ToString(),
                ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
                ["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz",
                ["quartz.jobStore.useProperties"] = "false",
                ["quartz.jobStore.dataSource"] = "default",
                ["quartz.jobStore.tablePrefix"] = "QRTZ_",
                ["quartz.jobStore.maxMisfiresToHandleAtATime"] = maxMisfiresToHandleAtTime.ToString(),
                ["quartz.jobStore.batchMisfireHandling"] = "true",
                ["quartz.serializer.type"] = serializerType
            };

            if (extraProperties != null)
            {
                foreach (string key in extraProperties.Keys)
                {
                    properties[key] = extraProperties[key];
                }
            }

            if (!dbConnectionStrings.TryGetValue(connectionStringId, out var connectionString))
            {
                throw new Exception("Unknown connection string id: " + connectionStringId);
            }
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = dbProvider;

            // Clear any old errors from the log
            FailFastLoggerFactoryAdapter.Errors.Clear();

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            return sched;
        }
    }
}