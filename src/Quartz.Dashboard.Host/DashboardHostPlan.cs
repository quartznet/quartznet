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

using System.Collections.Frozen;

using Microsoft.Extensions.Configuration;

namespace Quartz.Dashboard.Host;

/// <summary>
/// How a visitor is signed in.
/// </summary>
internal enum HostAuthenticationMode
{
    /// <summary>A token per role, presented on the sign-in form.</summary>
    Token,

    /// <summary>An identity provider, whose roles <c>Roles:*</c> map to the operator roles.</summary>
    OpenIdConnect,

    /// <summary>Nobody signs in; every visitor is the same anonymous operator.</summary>
    Unsecured,
}

/// <summary>A scheduler reached over its HTTP API.</summary>
internal sealed record HttpTargetPlan(string Name, Uri BaseAddress, string SchedulerName, string? BearerToken, IReadOnlyDictionary<string, string> Headers, TimeSpan? Timeout);

/// <summary>A database whose every scheduler is shown as a window.</summary>
internal sealed record StoreTargetPlan(string Name, string Provider, string ConnectionString, string? TablePrefix, string Serializer, bool ExecutionHistory, TimeSpan? RediscoveryInterval);

/// <summary>
/// What <c>Quartz:Dashboard</c> says, read and checked once: the settings, and every reason the host must
/// not start on them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here throws. A host whose configuration is wrong is still built, with nothing in it but the
/// guard that logs each problem as event 9402 and stops the start; that is how a container that will not
/// start says why in its log rather than in a stack trace from the middle of service registration.
/// </para>
/// <para>
/// The checks are the host's own; the dashboard and the HTTP client check their options again where they
/// are built, which is the second line rather than the first.
/// </para>
/// </remarks>
internal sealed class DashboardHostPlan
{
    /// <summary>The role names a token or the unsecured principal carries.</summary>
    internal const string ViewerRole = "Viewer";

    internal const string OperatorRole = "Operator";

    internal const string AdministratorRole = "Administrator";

    /// <summary>
    /// The shortest token <c>Token</c> mode accepts: one an attacker cannot run through in an afternoon.
    /// </summary>
    internal const int MinimumTokenLength = 16;

    /// <summary>
    /// The providers the host carries a driver for, by every name a setting may use, to the name the
    /// store is configured with.
    /// </summary>
    private static readonly FrozenDictionary<string, string> Providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [DataSourceOptions.Providers.SqlServer] = DataSourceOptions.Providers.SqlServer,
        [DataSourceOptions.Providers.Npgsql] = DataSourceOptions.Providers.Npgsql,
        [DataSourceOptions.Providers.MySqlConnector] = DataSourceOptions.Providers.MySqlConnector,
        // MySql.Data is not carried; the dialect is the same, and MySqlConnector reads the same database.
        [DataSourceOptions.Providers.MySql] = DataSourceOptions.Providers.MySqlConnector,
        [DataSourceOptions.Providers.Oracle] = DataSourceOptions.Providers.Oracle,
        [DataSourceOptions.Providers.Sqlite] = DataSourceOptions.Providers.Sqlite,
        // System.Data.SQLite is not carried either; Microsoft.Data.Sqlite reads the same file.
        [DataSourceOptions.Providers.SystemDataSqlite] = DataSourceOptions.Providers.Sqlite,
        [DataSourceOptions.Providers.Firebird] = DataSourceOptions.Providers.Firebird,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal const string SystemTextJsonSerializer = "SystemTextJson";

    internal const string NewtonsoftSerializer = "Newtonsoft";

    private DashboardHostPlan(DashboardHostSettings settings)
    {
        Settings = settings;
    }

    public DashboardHostSettings Settings { get; }

    public List<string> Problems { get; } = [];

    public HostAuthenticationMode Mode { get; private set; }

    public SchedulerAccess UnsecuredAccess { get; private set; } = SchedulerAccess.View;

    public List<HttpTargetPlan> HttpTargets { get; } = [];

    public List<StoreTargetPlan> StoreTargets { get; } = [];

    public bool AcceptsAgents => Settings.Agents.Accepts;

    public bool CanStart => Problems.Count == 0;

