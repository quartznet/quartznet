using Quartz.Jobs;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A job data map larger than a MySQL <c>BLOB</c> (65,535 bytes) round-trips through the job and
/// trigger tables, whose <c>JOB_DATA</c> columns are <c>MEDIUMBLOB</c>.
/// </summary>
[Category("db-mysql")]
[NonParallelizable]
public sealed class MySqlLargeJobDataTest
{
    private static readonly string LargeValue = new('x', 100_000);

    [Test]
    public async Task JobDataOver64KilobytesIsStoredAndReadBack()
    {
        IScheduler scheduler = await CreateScheduler();
        try
        {
            await scheduler.Clear();

            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("large-job-data")
                .UsingJobData("selection", LargeValue)
                .StoreDurably()
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("large-job-data")
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow.AddDays(1))
                .UsingJobData("selection", LargeValue)
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            IJobDetail storedJob = await scheduler.GetJobDetail(job.Key);
            ITrigger storedTrigger = await scheduler.GetTrigger(trigger.Key);

            storedJob.Should().NotBeNull();
            storedJob!.JobDataMap.GetString("selection").Should().Be(LargeValue);
            storedTrigger.Should().NotBeNull();
            storedTrigger!.JobDataMap.GetString("selection").Should().Be(LargeValue);
        }
        finally
        {
            await scheduler.Clear();
            await scheduler.Shutdown(false);
        }
    }

    private static ValueTask<IScheduler> CreateScheduler()
    {
        string connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
                                  ?? "Server = localhost; Database = quartznet; Uid = quartznet; Pwd = quartznet";

        return QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options =>
                {
                    options.InstanceName = "MySqlLargeJobDataTest";
                    options.GenerateInstanceId = true;
                })
                .UseDefaultThreadPool()
                .UsePersistentStore(store =>
                {
                    store.UseMySqlConnector(connectionString);
                    store.UseNewtonsoftJsonSerializer();
                }))
            .BuildScheduler();
    }
}
