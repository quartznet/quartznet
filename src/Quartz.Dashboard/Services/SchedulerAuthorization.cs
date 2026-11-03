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

using System.Collections.Concurrent;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Holds <see cref="QuartzDashboardOptions.SchedulerAuthorizationPolicy" /> against one scheduler, for
/// the three places the dashboard is about a scheduler: the picker that lists them, the page frame that
/// renders one, and the hub group that streams one's events — and, from 4.8,
/// <see cref="QuartzDashboardOptions.OperationAuthorizationPolicy" /> against each operation on it.
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
/// <para>
/// Seeing a scheduler means passing both policies: the scheduler policy for it, and the operation policy at
/// <see cref="SchedulerAccess.View" />. The operation policy's answers are kept for the circuit's life,
/// because a page decides which controls to render synchronously and a circuit's visitor does not change;
/// <see cref="LoadOperations" /> asks every operation of a scheduler once, and <see cref="Allows" /> reads
/// the answers.
/// </para>
/// </remarks>
internal sealed class SchedulerAuthorization
{
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly IAuthorizationService authorizationService;
    private readonly AuthenticationStateProvider authenticationStateProvider;
    private readonly SchedulerState? schedulerState;
    private readonly AgentRegistry? agents;

    /// <summary>
    /// The operation policy's answers for this circuit's visitor, by scheduler, operation and level.
    /// </summary>
    private readonly ConcurrentDictionary<OperationQuestion, bool> operationAnswers = new();

    /// <param name="options">The dashboard's options, for the policy name.</param>
    /// <param name="authorizationService">What evaluates the policy.</param>
    /// <param name="authenticationStateProvider">Whose circuit this is.</param>
    /// <param name="schedulerState">
    /// The circuit's listing, when there is one: a key the listing carries is asked about by the halves
    /// the listing has for it. The hub, whose caller is a connection, has none and reads the key.
    /// </param>
    /// <param name="agents">
    /// The agents this dashboard accepts, whose <c>MaxAccess</c> bounds what a visitor may do on their
    /// schedulers; <see langword="null" /> where there is no registry to ask.
    /// </param>
    public SchedulerAuthorization(
        IOptions<QuartzDashboardOptions> options,
        IAuthorizationService authorizationService,
        AuthenticationStateProvider authenticationStateProvider,
        SchedulerState? schedulerState = null,
        AgentRegistry? agents = null)
    {
        this.options = options;
        this.authorizationService = authorizationService;
        this.authenticationStateProvider = authenticationStateProvider;
        this.schedulerState = schedulerState;
        this.agents = agents;
    }

    /// <summary>
    /// Whether a per-scheduler or an operation policy is configured at all. A component reads it to know
    /// whether it has anything to wait for before it renders.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.SchedulerAuthorizationPolicy) || OperationsAuthorized;