    /// <summary>
    /// Reads <c>Quartz:Dashboard</c> out of <paramref name="configuration" /> and checks it.
    /// </summary>
    public static DashboardHostPlan Read(IConfiguration configuration)
    {
        DashboardHostSettings settings = new();
        List<string> bindingProblems = [];

        try
        {
            configuration.GetSection(DashboardHostSettings.SectionName).Bind(settings);
        }
        catch (InvalidOperationException e)
        {
            bindingProblems.Add($"{DashboardHostSettings.SectionName} could not be read: {e.Message}");
        }

        DashboardHostPlan plan = new(settings);
        plan.Problems.AddRange(bindingProblems);

        plan.CheckAuthentication();
        plan.CheckTargets(configuration);
        plan.CheckAgents();

        return plan;
    }

    private void CheckAuthentication()
    {
        AuthenticationSettings authentication = Settings.Authentication;

        if (string.IsNullOrWhiteSpace(authentication.Mode))
        {
            Problems.Add(
                "Quartz:Dashboard:Authentication:Mode is required: Token, OpenIdConnect or Unsecured. The host does not "
                + "guess how visitors sign in, because the guess that starts is the one that serves every scheduler to anyone.");
            return;
        }

        if (!Enum.TryParse(authentication.Mode, ignoreCase: true, out HostAuthenticationMode mode) || !Enum.IsDefined(mode))
        {
            Problems.Add($"Quartz:Dashboard:Authentication:Mode is '{authentication.Mode}', which is none of Token, OpenIdConnect and Unsecured.");
            return;
        }

        Mode = mode;

        if (mode != HostAuthenticationMode.Token)
        {
            if (authentication.Tokens.Any)
            {
                Problems.Add($"Quartz:Dashboard:Authentication:Tokens are set, but Mode is {mode}: tokens sign visitors in only in Token mode.");
            }

            if (authentication.AllowTokenInUrl)
            {
                Problems.Add($"Quartz:Dashboard:Authentication:AllowTokenInUrl is set, but Mode is {mode}: it applies to Token mode only.");
            }
        }

        if (mode != HostAuthenticationMode.OpenIdConnect && Settings.Roles.AnyRole)
        {
            Problems.Add(
                $"Quartz:Dashboard:Roles:Viewers, Operators and Administrators map an identity provider's roles, and Mode is {mode}. "
                + "In Token mode the token is the role; in Unsecured mode UnsecuredAccess is.");
        }

        if (mode != HostAuthenticationMode.Unsecured && authentication.UnsecuredAccess is not null)
        {
            Problems.Add($"Quartz:Dashboard:Authentication:UnsecuredAccess is set, but Mode is {mode}: it applies to Unsecured mode only.");
        }

        switch (mode)
        {
            case HostAuthenticationMode.Token:
                CheckTokens(authentication.Tokens);
                break;
            case HostAuthenticationMode.OpenIdConnect:
                CheckOpenIdConnect(authentication);
                break;
            case HostAuthenticationMode.Unsecured:
                CheckUnsecured(authentication);
                break;
        }

        CheckOperations();
    }

    private void CheckTokens(RoleTokenSettings tokens)
    {
        if (!tokens.Any)
        {
            Problems.Add("Token mode needs at least one of Quartz:Dashboard:Authentication:Tokens:Viewer, :Operator and :Administrator.");
            return;
        }

        List<(string Role, string Token)> given = [];
        foreach ((string role, string? token) in new[] { (ViewerRole, tokens.Viewer), (OperatorRole, tokens.Operator), (AdministratorRole, tokens.Administrator) })
        {
            if (token is null || token.Length == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(token) || token.Length < MinimumTokenLength)
            {
                Problems.Add($"Quartz:Dashboard:Authentication:Tokens:{role} is shorter than {MinimumTokenLength} characters; a token that short can be guessed.");
                continue;
            }

            given.Add((role, token));
        }

        for (int i = 0; i < given.Count; i++)
        {
            for (int j = i + 1; j < given.Count; j++)
            {
                if (string.Equals(given[i].Token, given[j].Token, StringComparison.Ordinal))
                {
                    Problems.Add($"Quartz:Dashboard:Authentication:Tokens:{given[i].Role} and :{given[j].Role} are the same token, so a visitor presenting it has no one role.");
                }
            }
        }
    }

