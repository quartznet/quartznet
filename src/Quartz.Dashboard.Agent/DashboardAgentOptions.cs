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

using Microsoft.AspNetCore.Http.Connections.Client;

namespace Quartz;

/// <summary>
/// How a scheduler dials out to a dashboard, and what it lets the dashboard do once it has.
/// </summary>
/// <remarks>
/// <para>
/// The agent is a plugin: <c>UseDashboardAgent</c> installs it on one scheduler, and that scheduler opens
/// one outbound connection to <see cref="Endpoint" /> — the dashboard's agent hub — and keeps it open for
/// as long as it runs. The dashboard then lists the scheduler as <see cref="SchedulerOrigin.Agent" />,
/// under the key <c>{Target}/{scheduler name}</c>, and drives it through that connection: every page, every
/// action, the execution history and the live events.
/// </para>
/// <para>
/// The connection crosses a trust boundary by design, and the narrowings here are the only ones that hold
/// against a dashboard that has been taken over: <see cref="ReadOnly" />, <see cref="IsOperationAllowed" />
/// and <see cref="IsJobTypeAllowed" /> are the agent's own decisions, made in the worker's process, and
/// nothing the dashboard sends can widen them. The dashboard's own <c>ReadOnly</c> hides its buttons; it
/// does not bind an agent.
/// </para>
/// </remarks>
public sealed class DashboardAgentOptions
{
    /// <summary>
    /// The dashboard's agent hub: the dashboard's path followed by <c>/agents</c>, for example
    /// <c>https://ops.example.com/quartz/agents</c>. Required.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// The bearer token the dashboard accepts — its <c>AcceptAgents(a => a.Tokens.Primary = …)</c>, or the
    /// secondary one while a rotation is under way. Required unless <see cref="AccessTokenProvider" /> is set.
    /// </summary>
    /// <remarks>
    /// Sent as an <c>Authorization: Bearer</c> header on every connection, never in a URL.
    /// </remarks>
    public string? Token { get; set; }

    /// <summary>
    /// Where the bearer token comes from when it is the host's own credential rather than a shared secret:
    /// a managed identity, an OIDC client-credentials flow. Asked before every connection, so a token that
    /// expires is renewed.
    /// </summary>
    public Func<CancellationToken, ValueTask<string?>>? AccessTokenProvider { get; set; }

    /// <summary>
    /// The name this process registers under, which is the first half of its scheduler's key on the
    /// dashboard. Defaults to <see cref="Environment.MachineName" />. Cannot contain <c>/</c> or <c>+</c>.
    /// </summary>
    /// <remarks>
    /// Two processes cannot share a target: the second to connect is refused until the first is gone, and
    /// an agent that reconnects under the same target and the same scheduler instance id takes its own
    /// entry over. Give each process a target of its own where the machine name is not one — containers
    /// that restart under new names, or several workers on one machine.
    /// </remarks>
    public string? Target { get; set; }

    /// <summary>
    /// Whether the agent refuses every operation that changes something. <see langword="false" /> by
    /// default. A refused operation is <c>403</c> to the dashboard, which shows the refusal.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Which job types a request through this agent may name. <see langword="null" /> — the default —
    /// refuses every one: an agent crosses a trust boundary, and with <c>Quartz.Jobs</c> on the probing
    /// path a dashboard that may name any <see cref="IJob" /> may run any executable on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the operations that store a job <em>by type name</em> are refused — add-job, schedule-job with
    /// a job, and schedule-jobs. Triggering, pausing, rescheduling and deleting a job the scheduler already
    /// holds work with the predicate unset. The refusal is <c>403</c> with problem details naming this
    /// option as the remedy.
    /// </para>
    /// <para>
    /// The predicate is given the job type name as it was written, and nothing resolves it first — the
    /// name is data until the scheduler loads it. Match on the string, and remember that one type has more
    /// than one spelling: <c>Acme.Jobs.Nightly, Acme.Jobs</c> and the same name carrying <c>Version</c>,
    /// <c>Culture</c> and <c>PublicKeyToken</c> are both it. A <c>StartsWith</c> on the namespace covers
    /// every spelling at once. The HTTP API's <c>QuartzHttpApiOptions.IsJobTypeAllowed</c> is the same
    /// predicate with the opposite default, because that surface is on the application's own network.
    /// </para>
    /// </remarks>
    public Func<string, bool>? IsJobTypeAllowed { get; set; }

    /// <summary>
    /// Which operations the agent accepts, by route name — <c>Shutdown</c>, <c>PauseJob</c>,
    /// <c>ScheduleJob</c>, the names the HTTP API publishes as its operation ids. <see langword="null" />
    /// — the default — accepts every one. A refused operation is <c>403</c>.
    /// </summary>
    public Func<string, bool>? IsOperationAllowed { get; set; }

    /// <summary>
    /// How often the agent tells the dashboard it is alive. Fifteen seconds by default; the dashboard may
    /// ask for another interval when it accepts the registration, and the agent adopts it.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How many of the dashboard's requests run at once. Four by default. The rest queue.
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 4;

    /// <summary>
    /// The most items one paged request may return: 1000 by default, and <c>0</c> for no limit, as
    /// <c>QuartzHttpApiOptions.MaxPageSize</c> bounds the HTTP API.
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// Configures the connection the agent dials out on: a proxy, a client certificate, which transports
    /// to try, or the handler a test routes it through.
    /// </summary>
    public Action<HttpConnectionOptions>? ConfigureConnection { get; set; }
}
