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

    public static async Task SeeingTheCluster(IScheduler scheduler)
    {
        #region sample_advanced_cluster_nodes

        List<ClusterNode> nodes = await scheduler.QueryClusterNodes();

        foreach (ClusterNode node in nodes)
        {
            string marker = node.IsCurrentNode ? " (this node)" : "";
            Console.WriteLine($"{node.InstanceId}{marker}: {node.State}, last check-in {node.LastCheckInUtc:u}");
        }

        // The verdicts come from the same predicate the failover sweep applies, so a node reported
        // Failed is one whose in-flight work the cluster is about to take over.
        List<ClusterNode> failed = nodes.FindAll(node => node.State == ClusterNodeState.Failed);

        #endregion

        _ = failed;
    }

    public static async Task WhatEachNodeIsRunning(IScheduler scheduler)
    {
        #region sample_advanced_cluster_node_firings

        List<ClusterNode> nodes = await scheduler.QueryClusterNodes();
        PagedResult<FireInstance> firings = await scheduler.QueryFireInstances(new FireInstanceQuery
        {
            // both states: what a node is holding is as interesting as what it is running, and a
            // reservation left behind by a dead node is what recovery is about to clear
            State = null
        });

        foreach (ClusterNode node in nodes)
        {
            int running = firings.Items.Count(firing =>
                firing.SchedulerInstanceId == node.InstanceId && firing.State == FireInstanceState.Executing);

            Console.WriteLine($"{node.InstanceId} ({node.State}) is running {running} job(s)");
        }

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
