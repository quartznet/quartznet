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

    static AdoSchedulerTest()
    {
        SystemTextJsonObjectSerializer.AddTriggerSerializer<TestBlobCronTriggerImpl>(new TestBlobCronTriggerImpl.SystemTextJsonSerializer());
    }

    public AdoSchedulerTest(Type serializerType, string provider) : base(provider, serializerType.Name)
    {
        serializer = (IObjectSerializer) Activator.CreateInstance(serializerType);
        serializer.Initialize();
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