    private void CheckOpenIdConnect(AuthenticationSettings authentication)
    {
        if (string.IsNullOrWhiteSpace(authentication.Authority))
        {
            Problems.Add("OpenIdConnect mode needs Quartz:Dashboard:Authentication:Authority, the identity provider's issuer.");
        }
        else if (!Uri.TryCreate(authentication.Authority, UriKind.Absolute, out _))
        {
            Problems.Add($"Quartz:Dashboard:Authentication:Authority is '{authentication.Authority}', which is not an absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(authentication.ClientId))
        {
            Problems.Add("OpenIdConnect mode needs Quartz:Dashboard:Authentication:ClientId.");
        }

        if (!Settings.Roles.AnyRole)
        {
            Problems.Add(
                "OpenIdConnect mode needs Quartz:Dashboard:Roles:Viewers, :Operators or :Administrators to name the identity "
                + "provider's roles. Without them every signed-in user would be refused everything, reads included, and a "
                + "dashboard that signs everyone in and shows nobody anything is a misconfiguration rather than a policy.");
        }

        foreach ((string list, List<string> roles) in new[] { ("Viewers", Settings.Roles.Viewers), ("Operators", Settings.Roles.Operators), ("Administrators", Settings.Roles.Administrators) })
        {
            if (roles.Exists(string.IsNullOrWhiteSpace))
            {
                Problems.Add($"Quartz:Dashboard:Roles:{list} holds a blank role, which no user is in.");
            }
        }
    }

    private void CheckUnsecured(AuthenticationSettings authentication)
    {
        SchedulerAccess access = authentication.UnsecuredAccess ?? SchedulerAccess.View;

        if (access is not (SchedulerAccess.View or SchedulerAccess.Operate))
        {
            Problems.Add(
                $"Quartz:Dashboard:Authentication:UnsecuredAccess is {access}; with nobody signed in it can be View, the default, "
                + "or Operate, and never Administer: adding a job names a type to run, and that is not anonymous.");
            return;
        }

        UnsecuredAccess = access;
    }

    private void CheckOperations()
    {
        try
        {
            // The same check RequireSchedulerRoles makes when the policy is built, made here so a misspelt
            // operation is a refusal to start rather than a failure on the first request.
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireSchedulerRoles(roles =>
            {
                roles.Viewers.Add(ViewerRole);
                foreach ((string operation, SchedulerAccess access) in Settings.Roles.Operations)
                {
                    roles.Operations[operation] = access;
                }
            });
        }
        catch (ArgumentException e)
        {
            Problems.Add($"Quartz:Dashboard:Roles:Operations: {e.Message}");
        }
    }

    private void CheckTargets(IConfiguration configuration)
    {
        foreach ((string name, TargetSettings target) in Settings.Targets)
        {
            if (TargetNameProblem(name) is { } problem)
            {
                Problems.Add($"Quartz:Dashboard:Targets:{name}: {problem}");
                continue;
            }

            if (target.IsHttp && target.IsStore)
            {
                Problems.Add(
                    $"Quartz:Dashboard:Targets:{name} says both how to reach a scheduler over HTTP (Url, BearerToken, Headers, Timeout, "
                    + "SchedulerName) and how to read a database (ConnectionString, ConnectionStringName, Provider, TablePrefix, "
                    + "Serializer, ExecutionHistory, RediscoveryInterval). A target is one or the other.");
                continue;
            }

            if (target.IsHttp)
            {
                CheckHttpTarget(name, target);
            }
            else if (target.IsStore)
            {
                CheckStoreTarget(name, target, configuration);
            }
            else
            {
                Problems.Add($"Quartz:Dashboard:Targets:{name} says nothing: give it a Url, or a ConnectionString or ConnectionStringName.");
            }
        }
    }

    private void CheckHttpTarget(string name, TargetSettings target)
    {
        int before = Problems.Count;

        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out Uri? url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            Problems.Add($"Quartz:Dashboard:Targets:{name}:Url is required and must be an absolute http or https address of the HTTP API, such as https://orders.internal/quartz-api/.");
        }

        if (string.IsNullOrWhiteSpace(target.SchedulerName))
        {
            Problems.Add($"Quartz:Dashboard:Targets:{name}:SchedulerName is required: the name of the scheduler the HTTP API serves.");
        }

        if (target.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            Problems.Add($"Quartz:Dashboard:Targets:{name}:Timeout must be positive, was {timeout}.");
        }

        if (Problems.Count > before)
        {
            return;
        }

        // A base address has to end in '/', or HttpScheduler refuses it: the routes are resolved relative to it.
        Uri baseAddress = url!.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
        HttpTargets.Add(new HttpTargetPlan(name, baseAddress, target.SchedulerName!, target.BearerToken, target.Headers, target.Timeout));
    }

    private void CheckStoreTarget(string name, TargetSettings target, IConfiguration configuration)
    {
        int before = Problems.Count;
        string? connectionString = target.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(target.ConnectionStringName))
        {
            connectionString = configuration.GetConnectionString(target.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Problems.Add($"Quartz:Dashboard:Targets:{name}:ConnectionStringName is '{target.ConnectionStringName}', and ConnectionStrings:{target.ConnectionStringName} is not set.");
            }
        }
        else if (string.IsNullOrWhiteSpace(connectionString))
        {
            Problems.Add($"Quartz:Dashboard:Targets:{name} needs a ConnectionString or a ConnectionStringName.");
        }

        // Each setting is checked whatever the others said, so one start names every mistake.
        string? provider = ResolveProvider(name, target.Provider, connectionString);
        string? serializer = ResolveSerializer(name, target.Serializer);

        if (target.RediscoveryInterval is { } interval && interval < TimeSpan.Zero)
        {
            Problems.Add($"Quartz:Dashboard:Targets:{name}:RediscoveryInterval cannot be negative, was {interval}. Zero asks once, at start-up.");
        }

        if (Problems.Count > before || provider is null || serializer is null || connectionString is null)
        {
            return;
        }

        StoreTargets.Add(new StoreTargetPlan(
            name,
            provider,
            connectionString,
            target.TablePrefix,
            serializer,
            target.ExecutionHistory ?? false,
            target.RediscoveryInterval is { } every ? (every == TimeSpan.Zero ? null : every) : TimeSpan.FromMinutes(1)));
    }