    /// <summary>
    /// Whether <see cref="QuartzDashboardOptions.OperationAuthorizationPolicy" /> is configured.
    /// </summary>
    public bool OperationsAuthorized => !string.IsNullOrWhiteSpace(options.Value.OperationAuthorizationPolicy);

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
    /// Whether <paramref name="user" /> may see the scheduler <paramref name="resource" /> names: the
    /// scheduler policy passes for it, and the operation policy at <see cref="SchedulerAccess.View" />.
    /// </summary>
    public ValueTask<bool> IsAuthorized(ClaimsPrincipal user, SchedulerResource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string? policyName = options.Value.SchedulerAuthorizationPolicy;
        if (string.IsNullOrWhiteSpace(policyName) && !OperationsAuthorized)
        {
            return new ValueTask<bool>(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(user, policyName, resource);

        async ValueTask<bool> Evaluate(ClaimsPrincipal user, string? policyName, SchedulerResource resource)
        {
            if (!string.IsNullOrWhiteSpace(policyName))
            {
                AuthorizationResult result = await authorizationService
                    .AuthorizeAsync(user, resource, policyName)
                    .ConfigureAwait(false);

                if (!result.Succeeded)
                {
                    return false;
                }
            }

            return await IsAllowed(user, resource, operation: null, SchedulerAccess.View).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks every operation that changes something on <paramref name="schedulerKey" />, once per circuit,
    /// so <see cref="Allows" /> can answer while a page renders. Asks nothing without an operation policy.
    /// </summary>
    public async ValueTask LoadOperations(string? schedulerKey, CancellationToken cancellationToken = default)
    {
        if (!OperationsAuthorized || string.IsNullOrWhiteSpace(schedulerKey))
        {
            return;
        }

        SchedulerResource resource = ResourceFor(schedulerKey);
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);

        foreach (WireRoute route in SchedulerRoutes.All)
        {
            if (route.Mutates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IsAllowed(state.User, resource, route.Name, route.Access).ConfigureAwait(false);
            }
        }

        await IsAllowed(state.User, resource, SchedulerRoutes.TriggerJob.Name, SchedulerRoutes.RequiredAccess(SchedulerRoutes.TriggerJob, carriesJobData: true))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the visitor may call <paramref name="route" /> on <paramref name="schedulerKey" />, from what
    /// is known without asking anything: the controls a page renders. A read is always allowed here; the
    /// frame has already decided whether the scheduler may be seen.
    /// </summary>
    /// <remarks>
    /// <see cref="QuartzDashboardOptions.ReadOnly" /> refuses every change. Then the scheduler's own
    /// ceiling — an agent's <c>MaxAccess</c> — and then the operation policy's answer, which is a refusal
    /// until <see cref="LoadOperations" /> has asked it: a control that appears once the answer is in is
    /// better than one that is offered and then refused. With neither a policy nor a ceiling below the
    /// level, this is what <c>!ReadOnly</c> always was.
    /// </remarks>
    public bool Allows(string? schedulerKey, WireRoute route, bool carriesJobData = false)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (!route.Mutates)
        {
            return true;
        }

        if (options.Value.ReadOnly)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(schedulerKey))
        {
            return !OperationsAuthorized;
        }

        SchedulerAccess required = SchedulerRoutes.RequiredAccess(route, carriesJobData);
        SchedulerResource resource = ResourceFor(schedulerKey);
        if (Ceiling(resource) < required)
        {
            return false;
        }

        return !OperationsAuthorized
               || operationAnswers.TryGetValue(new OperationQuestion(resource.SchedulerName, resource.Target, route.Name, required), out bool allowed) && allowed;
    }

    /// <summary>
    /// Refuses <paramref name="route" /> on the scheduler <paramref name="identity" /> names when the
    /// visitor's role does not allow it, before anything reaches the scheduler. Asks nothing without an
    /// operation policy.
    /// </summary>
    /// <exception cref="OperationRefusedException">The policy refused.</exception>
    public async ValueTask EnsureAllowed(SchedulerRef identity, WireRoute route, bool carriesJobData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (!OperationsAuthorized)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        SchedulerAccess required = SchedulerRoutes.RequiredAccess(route, carriesJobData);
        SchedulerResource resource = new(identity.SchedulerName) { Target = identity.Target };
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);

        if (!await IsAllowed(state.User, resource, route.Name, required).ConfigureAwait(false))
        {
            bool raisedByData = required > route.Access;
            throw new OperationRefusedException(route.Name + (raisedByData ? " with job data" : ""), identity.Key, required);
        }
    }

    /// <summary>
    /// The most the scheduler <paramref name="resource" /> names lets anybody do: an agent's
    /// <c>MaxAccess</c>, and <see cref="SchedulerAccess.Administer" /> for every other scheduler, whose
    /// bound — an HTTP target's own policy — the dashboard cannot read.
    /// </summary>
    private SchedulerAccess Ceiling(SchedulerResource resource)
    {
        return resource.Target is { } target && agents?.Find(target) is { } agent
            ? agent.MaxAccess
            : SchedulerAccess.Administer;
    }

    /// <summary>
    /// The operation policy's answer for one question, asked once per circuit, or <see langword="true" />
    /// with no policy configured.
    /// </summary>
    private async ValueTask<bool> IsAllowed(ClaimsPrincipal user, SchedulerResource resource, string? operation, SchedulerAccess access)
    {
        string? policyName = options.Value.OperationAuthorizationPolicy;
        if (string.IsNullOrWhiteSpace(policyName))
        {
            return true;
        }

        OperationQuestion question = new(resource.SchedulerName, resource.Target, operation, access);
        if (operationAnswers.TryGetValue(question, out bool known))
        {
            return known;
        }

        AuthorizationResult result = await authorizationService
            .AuthorizeAsync(user, resource with { Operation = operation, Access = access }, policyName)
            .ConfigureAwait(false);

        operationAnswers[question] = result.Succeeded;
        return result.Succeeded;
    }

    /// <summary>
    /// One question the operation policy is asked: a scheduler by its halves, an operation — null for
    /// seeing the scheduler at all — and the level.
    /// </summary>
    private readonly record struct OperationQuestion(string SchedulerName, string? Target, string? Operation, SchedulerAccess Access);

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
