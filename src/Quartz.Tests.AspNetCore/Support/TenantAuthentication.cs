using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.AspNetCore.Support;

/// <summary>
/// Authenticates a request as the tenant named in its <c>X-Tenant</c> header, so one test can drive the
/// API as two different tenants over two clients.
/// </summary>
/// <remarks>
/// A header rather than a token because none of what is under test is authentication: the claim is the
/// input to the policy, and how the caller came by it is the application's business.
/// </remarks>
public sealed class TenantAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "tenant";

    public const string TenantHeaderName = "X-Tenant";

    public const string TenantClaimType = "tenant";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeaderName, out Microsoft.Extensions.Primitives.StringValues tenant)
            || string.IsNullOrWhiteSpace(tenant))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, tenant.ToString()), new Claim(TenantClaimType, tenant.ToString())],
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>
/// The requirement the per-scheduler policy carries. It says nothing itself — what "owning" a scheduler
/// means is the handler's to decide — which is why it has no members.
/// </summary>
public sealed class SchedulerOwnerRequirement : IAuthorizationRequirement;

/// <summary>
/// Grants a scheduler to the tenant whose claim names it. The shape the documentation samples show, and
/// the shape both surfaces evaluate: one handler over <see cref="SchedulerResource" />.
/// </summary>
public sealed class SchedulerOwnerHandler : AuthorizationHandler<SchedulerOwnerRequirement, SchedulerResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SchedulerOwnerRequirement requirement,
        SchedulerResource resource)
    {
        string? tenant = context.User.FindFirst(TenantAuthenticationHandler.TenantClaimType)?.Value;
        if (string.Equals(tenant, resource.SchedulerName, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class TenantAuthenticationExtensions
{
    public const string SchedulerOwnerPolicy = "SchedulerOwner";

    /// <summary>
    /// Registers the tenant scheme and the per-scheduler policy the tests configure Quartz with.
    /// </summary>
    public static IServiceCollection AddTenantAuthorization(this IServiceCollection services)
    {
        services
            .AddAuthentication(TenantAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TenantAuthenticationHandler>(TenantAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(SchedulerOwnerPolicy, policy => policy.AddRequirements(new SchedulerOwnerRequirement()));

        services.AddSingleton<IAuthorizationHandler, SchedulerOwnerHandler>();

        return services;
    }
}
