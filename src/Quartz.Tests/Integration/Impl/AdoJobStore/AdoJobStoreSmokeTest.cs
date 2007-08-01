using System;
using System.Collections;
using System.Collections.Specialized;
using System.Threading;

using NUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Integration.Impl.AdoJobStore
{
    public class AdoJobStoreSmokeTest : IntegrationTest
    {
        private static readonly Hashtable dbConnectionStrings = new Hashtable();
        private bool clearJobs = true;
        private bool scheduleJobs = true;

        static AdoJobStoreSmokeTest()
        {
            dbConnectionStrings["Oracle"] = "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=quartznet;Password=quartznet;";
            dbConnectionStrings["SQLServer"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            dbConnectionStrings["MySQL"] = "Server = localhost; Database = quartz; Uid = quartznet; Pwd = quartznet";
            dbConnectionStrings["PostgreSQL"] = "Server=127.0.0.1;Port=5432;Userid=quartznet;Password=quartznet;Protocol=3;SSL=false; Pooling=true;MinPoolSize=1;MaxPoolSize=20;Encoding=UTF8;Timeout=15;SslMode=Disable;";
            dbConnectionStrings[""] = "";
            dbConnectionStrings[""] = "";
            dbConnectionStrings[""] = "";
            dbConnectionStrings[""] = "";
            dbConnectionStrings[""] = "";
        }

        [Test]
        public void TestPosgtreSQL()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";
            RunAdoJobStoreTest("Npgsql-10", "PostgreSQL", false, properties);
        }

        [Test]
        public void TestSqlServer11()
        {
            RunAdoJobStoreTest("SqlServer-11", "SQLServer", false);
        }

        [Test]
        public void TestSqlServer20()
        {
            RunAdoJobStoreTest("SqlServer-20", "SQLServer", false);
        }

        [Test]
        public void TestOracleClient20()
        {
            RunAdoJobStoreTest("OracleClient-20", "Oracle", false);
        }

        [Test]
        public void TestOracleODP20()
        {
            RunAdoJobStoreTest("OracleODP-20", "Oracle", false);
        }

        [Test]
        public void TestMySql50()
        {
            RunAdoJobStoreTest("MySql-50", "MySQL", false);
        }

        [Test]
        public void TestMySql51()
        {
            RunAdoJobStoreTest("MySql-51", "MySQL", false);
        }

        [Test]
        public void TestMySql10()
        {
            RunAdoJobStoreTest("MySql-10", "MySQL", false);
        }

        [Test]
        public void TestMySql109()
        {
            RunAdoJobStoreTest("MySql-109", "MySQL", false);
        }

        private void RunAdoJobStoreTest(string dbProvider, string connectionStringId, bool clustered)
        {
            RunAdoJobStoreTest(dbProvider, connectionStringId, clustered, null);
        }

        private void RunAdoJobStoreTest(string dbProvider, string connectionStringId, bool clustered, NameValueCollection extraProperties)
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.class"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "5";
            properties["quartz.threadPool.threadPriority"] = "Normal";
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.class"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
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

            if (connectionStringId == "SQLServer")
            {
                // if running MS SQL Server we need this
                properties["quartz.jobStore.selectWithLockSQL"] = "SELECT * FROM {0}LOCKS UPDLOCK WHERE LOCK_NAME = @lockName";
            }

            properties["quartz.dataSource.default.connectionString"] = (string) dbConnectionStrings[connectionStringId];
            properties["quartz.dataSource.default.provider"] = dbProvider;

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            sched = sf.GetScheduler();

            if (clearJobs)
            {
                CleanUp(sched);
            }

            if (scheduleJobs)
            {
                string schedId = sched.SchedulerInstanceId;

                int count = 1;

                JobDetail job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                job.RequestsRecovery = true;
                SimpleTrigger trigger = new SimpleTrigger("trig_" + count, schedId, 20, 5000L);

                trigger.StartTime = DateTime.Now.AddMilliseconds(1000L);
                sched.ScheduleJob(job, trigger);

                count++;
                job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                job.RequestsRecovery = (true);
                trigger = new SimpleTrigger("trig_" + count, schedId, 20, 5000L);

                trigger.StartTime = (DateTime.Now.AddMilliseconds(2000L));
                sched.ScheduleJob(job, trigger);

                count++;
                job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryStatefulJob));
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                job.RequestsRecovery = (true);
                trigger = new SimpleTrigger("trig_" + count, schedId, 20, 3000L);

                trigger.StartTime = (DateTime.Now.AddMilliseconds(1000L));
                sched.ScheduleJob(job, trigger);

                count++;
                job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                job.RequestsRecovery = (true);
                trigger = new SimpleTrigger("trig_" + count, schedId, 20, 4000L);

                trigger.StartTime = (DateTime.Now.AddMilliseconds(1000L));
                sched.ScheduleJob(job, trigger);

                count++;
                job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                job.RequestsRecovery = (true);
                trigger = new SimpleTrigger("trig_" + count, schedId, 20, 4500L);
                sched.ScheduleJob(job, trigger);

                count++;
                job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                job.RequestsRecovery = (true);
                CronTrigger ct = new CronTrigger("cron_trig_" + count, schedId, "0/10 * * * * ?");
                ct.StartTime = DateTime.Now.AddMilliseconds(1000);
                
                sched.ScheduleJob(job, ct);

                count++;
                job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
                // ask scheduler to re-Execute this job if it was in progress when
                // the scheduler went down...
                job.RequestsRecovery = (true);
                NthIncludedDayTrigger nt = new NthIncludedDayTrigger("cron_trig_" + count, schedId);
                nt.StartTime = DateTime.Now.Date.AddMilliseconds(1000);
                nt.N = 1;

                sched.ScheduleJob(job, nt);
            }

            sched.Start();
            Thread.Sleep(TimeSpan.FromSeconds(30));
            sched.Shutdown();
        }

        private static void CleanUp(IScheduler inScheduler)
        {
            // unschedule jobs
            string[] groups = inScheduler.TriggerGroupNames;
            for (int i = 0; i < groups.Length; i++)
            {
                string[] names = inScheduler.GetTriggerNames(groups[i]);
                for (int j = 0; j < names.Length; j++)
                    inScheduler.UnscheduleJob(names[j], groups[i]);
            }

            // delete jobs
            groups = inScheduler.JobGroupNames;
            for (int i = 0; i < groups.Length; i++)
            {
                string[] names = inScheduler.GetJobNames(groups[i]);
                for (int j = 0; j < names.Length; j++)
                    inScheduler.DeleteJob(names[j], groups[i]);
            }
        }


        public override void SetUp()
        {
            
        }

        public override void TearDown()
        {
            
        }
    }

    public class SimpleRecoveryJob : IJob
    {
        private const string COUNT = "count";

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
            if (data.Contains(COUNT))
            {
                count = data.GetInt(COUNT);
            }
            else
            {
                count = 0;
            }
            count++;
            data.Put(COUNT, count);

        }


    }

    public class SimpleRecoveryStatefulJob : SimpleRecoveryJob, IStatefulJob
    {

    }
}
