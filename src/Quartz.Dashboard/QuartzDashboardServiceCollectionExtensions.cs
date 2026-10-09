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

using System.Text.Json.Serialization;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore;
using Quartz.Configuration;
using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Registers the Quartz.NET Dashboard's services.
/// </summary>
public static class QuartzDashboardServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the dashboard renders with — its Blazor components, its SignalR hub, the
    /// execution history, the live event stream and the <see cref="IQuartzApiClient" /> the pages read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No option points the dashboard at a scheduler: it renders the schedulers this application
    /// registered — one registered with <c>AddQuartzHttpClient</c> included, which runs in another
    /// process. The client is registered with <c>TryAdd</c>, so an application that registers its own
    /// <see cref="IQuartzApiClient" /> first is the one the pages read. Call
    /// <c>MapQuartzDashboard()</c> on the built application to map the endpoints.
    /// </para>
    /// <para>
    /// The history is Quartz's: this calls <c>AddQuartzExecutionHistory()</c>, whose recorder writes what
    /// every scheduler in the container runs and misses. <see cref="IDashboardHistoryStore" /> is still
    /// the dashboard's seam and still works — <see cref="AddHistory" /> says which way the pair is
    /// joined.
    /// </para>
    /// <para>
    /// So are the live events: this calls <c>AddQuartzSchedulerEvents()</c>, and the Live Logs page reads
    /// the stream that installs — in this process for a local scheduler, and over the HTTP API's event
    /// route for one registered with <c>AddQuartzHttpClient</c>. The page needs no connection back to the
    /// dashboard's own hub, which is what used to fail behind a reverse proxy; the hub is still served and
    /// still fed, for clients of your own.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the dashboard, including the path it is served under.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    public static IServiceCollection AddQuartzDashboard(
        this IServiceCollection services,
        Action<QuartzDashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<QuartzDashboardOptions> optionsBuilder = services
            .AddOptions<QuartzDashboardOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DashboardPath) && options.DashboardPath.StartsWith('/'),
                "DashboardPath must start with '/'")
            .Validate(
                options => IsRoutableDashboardPath(options.DashboardPath),
                "DashboardPath must be a simple URL path: it cannot contain '{', '}', '?', '#', '.' or '..' segments, or empty segments ('//')")
            .Validate(
                options => options.HistoryRetention > TimeSpan.Zero,
                "HistoryRetention must be positive: a zero or negative window would forget every execution the moment it was recorded")
            .Validate(
                options => options.HistoryMaxEntriesPerScheduler > 0,
                "HistoryMaxEntriesPerScheduler must be at least 1")
            .Validate(
                options => options.ClusterDetectionInterval is null || options.ClusterDetectionInterval > TimeSpan.Zero,
                "ClusterDetectionInterval must be positive, or null to never merge targets into a cluster")
            .Validate(
                options => options.Agents is not { } agents || agents.HeartbeatInterval > TimeSpan.Zero,
                "AcceptAgents: HeartbeatInterval must be positive")
            .Validate(
                options => options.Agents is not { } agents || agents.OfflineAfterMissedHeartbeats >= 1,
                "AcceptAgents: OfflineAfterMissedHeartbeats must be at least 1")
            .Validate(
                options => options.Agents is not { } agents || agents.OperationTimeout > TimeSpan.Zero,
                "AcceptAgents: OperationTimeout must be positive")
            .Validate(
                options => options.Agents is not { } agents || agents.OperationTimeout <= TimerLimits.MaxDelay,
                $"AcceptAgents: OperationTimeout must be at most {TimerLimits.MaxDelay}, the longest a timer can wait")
            .Validate(
                options => options.Agents is not { } agents || agents.MaxMessageBytes > 0,
                "AcceptAgents: MaxMessageBytes must be positive")
            .Validate(
                options => options.Agents is not { } agents || agents.ForgetAfter > TimeSpan.Zero,
                "AcceptAgents: ForgetAfter must be positive")
            .Validate(
                options => options.Agents is not { } agents || IsAbsentOrNotBlank(agents.Tokens.Primary) && IsAbsentOrNotBlank(agents.Tokens.Secondary),
                "AcceptAgents: a token is null or has characters in it; a blank one would be a token every agent presents by leaving the header empty");

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // Refuses to start an application whose mapped dashboard nothing authorizes. Registered here
        // rather than at the map site, because a hosted service added to a built application is too late.
        services.TryAddSingleton<QuartzMappedEndpoints>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, QuartzEndpointAuthorizationGuard>());

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // The hub's scheduler status goes out as its name, the way Quartz writes it on every other wire:
        // a browser rendering a live event should read "Standby" rather than a number whose meaning
        // depends on the build. Named per enum rather than a blanket converter, because these options
        // belong to the whole application's SignalR and a host's own hubs must keep their own format.
        // The agent hub's payloads are read the same way on both ends, which is what AgentProtocol says.
        services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter<SchedulerStatus>());
                AgentProtocol.ConfigureHubPayloads(options.PayloadSerializerOptions);
            });
        services.AddHttpContextAccessor();

        AddAgents(services);

        // The dashboard reads the schedulers in its own process: every page goes through the in-process
        // client, which hands the pages triggers, calendars and job data maps as themselves. TryAdd, so an
        // application that registers its own IQuartzApiClient first is the one that answers.
        services.TryAddScoped<SchedulerState>();
        services.TryAddScoped<SchedulerAuthorization>();
        services.TryAddScoped<IQuartzApiClient>(static provider => new InProcessQuartzApiClient(
            provider.GetRequiredService<ISchedulerRepository>(),
            provider.GetRequiredService<ISchedulerRegistry>(),
            provider.GetRequiredService<IOptions<QuartzDashboardOptions>>(),
            provider.GetRequiredService<IExecutionHistoryStore>(),
            provider,
            provider.GetRequiredService<SchedulerAuthorization>(),
            provider.GetRequiredService<AttachedStores>()));

        AddAttachedStores(services);

        // The fleet monitor, which turns several targets fronting the nodes of one cluster into one row.
        // It runs its first pass once the host has started - after the HTTP targets are bound and the
        // attached stores discovered - and then on the interval the options say, and whenever a target
        // is added or removed.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FleetMonitor>());
        services.TryAddScoped<ToastService>();

        // The live events are Quartz's: this registers the broker every scheduler in the container
        // publishes into, which is what the Live Logs page reads — in process for a local scheduler, and
        // through the target's own reader for one registered with AddQuartzHttpClient. Idempotent, so a
        // process that also maps the HTTP API streams each event once.
        services.AddQuartzSchedulerEvents();

        // And the adapter that feeds the dashboard's hub from that stream, for clients of an application's
        // own. It subscribes when the first connection joins a scheduler, so a hub nobody has connected to
        // costs the schedulers nothing.
        services.TryAddSingleton<DashboardHubForwarder>();

        AddHistory(services);
        services.TryAddSingleton<DashboardActionLogService>();

        // Scoped over the singleton store: the entries are the process's, and who made one is the
        // circuit's. Pages talk to this, which writes both the page's own log and the application's.
        services.TryAddScoped<DashboardActionLog>();

        // The dashboard installs no plugin of its own any more. Both of the ones it had are Quartz's now,
        // installed into every scheduler in the container by the two calls above: the execution recorder
        // whose rows the History page reads, and the event publisher whose stream the Live Logs page reads.
        // DashboardLiveEventsPlugin and DashboardHistoryPlugin are still public and still work for an
        // application that registers one by name; registering either beside the core pair would push every
        // event twice and record every execution twice.

        return services;
    }

    private static bool IsAbsentOrNotBlank(string? token)
    {
        return token is null || !string.IsNullOrWhiteSpace(token);
    }

    /// <summary>
    /// Registers what accepting agents needs: the registry the hub writes, the sweep that judges their
    /// liveness, the hub's own message cap, and the shared services a dashboard fronting nothing but
    /// agents would otherwise lack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered whether or not <see cref="QuartzDashboardOptions.AcceptAgents" /> is called, because
    /// the options are read when they are resolved rather than when this runs; a dashboard that accepts
    /// none maps no hub, starts no timer and holds an empty registry.
    /// </para>
    /// <para>
    /// The shared services are the repository the agents' schedulers are bound into and the registry the
    /// listing reads. A dashboard beside <c>AddQuartz</c> or <c>AddQuartzHttpClient</c> has them already;
    /// one that fronts only agents — a fleet console with no scheduler of its own — gets them here. Every
    /// one of them is a <c>TryAdd</c>.
    /// </para>
    /// <para>
    /// The agent hub's receive cap is the answer cap plus what base64 adds to a body of that size, so an
    /// answer the agent judged small enough is not dropped by the channel; the browser hub keeps
    /// SignalR's own 32 KB.
    /// </para>
    /// </remarks>
    private static void AddAgents(IServiceCollection services)
    {
        services.AddQuartzSharedServices();
        services.TryAddSingleton<AgentRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AgentRegistryJanitor>());

        // SignalR reads a hub's own options only once HubOptionsSetup<THub> has copied the global ones onto
        // them and said so; AddHubOptions<THub> is what registers it, and this is the same registration with
        // a configure that can read the dashboard's options. Without the setup the handler keeps the global
        // cap whatever the hub's options say.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<HubOptions<DashboardAgentHub>>, HubOptionsSetup<DashboardAgentHub>>());
        services.AddOptions<HubOptions<DashboardAgentHub>>()
            .Configure<IOptions<QuartzDashboardOptions>>(static (hub, dashboard) =>
            {
                if (dashboard.Value.Agents is { } agents)
                {
                    hub.MaximumReceiveMessageSize = (agents.MaxMessageBytes / 3L) * 4L + 64 * 1024;
                }
            });
    }

    /// <summary>
    /// Registers the databases <see cref="QuartzDashboardOptions.AttachStore" /> points the dashboard
    /// at, and the discovery that opens a window onto every scheduler in each of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered whether or not anything is attached, because the options are read when they are
    /// resolved rather than when this runs, and the hosted service is where a container without keyed
    /// services would otherwise have to be asked. An empty set opens no connection, builds no
    /// container and starts no timer.
    /// </para>
    /// <para>
    /// <see cref="SchedulerWindowRegistry" /> is <c>TryAdd</c>ed here as well as by <c>AddQuartz</c>,
    /// so that the registration order of the two calls cannot decide whether there is one of it. There
    /// is one either way, and it is what the listing, the health check and the pages all read.
    /// </para>
    /// </remarks>
    private static void AddAttachedStores(IServiceCollection services)
    {
        services.TryAddSingleton<SchedulerWindowRegistry>();
        services.TryAddSingleton<SchedulerTargets>();
        services.TryAddSingleton<AttachedStores>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AttachedStoreDiscovery>(static provider =>
            new AttachedStoreDiscovery(
                provider.GetRequiredService<AttachedStores>(),
                provider.GetRequiredService<IOptions<QuartzDashboardOptions>>().Value.AttachedStores,
                provider)));
    }

    /// <summary>
    /// Joins the dashboard's history seam to the one Quartz keeps history behind, in whichever direction
    /// this application's registrations call for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two cases, and the difference between them is what the application registered before this call.
    /// An application that registered an <see cref="IDashboardHistoryStore" /> of its own — the
    /// documented 4.0 recipe, a history kept in a database — keeps it, and Quartz's history store becomes
    /// an adapter over it, so the recorder writes into it and the HTTP API's history routes answer out of
    /// it. Core's own default is the only descriptor replaced to do that: a store the application
    /// registered against <see cref="IExecutionHistoryStore" /> is what it said it wanted and is left
    /// alone.
    /// </para>
    /// <para>
    /// Otherwise the pair goes the other way: Quartz's store is the one, and
    /// <see cref="IDashboardHistoryStore" /> resolves to an adapter over it, so the 4.0 type still
    /// resolves and still answers.
    /// </para>
    /// <para>
    /// <see cref="QuartzDashboardOptions.HistoryRetention" /> and
    /// <see cref="QuartzDashboardOptions.HistoryMaxEntriesPerScheduler" /> stay the knobs while the
    /// dashboard is registered: they are written onto <see cref="ExecutionHistoryOptions" /> afterwards,
    /// so a deployment that configured the dashboard's two settings gets the history it asked for
    /// wherever it is read from.
    /// </para>
    /// </remarks>
    private static void AddHistory(IServiceCollection services)
    {
        services.AddQuartzExecutionHistory();

        services.AddOptions<ExecutionHistoryOptions>()
            .PostConfigure<IOptions<QuartzDashboardOptions>>(static (history, dashboard) =>
            {
                history.Retention = dashboard.Value.HistoryRetention;
                history.MaxEntriesPerScheduler = dashboard.Value.HistoryMaxEntriesPerScheduler;
            });

        if (!ApplicationRegisteredItsOwnDashboardStore(services))
        {
            services.TryAddSingleton<IDashboardHistoryStore>(static provider =>
                new DashboardHistoryStoreOverExecutionHistory(provider.GetRequiredService<IExecutionHistoryStore>()));
            return;
        }

        for (int i = 0; i < services.Count; i++)
        {
            ServiceDescriptor descriptor = services[i];
            if (descriptor.ServiceType == typeof(IExecutionHistoryStore)
                && descriptor.ImplementationType == typeof(InMemoryExecutionHistoryStore))
            {
                services[i] = ServiceDescriptor.Singleton<IExecutionHistoryStore>(static provider =>
                    new ExecutionHistoryStoreOverDashboardStore(provider.GetRequiredService<IDashboardHistoryStore>()));
            }
        }
    }

    private static bool ApplicationRegisteredItsOwnDashboardStore(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IDashboardHistoryStore))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly char[] InvalidDashboardPathChars = ['{', '}', '?', '#'];

    /// <summary>
    /// Validates that <see cref="QuartzDashboardOptions.DashboardPath"/> is a plain URL path: the
    /// value is concatenated into route templates (where <c>{</c>/<c>}</c> would be parsed as route
    /// parameters) and percent-encoded for client-side comparisons (where <c>?</c>/<c>#</c> and
    /// <c>.</c>/<c>..</c> segments would be truncated or collapsed, diverging from the server route).
    /// </summary>
    internal static bool IsRoutableDashboardPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            // the empty/whitespace case is reported by the "must start with '/'" validation
            return true;
        }

        string trimmed = path.Trim().Trim('/');
        if (trimmed.Length == 0)
        {
            // normalizes to the default "/quartz"
            return true;
        }

        foreach (string segment in trimmed.Split('/'))
        {
            if (segment.Length == 0
                || segment == "."
                || segment == ".."
                || segment.IndexOfAny(InvalidDashboardPathChars) >= 0)
            {
                return false;
            }
        }

        return true;
    }
}
