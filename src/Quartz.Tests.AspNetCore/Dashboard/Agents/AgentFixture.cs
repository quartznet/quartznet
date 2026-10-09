using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// A dashboard that accepts agents, served by a test server, and the workers that dial it.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard runs on a <see cref="FakeTimeProvider" /> of its own, which is what its agent registry
/// judges liveness on and what the janitor's sweep ticks on. Each worker has a clock of its own too, which
/// a test never advances unless it wants a heartbeat sent: advancing the dashboard's clock then makes an
/// agent overdue without the agent's own timer firing. SignalR's own keep-alive runs on real time and is
/// untouched by either.
/// </para>
/// <para>
/// The workers dial the test server over long polling through its handler, which is the transport a
/// <see cref="TestServer" /> carries; the hub methods, the correlation and the event stream are the same
/// over every transport. A dashboard started on Kestrel instead is dialled over a loopback socket with
/// WebSockets, which is the transport production uses.
/// </para>
/// </remarks>
internal sealed class AgentDashboard : IAsyncDisposable
{
    public const string Token = "agent-token";

    public const string SchedulerName = "QuartzScheduler";

    private readonly List<AgentWorker> workers = [];
    private readonly TransportSwitch transport;
    private IServiceScope? scope;

    private AgentDashboard(WebApplication app, FakeTimeProvider clock, RecordingLoggerProvider logs, TransportSwitch transport, Uri hubUri, bool kestrel)
    {
        App = app;
        Clock = clock;
        Logs = logs;
        HubUri = hubUri;
        IsKestrel = kestrel;
        this.transport = transport;
    }

    public WebApplication App { get; }

    /// <summary>The dashboard's clock: 12:00 UTC on 1 July 2026 until a test moves it.</summary>
    public FakeTimeProvider Clock { get; }

    public RecordingLoggerProvider Logs { get; }

    public AgentRegistry Registry => App.Services.GetRequiredService<AgentRegistry>();

    /// <summary>The agent hub: on the test server's address, or on the loopback port Kestrel took.</summary>
    public Uri HubUri { get; }

    /// <summary>Whether the dashboard listens on a real socket, which the workers then dial over WebSockets.</summary>
    public bool IsKestrel { get; }

    /// <summary>
    /// While set, every request to the agent hub is answered <c>503</c> before it reaches SignalR: what a
    /// proxy or a network between the worker and the dashboard going away looks like to the client's
    /// transport, which then reconnects on its own once the switch is cleared.
    /// </summary>
    public bool TransportDown
    {
        get => transport.Down;
        set => transport.Down = value;
    }

    /// <summary>
    /// The dashboard's client, resolved the way the pages resolve it.
    /// </summary>
    public IQuartzApiClient Client
    {
        get
        {
            scope ??= App.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();
        }
    }

    public static async Task<AgentDashboard> Start(
        Action<DashboardAgentHubOptions>? configureAgents = null,
        Action<IServiceCollection>? configureServices = null,
        Action<QuartzDashboardOptions>? configureDashboard = null,
        bool kestrel = false)
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        RecordingLoggerProvider logs = new();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        if (kestrel)
        {
            // A port the operating system picks, on loopback. The only caller is this process.
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        }
        else
        {
            builder.WebHost.UseTestServer();
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddQuartzDashboard(options =>
        {
            options.AcceptAgents(agents =>
            {
                agents.Tokens.Primary = Token;
                configureAgents?.Invoke(agents);
            });
            configureDashboard?.Invoke(options);
        });

        // SignalR's own client timeout runs on real time and is longer than any test here; said so
        // anyway, so a slow machine cannot turn a heartbeat case into a transport case.
        builder.Services.Configure<HubOptions<DashboardAgentHub>>(hub => hub.ClientTimeoutInterval = TimeSpan.FromMinutes(5));
        configureServices?.Invoke(builder.Services);

        TransportSwitch transport = new();

        WebApplication app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (transport.Down && context.Request.Path.StartsWithSegments("/quartz/agents"))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            await next(context);
        });
        app.MapQuartzDashboard().AllowAnonymous();
        await app.StartAsync();

        Uri baseAddress = kestrel
            ? new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/")
            : app.GetTestServer().BaseAddress;

        return new AgentDashboard(app, clock, logs, transport, new Uri(baseAddress, "/quartz/agents"), kestrel);
    }

    /// <summary>
    /// Starts a worker whose scheduler dials this dashboard, and waits until the dashboard has it.
    /// </summary>
    public async Task<AgentWorker> StartAgent(
        string target,
        Action<DashboardAgentOptions>? configure = null,
        string? instanceId = null,
        bool waitUntilRegistered = true,
        TimeProvider? clock = null,
        Action<IQuartzBuilder>? configureQuartz = null)
    {
        AgentWorker worker = await AgentWorker.Start(this, target, configure, instanceId, clock, configureQuartz);
        workers.Add(worker);

        if (waitUntilRegistered)
        {
            await worker.WaitUntilRegistered();
        }

        return worker;
    }

    /// <summary>
    /// A connection to the hub speaking the protocol by hand, for a case about what the hub does with a
    /// message the agent would never send. Not started.
    /// </summary>
    public HubConnection RawConnection(string? token = Token)
    {
        return new HubConnectionBuilder()
            .WithUrl(HubUri, http =>
            {
                ConfigureTransport(http);
                http.AccessTokenProvider = token is null ? null : () => Task.FromResult<string?>(token);
            })
            .AddJsonProtocol(json => AgentProtocol.ConfigureHubPayloads(json.PayloadSerializerOptions))
            .Build();
    }

    /// <summary>
    /// How a client reaches this dashboard: through the test server's handler over long polling, or over
    /// the loopback socket with WebSockets.
    /// </summary>
    public void ConfigureTransport(HttpConnectionOptions http)
    {
        if (IsKestrel)
        {
            http.Transports = HttpTransportType.WebSockets;
        }
        else
        {
            http.HttpMessageHandlerFactory = _ => App.GetTestServer().CreateHandler();
            http.Transports = HttpTransportType.LongPolling;
        }
    }

    public async ValueTask DisposeAsync()
    {
        scope?.Dispose();

        foreach (AgentWorker worker in workers)
        {
            await worker.DisposeAsync();
        }

        await App.StopAsync();
        await App.DisposeAsync();
    }

    /// <summary>The one flag the middleware above reads, shared with the dashboard that owns it.</summary>
    private sealed class TransportSwitch
    {
        public volatile bool Down;
    }
}

