using System.Net;
using System.Security.Claims;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.DashboardHost;

/// <summary>
/// Signing in to the dashboard host: a token per role in <c>Token</c> mode, the token-in-the-address link
/// an Aspire resource opens, and the anonymous operator of <c>Unsecured</c> mode — each held to the operator
/// roles by the dashboard's own client.
/// </summary>
public sealed class DashboardHostSignInTest
{
    private static readonly JobKeyDto Job = new(SeededStore.Job.Group, SeededStore.Job.Name);

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

    private Dictionary<string, string?> WithTheStore(Dictionary<string, string?>? more = null)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["Quartz:Dashboard:Targets:reports:ConnectionString"] = store.ConnectionString,
            ["Quartz:Dashboard:Targets:reports:RediscoveryInterval"] = "00:00:00",
        };

        foreach ((string key, string? value) in more ?? [])
        {
            settings[key] = value;
        }

        return settings;
    }

    [TestCase(HostUnderTest.ViewerToken, "Viewer")]
    [TestCase(HostUnderTest.OperatorToken, "Operator")]
    [TestCase(HostUnderTest.AdministratorToken, "Administrator")]
    public async Task EachTokenSignsInAtItsRoleWithAStrictHttpOnlyCookie(string token, string role)
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(WithTheStore());
        using HttpClient browser = host.Browser();

        HttpResponseMessage response = await HostUnderTest.SignIn(browser, token, returnUrl: "/quartz/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/quartz/jobs", "a local return address is honoured");

        string setCookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("quartz-dashboard=", StringComparison.Ordinal));
        setCookie.Should().ContainEquivalentOf("httponly");
        setCookie.Should().ContainEquivalentOf("samesite=strict");

        (await browser.GetAsync("/quartz")).StatusCode.Should().Be(HttpStatusCode.OK, "the cookie is the sign-in");

        using IServiceScope scope = host.Services.CreateScope();
        ClaimsPrincipal visitor = await HostUnderTest.Visitor(scope.ServiceProvider, HostUnderTest.SignInCookie(response));
        visitor.IsInRole(role).Should().BeTrue();
        visitor.Claims.Where(x => x.Type == ClaimTypes.Role).Should().ContainSingle("a token is one role");

        host.Logs.WithEventId(9403).Should().ContainSingle().Which.Message.Should().Be($"A visitor signed in as {role} through form");
    }

    [Test]
    public async Task AViewerReadsAndCannotPauseAnOperatorPausesAndCannotDelete()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(WithTheStore());
        using HttpClient browser = host.Browser();

        string? viewerCookie = HostUnderTest.SignInCookie(await HostUnderTest.SignIn(browser, HostUnderTest.ViewerToken));
        string? operatorCookie = HostUnderTest.SignInCookie(await HostUnderTest.SignIn(browser, HostUnderTest.OperatorToken));
        string? administratorCookie = HostUnderTest.SignInCookie(await HostUnderTest.SignIn(browser, HostUnderTest.AdministratorToken));

        using (IServiceScope scope = host.Services.CreateScope())
        {
            IQuartzApiClient viewer = HostUnderTest.ClientFor(scope.ServiceProvider, await HostUnderTest.Visitor(scope.ServiceProvider, viewerCookie));

            (await viewer.QueryJobs(SeededStore.SchedulerName, new DashboardJobQuery())).Items.Should().ContainSingle("a viewer reads");

            Func<Task> pause = () => viewer.PauseJob(SeededStore.SchedulerName, Job).AsTask();
            (await pause.Should().ThrowAsync<UnauthorizedAccessException>())
                .Which.Message.Should().Be($"Not authorized for PauseJob on scheduler 'reports/{SeededStore.SchedulerName}': it needs Operate.");
        }

        using (IServiceScope scope = host.Services.CreateScope())
        {
            IQuartzApiClient @operator = HostUnderTest.ClientFor(scope.ServiceProvider, await HostUnderTest.Visitor(scope.ServiceProvider, operatorCookie));

            (await @operator.PauseJob(SeededStore.SchedulerName, Job)).Should().BeTrue("an operator pauses");

            Func<Task> delete = () => @operator.DeleteJob(SeededStore.SchedulerName, Job).AsTask();
            (await delete.Should().ThrowAsync<UnauthorizedAccessException>()).Which.Message.Should().EndWith("it needs Administer.");
        }

        using (IServiceScope scope = host.Services.CreateScope())
        {
            IQuartzApiClient administrator = HostUnderTest.ClientFor(scope.ServiceProvider, await HostUnderTest.Visitor(scope.ServiceProvider, administratorCookie));

            (await administrator.ResumeJob(SeededStore.SchedulerName, Job)).Should().BeTrue();
            (await administrator.DeleteJob(SeededStore.SchedulerName, Job)).Should().BeTrue("an administrator does everything");
        }
    }

    [Test]
    public async Task AWrongTokenIsRefusedAndSignsNobodyIn()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens();
        using HttpClient browser = host.Browser();

        HttpResponseMessage response = await HostUnderTest.SignIn(browser, "not-a-token-0123456789");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        HostUnderTest.SignInCookie(response).Should().BeNull();
        (await response.Content.ReadAsStringAsync()).Should().Contain("That token is not valid.");
        host.Logs.WithEventId(9404).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Test]
    public async Task AReturnAddressOffTheSiteIsNotFollowed()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens();
        using HttpClient browser = host.Browser();

        foreach (string elsewhere in new[] { "https://evil.example/", "//evil.example/", "/\\evil.example/" })
        {
            HttpResponseMessage response = await HostUnderTest.SignIn(browser, HostUnderTest.ViewerToken, elsewhere);
            response.Headers.Location!.OriginalString.Should().Be("/quartz", $"'{elsewhere}' is not on this site");
        }
    }

    [TestCase("/\t/evil.example/")]
    [TestCase("/\n/evil.example/")]
    [TestCase("/\r/evil.example/")]
    [TestCase("/\u007F/evil.example/")]
    public async Task AReturnAddressWithAControlCharacterIsNotFollowed(string elsewhere)
    {
        await using HostUnderTest host = HostUnderTest.WithTokens();
        using HttpClient browser = host.Browser();

        // A browser drops a tab or a line break from an address, so '/<TAB>/evil.example/' is '//evil.example/'.
        HttpResponseMessage response = await HostUnderTest.SignIn(browser, HostUnderTest.ViewerToken, elsewhere);

        response.Headers.Location!.OriginalString.Should().Be("/quartz");
    }

    [Test]
    public async Task ATokenInTheAddressIsRefusedUnlessAllowed()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens();
        using HttpClient browser = host.Browser();

        HttpResponseMessage response = await browser.GetAsync($"/login?t={HostUnderTest.AdministratorToken}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        HostUnderTest.SignInCookie(response).Should().BeNull("only AllowTokenInUrl lets the address sign anybody in");
        (await response.Content.ReadAsStringAsync()).Should().Contain("does not take a token in its address");
        host.Logs.WithEventId(9405).Should().ContainSingle();
    }

    [Test]
    public async Task WithAllowTokenInUrlTheAddressSignsInAndTheTokenLeavesIt()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:AllowTokenInUrl"] = "true",
        });
        using HttpClient browser = host.Browser();

        HttpResponseMessage response = await browser.GetAsync($"/login?t={HostUnderTest.AdministratorToken}&ReturnUrl=%2Fquartz%2Fjobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string page = await response.Content.ReadAsStringAsync();
        page.Should().Contain("<meta http-equiv=\"refresh\" content=\"0;url=/quartz/jobs\">",
            "the browser moves on to an address without the token, as a navigation of this site's own");
        page.Should().NotContain(HostUnderTest.AdministratorToken);
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
        HostUnderTest.SignInCookie(response).Should().NotBeNull();

        (await browser.GetAsync("/quartz")).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Logs.WithEventId(9403).Should().ContainSingle().Which.Message.Should().Be("A visitor signed in as Administrator through link");

        HttpResponseMessage wrong = await host.Browser().GetAsync("/login?t=not-a-token-0123456789");
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        HostUnderTest.SignInCookie(wrong).Should().BeNull();
    }

    [Test]
    public async Task NoTokenReachesTheLogs()
    {
        await using HostUnderTest host = HostUnderTest.WithTokens(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:AllowTokenInUrl"] = "true",
            ["Quartz:Dashboard:Agents:Tokens:Primary"] = "agent-token-0123456789",
            // Everything at its most talkative, the hosting layer's request lines included: the host holds
            // those back whatever configuration says, because an address can carry a token.
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Trace",
            // A rule that names a provider outranks one that does not, so this is the setting that would let
            // the request line through if the host's hold were a rule with no provider.
            [$"Logging:{typeof(RecordingLoggerProvider).FullName}:LogLevel:Default"] = "Trace",
            [$"Logging:{typeof(RecordingLoggerProvider).FullName}:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Trace",
        });
        using HttpClient browser = host.Browser();

        await HostUnderTest.SignIn(browser, "wrong-token-0123456789");
        await HostUnderTest.SignIn(browser, HostUnderTest.ViewerToken);
        await browser.GetAsync($"/login?t={HostUnderTest.OperatorToken}");
        await browser.GetAsync($"/quartz?t={HostUnderTest.AdministratorToken}");
        await browser.GetAsync("/logout");

        host.Logs.Entries.Should().NotBeEmpty();
        host.Logs.WithEventId(9403).Should().HaveCount(2, "both sign-ins were logged, by role");

        string[] secrets = [HostUnderTest.ViewerToken, HostUnderTest.OperatorToken, HostUnderTest.AdministratorToken, "wrong-token-0123456789", "agent-token-0123456789"];
        List<RecordedLogEntry> leaks = host.Logs.Entries
            .Where(entry => secrets.Any(secret => entry.Message.Contains(secret, StringComparison.Ordinal)
                                                  || entry.Properties.Values.Any(value => value?.Contains(secret, StringComparison.Ordinal) == true)))
            .ToList();

        leaks.Should().BeEmpty("a token in a log is a token anyone with the logs holds");
    }

    [Test]
    public async Task UnsecuredSignsEveryVisitorInAtViewAndWarnsAtStart()
    {
        await using HostUnderTest host = HostUnderTest.Create(WithTheStore(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "Unsecured",
        }));
        using HttpClient browser = host.Browser();

        (await browser.GetAsync("/quartz")).StatusCode.Should().Be(HttpStatusCode.OK, "nobody signs in");
        host.Logs.WithEventId(9401).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);

        using IServiceScope scope = host.Services.CreateScope();
        IQuartzApiClient anonymous = HostUnderTest.ClientFor(scope.ServiceProvider, await HostUnderTest.Visitor(scope.ServiceProvider, cookie: null));

        (await anonymous.QueryJobs(SeededStore.SchedulerName, new DashboardJobQuery())).Items.Should().ContainSingle();

        Func<Task> pause = () => anonymous.PauseJob(SeededStore.SchedulerName, Job).AsTask();
        await pause.Should().ThrowAsync<UnauthorizedAccessException>("Unsecured is View unless UnsecuredAccess says Operate");
    }

    [Test]
    public async Task UnsecuredAtOperatePausesAndStillCannotDelete()
    {
        await using HostUnderTest host = HostUnderTest.Create(WithTheStore(new Dictionary<string, string?>
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "Unsecured",
            ["Quartz:Dashboard:Authentication:UnsecuredAccess"] = "Operate",
        }));
        using HttpClient browser = host.Browser();

        using IServiceScope scope = host.Services.CreateScope();
        IQuartzApiClient anonymous = HostUnderTest.ClientFor(scope.ServiceProvider, await HostUnderTest.Visitor(scope.ServiceProvider, cookie: null));

        (await anonymous.PauseJob(SeededStore.SchedulerName, Job)).Should().BeTrue();

        Func<Task> delete = () => anonymous.DeleteJob(SeededStore.SchedulerName, Job).AsTask();
        await delete.Should().ThrowAsync<UnauthorizedAccessException>();
        host.Logs.WithEventId(9401).Should().ContainSingle().Which.Message.Should().Contain("at Operate");
    }
}
