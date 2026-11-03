using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Dashboard.Host;
using Quartz.Tests.AspNetCore.Support;

// Named apart: inside Quartz.Tests.AspNetCore a bare Program is the HTTP API test host's, which the
// enclosing namespace finds before any using directive is consulted.
using HostProgram = Quartz.Dashboard.Host.Program;

namespace Quartz.Tests.AspNetCore.DashboardHost;

/// <summary>
/// The dashboard host, stood up from its own entry point with nothing but settings, as the tool and the
/// container start it.
/// </summary>
/// <remarks>
/// The settings reach the host as command-line arguments: <c>WebApplicationFactory</c> turns
/// <c>UseSetting</c> into <c>--key=value</c> for an application built with <c>WebApplication</c>, which is
/// what lets the host read them before it registers anything, exactly as it reads its environment
/// variables in a container.
/// </remarks>
internal sealed class HostUnderTest : IAsyncDisposable
{
    public const string ViewerToken = "viewer-token-0123456789";
    public const string OperatorToken = "operator-token-0123456789";
    public const string AdministratorToken = "administrator-token-0123456789";

    private static readonly Regex AntiforgeryField = new(
        "name=\"__RequestVerificationToken\" value=\"(?<value>[^\"]*)\"",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private HostUnderTest(WebApplicationFactory<HostProgram> factory, RecordingLoggerProvider logs)
    {
        Factory = factory;
        Logs = logs;
    }

    public WebApplicationFactory<HostProgram> Factory { get; }

    public RecordingLoggerProvider Logs { get; }

    public IServiceProvider Services => Factory.Services;

    /// <summary>
    /// A host configured with <paramref name="settings" />, not yet started: the first client starts it.
    /// </summary>
    public static HostUnderTest Create(IReadOnlyDictionary<string, string?> settings)
    {
        RecordingLoggerProvider logs = new();

        WebApplicationFactory<HostProgram> factory = new WebApplicationFactory<HostProgram>().WithWebHostBuilder(builder =>
        {
            foreach ((string key, string? value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

        return new HostUnderTest(factory, logs);
    }

    /// <summary>
    /// Token mode with a token for each of the three roles, and whatever else the case adds.
    /// </summary>
    public static HostUnderTest WithTokens(IReadOnlyDictionary<string, string?>? more = null)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["Quartz:Dashboard:Authentication:Mode"] = "Token",
            ["Quartz:Dashboard:Authentication:Tokens:Viewer"] = ViewerToken,
            ["Quartz:Dashboard:Authentication:Tokens:Operator"] = OperatorToken,
            ["Quartz:Dashboard:Authentication:Tokens:Administrator"] = AdministratorToken,
        };

        foreach ((string key, string? value) in more ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return Create(settings);
    }

    /// <summary>
    /// A browser: cookies kept, redirects not followed so a case can read them.
    /// </summary>
    public HttpClient Browser()
    {
        HttpClient client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        return client;
    }

    /// <summary>
    /// Fills in the sign-in form with <paramref name="token" /> and submits it, and returns the answer.
    /// </summary>
    public static async Task<HttpResponseMessage> SignIn(HttpClient browser, string token, string? returnUrl = null)
    {
        string form = await browser.GetStringAsync("/login");
        string antiforgery = WebUtility.HtmlDecode(AntiforgeryField.Match(form).Groups["value"].Value);
        antiforgery.Should().NotBeEmpty("the sign-in form carries an antiforgery token");

        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["token"] = token,
            ["__RequestVerificationToken"] = antiforgery,
        };

        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return await browser.PostAsync("/login", new FormUrlEncodedContent(fields));
    }

    /// <summary>
    /// The sign-in cookie a response set, as a <c>Cookie</c> header carries it.
    /// </summary>
    public static string? SignInCookie(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
            ? cookies.Select(x => x.Split(';')[0]).FirstOrDefault(x => x.StartsWith(HostAuthentication.CookieName + "=", StringComparison.Ordinal))
            : null;
    }

    /// <summary>
    /// The visitor a request carrying <paramref name="cookie" /> is, as the host's own authentication reads
    /// it; with no cookie, whoever the default scheme makes of an anonymous request.
    /// </summary>
    public static async Task<ClaimsPrincipal> Visitor(IServiceProvider scope, string? cookie)
    {
        DefaultHttpContext context = new() { RequestServices = scope };
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = cookie;
        }

        AuthenticateResult result = await context.AuthenticateAsync();
        return result.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
    }

    /// <summary>
    /// The dashboard's client in a scope whose visitor is <paramref name="visitor" />, as a circuit is.
    /// </summary>
    public static Quartz.Dashboard.Services.IQuartzApiClient ClientFor(IServiceProvider scope, ClaimsPrincipal visitor)
    {
        ((IHostEnvironmentAuthenticationStateProvider) scope.GetRequiredService<AuthenticationStateProvider>())
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(visitor)));

        return scope.GetRequiredService<Quartz.Dashboard.Services.IQuartzApiClient>();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}
