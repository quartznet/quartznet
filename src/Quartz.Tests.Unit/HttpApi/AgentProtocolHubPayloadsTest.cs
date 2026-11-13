using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// <see cref="AgentProtocol.ConfigureHubPayloads" /> is called on the application's one SignalR JSON
/// protocol, so the options it changes are every hub's, not only the agent hub's.
/// </summary>
public class AgentProtocolHubPayloadsTest
{
    private sealed class HostHubRow
    {
        public int Id { get; set; }
    }

    [Test]
    public void TheHostsOwnPayloadsStillSerializeAfterConfiguringOptionsWithNoResolver()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        AgentProtocol.ConfigureHubPayloads(options);

        string json = JsonSerializer.Serialize(new List<HostHubRow> { new() { Id = 1 } }, options);
        json.Should().Be("""[{"id":1}]""",
            "options with an empty chain fell back to reflection, and inserting the contract must not take that fallback away from the host's own hubs");
    }

    [Test]
    public void TheContractIsAskedFirstAndReflectionLast()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        AgentProtocol.ConfigureHubPayloads(options);

        options.TypeInfoResolverChain[0].Should().BeSameAs(HttpApiJsonContext.Default);
        options.TypeInfoResolverChain[^1].Should().BeOfType<DefaultJsonTypeInfoResolver>();
    }

    [Test]
    public void AResolverTheHostAlreadyChoseIsKeptAndNotJoinedByReflection()
    {
        IJsonTypeInfoResolver hostResolver = new DefaultJsonTypeInfoResolver();
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { TypeInfoResolver = hostResolver };

        AgentProtocol.ConfigureHubPayloads(options);

        options.TypeInfoResolverChain.Should().Equal(HttpApiJsonContext.Default, hostResolver);
    }

    [Test]
    public void ConfiguringTwiceLeavesTheChainAsConfiguringOnce()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        AgentProtocol.ConfigureHubPayloads(options);
        AgentProtocol.ConfigureHubPayloads(options);

        options.TypeInfoResolverChain.Should().HaveCount(2);
    }
}
