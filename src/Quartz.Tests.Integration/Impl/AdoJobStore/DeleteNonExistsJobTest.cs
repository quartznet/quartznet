using System.Data;
using System.Data.Common;

using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Diagnostics;
using Quartz.Tests.Integration.Utils;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

[TestFixture(TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(TestConstants.PostgresProvider, Category = "db-postgres")]
public class DeleteNonExistsJobTest
{
    private readonly string provider;
    private static readonly ILogger<DeleteNonExistsJobTest> logger = LogProvider.CreateLogger<DeleteNonExistsJobTest>();
    private const string DBName = "default";
    private const string SchedulerName = "DeleteNonExistsJobTestScheduler";
    private static IScheduler scheduler;

    public DeleteNonExistsJobTest(string provider)
    {
        this.provider = provider;
    }

    [SetUp]
    public async Task SetUp()
    {
        var properties = DatabaseHelper.CreatePropertiesForProvider(provider);
        properties["quartz.scheduler.instanceName"] = $"{SchedulerName}_{Guid.NewGuid()}";
        properties["quartz.scheduler.instanceId"] = "AUTO";

        // First we must get a reference to a scheduler
        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        scheduler = await sf.GetScheduler();

        await ResetDatabaseData();
    }

    private static async Task ResetDatabaseData()
    {
        using var conn = DBConnectionManager.Instance.GetConnection(DBName);
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

    private static async Task RunDbCommand(DbConnection conn, string sql)
    {
        using var dbCommand = conn.CreateCommand();
        dbCommand.CommandType = CommandType.Text;
        dbCommand.CommandText = sql;
        await dbCommand.ExecuteNonQueryAsync();
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

    private static async Task ModifyStoredJobClassName()
    {
        using var conn = DBConnectionManager.Instance.GetConnection(DBName);
        await conn.OpenAsync();
        await RunDbCommand(conn, "update qrtz_job_details set job_class_name='com.FakeNonExistsJob'");
        conn.Close();
    }

    public class TestJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context)
        {
            logger.LogInformation("Job is executing {Context}", context);
            await Task.Yield();
        }
    }
}