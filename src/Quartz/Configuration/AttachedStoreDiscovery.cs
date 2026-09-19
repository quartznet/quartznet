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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Configuration;

/// <summary>
/// One database an application asked this process to watch.
/// </summary>
/// <param name="Target">The name the store is attached under, which names its windows.</param>
/// <param name="Configure">
/// The store recipe — the cluster's own dialect, table prefix, serializer and custom trigger
/// serializers.
/// </param>
/// <param name="RediscoveryInterval">
/// How often the database is asked again for its scheduler names, or <see langword="null" /> to ask
/// only once, at start-up.
/// </param>
internal sealed record AttachedStoreDescriptor(
    string Target,
    Action<IPersistentStoreBuilder> Configure,
    TimeSpan? RediscoveryInterval);

/// <summary>
/// Opens a window onto every scheduler in each attached store, at start-up and then on a timer.
/// </summary>
/// <remarks>
/// <para>
/// A timer rather than a one-off, because the set of schedulers in a database is not fixed: a tenant
/// is onboarded, a new service starts scheduling into the shared tables, a cluster is renamed. Without
/// rediscovery the dashboard would show whatever was there the moment it started and never mention the
/// rest, which is the stale-inventory failure every shared-storage console is known for.
/// </para>
/// <para>
/// A round that cannot reach the database is logged and left for the next one. An attached database is
/// somebody else's, it can be down, and a dashboard that would not start because of it is a dashboard
/// that cannot be used to find out why. A recipe that is <em>wrong</em> is a different matter and
/// fails the host at start-up, because no later round will make it right.
/// </para>
/// </remarks>
internal sealed class AttachedStoreDiscovery : IHostedService, IAsyncDisposable
{
    private readonly AttachedStores stores;
    private readonly IReadOnlyList<AttachedStoreDescriptor> descriptors;
    private readonly IServiceProvider application;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AttachedStoreDiscovery> logger;
    private readonly List<ITimer> timers = [];

    private readonly CancellationTokenSource stopping = new();

    private bool disposed;

    public AttachedStoreDiscovery(
        AttachedStores stores,
        IReadOnlyList<AttachedStoreDescriptor> descriptors,
        IServiceProvider application)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(application);

        this.stores = stores;
        this.descriptors = descriptors;
        this.application = application;
        timeProvider = application.GetService<TimeProvider>() ?? TimeProvider.System;
        logger = application.GetService<ILoggerFactory>() is { } factory
            ? factory.CreateLogger<AttachedStoreDiscovery>()
            : LogProvider.CreateLogger<AttachedStoreDiscovery>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (AttachedStoreDescriptor descriptor in descriptors)
        {
            AttachedStore store = new(descriptor.Target, descriptor.Configure, application);

            // Added before the first round, so that a target whose database is unreachable at start-up
            // is still a target and is still swept by the timer.
            stores.Add(store);

            await Round(store, failFast: true, cancellationToken).ConfigureAwait(false);

            if (descriptor.RediscoveryInterval is { } interval && interval > TimeSpan.Zero)
            {
                AttachedStore watched = store;
                timers.Add(timeProvider.CreateTimer(
                    state =>
                    {
                        // Started and not awaited: a timer callback returns void, and every failure the
                        // round can produce is handled inside it, so there is no fault to observe.
                        _ = Rediscover(watched);
                    },
                    state: null,
                    interval,
                    interval));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!stopping.IsCancellationRequested)
        {
            stopping.Cancel();
        }

        foreach (ITimer timer in timers)
        {
            timer.Dispose();
        }

        timers.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// One rediscovery round, off the timer's callback.
    /// </summary>
    /// <remarks>
    /// The timer's callback is void-returning, so the round is started and not awaited — and every
    /// failure it can produce is handled inside <see cref="Round" />, which is what keeps an
    /// unobserved fault out of this.
    /// </remarks>
    private async Task Rediscover(AttachedStore store)
    {
        await Round(store, failFast: false, stopping.Token).ConfigureAwait(false);
    }

    private async ValueTask Round(AttachedStore store, bool failFast, CancellationToken cancellationToken)
    {
        try
        {
            await store.Synchronize(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping, which is not a discovery failure.
        }
        catch (SchedulerConfigException) when (failFast)
        {
            // The recipe cannot build a store at all, and no later round will change that. Said at
            // start-up, where a configuration mistake belongs.
            throw;
        }
        catch (Exception failure)
        {
            logger.AttachedStoreDiscoveryFailed(store.Target, failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        stopping.Dispose();
    }
}
