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
        // redis:7 is what the CI leg runs and what the package is supported against. The handler speaks
        // nothing but SET NX PX, GET, DEL and one EVAL, so another RESP server — Garnet, suggested on the
        // 4.0.0 announcement — is an image name away; that has not been run here, so it is an invitation
        // rather than a claim, and the default stays where support is.
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
