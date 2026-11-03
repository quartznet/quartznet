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

using System.Globalization;
using System.Reflection;

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Adds the Quartz.NET dashboard to an AppHost, attaches it to databases, and dials workers in to it.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard is the container image <c>ghcr.io/quartznet/quartz-dashboard</c> at this package's own
/// version, and everything it is told arrives as an environment variable: <c>Quartz__Dashboard__*</c> on the
/// dashboard, <c>Quartz__DashboardAgent__*</c> on a worker. Those keys are the contract; this package
/// references nothing of Quartz.
/// </para>
/// <para>
/// Visitors sign in with a token: the resource's URL in the Aspire dashboard carries the administrator's,
/// and opening it signs the browser in. Run one replica — the dashboard holds its agents' connections in
/// memory — and give a worker with replicas a target per replica, or only one of them is shown at a time.
/// </para>
/// </remarks>
public static class QuartzDashboardResourceBuilderExtensions
{
    /// <summary>The registry the image is published to.</summary>
    internal const string ImageRegistry = "ghcr.io";

    /// <summary>The image's repository.</summary>
    internal const string Image = "quartznet/quartz-dashboard";

    /// <summary>
    /// The image tag this package runs: its own version, so the dashboard and the package that describes it
    /// are released together.
    /// </summary>
    internal static string ImageTag { get; } = PackageVersion(typeof(QuartzDashboardResource).Assembly);

