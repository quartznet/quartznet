using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Data.SqlClient;

using NUnit.Framework;

using Quartz.Tests.Integration.TestHelpers;

namespace Quartz.Tests.Integration;

public class JobMaintenanceTests : IntegrationTest
{
    [Test]
    public async Task CanUnscheduleNonConstructableTypeJobs()
    {
        var scheduler = await GetACleanScheduler();

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

    private async Task<IScheduler> GetACleanScheduler()
    {
        var scheduler = await SchedulerHelper.CreateScheduler(nameof(JobMaintenanceTests));
        await scheduler.Clear();
        return scheduler;
    }

    // Rename the Job Type ClassName
    private static void RenameJobClass(string jobClassName)
    {
        using var dbConnection = new SqlConnection(TestConstants.SqlServerConnectionString);

        var sql = $@"
update {SchedulerHelper.TablePrefix}JOB_DETAILS
set JOB_CLASS_NAME = '{jobClassName}'
where SCHED_NAME = 'JobMaintenanceTestsScheduler'";
        dbConnection.Open();
        using var command = new SqlCommand(sql, dbConnection);
        command.ExecuteNonQuery();
    }

    public class KnownJobType : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }
}