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

    public SchedulerAuthorization(
        IOptions<QuartzDashboardOptions> options,
        IAuthorizationService authorizationService,
        AuthenticationStateProvider authenticationStateProvider)
    {
        this.options = options;
        this.authorizationService = authorizationService;
        this.authenticationStateProvider = authenticationStateProvider;
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
    public ValueTask<bool> IsAuthorized(string? schedulerName, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(schedulerName))
        {
            return new ValueTask<bool>(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(schedulerName, cancellationToken);

        async ValueTask<bool> Evaluate(string schedulerName, CancellationToken cancellationToken)
        {
            AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            return await IsAuthorized(state.User, schedulerName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether <paramref name="user" /> may see <paramref name="schedulerName" />, for a caller that
    /// already holds the principal — the hub, whose caller is a connection rather than a circuit.
    /// </summary>
    public ValueTask<bool> IsAuthorized(ClaimsPrincipal user, string? schedulerName, CancellationToken cancellationToken = default)
    {
        string? policyName = options.Value.SchedulerAuthorizationPolicy;
        if (string.IsNullOrWhiteSpace(policyName) || string.IsNullOrWhiteSpace(schedulerName))
        {
            return new ValueTask<bool>(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(user, policyName, schedulerName);

        async ValueTask<bool> Evaluate(ClaimsPrincipal user, string policyName, string schedulerName)
        {
            AuthorizationResult result = await authorizationService
                .AuthorizeAsync(user, new SchedulerResource(schedulerName), policyName)
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
            if (await IsAuthorized(state.User, scheduler.SchedulerName, cancellationToken).ConfigureAwait(false))
            {
                allowed.Add(scheduler);
            }
        }

        return allowed;
    }
}
