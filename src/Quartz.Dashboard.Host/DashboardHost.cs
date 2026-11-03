#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Net.Http.Headers;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging;

using MySqlConnector;

using Npgsql;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Oracle.ManagedDataAccess.Client;

using Quartz.Diagnostics;

namespace Quartz.Dashboard.Host;

/// <summary>
/// The host: what <c>Quartz:Dashboard</c> says, turned into the dashboard's registrations and routes.
/// </summary>
internal static class DashboardHost
{
    /// <summary>
    /// The settings the host starts from, beneath every file, variable and argument: the same log levels
    /// a new ASP.NET Core application's <c>appsettings.json</c> has.
    /// </summary>
    private static readonly Dictionary<string, string?> Defaults = new(StringComparer.Ordinal)
    {
        ["Logging:LogLevel:Default"] = "Information",
        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
    };

    /// <summary>
    /// Reads the configuration and registers what it describes, or, when it cannot be served, only the
    /// guard that refuses to start and says why.
    /// </summary>
    public static DashboardHostPlan AddDashboardHost(this WebApplicationBuilder builder)
    {
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = Defaults });

        // A request's address is logged at Information by the hosting layer, and an address can carry a token
        // (?t=, or a mistyped one). Held at Warning whatever the configuration says: the host never logs a token.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

        DashboardHostPlan plan = DashboardHostPlan.Read(builder.Configuration);
        string dashboardPath = DashboardPath(plan.Settings.DashboardPath);

        builder.Services.AddSingleton(plan);
        builder.Services.AddHostedService(provider => new HostStartupGuard(plan, dashboardPath, provider.GetRequiredService<ILoggerFactory>()));

        if (!plan.CanStart)
        {
            return plan;
        }

        builder.Services.AddHealthChecks();
        builder.Services.AddHostAuthentication(plan);

        IDataProtectionBuilder dataProtection = builder.Services.AddDataProtection().SetApplicationName("Quartz.Dashboard.Host");
        if (!string.IsNullOrWhiteSpace(plan.Settings.DataProtection.KeysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(plan.Settings.DataProtection.KeysPath));
        }

        AddTelemetry(builder);

        builder.Services.AddQuartzDashboard(options =>
        {
            DashboardHostSettings settings = plan.Settings;

            options.DashboardPath = dashboardPath;
            options.AuthorizationPolicy = HostAuthentication.VisitorPolicy;
            options.OperationAuthorizationPolicy = HostAuthentication.OperationPolicy;
            options.ReadOnly = settings.ReadOnly ?? options.ReadOnly;
            options.HistoryRetention = settings.HistoryRetention ?? options.HistoryRetention;
            options.HistoryMaxEntriesPerScheduler = settings.HistoryMaxEntriesPerScheduler ?? options.HistoryMaxEntriesPerScheduler;

            if (settings.ClusterDetectionInterval is { } interval)
            {
                // Configuration has no null to say "never"; zero says it.
                options.ClusterDetectionInterval = interval == TimeSpan.Zero ? null : interval;
            }

            foreach (StoreTargetPlan target in plan.StoreTargets)
            {
                options.AttachStore(
                    target.Name,
                    store => ConfigureStore(store, target),
                    attach => attach.RediscoveryInterval = target.RediscoveryInterval);
            }

            if (plan.AcceptsAgents)
            {
                options.AcceptAgents(agents => ConfigureAgents(agents, settings.Agents));
            }
        });

        foreach (HttpTargetPlan target in plan.HttpTargets)
        {
            AddHttpTarget(builder.Services, target);
        }

        return plan;
    }

    /// <summary>
    /// The pipeline and the routes: sign-in, the health probe, the assets and the dashboard.
    /// </summary>
    public static void UseDashboardHost(this WebApplication app, DashboardHostPlan plan)
    {
        if (!plan.CanStart)
        {
            // Nothing to serve: the guard refuses the start and logs why.
            return;
        }

        string dashboardPath = DashboardPath(plan.Settings.DashboardPath);

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapStaticAssets();

        // Liveness only: the process is up and answering. Whether a target or a database answers is the
        // dashboard's to show, and a probe that failed on it would restart a host that is working.
        app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

        app.MapGet("/", (HttpContext context) => Results.Redirect(context.Request.PathBase + dashboardPath)).AllowAnonymous();

        app.MapHostAuthentication(plan, dashboardPath);
        app.MapQuartzDashboard();
    }

    /// <summary>
    /// The dashboard's path as the dashboard itself normalizes it: rooted, without a trailing slash, and
    /// <c>/quartz</c> when unset.
    /// </summary>
    internal static string DashboardPath(string? configured)
    {
        string trimmed = configured?.Trim().Trim('/') ?? "";
        return trimmed.Length == 0 ? "/quartz" : "/" + trimmed;
    }

    internal static void ConfigureStore(IPersistentStoreBuilder store, StoreTargetPlan target)
    {
        // The drivers' own factories rather than their type names: nothing is resolved by name, and the
        // provider is the dialect the store speaks.
        switch (target.Provider)
        {
            case DataSourceOptions.Providers.SqlServer:
                store.UseSqlServer(SqlClientFactory.Instance, target.ConnectionString);
                break;
            case DataSourceOptions.Providers.Npgsql:
                store.UsePostgres(NpgsqlFactory.Instance, target.ConnectionString);
                break;
            case DataSourceOptions.Providers.MySqlConnector:
                store.UseMySqlConnector(MySqlConnectorFactory.Instance, target.ConnectionString);
                break;
            case DataSourceOptions.Providers.Oracle:
                store.UseOracle(OracleClientFactory.Instance, target.ConnectionString);
                break;
            case DataSourceOptions.Providers.Sqlite:
                store.UseSqlite(SqliteFactory.Instance, target.ConnectionString);
                break;
            case DataSourceOptions.Providers.Firebird:
                store.UseFirebird(FirebirdClientFactory.Instance, target.ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"No driver for provider '{target.Provider}'; DashboardHostPlan should have refused it.");
        }

        if (target.TablePrefix is { Length: > 0 } prefix)
        {
            store.ConfigureStore(options => options.TablePrefix = prefix);
        }

        if (target.Serializer == DashboardHostPlan.NewtonsoftSerializer)
        {
            store.UseNewtonsoftJsonSerializer();
        }
        else
        {
            store.UseSystemTextJsonSerializer();
        }

        if (target.ExecutionHistory)
        {
            store.UseExecutionHistory();
        }
    }

    private static void ConfigureAgents(DashboardAgentHubOptions agents, AgentSettings settings)
    {
        agents.Tokens.Primary = NullIfEmpty(settings.Tokens.Primary);
        agents.Tokens.Secondary = NullIfEmpty(settings.Tokens.Secondary);
        agents.HeartbeatInterval = settings.HeartbeatInterval ?? agents.HeartbeatInterval;
        agents.OfflineAfterMissedHeartbeats = settings.OfflineAfterMissedHeartbeats ?? agents.OfflineAfterMissedHeartbeats;
        agents.OperationTimeout = settings.OperationTimeout ?? agents.OperationTimeout;
        agents.MaxMessageBytes = settings.MaxMessageBytes ?? agents.MaxMessageBytes;
        agents.ForgetAfter = settings.ForgetAfter ?? agents.ForgetAfter;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static void AddHttpTarget(IServiceCollection services, HttpTargetPlan target)
    {
        string clientName = "Quartz.Dashboard.Host.Target." + target.Name;

        services.AddHttpClient(clientName, client =>
        {
            client.BaseAddress = target.BaseAddress;

            if (target.Timeout is { } timeout)
            {
                client.Timeout = timeout;
            }

            foreach ((string name, string value) in target.Headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
            }

            if (!string.IsNullOrEmpty(target.BearerToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", target.BearerToken);
            }
        });

        services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = target.SchedulerName;
            options.HttpClientName = clientName;
            options.Target = target.Name;
        });
    }

    /// <summary>
    /// Quartz's spans and instruments, ASP.NET Core's instruments and the host's logs, to the collector
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> names — which an Aspire AppHost sets — and to nowhere when it is
    /// not set. ASP.NET Core's request spans are left out: a span carries the request's address, and an
    /// address can carry a token.
    /// </summary>
    private static void AddTelemetry(WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return;
        }

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
            .WithMetrics(metrics => metrics.AddMeter(
                QuartzInstrumentation.MeterName,
                "Microsoft.AspNetCore.Hosting",
                "Microsoft.AspNetCore.Server.Kestrel",
                "System.Net.Http"))
            .WithLogging()
            .UseOtlpExporter();
    }
}
