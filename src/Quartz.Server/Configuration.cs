using Microsoft.Extensions.Configuration;

namespace Quartz.Server;

/// <summary>
/// Configuration for the Quartz server.
/// </summary>
/// <remarks>
/// Read from <c>appsettings.json</c> and environment variables rather than from a Full Framework
/// <c>.config</c> file, which modern .NET does not have.
/// </remarks>
public static class Configuration
{
    private const string SectionName = "QuartzServer";

    private static readonly IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>
    /// The name the service is registered under.
    /// </summary>
    public static string ServiceName => Get(nameof(ServiceName), "QuartzServer");

    /// <summary>
    /// The name shown for the service.
    /// </summary>
    public static string ServiceDisplayName => Get(nameof(ServiceDisplayName), "Quartz Server");

    /// <summary>
    /// The description shown for the service.
    /// </summary>
    public static string ServiceDescription => Get(nameof(ServiceDescription), "Quartz Job Scheduling Server");

    /// <summary>
    /// The server implementation to run.
    /// </summary>
    public static string ServerImplementationType =>
        Get(nameof(ServerImplementationType), typeof(QuartzServer).AssemblyQualifiedName!);

    /// <summary>
    /// The Quartz configuration section the scheduler is built from.
    /// </summary>
    public static IConfiguration Quartz => configuration.GetSection("Quartz");

    private static string Get(string key, string defaultValue)
    {
        var value = configuration.GetSection(SectionName)[key];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
