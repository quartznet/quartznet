using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore
{
    [Category("integration")]
    [TestFixture]
    public class AdoJobStoreSmokeTest
    {
        private static readonly Dictionary<string, string> dbConnectionStrings = new Dictionary<string, string>();
        private bool clearJobs = true;
        private bool scheduleJobs = true;
        private bool clustered = true;
        private ILogProvider oldProvider;

        private const string KeyResetEvent = "ResetEvent";

        static AdoJobStoreSmokeTest()
        {
            dbConnectionStrings["Oracle"] = "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=system;Password=oracle;";
            dbConnectionStrings["SQLServer"] = "Server=(local);Database=quartz;User Id=quartznet;Password=quartznet;";
            dbConnectionStrings["SQLServerCe"] = @"Data Source=C:\quartznet.sdf;Persist Security Info=False;";
            dbConnectionStrings["MySQL"] = "Server = localhost; Database = quartz; Uid = root; Pwd = Password12!";
            dbConnectionStrings["PostgreSQL"] = "Server=127.0.0.1;Port=5432;Userid=postgres;Password=Password12!;Pooling=true;MinPoolSize=1;MaxPoolSize=20;Timeout=15;SslMode=Disable;Database=quartz";
            dbConnectionStrings["SQLite"] = "Data Source=test.db;Version=3;";
            dbConnectionStrings["Firebird"] = "User=SYSDBA;Password=masterkey;Database=/quartz.fdb;DataSource=localhost;Port=3050;Dialect=3;Charset=NONE;Role=;Connection lifetime=15;Pooling=true;MinPoolSize=0;MaxPoolSize=50;Packet Size=8192;ServerType=0;";
        }

        [OneTimeSetUp]
        public void FixtureSetUp()
        {
            // set Adapter to report problems
            oldProvider = (ILogProvider) typeof(LogProvider).GetField("s_currentLogProvider", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            LogProvider.SetCurrentLogProvider(new FailFastLoggerFactoryAdapter());
        }

        [OneTimeTearDown]
        public void FixtureTearDown()
        {
            // default back to old
            LogProvider.SetCurrentLogProvider(oldProvider);
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public Task TestSqlServer(string serializerType)
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            return RunAdoJobStoreTest(TestConstants.DefaultSqlServerProvider, "SQLServer", serializerType, properties);
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public Task TestPostgreSQL(string serializerType)
        {
            NameValueCollection properties = new NameValueCollection();
            return RunAdoJobStoreTest("Npgsql", "PostgreSQL", serializerType, properties);
        }

#if !NETSTANDARD15_DBPROVIDERS

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public Task TestFirebird(string serializerType)
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.FirebirdDelegate, Quartz";
            return RunAdoJobStoreTest("Firebird", "Firebird", serializerType, properties);
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public async Task TestSqlServerCe351(string serializerType)
        {
            bool previousClustered = clustered;
            clustered = false;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("SqlServerCe-351", "SQLServerCe", serializerType, properties);
            }
            finally
            {
                clustered = previousClustered;
            }
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public async Task TestSqlServerCe352(string serializerType)
        {
            bool previousClustered = clustered;
            clustered = false;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("SqlServerCe-352", "SQLServerCe", serializerType, properties);
            }
            finally
            {
                clustered = previousClustered;
            }
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public async Task TestSqlServerCe400(string serializerType)
        {
            bool previousClustered = clustered;
            clustered = false;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("SqlServerCe-400", "SQLServerCe", serializerType, properties);
            }
            finally
            {
                clustered = previousClustered;
            }
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public Task TestOracleODPManaged(string serializerType)
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
            return RunAdoJobStoreTest("OracleODPManaged", "Oracle", serializerType, properties);
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public Task TestMySql(string serializerType)
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            return RunAdoJobStoreTest("MySql", "MySQL", serializerType, properties);
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public Task TestSQLite(string serializerType)
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz";
            return RunAdoJobStoreTest("SQLite", "SQLite", serializerType, properties);
        }

        [Test]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public async Task TestSQLiteClustered(string serializerType)
        {
            clustered = true;
            try
            {
                await TestSQLite(serializerType);
            }
            finally
            {
                clustered = false;
            }
        }
#endif // NETSTANDARD15_DBPROVIDERS

        public static string[] GetSerializerTypes()
        {
            return new[]
            {
                "json"
#if !NETSTANDARD15_DBPROVIDERS
                , "binary"
#endif
            };
        }


        private Task RunAdoJobStoreTest(string dbProvider, string connectionStringId, string serializerType)
        {
            return RunAdoJobStoreTest(dbProvider, connectionStringId, serializerType, null);
        }

        private async Task RunAdoJobStoreTest(
            string dbProvider,
            string connectionStringId,
            string serializerType,
            NameValueCollection extraProperties)
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.jobStore.clustered"] = clustered.ToString();
            properties["quartz.jobStore.clusterCheckinInterval"] = 1000.ToString();
            properties["quartz.serializer.type"] = serializerType;

            if (extraProperties != null)
            {
                foreach (string key in extraProperties.Keys)
                {
                    properties[key] = extraProperties[key];
                }
            }

            if (connectionStringId == "SQLite")
            {
                // if running SQLite we need this, SQL Server is sniffed automatically
                properties["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore, Quartz";
            }

            string connectionString;
            if (!dbConnectionStrings.TryGetValue(connectionStringId, out connectionString))
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
            SmokeTestPerformer performer = new SmokeTestPerformer();
            await performer.Test(sched, clearJobs, scheduleJobs);

            Assert.IsEmpty(FailFastLoggerFactoryAdapter.Errors, "Found error from logging output");
        }

        [Test]
        public async Task ShouldBeAbleToUseMixedProperties()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.useProperties"] = false.ToString();
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

            string connectionString;
            dbConnectionStrings.TryGetValue("SQLServer", out connectionString);
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();
            await sched.Clear();

            JobDetailImpl jobWithData = new JobDetailImpl("datajob", "jobgroup", typeof(NoOpJob));
            jobWithData.JobDataMap["testkey"] = "testvalue";
            IOperableTrigger triggerWithData = new SimpleTriggerImpl("datatrigger", "triggergroup", 20, TimeSpan.FromSeconds(5));
            triggerWithData.JobDataMap.Add("testkey", "testvalue");
            triggerWithData.EndTimeUtc = DateTime.UtcNow.AddYears(10);
            triggerWithData.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
            await sched.ScheduleJob(jobWithData, triggerWithData);
            await sched.Shutdown();

            // try again with changing the useproperties against same set of data
            properties["quartz.jobStore.useProperties"] = true.ToString();
            sf = new StdSchedulerFactory(properties);
            sched = await sf.GetScheduler();

            var triggerWithDataFromDb = await sched.GetTrigger(new TriggerKey("datatrigger", "triggergroup"));
            var jobWithDataFromDb = await sched.GetJobDetail(new JobKey("datajob", "jobgroup"));
            Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
            Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));

            // once more
            await sched.DeleteJob(jobWithData.Key);
            await sched.ScheduleJob(jobWithData, triggerWithData);
            await sched.Shutdown();

            properties["quartz.jobStore.useProperties"] = false.ToString();
            sf = new StdSchedulerFactory(properties);
            sched = await sf.GetScheduler();

            triggerWithDataFromDb = await sched.GetTrigger(new TriggerKey("datatrigger", "triggergroup"));
            jobWithDataFromDb = await sched.GetJobDetail(new JobKey("datajob", "jobgroup"));
            Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
            Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
        }

        [Test]
        [Explicit]
        [TestCaseSource(nameof(GetSerializerTypes))]
        public async Task TestSqlServerStress(string serializerType)
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.jobStore.clustered"] = clustered.ToString();

            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            await RunAdoJobStoreTest(TestConstants.DefaultSqlServerProvider, "SQLServer", serializerType, properties);

            string connectionString;
            if (!dbConnectionStrings.TryGetValue("SQLServer", out connectionString))
            {
                throw new Exception("Unknown connection string id: " + "SQLServer");
            }
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            try
            {
                await sched.Clear();

                if (scheduleJobs)
                {
                    ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                    ICalendar holidayCalendar = new HolidayCalendar();

                    for (int i = 0; i < 100000; ++i)
                    {
                        ITrigger trigger = new SimpleTriggerImpl("calendarsTrigger", "test", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                        JobDetailImpl jd = new JobDetailImpl("testJob", "test", typeof(NoOpJob));
                        await sched.ScheduleJob(jd, trigger);
                    }
                }
                await sched.Start();
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
            finally
            {
                await sched.Shutdown(false);
            }
        }

        [Test]
        public async Task TestGetTriggerKeysWithLike()
        {
            var sched = await CreateScheduler(null);

            await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith("foo"));
        }

        [Test]
        public async Task TestGetTriggerKeysWithEquals()
        {
            var sched = await CreateScheduler(null);

            await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("bar"));
        }

        [Test]
        public async Task TestGetJobKeysWithLike()
        {
            var sched = await CreateScheduler(null);

            await sched.GetJobKeys(GroupMatcher<JobKey>.GroupStartsWith("foo"));
        }

        [Test]
        public async Task TestGetJobKeysWithEquals()
        {
            var sched = await CreateScheduler(null);

            await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("bar"));
        }

        [Test]
        public async Task JobTypeNotFoundShouldNotBlock()
        {
            NameValueCollection properties = new NameValueCollection();
            properties.Add(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType, typeof(SpecialClassLoadHelper).AssemblyQualifiedName);
            var scheduler = await CreateScheduler(properties);

            await scheduler.DeleteJobs(new[] {JobKey.Create("bad"), JobKey.Create("good")});

            await scheduler.Start();

            var manualResetEvent = new ManualResetEventSlim(false);
            scheduler.Context.Put(KeyResetEvent, manualResetEvent);

            IJobDetail goodJob = JobBuilder.Create<GoodJob>().WithIdentity("good").Build();
            IJobDetail badJob = JobBuilder.Create<BadJob>().WithIdentity("bad").Build();

            var now = DateTimeOffset.UtcNow;
            ITrigger goodTrigger = TriggerBuilder.Create().WithIdentity("good").ForJob(goodJob)
                .StartAt(now.AddMilliseconds(1))
                .Build();

            ITrigger badTrigger = TriggerBuilder.Create().WithIdentity("bad").ForJob(badJob)
                .StartAt(now)
                .Build();

            var toSchedule = new Dictionary<IJobDetail, ISet<ITrigger>>();
            toSchedule.Add(badJob, new HashSet<ITrigger>()
            {
                badTrigger
            });
            toSchedule.Add(goodJob, new HashSet<ITrigger>()
            {
                goodTrigger
            });
            await scheduler.ScheduleJobs(toSchedule, true);

            manualResetEvent.Wait(TimeSpan.FromSeconds(20));

            Assert.That(await scheduler.GetTriggerState(badTrigger.Key), Is.EqualTo(TriggerState.Error));
        }

        private static async Task<IScheduler> CreateScheduler(NameValueCollection properties)
        {
            properties = properties ?? new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.jobStore.clustered"] = "false";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

            string connectionString = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();
            return sched;
        }

        [Test]
        [Explicit]
        public async Task StressTest()
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.jobStore.clustered"] = "false";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

            string connectionString = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            try
            {
                await sched.Clear();

                JobDetailImpl lonelyJob = new JobDetailImpl("lonelyJob", "lonelyGroup", typeof(SimpleRecoveryJob));
                lonelyJob.Durable = true;
                lonelyJob.RequestsRecovery = true;
                await sched.AddJob(lonelyJob, false);
                await sched.AddJob(lonelyJob, true);

                string schedId = sched.SchedulerInstanceId;

                JobDetailImpl job = new JobDetailImpl("job_to_use", schedId, typeof(SimpleRecoveryJob));

                for (int i = 0; i < 100000; ++i)
                {
                    IOperableTrigger trigger = new SimpleTriggerImpl("stressing_simple", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                    trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                    await sched.ScheduleJob(job, trigger);
                }

                for (int i = 0; i < 100000; ++i)
                {
                    IOperableTrigger ct = new CronTriggerImpl("stressing_cron", "0/1 * * * * ?");
                    ct.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                    await sched.ScheduleJob(job, ct);
                }

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                await sched.Start();
                await Task.Delay(TimeSpan.FromMinutes(3));
                stopwatch.Stop();
                Console.WriteLine("Took: " + stopwatch.Elapsed);
            }
            finally
            {
                await sched.Shutdown(false);
            }
        }

        public class BadJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                return Task.FromResult(0);
            }
        }

        public class GoodJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                try
                {
                    ((ManualResetEventSlim) context.Scheduler.Context.Get(KeyResetEvent)).Wait(TimeSpan.FromSeconds(20));
                    return Task.FromResult(0);
                }
                catch (SchedulerException ex)
                {
                    throw new JobExecutionException(ex);
                }
                catch (ThreadInterruptedException ex)
                {
                    throw new JobExecutionException(ex);
                }
                catch (TimeoutException ex)
                {
                    throw new JobExecutionException(ex);
                }
            }
        }

        public class SpecialClassLoadHelper : SimpleTypeLoadHelper
        {
            public override Type LoadType(string name)
            {
                if (!string.IsNullOrEmpty(name) && typeof(BadJob) == Type.GetType(name))
                {
                    throw new TypeLoadException();
                }
                return base.LoadType(name);
            }
        }
    }

    internal class DummyTriggerListener : ITriggerListener
    {
        public string Name => GetType().FullName;

        public Task TriggerFired(ITrigger trigger, IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context)
        {
            return Task.FromResult(false);
        }

        public Task TriggerMisfired(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode)
        {
            return TaskUtil.CompletedTask;
        }
    }

    internal class DummyJobListener : IJobListener
    {
        public string Name => GetType().FullName;

        public Task JobToBeExecuted(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobExecutionVetoed(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException)
        {
            return TaskUtil.CompletedTask;
        }
    }

    public class SimpleRecoveryJob : IJob
    {
        private const string Count = "count";

        /// <summary> 
        /// Called by the <see cref="IScheduler" /> when a
        /// <see cref="ITrigger" /> fires that is associated with
        /// the <see cref="IJob" />.
        /// </summary>
        public virtual async Task Execute(IJobExecutionContext context)
        {
            // delay for ten seconds
            await Task.Delay(TimeSpan.FromSeconds(10));

            JobDataMap data = context.JobDetail.JobDataMap;
            int count;
            if (data.ContainsKey(Count))
            {
                count = data.GetInt(Count);
            }
            else
            {
                count = 0;
            }
            count++;
            data.Put(Count, count);
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class SimpleRecoveryStatefulJob : SimpleRecoveryJob
    {
    }
}