    private string? ResolveProvider(string name, string? provider, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            if (Providers.TryGetValue(provider, out string? known))
            {
                return known;
            }

            Problems.Add(
                $"Quartz:Dashboard:Targets:{name}:Provider is '{provider}'. The host carries drivers for {SupportedProviders}.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            return ConnectionStringProviderInference.Infer(connectionString, name);
        }
        catch (SchedulerConfigException)
        {
            Problems.Add(
                $"Quartz:Dashboard:Targets:{name}: the host cannot tell which database the connection string is for. Name it in "
                + $"Quartz:Dashboard:Targets:{name}:Provider, one of {SupportedProviders}.");
            return null;
        }
    }

    private string? ResolveSerializer(string name, string? serializer)
    {
        if (string.IsNullOrWhiteSpace(serializer) || string.Equals(serializer, SystemTextJsonSerializer, StringComparison.OrdinalIgnoreCase))
        {
            return SystemTextJsonSerializer;
        }

        if (string.Equals(serializer, NewtonsoftSerializer, StringComparison.OrdinalIgnoreCase))
        {
            return NewtonsoftSerializer;
        }

        Problems.Add($"Quartz:Dashboard:Targets:{name}:Serializer is '{serializer}'; it can be SystemTextJson, the default, or Newtonsoft, as the nodes that write the database use.");
        return null;
    }

    private void CheckAgents()
    {
        AgentSettings agents = Settings.Agents;

        foreach ((string slot, string? token) in new[] { ("Primary", agents.Tokens.Primary), ("Secondary", agents.Tokens.Secondary) })
        {
            if (token is { Length: > 0 } && string.IsNullOrWhiteSpace(token))
            {
                Problems.Add($"Quartz:Dashboard:Agents:Tokens:{slot} is blank, which is a token every agent would present by sending nothing.");
            }
        }

        bool tuned = agents.HeartbeatInterval is not null || agents.OfflineAfterMissedHeartbeats is not null || agents.OperationTimeout is not null
                     || agents.MaxMessageBytes is not null || agents.ForgetAfter is not null;

        if (tuned && !agents.Accepts)
        {
            Problems.Add("Quartz:Dashboard:Agents is configured but names no token: set Quartz:Dashboard:Agents:Tokens:Primary to accept agents.");
        }
    }

    private static string SupportedProviders => string.Join(", ", Providers.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    /// <summary>
    /// The rule <c>SchedulerTargets</c> holds every target name to, said in the host's own words.
    /// </summary>
    internal static string? TargetNameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "a target needs a name.";
        }

        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('+', StringComparison.Ordinal))
        {
            return "a target name cannot contain '/', which separates it from a scheduler's name, or '+', which joins the members of a cluster.";
        }

        return null;
    }
}
