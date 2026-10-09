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

using System.Security.Cryptography;
using System.Text;

namespace Quartz;

/// <summary>
/// How the dashboard accepts agents: schedulers in other processes that dial out to it rather than being
/// dialled.
/// </summary>
/// <remarks>
/// <para>
/// Configured through <see cref="QuartzDashboardOptions.AcceptAgents" />, which is also what maps the
/// agent hub at <c>{DashboardPath}/agents</c>. An agent authenticates with a bearer token from
/// <see cref="Tokens" />, with the host's own authentication through <see cref="AuthorizationPolicy" />,
/// or with both; a hub with neither refuses to start, as the dashboard's own pages do.
/// </para>
/// <para>
/// The dashboard judges an agent's liveness by its heartbeats: one that misses
/// <see cref="OfflineAfterMissedHeartbeats" /> of them in a row is listed as
/// <see cref="SchedulerStatus.Unknown" /> with the time it was last heard from, and one that stays away
/// for <see cref="ForgetAfter" /> is forgotten. Everything else about an agent's scheduler is the agent's
/// own decision: the dashboard's <see cref="QuartzDashboardOptions.ReadOnly" /> hides its buttons, and
/// the agent's <c>DashboardAgentOptions</c> decide what it will do.
/// </para>
/// </remarks>
public sealed class DashboardAgentHubOptions
{
    /// <summary>
    /// The bearer tokens the hub accepts, Quartz's own scheme. Set at least <see cref="AgentTokens.Primary" />
    /// to authenticate agents this way.
    /// </summary>
    public AgentTokens Tokens { get; } = new();

    /// <summary>
    /// The authorization policy the hub is held to, for an agent that authenticates with the host's own
    /// scheme — a client certificate, a managed identity, an OIDC client credential. Null — the default
    /// — applies none. With <see cref="Tokens" /> set as well, both apply.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// How often the hub expects a heartbeat from each agent, which the agent adopts when it registers.
    /// Fifteen seconds by default.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How many heartbeats an agent may miss in a row before its scheduler is listed as
    /// <see cref="SchedulerStatus.Unknown" />. Three by default: 45 seconds of silence.
    /// </summary>
    public int OfflineAfterMissedHeartbeats { get; set; } = 3;

    /// <summary>
    /// How long the dashboard waits for an agent to answer one request before it cancels the request on
    /// both sides. Thirty seconds by default.
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The most bytes one answer may carry, stated to the agent when it registers; the agent refuses a
    /// larger page itself with <c>413</c>, so a listing too big for the channel says to lower <c>take</c>
    /// rather than tearing the connection down. Four megabytes by default.
    /// </summary>
    public int MaxMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// How long a disconnected agent stays listed — as <see cref="SchedulerStatus.Unknown" />, or as
    /// <see cref="SchedulerStatus.Shutdown" /> after it said goodbye — before the dashboard forgets it.
    /// One hour by default.
    /// </summary>
    public TimeSpan ForgetAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Which job types the dashboard will send to an agent at all, on top of what the agent itself
    /// accepts. Null — the default — sends every request on and leaves the decision to the agent's
    /// <c>DashboardAgentOptions.IsJobTypeAllowed</c>, which refuses every type until it is set.
    /// </summary>
    /// <remarks>
    /// A narrowing on this side binds nothing on the worker — the agent's own predicate does — and exists
    /// so that an operator can stop a job type at the dashboard without touching every worker.
    /// </remarks>
    public Func<string, bool>? IsJobTypeAllowed { get; set; }
}

/// <summary>
/// The bearer tokens a dashboard accepts from its agents: one in use, and one more while a rotation is
/// under way.
/// </summary>
/// <remarks>
/// To rotate: set <see cref="Secondary" /> to the new token, roll the agents over to it, move it to
/// <see cref="Primary" />, and clear <see cref="Secondary" />. Both are shared secrets: keep them in a
/// secret store, and treat the dashboard that holds them as the trust anchor of the fleet.
/// </remarks>
public sealed class AgentTokens
{
    /// <summary>
    /// The token agents present.
    /// </summary>
    public string? Primary { get; set; }

    /// <summary>
    /// A second token accepted beside the first, for the time it takes to roll agents from one to the
    /// other.
    /// </summary>
    public string? Secondary { get; set; }

    /// <summary>
    /// Whether any token is configured, which is what says the hub authenticates agents this way.
    /// </summary>
    internal bool HasAny => !string.IsNullOrEmpty(Primary) || !string.IsNullOrEmpty(Secondary);

    /// <summary>
    /// Whether an <c>Authorization</c> header carries one of the tokens, compared in constant time.
    /// </summary>
    internal bool Matches(string? authorizationHeader)
    {
        const string scheme = "Bearer ";
        if (authorizationHeader is null || !authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] presented = Encoding.UTF8.GetBytes(authorizationHeader.AsSpan(scheme.Length).Trim().ToString());
        return Matches(presented, Primary) || Matches(presented, Secondary);
    }

    private static bool Matches(byte[] presented, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(token);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
