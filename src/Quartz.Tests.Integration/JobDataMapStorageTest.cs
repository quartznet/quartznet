using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Tests.Integration.Utils;

namespace Quartz.Tests.Integration;

[TestFixture(TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(TestConstants.PostgresProvider, Category = "db-postgres")]
public class JobDataMapStorageTest : IntegrationTest
{
    private readonly string provider;

    public JobDataMapStorageTest(string provider)
    {
        this.provider = provider;
    }

    [Test]
    public async Task TestJobDataMapDirtyFlag()
    {
        IScheduler scheduler = await CreateScheduler("testBasicStorageFunctions");
        await scheduler.Clear();

        IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity("test")
            .UsingJobData("jfoo", "bar")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithCronSchedule("0 0 0 * * ?")
            .UsingJobData("tfoo", "bar")
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);

        IJobDetail storedJobDetail = await scheduler.GetJobDetail(new JobKey("test"));
        JobDataMap storedJobMap = storedJobDetail.JobDataMap;
        Assert.That(storedJobMap.Dirty, Is.False);

        ITrigger storedTrigger = await scheduler.GetTrigger(new TriggerKey("test"));
        JobDataMap storedTriggerMap = storedTrigger.JobDataMap;
        Assert.That(storedTriggerMap.Dirty, Is.False);
    }

    private async ValueTask<IScheduler> CreateScheduler(string name)
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out var driverDelegateType);

        var serializer = new NewtonsoftJsonObjectSerializer();
        serializer.Initialize();
        var jobStore = new JobStoreTX
        {
            DataSource = "default",
            TablePrefix = "QRTZ_",
            InstanceId = "AUTO",
            DriverDelegateType = driverDelegateType,
            ObjectSerializer = serializer
        };

        await DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new DefaultThreadPool(), jobStore);
        return SchedulerRepository.Instance.Lookup(name + "Scheduler");
    }
}