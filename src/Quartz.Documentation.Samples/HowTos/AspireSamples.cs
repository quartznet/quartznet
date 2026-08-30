using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Quartz.Diagnostics;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/aspire.md.
/// </summary>
/// <remarks>
/// Only the Quartz half of that page is compiled. The Aspire calls beside these — <c>AddServiceDefaults</c>,
/// <c>AddNpgsqlDataSource</c>, and everything in the AppHost — come from packages this repository does not
/// reference, so the page carries them as plain fences.
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
}
