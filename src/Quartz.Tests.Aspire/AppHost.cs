using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quartz.Tests.Aspire;

/// <summary>
/// An AppHost's application model, built and resolved the way the orchestrator resolves it, without running
/// anything.
/// </summary>
/// <remarks>
/// Endpoints are given addresses by hand, which is what the orchestrator does when it allocates them, and the
/// environment is resolved through <see cref="ExecutionConfigurationBuilder" />, which is what it hands a
/// process or a container. Parameters take their values from <c>Parameters:*</c> in the builder's
/// configuration, as they do from user secrets.
/// </remarks>
internal static class AppHost
{
    public const string AdminToken = "AdminToken0123456789abcdefABCDEF";
    public const string AgentToken = "AgentToken0123456789abcdefABCDEF";
    public const string DatabaseConnectionString = "Host=postgres;Port=5432;Database=quartz;Username=postgres;Password=secret";
    public const string OtlpEndpoint = "http://otlp.test:4317";

    public static IDistributedApplicationBuilder Builder()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            DisableDashboard = true,
        });

        // Where the dashboard's own telemetry goes. Not on localhost: a container is handed the address its
        // host is reached by from inside the container network, and only a running orchestrator knows that.
        builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = OtlpEndpoint;
        builder.Configuration["Parameters:quartz-admin-token"] = AdminToken;
        builder.Configuration["Parameters:quartz-agent-token"] = AgentToken;
        builder.Configuration["ConnectionStrings:quartz-db"] = DatabaseConnectionString;
        return builder;
    }

    /// <summary>
    /// Gives every endpoint of <paramref name="resource" /> an address on localhost, starting at
    /// <paramref name="port" />.
    /// </summary>
    public static void Allocate(IResource resource, int port)
    {
        foreach (EndpointAnnotation endpoint in resource.Annotations.OfType<EndpointAnnotation>())
        {
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", port++);
        }
    }

    /// <summary>
    /// The environment the orchestrator would hand <paramref name="resource" /> in run mode, resolved against
    /// the built (and never started) application's own services.
    /// </summary>
    public static async Task<Dictionary<string, string>> Environment(DistributedApplication app, IResource resource)
    {
        DistributedApplicationExecutionContext context = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();

        // Bounded: a value that waits on something only a running orchestrator provides - an endpoint nobody
        // allocated - would otherwise wait for ever, and a test that hangs names nothing.
        IExecutionConfigurationResult result = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(context, NullLogger.Instance, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        result.Exception.Should().BeNull();
        return result.EnvironmentVariables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// What a .NET process reads out of <paramref name="environment" />: the environment variable provider's
    /// own translation of <c>__</c> into the configuration separator.
    /// </summary>
    public static IConfiguration Configuration(Dictionary<string, string> environment)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(environment.Select(x => new KeyValuePair<string, string?>(x.Key.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal), x.Value)))
            .Build();
    }
}
