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

namespace Quartz.Dashboard.Host;

/// <summary>
/// Everything under <c>Quartz:Dashboard</c>: the dashboard's own options by their own names, the targets,
/// the agents, how visitors sign in, and which roles do what.
/// </summary>
/// <remarks>
/// Every value is nullable so that an absent key is told apart from a default, and so that the defaults
/// stay the dashboard's own rather than being restated here. The keys are the contract the container
/// image, the tool and <c>Quartz.Aspire.Hosting</c> share; the documentation page lists them.
/// </remarks>
internal sealed class DashboardHostSettings
{
    /// <summary>
    /// The section everything here is read from.
    /// </summary>
    internal const string SectionName = "Quartz:Dashboard";

    public string? DashboardPath { get; set; }

    public bool? ReadOnly { get; set; }

    public TimeSpan? ClusterDetectionInterval { get; set; }

    public TimeSpan? HistoryRetention { get; set; }

    public int? HistoryMaxEntriesPerScheduler { get; set; }

    public Dictionary<string, TargetSettings> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AgentSettings Agents { get; set; } = new();

    public AuthenticationSettings Authentication { get; set; } = new();

    public RoleSettings Roles { get; set; } = new();

    public DataProtectionSettings DataProtection { get; set; } = new();
}

/// <summary>
/// One target: a scheduler reached over its HTTP API, or a database every scheduler in which is shown.
/// </summary>
internal sealed class TargetSettings
{
    /// <summary>The HTTP API's base address, for example <c>https://orders.internal/quartz-api/</c>.</summary>
    public string? Url { get; set; }

    public string? BearerToken { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan? Timeout { get; set; }

    public string? SchedulerName { get; set; }

    public string? ConnectionString { get; set; }

    /// <summary>The name of a <c>ConnectionStrings:</c> entry, read from the host's configuration.</summary>
    public string? ConnectionStringName { get; set; }

    public string? Provider { get; set; }

    public string? TablePrefix { get; set; }

    /// <summary><c>SystemTextJson</c>, the default, or <c>Newtonsoft</c>.</summary>
    public string? Serializer { get; set; }

    public bool? ExecutionHistory { get; set; }

    public TimeSpan? RediscoveryInterval { get; set; }

    internal bool IsHttp => !string.IsNullOrWhiteSpace(Url)
                            || !string.IsNullOrWhiteSpace(BearerToken)
                            || Headers.Count > 0
                            || Timeout is not null
                            || !string.IsNullOrWhiteSpace(SchedulerName);

    internal bool IsStore => !string.IsNullOrWhiteSpace(ConnectionString)
                             || !string.IsNullOrWhiteSpace(ConnectionStringName)
                             || !string.IsNullOrWhiteSpace(Provider)
                             || !string.IsNullOrWhiteSpace(TablePrefix)
                             || !string.IsNullOrWhiteSpace(Serializer)
                             || ExecutionHistory is not null
                             || RediscoveryInterval is not null;
}

/// <summary>
/// How agents are accepted: <see cref="DashboardAgentHubOptions" /> by its own names.
/// </summary>
internal sealed class AgentSettings
{
    public AgentTokenSettings Tokens { get; set; } = new();

    public TimeSpan? HeartbeatInterval { get; set; }

    public int? OfflineAfterMissedHeartbeats { get; set; }

    public TimeSpan? OperationTimeout { get; set; }

    public int? MaxMessageBytes { get; set; }

    public TimeSpan? ForgetAfter { get; set; }

    internal bool Accepts => !string.IsNullOrWhiteSpace(Tokens.Primary) || !string.IsNullOrWhiteSpace(Tokens.Secondary);
}

internal sealed class AgentTokenSettings
{
    public string? Primary { get; set; }

    public string? Secondary { get; set; }
}

/// <summary>
/// How a visitor signs in. <see cref="Mode" /> is required.
/// </summary>
internal sealed class AuthenticationSettings
{
    /// <summary><c>Token</c>, <c>OpenIdConnect</c> or <c>Unsecured</c>.</summary>
    public string? Mode { get; set; }

    public RoleTokenSettings Tokens { get; set; } = new();

    public bool AllowTokenInUrl { get; set; }

    public SchedulerAccess? UnsecuredAccess { get; set; }

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public List<string> Scopes { get; set; } = [];

    public string? NameClaimType { get; set; }

    public string? RoleClaimType { get; set; }

    public bool? RequireHttpsMetadata { get; set; }
}

/// <summary>
/// The tokens of <c>Token</c> mode, one per role. A visitor who presents one is signed in at its role.
/// </summary>
internal sealed class RoleTokenSettings
{
    public string? Viewer { get; set; }

    public string? Operator { get; set; }

    public string? Administrator { get; set; }

    internal bool Any => !string.IsNullOrEmpty(Viewer) || !string.IsNullOrEmpty(Operator) || !string.IsNullOrEmpty(Administrator);
}

/// <summary>
/// Which of an identity provider's roles are the operator roles, and per-operation overrides: what
/// <see cref="SchedulerRoles" /> says, by its own names.
/// </summary>
internal sealed class RoleSettings
{
    public List<string> Viewers { get; set; } = [];

    public List<string> Operators { get; set; } = [];

    public List<string> Administrators { get; set; } = [];

    public Dictionary<string, SchedulerAccess> Operations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal bool AnyRole => Viewers.Count + Operators.Count + Administrators.Count > 0;
}

internal sealed class DataProtectionSettings
{
    /// <summary>A directory the data protection keys are kept in, so sign-ins survive a restart.</summary>
    public string? KeysPath { get; set; }
}
