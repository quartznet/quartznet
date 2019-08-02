using System.Collections.Specialized;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Logging;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore
{
    [TestFixture]
    [Category("sqlserver")]
    public class DeleteNonExistsJobTest
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(DeleteNonExistsJobTest));
        private const string DBName = "default";
        private const string SchedulerName = "DeleteNonExistsJobTestScheduler";
        private static IScheduler scheduler;

        [SetUp]
        public async Task SetUp()
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = SchedulerName;
            properties["quartz.scheduler.instanceId"] = "AUTO";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.dataSource.default.connectionString"] = TestConstants.SqlServerConnectionString;
            properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            scheduler = await sf.GetScheduler();

            await ResetDatabaseData();
        }

        private async Task ResetDatabaseData()
        {
            using (var conn = DBConnectionManager.Instance.GetConnection(DBName))
            {
                await conn.OpenAsync();
                await RunDbCommand(conn, "delete from qrtz_fired_triggers");
                await RunDbCommand(conn, "delete from qrtz_paused_trigger_grps");
                await RunDbCommand(conn, "delete from qrtz_scheduler_state");
                await RunDbCommand(conn, "delete from qrtz_locks");
                await RunDbCommand(conn, "delete from qrtz_simple_triggers");
                await RunDbCommand(conn, "delete from qrtz_simprop_triggers");
                await RunDbCommand(conn, "delete from qrtz_blob_triggers");
                await RunDbCommand(conn, "delete from qrtz_cron_triggers");
                await RunDbCommand(conn, "delete from qrtz_triggers");
                await RunDbCommand(conn, "delete from qrtz_job_details");
                await RunDbCommand(conn, "delete from qrtz_calendars");
                conn.Close();
            }
        }

        private async Task RunDbCommand(DbConnection conn, string sql)
        {
            using (var dbCommand = conn.CreateCommand())
            {
                dbCommand.CommandType = CommandType.Text;
                dbCommand.CommandText = sql;
                await dbCommand.ExecuteNonQueryAsync();
            }
        }

        [TearDown]
        public void TearDown()
        {
            scheduler.Shutdown(true);
        }

        [Test]
        public async Task DeleteJobDetailOnly()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob").StoreDurably().Build();
            await scheduler.AddJob(jobDetail, true);
            await ModifyStoredJobClassName();

            await scheduler.DeleteJob(jobDetail.Key);
        }

        [Test]
        public async Task DeleteJobDetailWithTrigger()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob2").StoreDurably().Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testjob2")
                .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                .Build();

            await scheduler.ScheduleJob(jobDetail, trigger);
            await ModifyStoredJobClassName();

            await scheduler.DeleteJob(jobDetail.Key);
        }

        [Test]
        public async Task DeleteTrigger()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob3").StoreDurably().Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testjob3")
                .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                .Build();
            await scheduler.ScheduleJob(jobDetail, trigger);
            await ModifyStoredJobClassName();

            await scheduler.UnscheduleJob(trigger.Key);
        }

        [Test]
        public async Task ReplaceJobDetail()
        {
            IJobDetail jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob3").StoreDurably().Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testjob3")
                .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                .Build();
            await scheduler.ScheduleJob(jobDetail, trigger);
            await ModifyStoredJobClassName();

            jobDetail = JobBuilder.Create<TestJob>().WithIdentity("testjob3").StoreDurably().Build();
            await scheduler.AddJob(jobDetail, true);
        }

        private async Task ModifyStoredJobClassName()
        {
            using (var conn = DBConnectionManager.Instance.GetConnection(DBName))
            {
                await conn.OpenAsync();
                await RunDbCommand(conn, "update qrtz_job_details set job_class_name='com.FakeNonExistsJob'");
                conn.Close();
            }
        }

        public class TestJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                log.InfoFormat("Job is executing {0}", context);
                return TaskUtil.CompletedTask;
            }
        }
    }
}