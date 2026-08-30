#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// The worker an Aspire AppHost would have started, with none of Aspire running.
/// </summary>
/// <remarks>
/// <para>
/// <c>Quartz.Aspire</c> takes no <c>Aspire.*</c> dependency, and its whole contract is an
/// <see cref="IHostApplicationBuilder"/> holding configuration the AppHost injected — so a test needs no
/// DCP, no container and no Docker to be the real thing. What arrives from
/// <c>WithReference(quartzDb)</c> is the environment variable <c>ConnectionStrings__quartz</c>, which the
/// default configuration builder reads back as <c>ConnectionStrings:quartz</c>; an in-memory source says
/// the same thing.
/// </para>
/// <para>
/// <see cref="Host.CreateEmptyApplicationBuilder"/> rather than <c>CreateApplicationBuilder</c>, so that
/// the machine's own environment variables and user secrets cannot reach a test.
/// </para>
/// </remarks>
internal static class AspireApplication
{
    /// <summary>
    /// The connection strings the databases Aspire ships a resource for actually inject, as of Aspire 13.
    /// They are the inputs the provider inference exists to read, so they are written once and shared.
    /// </summary>
    public const string Postgres = "Host=127.0.0.1;Port=51234;Username=postgres;Password=secret;Database=quartz";

    public const string SqlServer = "Server=127.0.0.1,51235;User ID=sa;Password=secret;TrustServerCertificate=True;Database=quartz";

    public const string MySql = "Server=127.0.0.1;Port=51236;User ID=root;Password=secret;Database=quartz";

    public const string Sqlite = "Data Source=quartz.db";

    public const string Oracle = "Data Source=127.0.0.1:51237/FREEPDB1;User Id=system;Password=secret";

    /// <summary>
    /// A worker with the given configuration and nothing else.
    /// </summary>
    public static HostApplicationBuilder Worker(params (string Key, string? Value)[] configuration)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Configuration.AddInMemoryCollection(
            configuration.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)));

        return builder;
    }

    /// <summary>
    /// A worker whose AppHost injected one connection string under <paramref name="connectionName"/>.
    /// </summary>
    public static HostApplicationBuilder WorkerWith(string connectionString, string connectionName = "quartz")
    {
        return Worker(($"ConnectionStrings:{connectionName}", connectionString));
    }

    /// <summary>
    /// The data source settings the scheduler ended up with.
    /// </summary>
    /// <remarks>
    /// A store's data source is named after the scheduler that owns it, and <c>quartz</c> for the default
    /// scheduler — <see cref="PersistentStoreBuilder.DefaultDataSourceName"/> — so that is the name these
    /// options are registered under.
    /// </remarks>
    public static DataSourceOptions DataSourceOf(IServiceProvider services, string? schedulerName = null)
    {
        return services.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get(schedulerName ?? "quartz");
    }

    /// <summary>
    /// The job store settings the scheduler ended up with.
    /// </summary>
    public static AdoJobStoreOptions StoreOf(IServiceProvider services, string? schedulerName = null)
    {
        return services.GetRequiredService<IOptionsMonitor<AdoJobStoreOptions>>().Get(schedulerName ?? Options.DefaultName);
    }
}
