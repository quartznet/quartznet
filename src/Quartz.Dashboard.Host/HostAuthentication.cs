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

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quartz.Dashboard.Host;

/// <summary>
/// How a visitor signs in, in each of the three modes, and the two policies the dashboard is held to.
/// </summary>
/// <remarks>
/// <para>
/// Every mode ends in the same place: a principal carrying roles, and
/// <see cref="SchedulerRolesAuthorizationExtensions.RequireSchedulerRoles" /> deciding what each role may
/// do. <see cref="VisitorPolicy" /> is the dashboard's <c>AuthorizationPolicy</c> — signed in, and in at
/// least one role — and <see cref="OperationPolicy" /> its <c>OperationAuthorizationPolicy</c>.
/// </para>
/// <para>
/// <c>Token</c> mode signs a visitor in with a cookie for the role whose token they presented: HttpOnly,
/// SameSite=Strict, and protected by the data protection keys. <c>OpenIdConnect</c> mode's cookie is
/// SameSite=Lax instead, because the identity provider sends the browser back from another site and a
/// strict cookie set on that redirect is not sent on the next request, which challenges again forever.
/// </para>
/// </remarks>
internal static class HostAuthentication
{
    internal const string VisitorPolicy = "Quartz.Dashboard.Host.Visitor";

    internal const string OperationPolicy = "Quartz.Dashboard.Host.Operations";

    internal const string CookieScheme = "Quartz.Dashboard.Host";

    internal const string UnsecuredScheme = "Quartz.Dashboard.Host.Unsecured";

    internal const string CookieName = "quartz-dashboard";

    internal const string LoginPath = "/login";

    internal const string LogoutPath = "/logout";

    /// <summary>The form field, and the query parameter, a token arrives in.</summary>
    internal const string TokenField = "token";

    internal const string TokenQuery = "t";

    internal const string ReturnUrlField = "ReturnUrl";

