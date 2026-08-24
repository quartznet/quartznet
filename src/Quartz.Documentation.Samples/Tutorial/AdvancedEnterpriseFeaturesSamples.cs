using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/advanced-enterprise-features.md.
/// </summary>
public static class AdvancedEnterpriseFeaturesSamples
{
    public static void Clustering(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_advanced_clustering

        builder.Services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                // every node in the cluster shares this name: it is what makes them one cluster
                options.InstanceName = "orders";

                // ...and each needs its own id. Generating one is the easy way to be sure.
                options.GenerateInstanceId = true;
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(connectionString);
                store.UseSystemTextJsonSerializer();
                store.UseClustering();
            });
        });

        #endregion
    }

    public static void CheckinInterval(IPersistentStoreBuilder store)
    {
        #region sample_advanced_checkin_interval

        store.UseClustering(cluster =>
        {
            cluster.CheckinInterval = TimeSpan.FromSeconds(10);
            cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
        });

        #endregion
    }

    public static void BatchAcquisition(IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            #region sample_advanced_batch_acquisition

            q.ConfigureScheduler(options =>
            {
                options.MaxBatchSize = 10;
                options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(1);
            });

            #endregion
        });
    }
}
