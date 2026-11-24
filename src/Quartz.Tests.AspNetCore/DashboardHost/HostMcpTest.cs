using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Quartz.Tests.AspNetCore.Mcp;

namespace Quartz.Tests.AspNetCore.DashboardHost;

/// <summary>
/// M12: the dashboard host serving the MCP endpoint for its whole fleet (#4245) — off unless asked for, read-only
/// unless told otherwise, bearer only, held to the dashboard's operator roles, and with its path and audience checked.
/// </summary>
public sealed class HostMcpTest
{
    private const string Path = "/quartz-mcp";
    private const string Window = "reports/" + SeededStore.SchedulerName;

    private SeededStore store = null!;
    private readonly List<McpClient> clients = [];

    [SetUp]
    public async Task SeedADatabase()
    {
        store = await SeededStore.Create();
    }

    [TearDown]
    public async Task DeleteTheDatabase()
    {
        foreach (McpClient client in clients)
        {
            await client.DisposeAsync();
        }

        clients.Clear();
        store.Dispose();
    }

    [Test]
    public async Task TheEndpointIsNotServedUnlessItIsEnabled()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(enabled: false));
        using HttpClient browser = host.Browser();
        await HostUnderTest.SignIn(browser, HostUnderTest.AdministratorToken);

        using HttpResponseMessage response = await browser.PostAsync(Path, Json("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));

        // 405 rather than 404 where another route of the host answers GET at the path: either way nothing takes the POST.
        response.StatusCode.Should().BeOneOf([HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed], "a new surface for an agent has to be asked for");
    }

    [Test]
    public async Task ItIsReadOnlyUnlessToldOtherwise()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings());

        List<string> tools = await McpTestHost.ToolNames(await Connect(host, HostUnderTest.AdministratorToken));

        tools.Should().HaveCount(10, "Quartz:Dashboard:Mcp:ReadOnly is true unless the host's configuration turns it off");
    }

    [TestCase(HostUnderTest.ViewerToken, 10)]
    [TestCase(HostUnderTest.OperatorToken, 17)]
    [TestCase(HostUnderTest.AdministratorToken, 19)]
    public async Task EachRolesTokenIsABearerAtThatRole(string token, int tools)
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new() { ["Quartz:Dashboard:Mcp:ReadOnly"] = "false" }));

        (await McpTestHost.ToolNames(await Connect(host, token))).Should().HaveCount(tools,
            "the dashboard's own roles, and a role token names a stable identity, so an administrator is offered the confirmed tools");
    }

    [Test]
    public async Task AnOperatorPausesATriggerOfTheAttachedStoreAndTheActionLogSaysSo()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new() { ["Quartz:Dashboard:Mcp:ReadOnly"] = "false" }));
        McpClient @operator = await Connect(host, HostUnderTest.OperatorToken);

        CallToolResult result = await McpTestHost.Call(@operator, "pause_trigger", new { scheduler = Window, group = "nightly", name = "at-midnight", reason = "paused from the host" });

        McpTestHost.Answer(result).GetProperty("applied").GetBoolean().Should().BeTrue();
        JsonElement listed = McpTestHost.Answer(await McpTestHost.Call(@operator, "list_triggers", new { scheduler = Window }));
        listed.GetProperty("triggers")[0].GetProperty("paused").GetProperty("by").GetString().Should().EndWith(" via MCP");
    }

    [Test]
    public async Task ASignInCookieDoesNotSignARequestInToTheEndpoint()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings());
        using HttpClient browser = host.Browser();
        string? cookie = HostUnderTest.SignInCookie(await HostUnderTest.SignIn(browser, HostUnderTest.AdministratorToken));
        cookie.Should().NotBeNull();

        using HttpRequestMessage request = new(HttpMethod.Post, Path) { Content = Json("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""") };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using HttpResponseMessage response = await host.Factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a cookie the browser sends on its own is what a cross-site request borrows, so the endpoint's policy never reads it");
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
    }

    [Test]
    public async Task OpenIdConnectWithTheEndpointEnabledNeedsTheApiAudience()
    {
        await using HostUnderTest host = HostUnderTest.Create(Settings(new()
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "OpenIdConnect",
            ["Quartz:Dashboard:Authentication:Authority"] = "https://login.example.com/",
            ["Quartz:Dashboard:Authentication:ClientId"] = "quartz-dashboard",
            ["Quartz:Dashboard:Roles:Viewers:0"] = "ops-readonly",
        }));

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Should().ContainSingle().Which.Message.Should().Contain("Quartz:Dashboard:Mcp:Enabled needs Quartz:Dashboard:Authentication:ApiAudience");
    }

    [TestCase("/quartz", "the host's own /quartz")]
    [TestCase("quartz-mcp", "must be a path starting with '/'")]
    [TestCase("/quartz-api", "overlaps the HTTP API's /quartz-api")]
    public async Task AMistakeInTheEndpointsPathIsARefusalToStart(string path, string problem)
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new()
        {
            ["Quartz:Dashboard:Mcp:Path"] = path,
            ["Quartz:Dashboard:HttpApi:Enabled"] = "true",
        }));

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Should().Contain(x => x.Message.Contains(problem, StringComparison.Ordinal));
    }

    [Test]
    public async Task TheSdksMessageDumpsAreHeldAtInformation()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new()
        {
            ["Quartz:Dashboard:Mcp:ReadOnly"] = "false",
            ["Logging:LogLevel:Default"] = "Trace",
        }));
        McpClient admin = await Connect(host, HostUnderTest.AdministratorToken);

        JsonElement preview = McpTestHost.Answer(await McpTestHost.Call(admin, "unschedule", new { scheduler = Window, group = "nightly", name = "at-midnight" }));
        string token = preview.GetProperty("confirmationToken").GetString()!;
        await McpTestHost.Call(admin, "unschedule", new { scheduler = Window, group = "nightly", name = "at-midnight", confirmationToken = token });

        host.Logs.Entries.Should().NotContain(entry => entry.Category.StartsWith("ModelContextProtocol", StringComparison.Ordinal) && entry.Level < Microsoft.Extensions.Logging.LogLevel.Information,
            "the SDK writes whole JSON-RPC messages below Information, and the host holds it there whatever the configuration says");
        host.Logs.Entries.Should().NotContain(entry => entry.Message.Contains(token, StringComparison.Ordinal));
    }

    /// <summary>
    /// The SDK's categories are held at Information even where the configuration names a longer one of them, which
    /// would otherwise win over a rule for the prefix (#4245 review).
    /// </summary>
    [Test]
    public async Task TheSdksLongerCategoriesAreHeldAtInformationToo()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new()
        {
            ["Quartz:Dashboard:Mcp:ReadOnly"] = "false",
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:ModelContextProtocol.Server"] = "Trace",
            ["Logging:LogLevel:ModelContextProtocol.AspNetCore"] = "Trace",
            ["Logging:LogLevel:ModelContextProtocol.Protocol"] = "Trace",
        }));
        McpClient admin = await Connect(host, HostUnderTest.AdministratorToken);

        JsonElement preview = McpTestHost.Answer(await McpTestHost.Call(admin, "unschedule", new { scheduler = Window, group = "nightly", name = "at-midnight" }));
        string token = preview.GetProperty("confirmationToken").GetString()!;
        await McpTestHost.Call(admin, "unschedule", new { scheduler = Window, group = "nightly", name = "at-midnight", confirmationToken = token });

        host.Logs.Entries.Should().NotContain(entry => entry.Category.StartsWith("ModelContextProtocol", StringComparison.Ordinal) && entry.Level < Microsoft.Extensions.Logging.LogLevel.Information,
            "a configured ModelContextProtocol.Server=Trace is longer than the clamp's own category, and must not reopen the message dumps");
        host.Logs.Entries.Should().NotContain(entry => entry.Message.Contains(token, StringComparison.Ordinal));
    }

    [Test]
    public async Task ATokenInTheAddressIsRefusedAndNeverLogged()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new() { ["Quartz:Dashboard:Authentication:AllowTokenInUrl"] = "true" }));
        using HttpClient client = host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using HttpRequestMessage request = new(HttpMethod.Post, Path + "?t=" + HostUnderTest.AdministratorToken)
        {
            Content = Json("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the endpoint never reads a token from the address, and a challenge is never a redirect");
        host.Logs.Entries.Should().NotContain(entry => entry.Message.Contains(HostUnderTest.AdministratorToken, StringComparison.Ordinal),
            "the host never logs a token, and an address can carry one");
    }

    [Test]
    public async Task ATokenInTheAddressIsRefusedBesideAValidBearer()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings());
        using HttpClient script = Bearer(host, HostUnderTest.OperatorToken);

        using HttpRequestMessage request = new(HttpMethod.Post, Path + "?t=" + HostUnderTest.AdministratorToken)
        {
            Content = Json("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using HttpResponseMessage response = await script.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Logs.Entries.Should().NotContain(entry => entry.Message.Contains(HostUnderTest.AdministratorToken, StringComparison.Ordinal));
    }

    private Dictionary<string, string?> Settings(Dictionary<string, string?>? more = null, bool enabled = true)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["Quartz:Dashboard:Targets:reports:ConnectionString"] = store.ConnectionString,
            ["Quartz:Dashboard:Targets:reports:RediscoveryInterval"] = "00:00:00",
            ["Quartz:Dashboard:Mcp:Enabled"] = enabled ? "true" : "false",
        };

        foreach ((string key, string? value) in more ?? [])
        {
            settings[key] = value;
        }

        return settings;
    }

    private async Task<McpClient> Connect(HostUnderTest host, string token)
    {
        HttpClient http = Bearer(host, token);
        HttpClientTransport transport = new(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, Path.TrimStart('/')), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            ownsHttpClient: true);
        McpClient client = await McpClient.CreateAsync(transport, new McpClientOptions { ClientInfo = new Implementation { Name = "host-test", Version = "1.0" } });
        clients.Add(client);
        return client;
    }

    private static HttpClient Bearer(HostUnderTest host, string token)
    {
        HttpClient client = host.Factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
