using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.AspNetCore.Support;

/// <summary>
/// Authenticates a request as a user holding the role named in its <c>X-Role</c> header, so one test can
/// drive the API as a viewer, an operator and an administrator over three clients.
/// </summary>
/// <remarks>
/// A header rather than a token for the reason <see cref="TenantAuthenticationHandler" /> gives: the role
/// claim is the input to the policy, and how the caller came by it is the application's business. A
/// request with no header is anonymous; one whose header is <c>nobody</c> is a signed-in user in no role.
/// </remarks>
public sealed class RoleAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "role";

    public const string RoleHeaderName = "X-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeaderName, out Microsoft.Extensions.Primitives.StringValues role)
            || string.IsNullOrWhiteSpace(role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, role + "-user"), new Claim(ClaimTypes.Role, role.ToString())],
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class RoleAuthenticationExtensions
{
    public const string OperatorRolesPolicy = "QuartzOperators";

    public const string Viewer = "quartz-viewer";

    public const string Operator = "quartz-operator";

    public const string Administrator = "quartz-admin";

    /// <summary>
    /// Registers the role scheme and an operation policy over the three roles, with whatever overrides
    /// <paramref name="configure" /> adds.
    /// </summary>
    public static IServiceCollection AddRoleAuthorization(this IServiceCollection services, Action<SchedulerRoles>? configure = null)
    {
        services
            .AddAuthentication(RoleAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleAuthenticationHandler>(RoleAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(OperatorRolesPolicy, policy => policy.RequireSchedulerRoles(roles =>
            {
                roles.Viewers.Add(Viewer);
                roles.Operators.Add(Operator);
                roles.Administrators.Add(Administrator);
                configure?.Invoke(roles);
            }));

        return services;
    }
}
