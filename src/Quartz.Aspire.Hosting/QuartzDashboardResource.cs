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

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// The Quartz.NET dashboard as an AppHost resource: the container image
/// <c>ghcr.io/quartznet/quartz-dashboard</c>, configured by the environment variables this package writes.
/// </summary>
/// <remarks>
/// Added with <c>AddQuartzDashboard</c>. Databases are attached with <c>WithAttachedStore</c>, and a worker
/// becomes one of its agents with <c>WithReference(dashboard)</c>. Visitors sign in with
/// <see cref="AdminToken" />, which the resource's URL carries, and agents authenticate with
/// <see cref="AgentToken" />. Run one replica: the dashboard holds its agents' connections in memory.
/// </remarks>
public sealed class QuartzDashboardResource : ContainerResource
{
    /// <summary>
    /// The name of the dashboard's HTTP endpoint.
    /// </summary>
    public const string HttpEndpointName = "http";

    /// <summary>
    /// The port the image listens on.
    /// </summary>
    internal const int ContainerPort = 8080;

    /// <summary>
    /// The path the dashboard is served under, which the host's default leaves at <c>/quartz</c>.
    /// </summary>
    internal const string DashboardPath = "/quartz";

    private readonly HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the resource.
    /// </summary>
    /// <param name="name">The resource's name.</param>
    /// <param name="adminToken">The token a visitor signs in with as an administrator.</param>
    /// <param name="agentToken">The token workers present when they dial in as agents.</param>
    public QuartzDashboardResource(string name, ParameterResource adminToken, ParameterResource agentToken)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(adminToken);
        ArgumentNullException.ThrowIfNull(agentToken);

        AdminToken = adminToken;
        AgentToken = agentToken;
    }

    /// <summary>
    /// The token a visitor signs in with as an administrator. Generated and kept in the AppHost's user secrets
    /// unless <c>AddQuartzDashboard</c> was handed one.
    /// </summary>
    public ParameterResource AdminToken { get; }

    /// <summary>
    /// The token workers present when they dial in. Generated and kept in the AppHost's user secrets unless
    /// <c>AddQuartzDashboard</c> was handed one.
    /// </summary>
    public ParameterResource AgentToken { get; }

    /// <summary>
    /// The dashboard's HTTP endpoint.
    /// </summary>
    public EndpointReference PrimaryEndpoint => field ??= new EndpointReference(this, HttpEndpointName);

    /// <summary>
    /// Claims a target name, which is half of every scheduler's identity on the dashboard, for a database or an
    /// agent.
    /// </summary>
    /// <exception cref="ArgumentException">The name cannot be a target name.</exception>
    /// <exception cref="InvalidOperationException">The name is already a target of this dashboard.</exception>
    internal void ClaimTarget(string target, string what)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains('/', StringComparison.Ordinal) || target.Contains('+', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{target}' cannot be a target of the Quartz dashboard '{Name}': a target name cannot be blank, contain '/', "
                + "which separates it from a scheduler's name, or contain '+', which joins the members of a cluster.",
                nameof(target));
        }

        if (!targets.Add(target))
        {
            throw new InvalidOperationException(
                $"The Quartz dashboard '{Name}' already has a target named '{target}', so {what} cannot be one too: the "
                + "target is half of every scheduler's identity on the dashboard, and two of them under one name would "
                + "refuse each other at run time. Give it a target of its own.");
        }
    }
}
