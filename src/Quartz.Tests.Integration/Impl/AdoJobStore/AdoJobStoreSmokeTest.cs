using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Spi;

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

        static AdoJobStoreSmokeTest()
        {
            dbConnectionStrings["Oracle"] =
                "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=quartznet;Password=quartznet;";
            dbConnectionStrings["SQLServer"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            dbConnectionStrings["SQLServerCe"] = @"Data Source=C:\quartznet.sdf;Persist Security Info=False;";
            dbConnectionStrings["MySQL"] = "Server = localhost; Database = quartz; Uid = quartznet; Pwd = quartznet";
            dbConnectionStrings["PostgreSQL"] =
                "Server=127.0.0.1;Port=5432;Userid=quartznet;Password=quartznet;Protocol=3;SSL=false; Pooling=true;MinPoolSize=1;MaxPoolSize=20;Encoding=UTF8;Timeout=15;SslMode=Disable;";
            dbConnectionStrings["SQLite"] = "Data Source=test.db;Version=3;";
            dbConnectionStrings["Firebird"] = "User=SYSDBA;Password=masterkey;Database=c:\\quartznet;DataSource=localhost;Port=3050;Dialect=3; Charset=NONE;Role=;Connection lifetime=15;Pooling=true;MinPoolSize=0;MaxPoolSize=50;Packet Size=8192;ServerType=0;";
        }

        [Test]
        public void TestFirebird()
        {
            RunAdoJobStoreTest("Firebird-201", "Firebird");
        }

        [Test]
        public void TestPostgreSQL()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";
            RunAdoJobStoreTest("Npgsql-10", "PostgreSQL", properties);
        }

        [Test]
        public void TestSqlServer11()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            RunAdoJobStoreTest("SqlServer-11", "SQLServer", properties);
        }

        [Test]
        public void TestSqlServer20()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            RunAdoJobStoreTest("SqlServer-20", "SQLServer", properties);
        }


        [Test]
        public void TestSqlServerCe351()
        {
            bool previousClustered = clustered;
            clustered = false;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
            try
            {
                RunAdoJobStoreTest("SqlServerCe-351", "SQLServerCe", properties);
            }
            finally
            {
                clustered = previousClustered;
            }
        }


        [Test]
        public void TestOracleClient20()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
            RunAdoJobStoreTest("OracleClient-20", "Oracle", properties);
        }

        [Test]
        public void TestOracleODP20()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
            RunAdoJobStoreTest("OracleODP-20", "Oracle", properties);
        }

        [Test]
        public void TestMySql50()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            RunAdoJobStoreTest("MySql-50", "MySQL", properties);
        }

        [Test]
        public void TestMySql51()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            RunAdoJobStoreTest("MySql-51", "MySQL", properties);
        }

        [Test]
        public void TestMySql10()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            RunAdoJobStoreTest("MySql-10", "MySQL", properties);
        }

        [Test]
        public void TestMySql109()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
            RunAdoJobStoreTest("MySql-109", "MySQL", properties);
        }

        [Test]
        public void TestSQLite10()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz";
            RunAdoJobStoreTest("SQLite-10", "SQLite", properties);
        }

        [Test]
        public void TestSQLite10Clustered()
        {
            clustered = true;
            try
            {
                TestSQLite10();
            }
            finally
            {
                clustered = false;
            }
        }


        private void RunAdoJobStoreTest(string dbProvider, string connectionStringId)
        {
            RunAdoJobStoreTest(dbProvider, connectionStringId, null);
        }

        private void RunAdoJobStoreTest(string dbProvider, string connectionStringId,
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

            if (extraProperties != null)
            {
                foreach (string key in extraProperties.Keys)
                {
                    properties[key] = extraProperties[key];
                }
            }

            if (connectionStringId == "SQLServer" || connectionStringId == "SQLite")
            {
                // if running MS SQL Server we need this
                properties["quartz.jobStore.lockHandler.type"] =
                    "Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore, Quartz";
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
            IScheduler sched = sf.GetScheduler();

            try
            {
                if (clearJobs)
                {
                    CleanUp(sched);
                }

                if (scheduleJobs)
                {
                    ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                    ICalendar holidayCalendar = new HolidayCalendar();

                    // QRTZNET-86
                    ITrigger t = sched.GetTrigger(new TriggerKey("NonExistingTrigger", "NonExistingGroup"));
                    Assert.IsNull(t);

                    AnnualCalendar cal = new AnnualCalendar();
                    sched.AddCalendar("annualCalendar", cal, false, true);

                    IOperableTrigger calendarsTrigger = new SimpleTriggerImpl("calendarsTrigger", "test", 20, TimeSpan.FromMilliseconds(5));
                    calendarsTrigger.CalendarName = "annualCalendar";

                    JobDetailImpl jd = new JobDetailImpl("testJob", "test", typeof(NoOpJob));
                    sched.ScheduleJob(jd, calendarsTrigger);

                    // QRTZNET-93
                    sched.AddCalendar("annualCalendar", cal, true, true);

                    sched.AddCalendar("baseCalendar", new BaseCalendar(), false, true);
                    sched.AddCalendar("cronCalendar", cronCalendar, false, true);
                    sched.AddCalendar("dailyCalendar", new DailyCalendar(DateTime.Now.Date, DateTime.Now.AddMinutes(1)), false, true);
                    sched.AddCalendar("holidayCalendar", holidayCalendar, false, true);
                    sched.AddCalendar("monthlyCalendar", new MonthlyCalendar(), false, true);
                    sched.AddCalendar("weeklyCalendar", new WeeklyCalendar(), false, true);

                    sched.AddCalendar("cronCalendar", cronCalendar, true, true);
                    sched.AddCalendar("holidayCalendar", holidayCalendar, true, true);

                    Assert.IsNotNull(sched.GetCalendar("annualCalendar"));

                    JobDetailImpl lonelyJob = new JobDetailImpl("lonelyJob", "lonelyGroup", typeof(SimpleRecoveryJob));
                    lonelyJob.Durable = true;
                    lonelyJob.RequestsRecovery = true;
                    sched.AddJob(lonelyJob, false);
                    sched.AddJob(lonelyJob, true);
                    
                    string schedId = sched.SchedulerInstanceId;

                    int count = 1;

                    JobDetailImpl job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));

                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = true;
                    IOperableTrigger trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));

                    trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
                    sched.ScheduleJob(job, trigger);

                    // check that trigger was stored
                    ITrigger persisted = sched.GetTrigger(new TriggerKey("trig_" + count, schedId));
                    Assert.IsNotNull(persisted);
                    Assert.IsTrue(persisted is SimpleTriggerImpl);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(2000L));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryStatefulJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(3));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromSeconds(4));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTriggerImpl("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    IOperableTrigger ct = new CronTriggerImpl("cron_trig_" + count, schedId, "0/10 * * * * ?");
                    ct.StartTimeUtc = DateTime.Now.AddMilliseconds(1000);

                    sched.ScheduleJob(job, ct);

                    count++;
                    job = new JobDetailImpl("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    NthIncludedDayTrigger nt = new NthIncludedDayTrigger("cron_trig_" + count, schedId);
                    nt.StartTimeUtc = DateTime.Now.Date.AddMilliseconds(1000);
                    nt.N = 1;

                    sched.ScheduleJob(job, nt);

                    sched.Start();

                    sched.PauseAll();

                    sched.ResumeAll();

                    sched.PauseJob(new JobKey("job_1", schedId));

                    sched.ResumeJob(new JobKey("job_1", schedId));

                    sched.PauseJobGroup(schedId);
                    
                    Thread.Sleep(1000);

                    sched.ResumeJobGroup(schedId);

                    sched.PauseTrigger(new TriggerKey("trig_2", schedId));
                    sched.ResumeTrigger(new TriggerKey("trig_2", schedId));

                    sched.PauseTriggerGroup(schedId);
                    
                    Assert.AreEqual(1, sched.GetPausedTriggerGroups().Count);

                    Thread.Sleep(1000);
                    sched.ResumeTriggerGroup(schedId);


                    Thread.Sleep(TimeSpan.FromSeconds(20));

                    sched.Standby();

                    CollectionAssert.IsNotEmpty(sched.GetCalendarNames());
                    CollectionAssert.IsNotEmpty(sched.GetJobKeys(schedId));

                    CollectionAssert.IsNotEmpty(sched.GetTriggersOfJob(new JobKey("job_2", schedId)));
                    Assert.IsNotNull(sched.GetJobDetail(new JobKey("job_2", schedId)));

                    sched.DeleteCalendar("cronCalendar");
                    sched.DeleteCalendar("holidayCalendar");
                    sched.DeleteJob(new JobKey("lonelyJob", "lonelyGroup"));
                    
                }
            }
            finally
            {
                sched.Shutdown(false);
            }
        }


        [Test]
        [Explicit]
        public void TestSqlServerStress()
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
            RunAdoJobStoreTest("SqlServer-20", "SQLServer", properties);
            properties["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore, Quartz";

            string connectionString;
            if (!dbConnectionStrings.TryGetValue("SQLServer", out connectionString))
            {
                throw new Exception("Unknown connection string id: " + "SQLServer");
            }
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();

            try
            {
                if (clearJobs)
                {
                    CleanUp(sched);
                }

                if (scheduleJobs)
                {
                    ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                    ICalendar holidayCalendar = new HolidayCalendar();

                    for (int i = 0; i < 100000; ++i)
                    {
                        ITrigger trigger = new SimpleTriggerImpl("calendarsTrigger", "test", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                        JobDetailImpl jd = new JobDetailImpl("testJob", "test", typeof(NoOpJob));
                        sched.ScheduleJob(jd, trigger);
                    }


        
                    sched.Start();
                }
            }
            finally
            {
                sched.Shutdown(false);
            }

        }

        private static void CleanUp(IScheduler inScheduler)
        {
            // unschedule jobs
            IList<string> groups = inScheduler.GetTriggerGroupNames();
            foreach (string group in groups)
            {
                IList<TriggerKey> names = inScheduler.GetTriggerKeys(group);
                for (int j = 0; j < names.Count; j++)
                {
                    inScheduler.UnscheduleJob(names[j]);
                }
            }

            // delete jobs
            groups = inScheduler.GetJobGroupNames();
            foreach (string group in groups)
            {
                IList<JobKey> jobNames = inScheduler.GetJobKeys(group);
                foreach (JobKey jobKey in jobNames)
                {
                    inScheduler.DeleteJob(jobKey);
                }
            }

            inScheduler.DeleteCalendar("annualCalendar");
            inScheduler.DeleteCalendar("baseCalendar");
            inScheduler.DeleteCalendar("cronCalendar");
            inScheduler.DeleteCalendar("dailyCalendar");
            inScheduler.DeleteCalendar("holidayCalendar");
            inScheduler.DeleteCalendar("monthlyCalendar");
            inScheduler.DeleteCalendar("weeklyCalendar");
        }

        [Test]
        [Explicit]
        public void StressTest()
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
            properties["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore, Quartz";

            string connectionString = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.connectionString"] = connectionString;
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();

            try
            {
                    CleanUp(sched);

                    JobDetailImpl lonelyJob = new JobDetailImpl("lonelyJob", "lonelyGroup", typeof(SimpleRecoveryJob));
                    lonelyJob.Durable = true;
                    lonelyJob.RequestsRecovery = true;
                    sched.AddJob(lonelyJob, false);
                    sched.AddJob(lonelyJob, true);

                    string schedId = sched.SchedulerInstanceId;

                    JobDetailImpl job = new JobDetailImpl("job_to_use", schedId, typeof(SimpleRecoveryJob));

                    for (int i = 0; i < 100000; ++i)
                    {
                        IOperableTrigger trigger = new SimpleTriggerImpl("stressing_simple", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                        trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                        sched.ScheduleJob(job, trigger);
                    }

                    for (int i = 0; i < 100000; ++i)
                    {
                        IOperableTrigger ct = new CronTriggerImpl("stressing_cron", "0/1 * * * * ?");
                        ct.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                        sched.ScheduleJob(job, ct);
                    }
    
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    sched.Start();
                    Thread.Sleep(TimeSpan.FromMinutes(3));
                    stopwatch.Stop();
                    Console.WriteLine("Took: " + stopwatch.Elapsed);
            }
            finally
            {
                sched.Shutdown(false);
            }
        }

    }

    internal class DummyTriggerListener : ITriggerListener
    {
        public string Name
        {
            get { return GetType().FullName; }
        }

        public void TriggerFired(ITrigger trigger, IJobExecutionContext context)
        {
        }

        public bool VetoJobExecution(ITrigger trigger, IJobExecutionContext context)
        {
            return false;
        }

        public void TriggerMisfired(ITrigger trigger)
        {
        }

        public void TriggerComplete(ITrigger trigger, IJobExecutionContext context,
                                    SchedulerInstruction triggerInstructionCode)
        {
        }
    }

    internal class DummyJobListener : IJobListener
    {

        public string Name
        {
            get { return GetType().FullName; }
        }

        public void JobToBeExecuted(IJobExecutionContext context)
        {
            
        }

        public void JobExecutionVetoed(IJobExecutionContext context)
        {
            
        }

        public void JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException)
        {
            
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
            try
            {
                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
            catch (ThreadInterruptedException)
            {
            }

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