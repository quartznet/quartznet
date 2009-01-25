using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading;

using Common.Logging;
using Common.Logging.Simple;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Job;

namespace Quartz.Tests.Integration.Impl.AdoJobStore
{
    [Category("integration")]
    [TestFixture]
    public class AdoJobStoreSmokeTest
    {
        private static readonly Hashtable dbConnectionStrings = new Hashtable();
        private bool clearJobs = true;
        private bool scheduleJobs = true;
        private bool clustered = true;

        [TestFixtureSetUp]
        public void SetUpFixture()
        {
            // configure logging
            LogManager.Adapter = new ConsoleOutLoggerFactoryAdapter();

            dbConnectionStrings["Oracle"] =
                "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=quartznet;Password=quartznet;";
            dbConnectionStrings["SQLServer"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
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
        public void TestOracleClient20()
        {
            RunAdoJobStoreTest("OracleClient-20", "Oracle");
        }

        [Test]
        public void TestOracleODP20()
        {
            RunAdoJobStoreTest("OracleODP-20", "Oracle");
        }

        [Test]
        public void TestMySql50()
        {
            RunAdoJobStoreTest("MySql-50", "MySQL");
        }

        [Test]
        public void TestMySql51()
        {
            RunAdoJobStoreTest("MySql-51", "MySQL");
        }

        [Test]
        public void TestMySql10()
        {
            RunAdoJobStoreTest("MySql-10", "MySQL");
        }

        [Test]
        public void TestMySql109()
        {
            RunAdoJobStoreTest("MySql-109", "MySQL");
        }

        [Test]
        public void TestSQLite10()
        {
            RunAdoJobStoreTest("SQLite-10", "SQLite");
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

            properties["quartz.dataSource.default.connectionString"] = (string) dbConnectionStrings[connectionStringId];
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
                    Trigger t = sched.GetTrigger("NonExistingTrigger", "NonExistingGroup");
                    Assert.IsNull(t);

                    AnnualCalendar cal = new AnnualCalendar();
                    sched.AddCalendar("annualCalendar", cal, false, true);

                    SimpleTrigger calendarsTrigger = new SimpleTrigger("calendarsTrigger", "test", 20, TimeSpan.FromMilliseconds(5));
                    calendarsTrigger.CalendarName = "annualCalendar";

                    JobDetail jd = new JobDetail("testJob", "test", typeof(NoOpJob));
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

                    JobDetail lonelyJob = new JobDetail("lonelyJob", "lonelyGroup", typeof(SimpleRecoveryJob));
                    lonelyJob.Durable = true;
                    lonelyJob.RequestsRecovery = true;
                    sched.AddJob(lonelyJob, false);
                    sched.AddJob(lonelyJob, true);

                    sched.AddJobListener(new DummyJobListener());
                    sched.AddTriggerListener(new DummyTriggerListener());
                    
                    string schedId = sched.SchedulerInstanceId;

                    int count = 1;

                    JobDetail job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    job.AddJobListener(new DummyJobListener().Name);

                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = true;
                    SimpleTrigger trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));

                    trigger.AddTriggerListener(new DummyTriggerListener().Name);
                    trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
                    sched.ScheduleJob(job, trigger);

                    // check that trigger was stored
                    Trigger persisted = sched.GetTrigger("trig_" + count, schedId);
                    Assert.IsNotNull(persisted);
                    Assert.IsTrue(persisted is SimpleTrigger);

                    count++;
                    job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromSeconds(5));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(2000L));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryStatefulJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromSeconds(3));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromSeconds(4));

                    trigger.StartTimeUtc = (DateTime.Now.AddMilliseconds(1000L));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));
                    sched.ScheduleJob(job, trigger);

                    count++;
                    job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
                    // ask scheduler to re-Execute this job if it was in progress when
                    // the scheduler went down...
                    job.RequestsRecovery = (true);
                    CronTrigger ct = new CronTrigger("cron_trig_" + count, schedId, "0/10 * * * * ?");
                    ct.StartTimeUtc = DateTime.Now.AddMilliseconds(1000);

                    sched.ScheduleJob(job, ct);

                    count++;
                    job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
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

                    sched.PauseJob("job_1", schedId);

                    sched.ResumeJob("job_1", schedId);

                    sched.PauseJobGroup(schedId);
                    
                    Thread.Sleep(1000);

                    sched.ResumeJobGroup(schedId);

                    sched.PauseTrigger("trig_2", schedId);
                    sched.ResumeTrigger("trig_2", schedId);

                    sched.PauseTriggerGroup(schedId);
                    
                    Assert.AreEqual(1, sched.GetPausedTriggerGroups().Count);

                    Thread.Sleep(1000);
                    sched.ResumeTriggerGroup(schedId);


                    Thread.Sleep(TimeSpan.FromSeconds(20));

                    sched.Standby();

                    Assert.IsNotEmpty(sched.GetCalendarNames());
                    Assert.IsNotEmpty(sched.GetJobNames(schedId));

                    Assert.IsNotEmpty(sched.GetTriggersOfJob("job_2", schedId));
                    Assert.IsNotNull(sched.GetJobDetail("job_2", schedId));

                    sched.RemoveJobListener(new DummyJobListener().Name);
                    sched.RemoveTriggerListener(new DummyTriggerListener().Name);

                    sched.DeleteCalendar("cronCalendar");
                    sched.DeleteCalendar("holidayCalendar");
                    sched.DeleteJob("lonelyJob", "lonelyGroup");
                    
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
            string[] groups = inScheduler.TriggerGroupNames;
            for (int i = 0; i < groups.Length; i++)
            {
                string[] names = inScheduler.GetTriggerNames(groups[i]);
                for (int j = 0; j < names.Length; j++)
                {
                    inScheduler.UnscheduleJob(names[j], groups[i]);
                }
            }

            // delete jobs
            groups = inScheduler.JobGroupNames;
            for (int i = 0; i < groups.Length; i++)
            {
                string[] names = inScheduler.GetJobNames(groups[i]);
                for (int j = 0; j < names.Length; j++)
                {
                    inScheduler.DeleteJob(names[j], groups[i]);
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
    }

    internal class DummyTriggerListener : ITriggerListener
    {
        public string Name
        {
            get { return GetType().FullName; }
        }

        public void TriggerFired(Trigger trigger, JobExecutionContext context)
        {
        }

        public bool VetoJobExecution(Trigger trigger, JobExecutionContext context)
        {
            return false;
        }

        public void TriggerMisfired(Trigger trigger)
        {
        }

        public void TriggerComplete(Trigger trigger, JobExecutionContext context,
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

        public void JobToBeExecuted(JobExecutionContext context)
        {
            
        }

        public void JobExecutionVetoed(JobExecutionContext context)
        {
            
        }

        public void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException)
        {
            
        }
    }

    public class SimpleRecoveryJob : IJob
    {
        private const string Count = "count";

        /// <summary> 
        /// Called by the <see cref="IScheduler" /> when a
        /// <see cref="Trigger" /> fires that is associated with
        /// the <see cref="IJob" />.
        /// </summary>
        public virtual void Execute(JobExecutionContext context)
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
            if (data.Contains(Count))
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

    public class SimpleRecoveryStatefulJob : SimpleRecoveryJob, IStatefulJob
    {
    }
}