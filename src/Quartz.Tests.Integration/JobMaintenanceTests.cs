using System.Data.Common;

using FluentAssertions;

using Microsoft.Data.SqlClient;

using Npgsql;

using NUnit.Framework;

using Quartz.Tests.Integration.TestHelpers;

namespace Quartz.Tests.Integration;

[TestFixture(TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(TestConstants.PostgresProvider, Category = "db-postgres")]
public class JobMaintenanceTests : IntegrationTest
{
    private readonly string provider;

    public JobMaintenanceTests(string provider)
    {
        this.provider = provider;
    }

    [Test]
    public async Task CanUnscheduleNonConstructableTypeJobs()
    {
        var scheduler = await GetCleanScheduler();

        const string jobKey = "CanDeleteUnknownTypeJobs";
        const string triggerKey = "CanDeleteUnknownTypeJobsTrigger";
        var jobDetail = JobBuilder.Create<KnownJobType>()
            .WithIdentity(jobKey)
            .UsingJobData("jfoo", "bar")
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule("0 0 0 * * ?")
            .UsingJobData("tfoo", "bar")
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);

        RenameJobClass("Quartz.Tests.Integration.JobMaintenanceTests+UnKnownJobType");

        // assert job is stored
        var storedJobDetail = await scheduler.GetJobDetail(new JobKey(jobKey));
        var storedJobMap = storedJobDetail.JobDataMap;
        Assert.That(storedJobMap.Dirty, Is.False);

        var storedTrigger = await scheduler.GetTrigger(new TriggerKey(triggerKey));
        var storedTriggerMap = storedTrigger.JobDataMap;
        Assert.That(storedTriggerMap.Dirty, Is.False);

        // assert can unSchedule
        var unscheduleResponse = await scheduler.UnscheduleJob(new TriggerKey(triggerKey), CancellationToken.None);
        unscheduleResponse.Should().BeTrue();
    }

    private async Task<IScheduler> GetCleanScheduler()
    {
        var scheduler = await SchedulerHelper.CreateScheduler(provider, nameof(JobMaintenanceTests));
        await scheduler.Clear();
        return scheduler;
    }

    // Rename the Job Type ClassName
    private void RenameJobClass(string jobClassName)
    {
        using DbConnection dbConnection = provider == TestConstants.DefaultSqlServerProvider
            ? new SqlConnection(TestConstants.SqlServerConnectionString)
            : new NpgsqlConnection(TestConstants.PostgresConnectionString);

        var sql = $@"
update {SchedulerHelper.TablePrefix}JOB_DETAILS
set JOB_CLASS_NAME = '{jobClassName}'
where SCHED_NAME = 'JobMaintenanceTestsScheduler'";

        dbConnection.Open();

        using DbCommand command = provider == TestConstants.DefaultSqlServerProvider
            ? new SqlCommand(sql, (SqlConnection) dbConnection)
            : new NpgsqlCommand(sql, (NpgsqlConnection) dbConnection);

        command.ExecuteNonQuery();
    }

    public class KnownJobType : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }
}