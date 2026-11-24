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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using Quartz.AspNetCore;
using Quartz.Mcp.Impl;

namespace Quartz;

/// <summary>
/// Maps the Quartz MCP endpoint. Added in 4.9.
/// </summary>
public static class QuartzMcpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Quartz MCP endpoint at <c>/quartz-mcp</c>.
    /// </summary>
    /// <inheritdoc cref="MapQuartzMcp(IEndpointRouteBuilder, string)" path="/remarks" />
    public static IEndpointConventionBuilder MapQuartzMcp(this IEndpointRouteBuilder builder)
    {
        return MapQuartzMcp(builder, QuartzMcpOptions.DefaultPath);
    }

    /// <summary>
    /// Maps the Quartz MCP endpoint at <paramref name="pattern" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Streamable HTTP, <c>POST</c> only. In front of it are the request guard — an <c>Origin</c> not in
    /// <see cref="QuartzMcpOptions.AllowedOrigins" /> is <c>403</c>, a body not declared <c>application/json</c> is
    /// <c>415</c> — and the startup check every Quartz endpoint has: the application refuses to start if nothing
    /// authorizes the endpoint. Say who may with <c>.RequireAuthorization(…)</c> on what this returns, which composes
    /// with <c>.RequireRateLimiting(…)</c> as any endpoint does. Do not authorize it with a cookie: a browser sends a
    /// cookie on its own.
    /// </para>
    /// <para>
    /// The container's MCP server must hold Quartz's tools and no others; one that holds an application's own
    /// tools as well is refused here, naming them.
    /// </para>
    /// </remarks>
    /// <param name="builder">The endpoint route builder.</param>
    /// <param name="pattern">The path the endpoint is served at, starting with <c>/</c>.</param>
    public static IEndpointConventionBuilder MapQuartzMcp(this IEndpointRouteBuilder builder, string pattern)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(pattern) || !pattern.StartsWith('/'))
        {
            throw new ArgumentException($"The route pattern is required and must start with '/', was '{pattern}'.", nameof(pattern));
        }

        IServiceProvider services = builder.ServiceProvider;
        if (services.GetService<McpToolInvoker>() is null)
        {
            throw new InvalidOperationException("The Quartz MCP server is not configured. Call services.AddQuartzMcp() first.");
        }

        RefuseOtherTools(services);

        services.GetRequiredService<QuartzMappedEndpoints>().Track(builder);
        QuartzMcpOptions options = services.GetRequiredService<IOptions<QuartzMcpOptions>>().Value;

        IEndpointConventionBuilder endpoint = builder.MapMcp(pattern);

        // Outermost: a page of another origin and a body that is not JSON are refused before the SDK reads anything.
        new McpRequestGuard(options.AllowedOrigins, services.GetRequiredService<ILoggerFactory>()).Apply(endpoint);

        // A policy of either kind is asked on every tool call, reads included, so it states an authorization rule for
        // the endpoint as the HTTP API's two policies do for its routes.
        bool authorizedByOptions = !string.IsNullOrWhiteSpace(options.SchedulerAuthorizationPolicy)
                                   || !string.IsNullOrWhiteSpace(options.OperationAuthorizationPolicy);
        QuartzEndpointMarker marker = new(McpSurface, McpRemedies, authorizedByOptions);
        endpoint.Add(endpointBuilder => endpointBuilder.Metadata.Add(marker));

        return endpoint;
    }

    /// <summary>
    /// Refuses a container whose MCP server holds a tool that is not Quartz's.
    /// </summary>
    /// <remarks>
    /// Silent merging would serve the application's tools under Quartz's path, held to none of its checks, and
    /// Quartz's under the application's.
    /// </remarks>
    private static void RefuseOtherTools(IServiceProvider services)
    {
        McpServerOptions server = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        List<string> others = [];
        foreach (McpServerTool tool in server.ToolCollection ?? [])
        {
            if (tool is not QuartzTool)
            {
                others.Add(tool.ProtocolTool.Name);
            }
        }

        if (others.Count > 0)
        {
            others.Sort(StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"The container's MCP server holds tools that are not Quartz's: {string.Join(", ", others)}. MapQuartzMcp serves the "
                + "container's one MCP server, held to Quartz's checks, and would serve these beside Quartz's tools. Host Quartz's "
                + "MCP endpoint in a process of its own, or serve your tools without AddQuartzMcp.");
        }
    }

    private const string McpSurface = "The Quartz MCP endpoint";

    private const string McpRemedies = """
          - app.MapQuartzMcp().RequireAuthorization() authorizes the whole endpoint, by a bearer token rather than a cookie;
          - services.AddQuartzMcp(options => options.OperationAuthorizationPolicy = "...") with RequireSchedulerRoles holds each tool to the operator roles;
          - app.MapQuartzMcp().AllowAnonymous() serves it to anyone, deliberately.
        """;
}
