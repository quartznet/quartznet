using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Quartz.Diagnostics;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/aspire.md and
/// docs/documentation/quartz-4.x/packages/aspire.md.
/// </summary>
/// <remarks>
/// Only the Quartz half of those pages is compiled. The Aspire calls beside these —
/// <c>AddServiceDefaults</c>, <c>AddNpgsqlDataSource</c>, and everything in the AppHost — come from
/// packages this repository does not reference, so the pages carry them as plain fences.
/// </remarks>
public static class AspireSamples
{
    public static void PersistentStoreFromAConnectionName(IHostApplicationBuilder builder)
    {
        #region sample_aspire_add_persistent_store

        builder.AddQuartzPersistentStore("quartz");
        builder.AddQuartz();
        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void SettingsFromCode(IHostApplicationBuilder builder)
    {
        #region sample_aspire_settings

        builder.AddQuartzPersistentStore("quartz", settings =>
        {
            settings.Provider = DataSourceOptions.Providers.Npgsql;
            settings.Clustered = true;
        });

        #endregion
    }

    public static void TwoDatabasesOnTwoSchedulers(IHostApplicationBuilder builder)
    {
        #region sample_aspire_two_schedulers

        builder.AddQuartz("orders");
        builder.AddQuartz("billing");

        builder.AddQuartzPersistentStore("orders-db", settings => settings.SchedulerName = "orders");
        builder.AddQuartzPersistentStore("billing-db", settings => settings.SchedulerName = "billing");

        #endregion
    }

    public static void SubscribeToQuartzSignals(IHostApplicationBuilder builder)
    {
        #region sample_aspire_subscribe

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
            .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));

        #endregion
    }

    public static void PersistentStoreOverRegisteredDataSource(IHostApplicationBuilder builder)
    {
        #region sample_aspire_persistent_store

        builder.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "orders";
                options.GenerateInstanceId = true;
            });

            q.UsePersistentStore(store =>
            {
                store.UsePostgres(db => db.UseRegisteredDataSource = true);
                store.UseClustering();
            });
        });

        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void PersistentStoreOverKeyedDataSource(IHostApplicationBuilder builder)
    {
        #region sample_aspire_keyed_data_source

        builder.AddQuartz(q => q.UsePersistentStore(store =>
            store.UsePostgres(db => db.DataSourceServiceKey = "quartz")));

        #endregion
    }

    public static void PersistentStoreOverConnectionString(IHostApplicationBuilder builder)
    {
        #region sample_aspire_sql_server

        builder.AddQuartz(q => q.UsePersistentStore(store =>
            store.UseSqlServer(db => db.ConnectionStringName = "quartz")));

        #endregion
    }

    public static void HealthChecks(IHostApplicationBuilder builder)
    {
        #region sample_aspire_health_checks

        builder.Services.AddQuartzHealthChecks();

        #endregion
    }

    public static void TagTheHealthCheck(IHostApplicationBuilder builder)
    {
        #region sample_aspire_health_check_tags

        builder.Services.Configure<QuartzHealthCheckOptions>(options => options.Tags.Add("live"));

        #endregion
    }

    public static void HealthEndpointFromAWorker(string[] args)
    {
        #region sample_aspire_health_endpoint

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.AddQuartzPersistentStore("quartz");
        builder.AddQuartz();
        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        WebApplication app = builder.Build();

        app.MapHealthChecks("/health");

        app.Run();

        #endregion
    }

    public static void DegradedLeavesTheRotation(WebApplication app)
    {
        #region sample_aspire_degraded_is_503

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        });

        #endregion
    }

    public static void TurnTheSignalsOff(IHostApplicationBuilder builder)
    {
        #region sample_aspire_disable_signals

        builder.AddQuartzPersistentStore("quartz", settings =>
        {
            settings.DisableTracing = true;
            settings.DisableMetrics = true;
            settings.DisableHealthChecks = true;
        });

        #endregion
    }
}
