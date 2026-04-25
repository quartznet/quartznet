using Testcontainers.Redis;

namespace Quartz.Tests.Integration.Impl.Redis;

[SetUpFixture]
public class RedisTestEnvironment
{
    private static RedisContainer container;

    public static string ConnectionString { get; private set; } = "";

    [OneTimeSetUp]
    public async Task SetUp()
    {
        container = new RedisBuilder("redis:7").Build();
        await container.StartAsync();
        ConnectionString = container.GetConnectionString();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        if (container != null)
        {
            await container.DisposeAsync();
        }
    }
}
