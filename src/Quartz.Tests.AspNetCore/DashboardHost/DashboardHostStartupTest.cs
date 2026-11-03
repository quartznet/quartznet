using System.Net;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.DashboardHost;

/// <summary>
/// What the dashboard host makes of its settings: what it starts with, and what it refuses to start on.
/// </summary>
public sealed class DashboardHostStartupTest
{
    [Test]
    public async Task WithoutAnAuthenticationModeTheHostRefusesToStartAndSaysWhy()
    {
        await using HostUnderTest host = HostUnderTest.Create(new Dictionary<string, string?>());

        Action start = () => host.Browser();

        // Program.Main ends a refused start with exit code 1 rather than an unhandled exception, so the factory
        // meets the refusal as the host it was handed being gone. What says why is the log, as in a container.
        start.Should().Throw<InvalidOperationException>();
        RecordedLogEntry refusal = host.Logs.WithEventId(9402).Should().ContainSingle().Subject;
        refusal.Level.Should().Be(LogLevel.Critical);
        refusal.Message.Should().Contain("Quartz:Dashboard:Authentication:Mode is required");
    }

    [TestCase("Quartz:Dashboard:Authentication:Mode", "Basic", "none of Token, OpenIdConnect and Unsecured")]
    [TestCase("Quartz:Dashboard:Authentication:Tokens:Viewer", "short", "shorter than 16 characters")]
    [TestCase("Quartz:Dashboard:Authentication:Tokens:Operator", HostUnderTest.ViewerToken, "are the same token")]
    [TestCase("Quartz:Dashboard:Roles:Operations:Fly", "Operate", "Roles:Operations: Operations names 'Fly'")]
    [TestCase("Quartz:Dashboard:Roles:Administrators:0", "ops", "map an identity provider's roles")]
    [TestCase("Quartz:Dashboard:Agents:HeartbeatInterval", "00:00:05", "names no token")]
    [TestCase("Quartz:Dashboard:Targets:a/b:Url", "https://orders.internal/quartz-api/", "cannot contain '/'")]
    [TestCase("Quartz:Dashboard:Targets:orders:Url", "orders.internal", "must be an absolute http or https address")]
    [TestCase("Quartz:Dashboard:Targets:orders:Provider", "Db2", "The host carries drivers for")]
    [TestCase("Quartz:Dashboard:Targets:orders:ConnectionString", "Endpoint=nowhere", "cannot tell which database")]
    [TestCase("Quartz:Dashboard:Targets:orders:ConnectionStringName", "missing", "ConnectionStrings:missing is not set")]
    [TestCase("Quartz:Dashboard:Targets:orders:Serializer", "Xml", "can be SystemTextJson, the default, or Newtonsoft")]
    [TestCase("Quartz:Dashboard:Targets:orders:Timeout", "-00:00:01", "Timeout must be positive")]
    [TestCase("Quartz:Dashboard:Targets:orders:RediscoveryInterval", "-00:00:01", "RediscoveryInterval cannot be negative")]
    [TestCase("Quartz:Dashboard:Targets:orders:ExecutionHistory", "true", "needs a ConnectionString or a ConnectionStringName")]
    [TestCase("Quartz:Dashboard:Agents:Tokens:Primary", "   ", "is blank")]
    [TestCase("Quartz:Dashboard:Authentication:UnsecuredAccess", "Operate", "applies to Unsecured mode only")]
    [TestCase("Quartz:Dashboard:ReadOnly", "perhaps", "could not be read")]
    [TestCase("Quartz:Dashboard:Targets:orders:Port", "5000", "says nothing")]
    public async Task AMistakeInTheSettingsIsARefusalToStartNamingIt(string key, string value, string problem)
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(new Dictionary<string, string?> { [key] = value });

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Should().Contain(x => x.Message.Contains(problem, StringComparison.Ordinal));
    }

    [Test]
    public async Task ATargetThatIsBothAnHttpTargetAndAStoreIsRefused()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Targets:orders:Url"] = "https://orders.internal/quartz-api/",
            ["Quartz:Dashboard:Targets:orders:SchedulerName"] = "Orders",
            ["Quartz:Dashboard:Targets:orders:ConnectionString"] = "Data Source=orders.db",
        });

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Should().ContainSingle().Which.Message.Should().Contain("A target is one or the other");
    }

    [Test]
    public async Task OpenIdConnectWithoutRolesRefusesToStart()
    {
        await using HostUnderTest host = HostUnderTest.Create(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "OpenIdConnect",
            ["Quartz:Dashboard:Authentication:Authority"] = "https://login.example.com/",
            ["Quartz:Dashboard:Authentication:ClientId"] = "quartz-dashboard",
        });

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Should().ContainSingle()
            .Which.Message.Should().Contain("OpenIdConnect mode needs Quartz:Dashboard:Roles:Viewers, :Operators or :Administrators");
    }

    [Test]
    public async Task OpenIdConnectNamesEveryMistakeAtOnce()
    {
        await using HostUnderTest host = HostUnderTest.Create(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "openidconnect",
            ["Quartz:Dashboard:Authentication:Authority"] = "not a uri",
            ["Quartz:Dashboard:Authentication:Tokens:Viewer"] = HostUnderTest.ViewerToken,
            ["Quartz:Dashboard:Authentication:AllowTokenInUrl"] = "true",
            ["Quartz:Dashboard:Roles:Viewers:0"] = " ",
        });

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Select(x => x.Message).Should().Contain(
        [
            "The dashboard host refuses to start: Quartz:Dashboard:Authentication:Tokens are set, but Mode is OpenIdConnect: tokens sign visitors in only in Token mode.",
            "The dashboard host refuses to start: Quartz:Dashboard:Authentication:AllowTokenInUrl is set, but Mode is OpenIdConnect: it applies to Token mode only.",
            "The dashboard host refuses to start: Quartz:Dashboard:Authentication:Authority is 'not a uri', which is not an absolute URI.",
            "The dashboard host refuses to start: OpenIdConnect mode needs Quartz:Dashboard:Authentication:ClientId.",
            "The dashboard host refuses to start: Quartz:Dashboard:Roles:Viewers holds a blank role, which no user is in.",
        ]);

        await using HostUnderTest missing = HostUnderTest.Create(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "OpenIdConnect",
            ["Quartz:Dashboard:Roles:Viewers:0"] = "readers",
        });

        Action startWithoutAuthority = () => missing.Browser();
        startWithoutAuthority.Should().Throw<InvalidOperationException>();
        missing.Logs.WithEventId(9402).Should().Contain(x => x.Message.Contains("needs Quartz:Dashboard:Authentication:Authority", StringComparison.Ordinal));
    }

    [Test]
    public async Task TelemetryAndPersistedKeysAreTurnedOnBySettings()
    {
        DirectoryInfo keys = Directory.CreateTempSubdirectory("quartz-dashboard-keys-");
        try
        {
            await using HostUnderTest host = HostUnderTest.WithTokens(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:4317",
                ["Quartz:Dashboard:DataProtection:KeysPath"] = keys.FullName,
            });
            using HttpClient browser = host.Browser();

            host.Services.GetService<OpenTelemetry.Metrics.MeterProvider>().Should().NotBeNull("an OTLP endpoint turns the export on");
            host.Services.GetService<OpenTelemetry.Trace.TracerProvider>().Should().NotBeNull();

            (await HostUnderTest.SignIn(browser, HostUnderTest.ViewerToken)).StatusCode.Should().Be(HttpStatusCode.Redirect);
            keys.EnumerateFiles("key-*.xml").Should().NotBeEmpty("the cookie's key is kept where the setting says, so a sign-in survives a restart");
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    [Test]
    public async Task OpenIdConnectWithRolesSignsInThroughTheIdentityProvider()
    {
        await using HostUnderTest host = HostUnderTest.Create(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "OpenIdConnect",
            ["Quartz:Dashboard:Authentication:Authority"] = "https://login.example.com/",
            ["Quartz:Dashboard:Authentication:ClientId"] = "quartz-dashboard",
            ["Quartz:Dashboard:Authentication:ClientSecret"] = "client-secret-0123456789",
            ["Quartz:Dashboard:Authentication:RoleClaimType"] = "groups",
            ["Quartz:Dashboard:Authentication:Scopes:0"] = "openid",
            ["Quartz:Dashboard:Authentication:Scopes:1"] = "groups",
            ["Quartz:Dashboard:Roles:Viewers:0"] = "quartz-readers",
            ["Quartz:Dashboard:Roles:Administrators:0"] = "quartz-admins",
        });

        using HttpClient browser = host.Browser();

        IAuthenticationSchemeProvider schemes = host.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);

        OpenIdConnectOptions oidc = host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        oidc.Authority.Should().Be("https://login.example.com/");
        oidc.ClientId.Should().Be("quartz-dashboard");
        oidc.ResponseType.Should().Be("code");
        oidc.TokenValidationParameters.RoleClaimType.Should().Be("groups");
        oidc.Scope.Should().Equal(["openid", "groups"]);

        host.Logs.WithEventId(9400).Should().ContainSingle().Which.Message.Should().Contain("authentication OpenIdConnect");
        host.Logs.Entries.Should().NotContain(x => x.Message.Contains("client-secret-0123456789", StringComparison.Ordinal));
    }

    [Test]
    public async Task UnsecuredIsNeverAdminister()
    {
        await using HostUnderTest host = HostUnderTest.Create(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "Unsecured",
            ["Quartz:Dashboard:Authentication:UnsecuredAccess"] = "Administer",
        });

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Should().ContainSingle().Which.Message.Should().Contain("never Administer");
    }

    [Test]
    public async Task TheHealthProbeIsAnonymousAndThePagesAreNot()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens();
        using HttpClient browser = host.Browser();

        HttpResponseMessage health = await browser.GetAsync("/healthz");
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        (await health.Content.ReadAsStringAsync()).Should().Be("Healthy");

        HttpResponseMessage page = await browser.GetAsync("/quartz");
        page.StatusCode.Should().Be(HttpStatusCode.Redirect, "a visitor who has not signed in is sent to sign in");
        page.Headers.Location!.OriginalString.Should().StartWith("http://localhost/login?ReturnUrl=%2Fquartz");

        using HttpClient script = host.Factory.CreateClient();
        (await script.GetAsync("/quartz")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a request that is not for a page is told 401 rather than redirected to a form it cannot fill in");

        HttpResponseMessage root = await browser.GetAsync("/");
        root.Headers.Location!.OriginalString.Should().Be("/quartz");
    }

    [Test]
    public async Task TargetsComeFromConfiguration()
    {
        using SeededStore store = await SeededStore.Create();

        await using HostUnderTest host = HostUnderTest.WithTokens(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Targets:orders:Url"] = "https://user:secret@orders.internal/quartz-api",
            ["Quartz:Dashboard:Targets:orders:SchedulerName"] = "OrdersScheduler",
            ["Quartz:Dashboard:Targets:orders:BearerToken"] = "orders-bearer-0123456789",
            ["Quartz:Dashboard:Targets:orders:Headers:X-Tenant"] = "acme",
            ["Quartz:Dashboard:Targets:orders:Timeout"] = "00:00:20",
            ["Quartz:Dashboard:Targets:reports:ConnectionString"] = store.ConnectionString,
            ["Quartz:Dashboard:Targets:reports:RediscoveryInterval"] = "00:00:00",
            ["Quartz:Dashboard:Agents:Tokens:Primary"] = "agent-token-0123456789",
            ["Quartz:Dashboard:ReadOnly"] = "true",
            ["Quartz:Dashboard:HistoryRetention"] = "02:00:00",
        });

        using HttpClient browser = host.Browser();

        IScheduler orders = host.Services.GetRequiredKeyedService<IScheduler>("orders");
        orders.Should().BeOfType<HttpScheduler>().Which.SchedulerName.Should().Be("OrdersScheduler");

        HttpClient ordersClient = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("Quartz.Dashboard.Host.Target.orders");
        ordersClient.BaseAddress.Should().Be(new Uri("https://user:secret@orders.internal/quartz-api/"), "the base address gains the slash HttpScheduler needs");
        ordersClient.Timeout.Should().Be(TimeSpan.FromSeconds(20));
        ordersClient.DefaultRequestHeaders.Authorization!.ToString().Should().Be("Bearer orders-bearer-0123456789");
        ordersClient.DefaultRequestHeaders.GetValues("X-Tenant").Should().Equal(["acme"]);

        string? cookie = HostUnderTest.SignInCookie(await HostUnderTest.SignIn(browser, HostUnderTest.ViewerToken));
        using IServiceScope scope = host.Services.CreateScope();
        IQuartzApiClient client = HostUnderTest.ClientFor(scope.ServiceProvider, await HostUnderTest.Visitor(scope.ServiceProvider, cookie));

        PagedResult<JobKeyDto> jobs = await client.QueryJobs(SeededStore.SchedulerName, new DashboardJobQuery());
        jobs.Items.Should().ContainSingle(x => x.Name == SeededStore.Job.Name,
            "the database named by the store-attached target is read as a window onto the scheduler in it");

        QuartzDashboardOptions options = host.Services.GetRequiredService<IOptions<QuartzDashboardOptions>>().Value;
        options.ReadOnly.Should().BeTrue();
        options.HistoryRetention.Should().Be(TimeSpan.FromHours(2));
        options.Agents!.Tokens.Primary.Should().Be("agent-token-0123456789");
        options.AttachedStores.Should().ContainSingle().Which.RediscoveryInterval.Should().BeNull("zero asks the database once");

        host.Logs.WithEventId(9407).Should().ContainSingle()
            .Which.Message.Should().Be("HTTP target orders reaches scheduler OrdersScheduler at https://orders.internal/quartz-api/",
                "an address is logged without the credentials it may carry");
        host.Logs.WithEventId(9408).Should().ContainSingle().Which.Message.Should().Contain("reports reads a SQLite-Microsoft database");
        host.Logs.WithEventId(9409).Should().ContainSingle().Which.Message.Should().Contain("/quartz/agents");
        host.Logs.WithEventId(9400).Should().ContainSingle()
            .Which.Message.Should().Be("Dashboard host started at /quartz: authentication Token, 1 HTTP target(s), 1 attached store(s), accepting agents: True");
    }

    [Test]
    public async Task ADashboardPathMovesThePagesAndTheRootRedirect()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:DashboardPath"] = "/ops/quartz/",
        });

        using HttpClient browser = host.Browser();

        (await browser.GetAsync("/")).Headers.Location!.OriginalString.Should().Be("/ops/quartz");

        HttpResponseMessage signedIn = await HostUnderTest.SignIn(browser, HostUnderTest.ViewerToken);
        signedIn.Headers.Location!.OriginalString.Should().Be("/ops/quartz");
        (await browser.GetAsync("/ops/quartz")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
