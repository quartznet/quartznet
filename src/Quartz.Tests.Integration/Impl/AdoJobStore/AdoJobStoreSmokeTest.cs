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
            dbConnectionStrings["Oracle"] = "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=quartznet;Password=quartznet;";
            dbConnectionStrings["SQLServer"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            dbConnectionStrings["SQLServerCe"] = @"Data Source=C:\quartznet.sdf;Persist Security Info=False;";
            dbConnectionStrings["MySQL"] = "Server = localhost; Database = quartz; Uid = quartznet; Pwd = quartznet";
            dbConnectionStrings["PostgreSQL"] = "Server=127.0.0.1;Port=5432;Userid=quartznet;Password=quartznet;Protocol=3;SSL=false;Pooling=true;MinPoolSize=1;MaxPoolSize=20;Encoding=UTF8;Timeout=15;SslMode=Disable;Database=quartznet";
            dbConnectionStrings["SQLite"] = "Data Source=test.db;Version=3;";
            dbConnectionStrings["Firebird"] = "User=SYSDBA;Password=masterkey;Database=C:/Temp/quartznet/quartznet.fdb;DataSource=localhost;Port=3050;Dialect=3;Charset=NONE;Role=;Connection lifetime=15;Pooling=true;MinPoolSize=0;MaxPoolSize=50;Packet Size=8192;ServerType=0;";
        }

        [TestFixtureSetUp]
        public void FixtureSetUp()
        {
            // set Adapter to report problems
            oldProvider = (ILogProvider) typeof (LogProvider).GetField("_currentLogProvider", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            LogProvider.SetCurrentLogProvider(new FailFastLoggerFactoryAdapter());
        }

        [TestFixtureTearDown]
        public void FixtureTearDown()
        {
            // default back to old
            LogProvider.SetCurrentLogProvider(oldProvider);
        }

        [Test]
        public async Task TestFirebird()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.FirebirdDelegate, Quartz";
            await RunAdoJobStoreTest("Firebird-450", "Firebird", properties);
        }

        [Test]
        public async Task TestPostgreSQL10()
        {
            // we don't support Npgsql-10 anymore
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("Npgsql-10", "PostgreSQL", properties);
                Assert.Fail("No error from using Npgsql-10");
            }
            catch (SchedulerException ex)
            {
                Assert.IsNotNull(ex.InnerException);
                Assert.AreEqual("Npgsql-10 provider is no longer supported.", ex.InnerException.Message);
            }
        }

        [Test]
        public async Task TestPostgreSQL20()
        {
            NameValueCollection properties = new NameValueCollection();
            await RunAdoJobStoreTest("Npgsql-20", "PostgreSQL", properties);
        }

        [Test]
        public async Task TestSqlServer11()
        {
            // we don't support SQL Server 1.1
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("SqlServer-11", "SQLServer", properties);
                Assert.Fail("No error from using SqlServer-11");
            }
            catch (SchedulerException ex)
            {
                Assert.IsNotNull(ex.InnerException);
                Assert.AreEqual("SqlServer-11 provider is no longer supported.", ex.InnerException.Message);
            }
        }

        [Test]
        public async Task TestSqlServer20()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            await RunAdoJobStoreTest("SqlServer-20", "SQLServer", properties);
        }

        [Test]
        public async Task TestSqlServerCe351()
        {
            bool previousClustered = clustered;
            clustered = false;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("SqlServerCe-351", "SQLServerCe", properties);
            }
            finally
            {
                clustered = previousClustered;
            }
        }

        [Test]
        public async Task TestSqlServerCe352()
        {
            bool previousClustered = clustered;
            clustered = false;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("SqlServerCe-352", "SQLServerCe", properties);
            }
            finally
            {
                clustered = previousClustered;
            }
        }

        [Test]
        public async Task TestSqlServerCe400()
        {
            bool previousClustered = clustered;
            clustered = false;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                await RunAdoJobStoreTest("SqlServerCe-400", "SQLServerCe", properties);
            }
            finally
            {
                clustered = previousClustered;
            }
        }

        [Test]
        public async Task TestOracleODPManaged4011()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
            await RunAdoJobStoreTest("OracleODPManaged-1123-40", "Oracle", properties);
        }

        [Test]
        public async Task TestOracleODPManaged4012()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
            await RunAdoJobStoreTest("OracleODPManaged-1211-40", "Oracle", properties);
        }

        [Test]
        public async Task TestOracleODP20()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
            await RunAdoJobStoreTest("OracleODP-20", "Oracle", properties);
        }

        [Test]
        public async Task TestMySql50()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            await RunAdoJobStoreTest("MySql-50", "MySQL", properties);
        }

        [Test]
        public async Task TestMySql51()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            await RunAdoJobStoreTest("MySql-51", "MySQL", properties);
        }

        [Test]
        public async Task TestMySql65()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            await RunAdoJobStoreTest("MySql-65", "MySQL", properties);
        }

        [Test]
        public async Task TestMySql10()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            await RunAdoJobStoreTest("MySql-10", "MySQL", properties);
        }

        [Test]
        public async Task TestMySql109()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            await RunAdoJobStoreTest("MySql-109", "MySQL", properties);
        }

        [Test]
        public async Task TestSQLite10()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz";
            await RunAdoJobStoreTest("SQLite-10", "SQLite", properties);
        }

        [Test]
        public async Task TestSQLite10Clustered()
        {
            clustered = true;
            try
            {
                await TestSQLite10();
            }
            finally
            {
                clustered = false;
            }
        }

        private async Task RunAdoJobStoreTest(string dbProvider, string connectionStringId)
        {
            await RunAdoJobStoreTest(dbProvider, connectionStringId, null);
        }

        private async Task RunAdoJobStoreTest(string dbProvider, string connectionStringId,
            NameValueCollection extraProperties)
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.threadPool.threadPriority"] = "Normal";
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.jobStore.clustered"] = clustered.ToString();
            properties["quartz.jobStore.clusterCheckinInterval"] = 1000.ToString();

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

            string connectionString;
            dbConnectionStrings.TryGetValue("SQLServer", out connectionString);
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();
            await sched.ClearAsync();

            JobDetailImpl jobWithData = new JobDetailImpl("datajob", "jobgroup", typeof (NoOpJob));
            jobWithData.JobDataMap["testkey"] = "testvalue";
            IOperableTrigger triggerWithData = new SimpleTriggerImpl("datatrigger", "triggergroup", 20, TimeSpan.FromSeconds(5));
            triggerWithData.JobDataMap.Add("testkey", "testvalue");
            triggerWithData.EndTimeUtc = DateTime.UtcNow.AddYears(10);
            triggerWithData.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
            await sched.ScheduleJobAsync(jobWithData, triggerWithData);
            await sched.ShutdownAsync();

            // try again with changing the useproperties against same set of data
            properties["quartz.jobStore.useProperties"] = true.ToString();
            sf = new StdSchedulerFactory(properties);
            sched = await sf.GetScheduler();

            var triggerWithDataFromDb = await sched.GetTriggerAsync(new TriggerKey("datatrigger", "triggergroup"));
            var jobWithDataFromDb = await sched.GetJobDetailAsync(new JobKey("datajob", "jobgroup"));
            Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
            Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));

            // once more
            await sched.DeleteJobAsync(jobWithData.Key);
            await sched.ScheduleJobAsync(jobWithData, triggerWithData);
            await sched.ShutdownAsync();

            properties["quartz.jobStore.useProperties"] = false.ToString();
            sf = new StdSchedulerFactory(properties);
            sched = await sf.GetScheduler();

            triggerWithDataFromDb = await sched.GetTriggerAsync(new TriggerKey("datatrigger", "triggergroup"));
            jobWithDataFromDb = await sched.GetJobDetailAsync(new JobKey("datajob", "jobgroup"));
            Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
            Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
        }

        [Test]
        [Explicit]
        public async Task TestSqlServerStress()
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.threadPool.threadPriority"] = "Normal";
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.jobStore.clustered"] = clustered.ToString();

            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            await RunAdoJobStoreTest("SqlServer-20", "SQLServer", properties);

            string connectionString;
            if (!dbConnectionStrings.TryGetValue("SQLServer", out connectionString))
            {
                throw new Exception("Unknown connection string id: " + "SQLServer");
            }
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            try
            {
                await sched.ClearAsync();

                if (scheduleJobs)
                {
                    ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                    ICalendar holidayCalendar = new HolidayCalendar();

                    for (int i = 0; i < 100000; ++i)
                    {
                        ITrigger trigger = new SimpleTriggerImpl("calendarsTrigger", "test", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                        JobDetailImpl jd = new JobDetailImpl("testJob", "test", typeof (NoOpJob));
                        await sched.ScheduleJobAsync(jd, trigger);
                    }
                }
                await sched.StartAsync();
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
            finally
            {
                await sched.ShutdownAsync(false);
            }
        }

        [Test]
        public async Task TestGetTriggerKeysWithLike()
        {
            var sched = await CreateScheduler(null);

            await sched.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupStartsWith("foo"));
        }

        [Test]
        public async Task TestGetTriggerKeysWithEquals()
        {
            var sched = await CreateScheduler(null);

            await sched.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals("bar"));
        }

        [Test]
        public async Task TestGetJobKeysWithLike()
        {
            var sched = await CreateScheduler(null);

            await sched.GetJobKeysAsync(GroupMatcher<JobKey>.GroupStartsWith("foo"));
        }

        [Test]
        public async Task TestGetJobKeysWithEquals()
        {
            var sched = await CreateScheduler(null);

            await sched.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals("bar"));
        }

        [Test]
        public async Task JobTypeNotFoundShouldNotBlock()
        {
            NameValueCollection properties = new NameValueCollection();
            properties.Add(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType, typeof (SpecialClassLoadHelper).AssemblyQualifiedName);
            var scheduler = await CreateScheduler(properties);

            await scheduler.DeleteJobsAsync(new[] {JobKey.Create("bad"), JobKey.Create("good")});

            await scheduler.StartAsync();

            var manualResetEvent = new ManualResetEvent(false);
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
            await scheduler.ScheduleJobsAsync(toSchedule, true);

            manualResetEvent.WaitOne(TimeSpan.FromSeconds(20));

            Assert.That(scheduler.GetTriggerStateAsync(badTrigger.Key), Is.EqualTo(TriggerState.Error));
        }

        private static async Task<IScheduler> CreateScheduler(NameValueCollection properties)
        {
            properties = properties ?? new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.threadPool.threadPriority"] = "Normal";
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
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

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
            properties["quartz.threadPool.threadPriority"] = "Normal";
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
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            try
            {
                await sched.ClearAsync();

                JobDetailImpl lonelyJob = new JobDetailImpl("lonelyJob", "lonelyGroup", typeof (SimpleRecoveryJob));
                lonelyJob.Durable = true;
                lonelyJob.RequestsRecovery = true;
                await sched.AddJobAsync(lonelyJob, false);
                await sched.AddJobAsync(lonelyJob, true);

                string schedId = sched.SchedulerInstanceId;

                JobDetailImpl job = new JobDetailImpl("job_to_use", schedId, typeof (SimpleRecoveryJob));

                for (int i = 0; i < 100000; ++i)
                {
                    IOperableTrigger trigger = new SimpleTriggerImpl("stressing_simple", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                    trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                    await sched.ScheduleJobAsync(job, trigger);
                }

                for (int i = 0; i < 100000; ++i)
                {
                    IOperableTrigger ct = new CronTriggerImpl("stressing_cron", "0/1 * * * * ?");
                    ct.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                    await sched.ScheduleJobAsync(job, ct);
                }

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                await sched.StartAsync();
                await Task.Delay(TimeSpan.FromMinutes(3));
                stopwatch.Stop();
                Console.WriteLine("Took: " + stopwatch.Elapsed);
            }
            finally
            {
                await sched.ShutdownAsync(false);
            }
        }

        public class BadJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        public class GoodJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                try
                {
                    ((ManualResetEvent) context.Scheduler.Context.Get(KeyResetEvent)).WaitOne(TimeSpan.FromSeconds(20));
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
                if (!string.IsNullOrEmpty(name) && typeof (BadJob) == Type.GetType(name))
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

        public Task TriggerFiredAsync(ITrigger trigger, IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task<bool> VetoJobExecutionAsync(ITrigger trigger, IJobExecutionContext context)
        {
            return Task.FromResult(false);
        }

        public Task TriggerMisfiredAsync(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerCompleteAsync(ITrigger trigger, IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode)
        {
            return TaskUtil.CompletedTask;
        }
    }

    internal class DummyJobListener : IJobListener
    {
        public string Name => GetType().FullName;

        public Task JobToBeExecutedAsync(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobExecutionVetoedAsync(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobWasExecutedAsync(IJobExecutionContext context, JobExecutionException jobException)
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
        public virtual void Execute(IJobExecutionContext context)
        {
            // delay for ten seconds
            Thread.Sleep(TimeSpan.FromSeconds(10));

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