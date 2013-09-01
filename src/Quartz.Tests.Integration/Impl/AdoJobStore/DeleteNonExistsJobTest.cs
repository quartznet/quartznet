using System.Collections.Specialized;
using System.Data;

using Common.Logging;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore
{
    [TestFixture]
    public class DeleteNonExistsJobTest
    {
        private static readonly ILog log = LogManager.GetLogger<DeleteNonExistsJobTest>();
        private const string DBName = "default";
        private const string SchedulerName = "DeleteNonExistsJobTestScheduler";
        private static IScheduler scheduler;

        [SetUp]
        public void SetUp()
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = SchedulerName;
            properties["quartz.scheduler.instanceId"] = "AUTO";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.dataSource.default.connectionString"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            scheduler = sf.GetScheduler();

            ResetDatabaseData();
        }

        private void ResetDatabaseData()
        {
            using (var conn = DBConnectionManager.Instance.GetConnection(DBName))
            {
                conn.Open();
                RunDbCommand(conn, "delete from qrtz_fired_triggers");
                RunDbCommand(conn, "delete from qrtz_paused_trigger_grps");
                RunDbCommand(conn, "delete from qrtz_scheduler_state");
                RunDbCommand(conn, "delete from qrtz_locks");
                RunDbCommand(conn, "delete from qrtz_simple_triggers");
                RunDbCommand(conn, "delete from qrtz_simprop_triggers");
                RunDbCommand(conn, "delete from qrtz_blob_triggers");
                RunDbCommand(conn, "delete from qrtz_cron_triggers");
                RunDbCommand(conn, "delete from qrtz_triggers");
                RunDbCommand(conn, "delete from qrtz_job_details");
                RunDbCommand(conn, "delete from qrtz_calendars");
                conn.Close();
            }
        }

        private void RunDbCommand(IDbConnection conn, string sql)
        {
            using (IDbCommand dbCommand = conn.CreateCommand())
            {
                dbCommand.CommandType = CommandType.Text;
                dbCommand.CommandText = sql;
                dbCommand.ExecuteNonQuery();
            }
        }

        [TearDown]
        public void TearDown()
        {
            scheduler.Shutdown(true);
        }

        [Test]
        public void DeleteJobDetailOnly()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob").StoreDurably().Build();
            scheduler.AddJob(jobDetail, true);
            ModifyStoredJobClassName();

            scheduler.DeleteJob(jobDetail.Key);
        }

        [Test]
        public void DeleteJobDetailWithTrigger()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob2").StoreDurably().Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testjob2")
                .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                .Build();
            scheduler.ScheduleJob(jobDetail, trigger);
            ModifyStoredJobClassName();

            scheduler.DeleteJob(jobDetail.Key);
        }

        [Test]
        public void DeleteTrigger()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob3").StoreDurably().Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testjob3")
                .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                .Build();
            scheduler.ScheduleJob(jobDetail, trigger);
            ModifyStoredJobClassName();

            scheduler.UnscheduleJob(trigger.Key);
        }

        [Test]
        public void ReplaceJobDetail()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob3").StoreDurably().Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testjob3")
                .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                .Build();
            scheduler.ScheduleJob(jobDetail, trigger);
            ModifyStoredJobClassName();

            jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob3").StoreDurably().Build();
            scheduler.AddJob(jobDetail, true);
        }

        private void ModifyStoredJobClassName()
        {
            using (var conn = DBConnectionManager.Instance.GetConnection(DBName))
            {
                conn.Open();
                RunDbCommand(conn, "update qrtz_job_details set job_class_name='com.FakeNonExistsJob'");
                conn.Close();
            }
        }

        public class TestJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                log.InfoFormat("Job is executing {0}", context);
            }
        }
    }
}