    /// <summary>
    /// Adds the Quartz.NET dashboard as a container resource.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dashboard signs visitors in with <paramref name="adminToken" />, and the resource's URL carries it,
    /// so opening the dashboard from the Aspire dashboard signs the browser in as an administrator. It
    /// accepts workers that present <paramref name="agentToken" />, which <c>WithReference(dashboard)</c>
    /// hands them. Both are generated secret parameters, kept in the AppHost's user secrets, unless given.
    /// </para>
    /// <para>
    /// The image is <c>ghcr.io/quartznet/quartz-dashboard</c> at this package's version;
    /// <c>WithImageTag</c> runs another. Its HTTP endpoint is named <c>http</c>, and its health check is
    /// <c>/healthz</c>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The AppHost's builder.</param>
    /// <param name="name">The resource's name.</param>
    /// <param name="port">The host port the dashboard is reached on; unset, Aspire picks one.</param>
    /// <param name="adminToken">The administrator's sign-in token; unset, a generated secret.</param>
    /// <param name="agentToken">The token agents present; unset, a generated secret.</param>
    /// <returns>The dashboard resource's builder.</returns>
    public static IResourceBuilder<QuartzDashboardResource> AddQuartzDashboard(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null,
        IResourceBuilder<ParameterResource>? adminToken = null,
        IResourceBuilder<ParameterResource>? agentToken = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Letters and digits only: the administrator's token travels in a URL, where anything else would
        // have to be escaped, and 32 of them are 190 bits.
        GenerateParameterDefault token = new() { MinLength = 32, Special = false };

        ParameterResource admin = adminToken?.Resource
            ?? ParameterResourceBuilderExtensions.CreateGeneratedParameter(builder, $"{name}-admin-token", secret: true, token);
        ParameterResource agent = agentToken?.Resource
            ?? ParameterResourceBuilderExtensions.CreateGeneratedParameter(builder, $"{name}-agent-token", secret: true, token);

        QuartzDashboardResource dashboard = new(name, admin, agent);

        return builder.AddResource(dashboard)
            .WithImage(Image, ImageTag)
            .WithImageRegistry(ImageRegistry)
            .WithHttpEndpoint(port: port, targetPort: QuartzDashboardResource.ContainerPort, name: QuartzDashboardResource.HttpEndpointName)
            .WithHttpHealthCheck("/healthz")
            .WithOtlpExporter()
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables[QuartzDashboardEnvironment.AuthenticationMode] = "Token";
                context.EnvironmentVariables[QuartzDashboardEnvironment.AllowTokenInUrl] = "true";
                context.EnvironmentVariables[QuartzDashboardEnvironment.AdministratorToken] = dashboard.AdminToken;
                context.EnvironmentVariables[QuartzDashboardEnvironment.AgentToken] = dashboard.AgentToken;
            })
            .WithUrls(async context =>
            {
                // The administrator's sign-in link is the resource's URL. The host takes a token in its
                // address only because this resource says AllowTokenInUrl, and moves the browser on to an
                // address without it.
                string token = await dashboard.AdminToken.GetValueAsync(context.CancellationToken).ConfigureAwait(false) ?? "";

                foreach (ResourceUrlAnnotation url in context.Urls)
                {
                    if (url.Endpoint?.EndpointName == QuartzDashboardResource.HttpEndpointName)
                    {
                        url.Url = $"{url.Url.TrimEnd('/')}/login?t={Uri.EscapeDataString(token)}";
                        url.DisplayText = "Quartz dashboard";
                    }
                }
            });
    }

    /// <summary>
    /// Points the dashboard at a database, so that every scheduler in it is shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is asked of the workers that use the database: the dashboard reads the job store's tables
    /// itself. <paramref name="configure" /> says what they were configured with — the provider, the table
    /// prefix, the serializer, whether they keep their execution history there — and the provider is worked
    /// out from the connection string when it is not said.
    /// </para>
    /// <para>
    /// The dashboard waits for the database. It reads job data of the types Quartz itself writes; a job data
    /// map holding a type of the application's own is shown as unreadable, since the dashboard does not have
    /// the type.
    /// </para>
    /// </remarks>
    /// <param name="builder">The dashboard's builder.</param>
    /// <param name="database">The database the workers' job store is in.</param>
    /// <param name="target">The name the database is shown under; unset, the database resource's name.</param>
    /// <param name="configure">What the workers' job store was configured with.</param>
    /// <returns>The dashboard's builder.</returns>
    /// <exception cref="InvalidOperationException">The dashboard already has a target of that name.</exception>
    public static IResourceBuilder<QuartzDashboardResource> WithAttachedStore(
        this IResourceBuilder<QuartzDashboardResource> builder,
        IResourceBuilder<IResourceWithConnectionString> database,
        string? target = null,
        Action<QuartzAttachedStoreSettings>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        string name = target ?? database.Resource.Name;
        builder.Resource.ClaimTarget(name, $"the database '{database.Resource.Name}'");

        QuartzAttachedStoreSettings settings = new();
        configure?.Invoke(settings);

        return builder
            .WaitFor(database)
            .WithReferenceRelationship(database)
            .WithEnvironment(context =>
            {
                Dictionary<string, object> environment = context.EnvironmentVariables;

                environment[QuartzDashboardEnvironment.Target(name, "ConnectionString")] = database.Resource.ConnectionStringExpression;
                Set(environment, name, "Provider", settings.Provider);
                Set(environment, name, "TablePrefix", settings.TablePrefix);
                Set(environment, name, "Serializer", settings.Serializer);

                if (settings.ExecutionHistory)
                {
                    environment[QuartzDashboardEnvironment.Target(name, "ExecutionHistory")] = "true";
                }

                if (settings.RediscoveryInterval is { } interval)
                {
                    environment[QuartzDashboardEnvironment.Target(name, "RediscoveryInterval")] = interval.ToString("c", CultureInfo.InvariantCulture);
                }
            });
    }

    /// <summary>
    /// Dials this resource's scheduler in to the dashboard as an agent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sets <c>Quartz__DashboardAgent__Endpoint</c>, <c>Token</c> and <c>Target</c> on this resource — or
    /// <c>Quartz__Schedulers__{schedulerName}__DashboardAgent__*</c> for one named scheduler among several —
    /// which is what <c>UseDashboardAgent()</c> reads. The worker calls that, and nothing else changes in it.
    /// </para>
    /// <para>
    /// The target is the resource's name, or <c>{name}-{schedulerName}</c>. A dashboard holds one scheduler per
    /// target, so a resource with replicas shows one of them at a time; the others are refused until it
    /// leaves.
    /// </para>
    /// </remarks>
    /// <typeparam name="TDestination">The worker's resource type.</typeparam>
    /// <param name="builder">The worker's builder.</param>
    /// <param name="dashboard">The dashboard to dial in to.</param>
    /// <param name="schedulerName">The named scheduler to dial in, when the worker has several.</param>
    /// <returns>The worker's builder.</returns>
    /// <exception cref="InvalidOperationException">The dashboard already has a target of that name.</exception>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder,
        IResourceBuilder<QuartzDashboardResource> dashboard,
        string? schedulerName = null)
        where TDestination : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dashboard);

        if (schedulerName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        }

        string target = schedulerName is null ? builder.Resource.Name : $"{builder.Resource.Name}-{schedulerName}";
        dashboard.Resource.ClaimTarget(target, $"the agent '{builder.Resource.Name}'");

        QuartzDashboardResource resource = dashboard.Resource;
        ReferenceExpression endpoint = ReferenceExpression.Create(
            $"{resource.PrimaryEndpoint.Property(EndpointProperty.Url)}{QuartzDashboardResource.DashboardPath}/agents");

        return builder
            .WithReferenceRelationship(dashboard)
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables[QuartzDashboardEnvironment.Agent(schedulerName, "Endpoint")] = endpoint;
                context.EnvironmentVariables[QuartzDashboardEnvironment.Agent(schedulerName, "Token")] = resource.AgentToken;
                context.EnvironmentVariables[QuartzDashboardEnvironment.Agent(schedulerName, "Target")] = target;
            });
    }

    private static void Set(Dictionary<string, object> environment, string target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            environment[QuartzDashboardEnvironment.Target(target, key)] = value;
        }
    }

    /// <summary>
    /// A package's version as an image tag: the informational version without the commit SourceLink appends.
    /// </summary>
    internal static string PackageVersion(Assembly assembly)
    {
        return Tag(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString(3)
                   ?? "latest");
    }

    /// <summary>
    /// An informational version as a tag: a tag cannot carry the <c>+commit</c> build metadata.
    /// </summary>
    internal static string Tag(string informationalVersion)
    {
        int metadata = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? informationalVersion : informationalVersion[..metadata];
    }
}

/// <summary>
/// The environment variables the dashboard host and the agent read: the contract between this package and
/// the two, which it shares no code with.
/// </summary>
internal static class QuartzDashboardEnvironment
{
    public const string AuthenticationMode = "Quartz__Dashboard__Authentication__Mode";

    public const string AllowTokenInUrl = "Quartz__Dashboard__Authentication__AllowTokenInUrl";

    public const string AdministratorToken = "Quartz__Dashboard__Authentication__Tokens__Administrator";

    public const string AgentToken = "Quartz__Dashboard__Agents__Tokens__Primary";

    public static string Target(string target, string key) => $"Quartz__Dashboard__Targets__{target}__{key}";

    public static string Agent(string? schedulerName, string key) => schedulerName is null
        ? $"Quartz__DashboardAgent__{key}"
        : $"Quartz__Schedulers__{schedulerName}__DashboardAgent__{key}";
}
