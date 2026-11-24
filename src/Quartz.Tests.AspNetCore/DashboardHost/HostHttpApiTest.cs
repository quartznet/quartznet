using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

using Quartz.Dashboard.Host;
using Quartz.HttpApiContract;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.DashboardHost;

/// <summary>
/// The dashboard host serving the scheduler HTTP API for its whole fleet (#4218): off unless asked for,
/// bearer only, held to the dashboard's operator roles, and closed to a page of another origin.
/// </summary>
public sealed class HostHttpApiTest
{
    private const string Jobs = "/quartz-api/schedulers/" + SeededStore.SchedulerName + "/jobs";
    private const string Job = Jobs + "/batch/nightly";
    private const string Target = "?target=reports";

    private SeededStore store = null!;

    [SetUp]
    public async Task SeedADatabase()
    {
        store = await SeededStore.Create();
    }

    [TearDown]
    public void DeleteTheDatabase()
    {
        store.Dispose();
    }

    [Test]
    public async Task TheApiIsNotServedUnlessItIsEnabled()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(enabled: false));
        using HttpClient browser = host.Browser();
        await HostUnderTest.SignIn(browser, HostUnderTest.AdministratorToken);

        using HttpResponseMessage response = await browser.GetAsync(Jobs + Target);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a new write surface on an existing deployment has to be asked for");
    }

    [TestCase(HostUnderTest.ViewerToken, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(HostUnderTest.OperatorToken, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [TestCase(HostUnderTest.AdministratorToken, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK)]
    public async Task EachRolesTokenIsABearerAtThatRole(string token, HttpStatusCode read, HttpStatusCode pause, HttpStatusCode delete)
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings());
        using HttpClient script = Script(host, token);

        using HttpResponseMessage jobs = await script.GetAsync(Jobs + Target);
        using HttpResponseMessage paused = await script.PostAsync(Job + "/pause" + Target, content: null);
        using HttpResponseMessage deleted = await script.DeleteAsync(Job + Target);

        jobs.StatusCode.Should().Be(read, await jobs.Content.ReadAsStringAsync());
        jobs.Headers.GetValues(HttpApiConstants.TargetHeader).Should().Equal(["reports"]);
        (await jobs.Content.ReadAsStringAsync()).Should().Contain("nightly", "the window reads the attached store");
        paused.StatusCode.Should().Be(pause, "a viewer reads, an operator pauses: the dashboard's own roles");
        deleted.StatusCode.Should().Be(delete, "and only an administrator deletes");
    }

    [Test]
    public async Task ASignInCookieDoesNotSignARequestInToTheApi()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings());
        using HttpClient browser = host.Browser();
        string? cookie = HostUnderTest.SignInCookie(await HostUnderTest.SignIn(browser, HostUnderTest.AdministratorToken));
        cookie.Should().NotBeNull();

        using HttpRequestMessage request = new(HttpMethod.Get, Jobs + Target);
        request.Headers.Add("Cookie", cookie);
        using HttpResponseMessage response = await host.Factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a cookie the browser sends on its own is what a cross-site request borrows, so the API's policy never reads it");
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
    }

    [Test]
    public async Task ATokenInTheAddressIsNeverReadAndNeverLogged()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new() { ["Quartz:Dashboard:Authentication:AllowTokenInUrl"] = "true" }));
        using HttpClient client = host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using HttpResponseMessage response = await client.GetAsync(Jobs + Target + "&t=" + HostUnderTest.AdministratorToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a challenge, never a redirect to a sign-in page");
        host.Logs.Entries.Should().NotContain(entry => entry.Message.Contains(HostUnderTest.AdministratorToken, StringComparison.Ordinal),
            "the host never logs a token, and an address can carry one");
    }

    /// <summary>
    /// A token in the address is refused even beside a valid bearer: a failure further in is logged with the
    /// request's URL, query and all, and the value must reach no log.
    /// </summary>
    [Test]
    public async Task ATokenInTheAddressIsRefusedBesideAValidBearer()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings());
        using HttpClient script = Script(host, HostUnderTest.OperatorToken);

        using HttpResponseMessage read = await script.GetAsync(Jobs + Target + "&t=" + HostUnderTest.AdministratorToken);
        using HttpResponseMessage failing = await script.PostAsync("/quartz-api/schedulers/nobody/start" + Target + "&t=" + HostUnderTest.AdministratorToken, content: null);

        read.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the API never reads a token from the address, and refuses one that is there");
        failing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Logs.Entries.Should().NotContain(entry => entry.Message.Contains(HostUnderTest.AdministratorToken, StringComparison.Ordinal),
            "the host never logs a token, and an address can carry one");
    }

    [Test]
    public async Task AnUnknownBearerIsRefusedAndLoggedWithoutItsValue()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings());
        using HttpClient script = Script(host, "not-a-token-of-this-host");

        using HttpResponseMessage response = await script.GetAsync(Jobs + Target);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Logs.WithEventId(9410).Should().ContainSingle().Which.Message.Should().NotContain("not-a-token-of-this-host");
    }

    [Test]
    public async Task ABrowserRequestFromAnOriginNotListedIsRefused()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new() { ["Quartz:Dashboard:AllowedOrigins:0"] = "https://ops.example.com" }));
        using HttpClient script = Script(host, HostUnderTest.OperatorToken);

        using HttpResponseMessage evil = await script.SendAsync(WithOrigin(HttpMethod.Post, Job + "/pause" + Target, "https://evil.example"));
        using HttpResponseMessage listed = await script.SendAsync(WithOrigin(HttpMethod.Post, Job + "/pause" + Target, "https://ops.example.com"));
        using HttpResponseMessage none = await script.PostAsync(Job + "/pause" + Target, content: null);

        evil.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a page of another origin is refused even with a valid bearer");
        (await evil.Content.ReadAsStringAsync()).Should().Contain("https://evil.example");
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        none.StatusCode.Should().Be(HttpStatusCode.OK, "a script sends no Origin");
    }

    [Test]
    public async Task UnsecuredModeRefusesEveryBrowserOrigin()
    {
        await using HostUnderTest host = HostUnderTest.Create(Settings(new()
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "Unsecured",
            ["Quartz:Dashboard:Authentication:UnsecuredAccess"] = "Operate",
        }));
        using HttpClient client = host.Factory.CreateClient();

        using HttpResponseMessage rebound = await client.SendAsync(WithOrigin(HttpMethod.Post, Job + "/pause" + Target, "http://attacker.example"));
        using HttpResponseMessage script = await client.PostAsync(Job + "/pause" + Target, content: null);

        rebound.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "no credential is needed in Unsecured mode, so the Origin check is what stops a DNS-rebinding page");
        script.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task OpenIdConnectTakesAJwtIssuedForTheApiAudience()
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes("a signing key long enough for HMAC-SHA256 in a test"));
        const string issuer = "https://login.example.com/";

        await using HostUnderTest host = HostUnderTest.Create(
            Settings(new()
            {
                ["Quartz:Dashboard:Authentication:Mode"] = "OpenIdConnect",
                ["Quartz:Dashboard:Authentication:Authority"] = issuer,
                ["Quartz:Dashboard:Authentication:ClientId"] = "quartz-dashboard",
                ["Quartz:Dashboard:Authentication:ApiAudience"] = "quartz-api",
                ["Quartz:Dashboard:Roles:Viewers:0"] = "ops-readonly",
            }),
            services => services.PostConfigure<JwtBearerOptions>(HostAuthentication.BearerScheme, jwt =>
            {
                OpenIdConnectConfiguration configuration = new() { Issuer = issuer };
                configuration.SigningKeys.Add(key);
                jwt.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));

        using HttpClient forTheApi = Bearer(host, Jwt(key, issuer, audience: "quartz-api"));
        using HttpClient forAnother = Bearer(host, Jwt(key, issuer, audience: "another-api"));

        using HttpResponseMessage accepted = await forTheApi.GetAsync(Jobs + Target);
        using HttpResponseMessage refused = await forAnother.GetAsync(Jobs + Target);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a token issued for another audience was not issued for this host");
    }

    [TestCase("Quartz:Dashboard:HttpApi:Path", "/quartz", "the host's own /quartz")]
    [TestCase("Quartz:Dashboard:HttpApi:Path", "/login", "the host's own /login")]
    [TestCase("Quartz:Dashboard:HttpApi:Path", "/healthz/api", "the host's own /healthz")]
    [TestCase("Quartz:Dashboard:HttpApi:Path", "/", "the host's own root")]
    [TestCase("Quartz:Dashboard:HttpApi:Path", "quartz-api", "must be a path starting with '/'")]
    [TestCase("Quartz:Dashboard:Authentication:ApiAudience", "quartz-api", "applies to OpenIdConnect mode only")]
    [TestCase("Quartz:Dashboard:AllowedOrigins:0", "ops.example.com", "AllowedOrigins: 'ops.example.com' is not an origin")]
    public async Task AMistakeInTheApisSettingsIsARefusalToStart(string key, string value, string problem)
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(Settings(new() { [key] = value }));

        Action start = () => host.Browser();

        start.Should().Throw<InvalidOperationException>();
        host.Logs.WithEventId(9402).Should().Contain(x => x.Message.Contains(problem, StringComparison.Ordinal));
    }

    [Test]
    public async Task OpenIdConnectWithTheApiEnabledNeedsTheApiAudience()
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
        host.Logs.WithEventId(9402).Should().ContainSingle().Which.Message.Should().Contain("needs Quartz:Dashboard:Authentication:ApiAudience");
    }

    private Dictionary<string, string?> Settings(Dictionary<string, string?>? more = null, bool enabled = true)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["Quartz:Dashboard:Targets:reports:ConnectionString"] = store.ConnectionString,
            ["Quartz:Dashboard:Targets:reports:RediscoveryInterval"] = "00:00:00",
            ["Quartz:Dashboard:HttpApi:Enabled"] = enabled ? "true" : "false",
        };

        foreach ((string key, string? value) in more ?? [])
        {
            settings[key] = value;
        }

        return settings;
    }

    private static HttpClient Script(HostUnderTest host, string token) => Bearer(host, token);

    private static HttpClient Bearer(HostUnderTest host, string token)
    {
        HttpClient client = host.Factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpRequestMessage WithOrigin(HttpMethod method, string uri, string origin)
    {
        HttpRequestMessage request = new(method, uri);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    private static string Jwt(SecurityKey key, string issuer, string audience)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity([new Claim("name", "ci-pipeline"), new Claim("roles", "ops-readonly")]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
    }
}