/// <summary>
/// One worker: a container with a scheduler and the agent plugin, dialling the dashboard.
/// </summary>
internal sealed class AgentWorker : IAsyncDisposable
{
    private readonly AgentDashboard dashboard;
    private readonly ServiceProvider provider;
    private readonly TimeProvider clock;

    private AgentWorker(AgentDashboard dashboard, ServiceProvider provider, IScheduler scheduler, string target, TimeProvider clock, RecordingLoggerProvider logs)
    {
        this.dashboard = dashboard;
        this.provider = provider;
        this.clock = clock;
        Scheduler = scheduler;
        Target = target;
        Logs = logs;
    }

    public IScheduler Scheduler { get; }

    public string Target { get; }

    /// <summary>The worker's clock, which a test advances to make it heartbeat or retry.</summary>
    public FakeTimeProvider Clock => clock as FakeTimeProvider
        ?? throw new InvalidOperationException("This worker runs on the system clock; start it without one to drive its heartbeats by hand.");

    public RecordingLoggerProvider Logs { get; }

    public IServiceProvider Services => provider;

    public string Key => $"{Target}/{Scheduler.SchedulerName}";

    public SchedulerEventBroker Broker => provider.GetRequiredService<SchedulerEventBroker>();

    public DashboardAgentPlugin Plugin => provider.GetServices<ISchedulerPlugin>().OfType<DashboardAgentPlugin>().Single();

    /// <summary>
    /// Starts a worker on a clock of its own, stopped at the dashboard's noon, unless the test hands one
    /// in: a case about what the scheduler does over time runs it on <see cref="TimeProvider.System" />.
    /// </summary>
    public static async Task<AgentWorker> Start(
        AgentDashboard dashboard,
        string target,
        Action<DashboardAgentOptions>? configure,
        string? instanceId,
        TimeProvider? clock = null,
        Action<IQuartzBuilder>? configureQuartz = null)
    {
        clock ??= new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        RecordingLoggerProvider logs = new();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddSingleton(clock);
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = AgentDashboard.SchedulerName;
                options.InstanceId = instanceId ?? target + "-node";
            });

            quartz.UseDashboardAgent(options =>
            {
                options.Endpoint = dashboard.HubUri;
                options.Token = AgentDashboard.Token;
                options.Target = target;
                options.ConfigureConnection = dashboard.ConfigureTransport;
                configure?.Invoke(options);
            });

            configureQuartz?.Invoke(quartz);
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        return new AgentWorker(dashboard, provider, scheduler, target, clock, logs);
    }

    /// <summary>
    /// Waits until the dashboard holds this worker's registration on a live connection.
    /// </summary>
    public Task WaitUntilRegistered()
    {
        return Eventually.Until(() => dashboard.Registry.Find(Target) is { ConnectionId: not null } entry
            && string.Equals(entry.SchedulerInstanceId, Scheduler.SchedulerInstanceId, StringComparison.Ordinal)
            && Plugin.Connection?.IsRegistered == true);
    }

    /// <summary>
    /// Sends one heartbeat, by moving the worker's clock past its interval, and waits until the dashboard
    /// has heard it.
    /// </summary>
    public async Task Heartbeat()
    {
        DateTimeOffset before = dashboard.Registry.Find(Target)?.LastSeenUtc ?? DateTimeOffset.MinValue;
        Clock.Advance(TimeSpan.FromSeconds(16));
        await Eventually.Until(() => dashboard.Registry.Find(Target) is { } entry && entry.LastSeenUtc > before);
    }

    /// <summary>
    /// Drops the connection without a goodbye and stops dialling: what a killed process looks like.
    /// </summary>
    public ValueTask Vanish() => Plugin.Vanish();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
        }
        catch (SchedulerException)
        {
            // Already shut down by the test.
        }

        await provider.DisposeAsync();
    }
}