    public static void AddHostAuthentication(this IServiceCollection services, DashboardHostPlan plan)
    {
        AuthenticationSettings settings = plan.Settings.Authentication;

        switch (plan.Mode)
        {
            case HostAuthenticationMode.Token:
                services.AddAuthentication(CookieScheme)
                    .AddCookie(CookieScheme, cookie => ConfigureCookie(cookie, SameSiteMode.Strict, loginPath: LoginPath));
                services.AddSingleton(new TokenRoles(settings.Tokens));
                break;

            case HostAuthenticationMode.OpenIdConnect:
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = CookieScheme;
                        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                    })
                    .AddCookie(CookieScheme, cookie => ConfigureCookie(cookie, SameSiteMode.Lax, loginPath: null))
                    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, oidc => ConfigureOpenIdConnect(oidc, settings));
                break;

            case HostAuthenticationMode.Unsecured:
                services.AddAuthentication(UnsecuredScheme)
                    .AddScheme<UnsecuredAuthenticationOptions, UnsecuredAuthenticationHandler>(UnsecuredScheme, options => options.Access = plan.UnsecuredAccess);
                break;
        }

        (string[] viewers, string[] operators, string[] administrators) = Roles(plan);
        string[] everyRole = [.. viewers, .. operators, .. administrators];

        services.AddAuthorization(options =>
        {
            options.AddPolicy(VisitorPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(everyRole));
            options.AddPolicy(OperationPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireSchedulerRoles(roles =>
                {
                    roles.Viewers.AddRange(viewers);
                    roles.Operators.AddRange(operators);
                    roles.Administrators.AddRange(administrators);
                    foreach ((string operation, SchedulerAccess access) in plan.Settings.Roles.Operations)
                    {
                        roles.Operations[operation] = access;
                    }
                }));

            // Anything mapped without a policy of its own still needs a signed-in visitor: the health
            // probe, the sign-in page and the redirect at the root say AllowAnonymous out loud.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
    }

    /// <summary>
    /// The role names each operator role is granted by: the host's own in <c>Token</c> and
    /// <c>Unsecured</c> mode, the identity provider's in <c>OpenIdConnect</c> mode.
    /// </summary>
    internal static (string[] Viewers, string[] Operators, string[] Administrators) Roles(DashboardHostPlan plan)
    {
        if (plan.Mode == HostAuthenticationMode.OpenIdConnect)
        {
            RoleSettings roles = plan.Settings.Roles;
            return ([.. roles.Viewers], [.. roles.Operators], [.. roles.Administrators]);
        }

        return ([DashboardHostPlan.ViewerRole], [DashboardHostPlan.OperatorRole], [DashboardHostPlan.AdministratorRole]);
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie, SameSiteMode sameSite, string? loginPath)
    {
        cookie.Cookie.Name = CookieName;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = sameSite;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
        cookie.SlidingExpiration = true;

        if (loginPath is not null)
        {
            cookie.LoginPath = loginPath;
        }

        cookie.LogoutPath = LogoutPath;

        cookie.Events.OnRedirectToLogin = context =>
        {
            // A page is sent to sign in; the circuit, the hub and the assets are told 401, which is what a
            // script makes sense of.
            if (IsPageRequest(context.Request))
            {
                context.Response.Redirect(context.RedirectUri);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }

            return Task.CompletedTask;
        };

        cookie.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // The sign-in endpoints say where the visitor goes next. Left to the handler, a sign-in on the login
        // path follows a ReturnUrl in the query with a 302 of its own, which is the redirect a strict cookie
        // set on a link from another site is not sent on.
        cookie.Events.OnRedirectToReturnUrl = static _ => Task.CompletedTask;
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions oidc, AuthenticationSettings settings)
    {
        oidc.SignInScheme = CookieScheme;
        oidc.Authority = settings.Authority;
        oidc.ClientId = settings.ClientId;
        oidc.ClientSecret = settings.ClientSecret;
        oidc.ResponseType = "code";
        oidc.UsePkce = true;
        oidc.SaveTokens = false;
        oidc.MapInboundClaims = false;
        oidc.RequireHttpsMetadata = settings.RequireHttpsMetadata ?? true;
        oidc.TokenValidationParameters.NameClaimType = string.IsNullOrWhiteSpace(settings.NameClaimType) ? "name" : settings.NameClaimType;
        oidc.TokenValidationParameters.RoleClaimType = string.IsNullOrWhiteSpace(settings.RoleClaimType) ? "roles" : settings.RoleClaimType;

        if (settings.Scopes.Count > 0)
        {
            oidc.Scope.Clear();
            foreach (string scope in settings.Scopes)
            {
                oidc.Scope.Add(scope);
            }
        }
    }

    private static bool IsPageRequest(HttpRequest request)
    {
        return HttpMethods.IsGet(request.Method)
               && request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps the sign-in form, the sign-out, and the token-in-the-address link of <c>Token</c> mode.
    /// </summary>
    public static void MapHostAuthentication(this WebApplication app, DashboardHostPlan plan, string dashboardPath)
    {
        if (plan.Mode != HostAuthenticationMode.Token)
        {
            app.MapGet(LogoutPath, async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieScheme).ConfigureAwait(false);
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(DashboardHostLog.Category).SignedOut();
                return Results.Redirect(context.Request.PathBase + dashboardPath);
            }).AllowAnonymous();
            return;
        }

        bool allowTokenInUrl = plan.Settings.Authentication.AllowTokenInUrl;

        app.MapGet(LoginPath, async (HttpContext context, IAntiforgery antiforgery, TokenRoles roles, ILoggerFactory loggers) =>
        {
            ILogger logger = loggers.CreateLogger(DashboardHostLog.Category);
            string returnUrl = LocalReturnUrl(context.Request.Query[ReturnUrlField], context.Request.PathBase + dashboardPath);

            if (!context.Request.Query.TryGetValue(TokenQuery, out Microsoft.Extensions.Primitives.StringValues presented))
            {
                return LoginPage(context, antiforgery, returnUrl, message: null, StatusCodes.Status200OK);
            }

            if (!allowTokenInUrl)
            {
                logger.TokenInUrlRefused();
                return LoginPage(context, antiforgery, returnUrl,
                    "This dashboard does not take a token in its address. Sign in below.", StatusCodes.Status400BadRequest);
            }

            string? role = roles.Match(presented.ToString());
            if (role is null)
            {
                logger.SignInRefused("link");
                return LoginPage(context, antiforgery, returnUrl, "That token is not valid.", StatusCodes.Status401Unauthorized);
            }

            await SignIn(context, role).ConfigureAwait(false);
            logger.SignedIn(role, "link");

            // Not a 302: a strict cookie set on a navigation that started on another site is not sent on a
            // redirect that continues it, and the visitor would land on this form again. A page that moves
            // itself on is a navigation of this site's own. Either way the token leaves the address bar.
            return Results.Content(RedirectPage(returnUrl), "text/html; charset=utf-8");
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost(LoginPath, async (HttpContext context, IAntiforgery antiforgery, TokenRoles roles, ILoggerFactory loggers) =>
        {
            ILogger logger = loggers.CreateLogger(DashboardHostLog.Category);

            if (!context.Request.HasFormContentType || !await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false))
            {
                return Results.BadRequest();
            }

            IFormCollection form = await context.Request.ReadFormAsync().ConfigureAwait(false);
            string returnUrl = LocalReturnUrl(form[ReturnUrlField], context.Request.PathBase + dashboardPath);

            string? role = roles.Match(form[TokenField].ToString());
            if (role is null)
            {
                logger.SignInRefused("form");
                return LoginPage(context, antiforgery, returnUrl, "That token is not valid.", StatusCodes.Status401Unauthorized);
            }

            await SignIn(context, role).ConfigureAwait(false);
            logger.SignedIn(role, "form");

            return Results.Redirect(returnUrl);
        }).AllowAnonymous().DisableAntiforgery();

        app.MapGet(LogoutPath, async (HttpContext context, ILoggerFactory loggers) =>
        {
            await context.SignOutAsync(CookieScheme).ConfigureAwait(false);
            loggers.CreateLogger(DashboardHostLog.Category).SignedOut();
            return Results.Redirect(context.Request.PathBase + LoginPath);
        }).AllowAnonymous();
    }

    private static Task SignIn(HttpContext context, string role)
    {
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, role.ToLowerInvariant()), new Claim(ClaimTypes.Role, role)],
            CookieScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return context.SignInAsync(CookieScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// The address to go on to after signing in: one on this site, or the dashboard. Anything else — an
    /// absolute URL, a protocol-relative one, a backslash a browser reads as a slash — is an open redirect.
    /// </summary>
    internal static string LocalReturnUrl(string? candidate, string fallback)
    {
        if (string.IsNullOrEmpty(candidate)
            || candidate[0] != '/'
            || (candidate.Length > 1 && (candidate[1] == '/' || candidate[1] == '\\'))
            || candidate.Contains('\\', StringComparison.Ordinal))
        {
            return fallback;
        }

        return candidate;
    }

    private static IResult LoginPage(HttpContext context, IAntiforgery antiforgery, string returnUrl, string? message, int statusCode)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        HtmlEncoder html = HtmlEncoder.Default;

        string error = message is null ? "" : $"<p class=\"error\" role=\"alert\">{html.Encode(message)}</p>";

        string body = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="referrer" content="no-referrer">
            <link rel="icon" href="data:,">
            <title>Sign in - Quartz.NET Dashboard</title>
            <style>
            body { font-family: system-ui, sans-serif; background: #f5f6f8; display: flex; min-height: 100vh; align-items: center; justify-content: center; margin: 0; }
            form { background: #fff; padding: 2rem; border-radius: .5rem; box-shadow: 0 1px 4px rgba(0,0,0,.15); width: 22rem; }
            h1 { font-size: 1.25rem; margin: 0 0 1rem; }
            label { display: block; margin-bottom: .25rem; }
            input[type=password] { width: 100%; box-sizing: border-box; padding: .5rem; margin-bottom: 1rem; }
            button { padding: .5rem 1rem; }
            .error { color: #b00020; }
            </style>
            </head>
            <body>
            <form method="post" action="{{html.Encode(context.Request.PathBase + LoginPath)}}">
            <h1>Quartz.NET Dashboard</h1>
            {{error}}
            <label for="token">Token</label>
            <input id="token" name="{{TokenField}}" type="password" autocomplete="current-password" required autofocus>
            <input type="hidden" name="{{html.Encode(tokens.FormFieldName)}}" value="{{html.Encode(tokens.RequestToken ?? "")}}">
            <input type="hidden" name="{{ReturnUrlField}}" value="{{html.Encode(returnUrl)}}">
            <button type="submit">Sign in</button>
            </form>
            </body>
            </html>
            """;

        context.Response.Headers.CacheControl = "no-store";
        return Results.Content(body, "text/html; charset=utf-8", Encoding.UTF8, statusCode);
    }

    private static string RedirectPage(string returnUrl)
    {
        string target = HtmlEncoder.Default.Encode(returnUrl);
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="referrer" content="no-referrer">
            <meta http-equiv="refresh" content="0;url={target}">
            <title>Signed in - Quartz.NET Dashboard</title>
            </head>
            <body><p>Signed in. <a href="{target}">Continue to the dashboard</a>.</p></body>
            </html>
            """;
    }
}

/// <summary>
/// The tokens of <c>Token</c> mode, compared in constant time.
/// </summary>
/// <remarks>
/// Each token is held as its SHA-256 digest and a presented token is hashed before it is compared, so the
/// comparison takes the same time whatever the presented token's length, and every role is compared every
/// time so the time does not say which role came closest.
/// </remarks>
internal sealed class TokenRoles
{
    private readonly (string Role, byte[] Digest)[] tokens;

    public TokenRoles(RoleTokenSettings settings)
    {
        List<(string Role, byte[] Digest)> given = [];
        Add(given, DashboardHostPlan.ViewerRole, settings.Viewer);
        Add(given, DashboardHostPlan.OperatorRole, settings.Operator);
        Add(given, DashboardHostPlan.AdministratorRole, settings.Administrator);
        tokens = [.. given];
    }

    private static void Add(List<(string Role, byte[] Digest)> given, string role, string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            given.Add((role, SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        }
    }

    /// <summary>
    /// The role <paramref name="presented" /> signs a visitor in at, or <see langword="null" />.
    /// </summary>
    public string? Match(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return null;
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        string? matched = null;

        foreach ((string role, byte[] expected) in tokens)
        {
            if (CryptographicOperations.FixedTimeEquals(digest, expected))
            {
                matched = role;
            }
        }

        return matched;
    }
}

internal sealed class UnsecuredAuthenticationOptions : AuthenticationSchemeOptions
{
    public SchedulerAccess Access { get; set; } = SchedulerAccess.View;
}

/// <summary>
/// <c>Unsecured</c> mode: every request is the same anonymous visitor, at the one role
/// <c>UnsecuredAccess</c> names.
/// </summary>
internal sealed class UnsecuredAuthenticationHandler(
    IOptionsMonitor<UnsecuredAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<UnsecuredAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string role = Options.Access == SchedulerAccess.Operate ? DashboardHostPlan.OperatorRole : DashboardHostPlan.ViewerRole;

        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, "anonymous"), new Claim(ClaimTypes.Role, role)],
            HostAuthentication.UnsecuredScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
