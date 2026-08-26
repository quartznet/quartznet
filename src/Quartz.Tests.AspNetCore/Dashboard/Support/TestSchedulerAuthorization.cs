using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace Quartz.Tests.AspNetCore.Dashboard.Support;

/// <summary>
/// An <see cref="IAuthorizationService" /> that answers for a fixed set of scheduler names, so a test can
/// say which tenant the visitor is without writing a policy and a handler for it.
/// </summary>
/// <remarks>
/// It records what it was asked, because the resource and the policy name are half of what the dashboard
/// promises: one <c>AuthorizationHandler&lt;TRequirement, SchedulerResource&gt;</c> answers for every
/// scheduler-scoped decision. The end-to-end shape — a real policy, a real handler — is exercised against
/// the HTTP API, where the container is a real one.
/// </remarks>
internal sealed class TestSchedulerAuthorizationService : IAuthorizationService
{
    /// <summary>
    /// The schedulers the visitor passes for. Compared ignoring case, the way every scheduler lookup in
    /// Quartz is.
    /// </summary>
    public HashSet<string> Allowed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every (policy, resource) pair this service was asked about, in order.
    /// </summary>
    public List<(string PolicyName, object? Resource)> Asked { get; } = [];

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
    {
        throw new NotSupportedException("The dashboard evaluates a named policy, never a bare requirement set.");
    }

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
    {
        Asked.Add((policyName, resource));

        bool succeeded = resource is SchedulerResource scheduler && Allowed.Contains(scheduler.SchedulerName);
        return Task.FromResult(succeeded ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}

/// <summary>
/// The visitor a rendered dashboard belongs to. The real one is the circuit's, seeded by the framework
/// from the request that started it; bUnit renders no circuit, so a test supplies the principal itself.
/// </summary>
internal sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
{
    public ClaimsPrincipal User { get; set; } = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(new AuthenticationState(User));
    }
}
