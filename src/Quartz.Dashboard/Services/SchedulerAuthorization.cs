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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Holds <see cref="QuartzDashboardOptions.SchedulerAuthorizationPolicy" /> against one scheduler, for
/// the three places the dashboard is about a scheduler: the picker that lists them, the page frame that
/// renders one, and the hub group that streams one's events.
/// </summary>
/// <remarks>
/// <para>
/// The visitor comes from <see cref="AuthenticationStateProvider" /> rather than from an
/// <c>HttpContext</c>, because a rendered dashboard is a circuit and its request is long gone. The hub
/// has its own caller, so it passes one in.
/// </para>
/// <para>
/// With no policy configured nothing here is asked anything: <see cref="IsEnabled" /> is false and every
/// scheduler passes, which is what keeps a dashboard that never set the option exactly as it was.
/// </para>
/// </remarks>
internal sealed class SchedulerAuthorization
{
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly IAuthorizationService authorizationService;
    private readonly AuthenticationStateProvider authenticationStateProvider;
    private readonly SchedulerState? schedulerState;

    /// <param name="options">The dashboard's options, for the policy name.</param>
    /// <param name="authorizationService">What evaluates the policy.</param>
    /// <param name="authenticationStateProvider">Whose circuit this is.</param>
    /// <param name="schedulerState">
    /// The circuit's listing, when there is one: a key the listing carries is asked about by the halves
    /// the listing has for it. The hub, whose caller is a connection, has none and reads the key.
    /// </param>
    public SchedulerAuthorization(
        IOptions<QuartzDashboardOptions> options,
        IAuthorizationService authorizationService,
        AuthenticationStateProvider authenticationStateProvider,
        SchedulerState? schedulerState = null)
    {
        this.options = options;
        this.authorizationService = authorizationService;
        this.authenticationStateProvider = authenticationStateProvider;
        this.schedulerState = schedulerState;
    }

    /// <summary>
    /// Whether a per-scheduler policy is configured at all. A component reads it to know whether it has
    /// anything to wait for before it renders.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.SchedulerAuthorizationPolicy);

    /// <summary>
    /// Whether the visitor this circuit belongs to may see <paramref name="schedulerName" />. A blank name
    /// is no scheduler at all — the dashboard before its first listing has answered — and passes.
    /// </summary>
    /// <remarks>
    /// A key the circuit's listing carries is asked about by the halves the listing has for it, so a
    /// scheduler of this process whose name contains <c>/</c> is asked about by its name; a key the
    /// listing does not carry is read as <c>target/name</c>. A caller that can resolve the key — the
    /// client, which holds the repository — asks through <see cref="IsAuthorized(SchedulerResource, CancellationToken)" />
    /// with the halves the scheduler actually has.
    /// </remarks>
    public ValueTask<bool> IsAuthorized(string? schedulerName, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(schedulerName))
        {
            return new ValueTask<bool>(true);
        }

        return IsAuthorized(ResourceFor(schedulerName), cancellationToken);
    }

    /// <summary>
    /// Whether the visitor this circuit belongs to may see the scheduler <paramref name="resource" />
    /// names.
    /// </summary>
    public ValueTask<bool> IsAuthorized(SchedulerResource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!IsEnabled)
        {
            return new ValueTask<bool>(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(resource, cancellationToken);

        async ValueTask<bool> Evaluate(SchedulerResource resource, CancellationToken cancellationToken)
        {
            AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            return await IsAuthorized(state.User, resource, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether <paramref name="user" /> may see <paramref name="schedulerName" />, for a caller that
    /// already holds the principal — the hub, whose caller is a connection rather than a circuit.
    /// </summary>
    public ValueTask<bool> IsAuthorized(ClaimsPrincipal user, string? schedulerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            return new ValueTask<bool>(true);
        }

        return IsAuthorized(user, ResourceFor(schedulerName), cancellationToken);
    }

    /// <summary>
    /// The resource a key names: the halves the listing has for it when the listing carries it, and the
    /// key read as <c>target/name</c> otherwise.
    /// </summary>
    private SchedulerResource ResourceFor(string schedulerKey)
    {
        return schedulerState?.Find(schedulerKey) is { } header
            ? new SchedulerResource(header.SchedulerName) { Target = header.Target }
            : SchedulerResource.For(schedulerKey);
    }

    /// <summary>
    /// Whether <paramref name="user" /> may see the scheduler <paramref name="resource" /> names.
    /// </summary>
    public ValueTask<bool> IsAuthorized(ClaimsPrincipal user, SchedulerResource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string? policyName = options.Value.SchedulerAuthorizationPolicy;
        if (string.IsNullOrWhiteSpace(policyName))
        {
            return new ValueTask<bool>(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(user, policyName, resource);

        async ValueTask<bool> Evaluate(ClaimsPrincipal user, string policyName, SchedulerResource resource)
        {
            AuthorizationResult result = await authorizationService
                .AuthorizeAsync(user, resource, policyName)
                .ConfigureAwait(false);

            return result.Succeeded;
        }
    }

    /// <summary>
    /// The schedulers of <paramref name="schedulers" /> the visitor may see, in the order they arrived.
    /// </summary>
    /// <remarks>
    /// The listing is filtered rather than annotated, because a name in the picker is a name the visitor
    /// can select — and the count of tenants in a process is itself something a tenant should not learn.
    /// </remarks>
    public async ValueTask<List<SchedulerHeaderDto>> Filter(
        IReadOnlyList<SchedulerHeaderDto> schedulers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedulers);

        if (!IsEnabled)
        {
            return [.. schedulers];
        }

        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);

        List<SchedulerHeaderDto> allowed = new(schedulers.Count);
        foreach (SchedulerHeaderDto scheduler in schedulers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The resource is built from the header's own halves rather than read back out of its key:
            // a scheduler of this process whose name contains '/' is asked about by that name, as it
            // always was, and only a scheduler reached through a target carries one.
            SchedulerResource resource = new(scheduler.SchedulerName) { Target = scheduler.Target };
            if (await IsAuthorized(state.User, resource, cancellationToken).ConfigureAwait(false))
            {
                allowed.Add(scheduler);
            }
        }

        return allowed;
    }
}
