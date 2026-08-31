using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.Operations;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/operations.md.
/// </summary>
/// <remarks>
/// In a namespace of its own so the operator-shaped helpers can carry the names the page uses without
/// colliding with the shared types in <c>SampleTypes.cs</c>.
/// </remarks>
public static class OperationsSamples
{
    public static void InstanceIdFromPodName(IServiceCollection services, string connectionString)
    {
        #region sample_operations_instance_id_from_pod_name

        // POD_NAME comes from the Downward API: fieldRef fieldPath: metadata.name. On a StatefulSet
        // that is "<set>-<ordinal>", which the same replica gets back after a restart.
        string? podName = Environment.GetEnvironmentVariable("POD_NAME");

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "orders";

                if (podName is { Length: > 0 })
                {
                    options.InstanceId = podName;
                }
                else
                {
                    // Nothing injected the pod name — a developer's machine, or a manifest that has
                    // not been updated. Fall back to a generated id, which is unique but not stable.
                    options.GenerateInstanceId = true;
                }
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

    public static async ValueTask StaleFirings(IScheduler scheduler, TimeProvider timeProvider, ILogger logger)
    {
        #region sample_operations_stale_firings

        List<ClusterNode> nodes = await scheduler.QueryClusterNodes();
        HashSet<string> known = nodes.Select(node => node.InstanceId).ToHashSet(StringComparer.Ordinal);

        // State = null lists reservations as well as executions; the default lists executions only.
        PagedResult<FireInstance> firings = await scheduler.QueryFireInstances(new FireInstanceQuery
        {
            State = null,
            Take = 500
        });

        DateTimeOffset cutoff = timeProvider.GetUtcNow().AddHours(-1);

        foreach (FireInstance firing in firings.Items)
        {
            // A row whose node is no longer listed is a leak: no peer will recognise it as its own,
            // and only a node's first check-in sweeps firings with no scheduler-state row behind them.
            if (!known.Contains(firing.SchedulerInstanceId))
            {
                logger.LogWarning(
                    "Firing {FireInstanceId} of {Trigger} belongs to {Node}, which the cluster no longer lists.",
                    firing.FireInstanceId, firing.TriggerKey, firing.SchedulerInstanceId);
            }
            else if (firing.FireTimeUtc < cutoff)
            {
                logger.LogWarning(
                    "Firing {FireInstanceId} of {Trigger} has been {State} on {Node} since {FireTime}.",
                    firing.FireInstanceId, firing.TriggerKey, firing.State, firing.SchedulerInstanceId,
                    firing.FireTimeUtc);
            }
        }

        #endregion
    }

    public static void StoreTimeouts(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q =>
        {
            #region sample_operations_store_timeouts

            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(connectionString);
                store.UseSystemTextJsonSerializer();
                store.UseClustering();

                store.ConfigureStore(options =>
                {
                    // Every statement the store issues, the lock handler's included. Left unset it is
                    // whatever the provider gives a new command, usually 30 seconds.
                    options.CommandTimeout = TimeSpan.FromSeconds(15);

                    // A deadlock or a dropped connection is retried this many times, this far apart.
                    options.MaxTransientRetries = 3;
                    options.TransientRetryInterval = TimeSpan.FromSeconds(1);

                    // How long the check-in and misfire loops back off after a failure that was not
                    // transient — a database that is down rather than busy.
                    options.DbRetryInterval = TimeSpan.FromSeconds(15);
                });
            });

            #endregion
        });
    }

    public static void ReadinessProbe(IServiceCollection services)
    {
        #region sample_operations_readiness_probe

        // Tagged, so a readiness endpoint can select it while the liveness endpoint does not: a
        // scheduler in standby, or one whose database is unreachable, should leave the rotation
        // without the process being killed.
        services.AddHealthChecks().AddQuartz(options => options.Tags.Add("ready"));

        #endregion
    }
}
