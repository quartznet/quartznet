using Quartz.Tests.Integration.TestHelpers;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Tests.Integration.Utils;

namespace Quartz.Tests.Integration;

[TestFixture(typeof(SystemTextJsonObjectSerializer), TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(typeof(SystemTextJsonObjectSerializer), TestConstants.PostgresProvider, Category = "db-postgres")]
[TestFixture(typeof(NewtonsoftJsonObjectSerializer), TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(typeof(NewtonsoftJsonObjectSerializer), TestConstants.PostgresProvider, Category = "db-postgres")]
[NonParallelizable]
public class AdoSchedulerTest : AbstractSchedulerTest
{
    private readonly IObjectSerializer serializer;

    // No custom trigger serializer is registered for TestBlobCronTriggerImpl: the scheduler under test is
    // built by SchedulerHelper.CreateScheduler, which uses the Newtonsoft serializer without the optimized
    // trigger converters, so an unknown trigger type is persisted as a reflected blob and never reaches a
    // trigger serializer at all. The registration this fixture used to make went into a process-global
    // System.Text.Json map that the scheduler under test never read.
    public AdoSchedulerTest(Type serializerType, string provider) : base(provider, serializerType.Name)
    {
        serializer = (IObjectSerializer) Activator.CreateInstance(serializerType);
    }

    protected override async ValueTask<IScheduler> CreateScheduler(string name, int threadPoolSize)
    {
        return await SchedulerHelper.CreateScheduler(
            provider,
            options =>
            {
                options.InstanceName = CreateSchedulerName(name);
                options.GenerateInstanceId = true;
            });
    }
}
