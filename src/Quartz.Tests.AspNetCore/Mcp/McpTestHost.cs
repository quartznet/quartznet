using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// An application with a scheduler over the in-memory store and the Quartz MCP endpoint mapped, called with the SDK's
/// own client over the test server.
/// </summary>
/// <remarks>
/// <para>
/// A caller is a role, named in <c>X-Role</c> as the HTTP API's operation tests name it; no header is an anonymous
/// caller. Writable by default, under the three-role operation policy, because most cases are about what a role may
/// change; a case about the defaults says <c>writable: false</c>.
/// </para>
/// <para>
/// Everything logged, at every level, is kept, so a case can say what a line carries and that no line carries a
/// token.
/// </para>
/// </remarks>
internal sealed class McpTestHost : IAsyncDisposable
{
    public const string Path = "/quartz-mcp";

    private readonly List<McpClient> clients = [];

    private McpTestHost(WebApplication app, IScheduler scheduler, RecordingLoggerProvider logs)
    {
        App = app;
        Scheduler = scheduler;
        Logs = logs;
    }

    public WebApplication App { get; }

    /// <summary>The application's own scheduler, <see cref="TestData.SchedulerName" />'s default name aside.</summary>
    public IScheduler Scheduler { get; }

    public RecordingLoggerProvider Logs { get; }

    public IServiceProvider Services => App.Services;

    public static async Task<McpTestHost> Start(
        Action<QuartzMcpOptions>? mcp = null,
        Action<IServiceCollection>? services = null,
        Action<SchedulerRoles>? roles = null,
        Action<WebApplication>? map = null,
        bool writable = true)
    {
        TestContentRoot.Apply();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        RecordingLoggerProvider logs = new();
        builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace);

        builder.Services.AddQuartz();
        builder.Services.AddRoleAuthorization(roles);
        builder.Services.AddQuartzMcp(options =>
        {
            if (writable)
            {
                options.ReadOnly = false;
                options.OperationAuthorizationPolicy = RoleAuthenticationExtensions.OperatorRolesPolicy;
            }

            mcp?.Invoke(options);
        });
        services?.Invoke(builder.Services);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        if (map is null)
        {
            app.MapQuartzMcp().AllowAnonymous();
        }
        else
        {
            map(app);
        }

        await app.StartAsync();

        IScheduler scheduler = await app.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        return new McpTestHost(app, scheduler, logs);
    }

    /// <summary>
    /// The SDK's client, connected as <paramref name="role" />, naming itself <paramref name="clientName" />.
    /// </summary>
    public async Task<McpClient> Connect(string? role, string clientName = "test-agent", Action<HttpClient>? configure = null)
    {
        HttpClient http = App.GetTestClient();
        if (role is not null)
        {
            http.DefaultRequestHeaders.Add(RoleAuthenticationHandler.RoleHeaderName, role);
        }

        configure?.Invoke(http);

        HttpClientTransport transport = new(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, Path.TrimStart('/')),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            ownsHttpClient: true);

        McpClient client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ClientInfo = new Implementation { Name = clientName, Version = "1.0" } });
        clients.Add(client);
        return client;
    }

    /// <summary>The names <c>tools/list</c> offers <paramref name="client" />, sorted.</summary>
    public static async Task<List<string>> ToolNames(McpClient client)
    {
        ListToolsResult result = await client.ListToolsAsync(new ListToolsRequestParams());
        return [.. result.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal)];
    }

    /// <summary>A call, with its arguments as a test writes them.</summary>
    public static ValueTask<CallToolResult> Call(McpClient client, string tool, object? arguments = null)
    {
        Dictionary<string, object?>? values = arguments is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments));
        return client.CallToolAsync(tool, values);
    }

    /// <summary>A successful call's answer.</summary>
    public static JsonElement Answer(CallToolResult result)
    {
        result.IsError.Should().NotBe(true, Text(result));
        result.StructuredContent.Should().NotBeNull();
        return result.StructuredContent!.Value;
    }

    /// <summary>A refused call's error.</summary>
    public static JsonElement Error(CallToolResult result)
    {
        result.IsError.Should().BeTrue("the call should have been refused: " + Text(result));
        return result.StructuredContent!.Value.GetProperty("error");
    }

    public static string Text(CallToolResult result)
    {
        return string.Join(" ", result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
    }

    /// <summary>A durable job and a trigger on it, an hour out, in the application's scheduler.</summary>
    public async Task<(JobKey Job, TriggerKey Trigger)> ScheduleJob(string group = "reports", string name = "nightly", JobDataMap? jobData = null, JobDataMap? triggerData = null)
    {
        IJobDetail job = JobBuilder.Create<DummyJob>().WithIdentity(name, group).UsingJobData(jobData ?? new JobDataMap()).StoreDurably().Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob(job)
            .UsingJobData(triggerData ?? new JobDataMap())
            .WithCronSchedule("0 0 2 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        await Scheduler.ScheduleJob(job, trigger);
        return (job.Key, trigger.Key);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (McpClient client in clients)
        {
            await client.DisposeAsync();
        }

        await Scheduler.Shutdown(waitForJobsToComplete: false);
        await App.DisposeAsync();
    }
}
