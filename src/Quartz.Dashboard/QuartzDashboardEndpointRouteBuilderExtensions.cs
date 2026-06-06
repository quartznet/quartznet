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

using System.Reflection;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Components;
using Quartz.Dashboard.Hubs;

namespace Quartz;

public static class QuartzDashboardEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Quartz.NET Dashboard endpoints with its own Blazor component root.
    /// Use this overload when the host application does not have its own Blazor Server setup.
    /// </summary>
    public static RazorComponentsEndpointConventionBuilder MapQuartzDashboard(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Map Blazor components at root level — pages already have /quartz prefix in @page directives
        RazorComponentsEndpointConventionBuilder components = builder
            .MapRazorComponents<QuartzDashboardApp>()
            .AddInteractiveServerRenderMode();

        MapQuartzDashboardCore(builder, components, dashboardOwnsComponents: true);

        return components;
    }

    /// <summary>
    /// Maps the Quartz.NET Dashboard endpoints into an existing Blazor Server application.
    /// Use this overload when the host application already calls
    /// <c>MapRazorComponents&lt;App&gt;().AddInteractiveServerRenderMode()</c>
    /// to avoid registering a second <c>/_blazor</c> SignalR endpoint.
    /// </summary>
    /// <remarks>
    /// The host application's interactive router must also be able to resolve the dashboard pages:
    /// add <c>typeof(QuartzDashboardApp).Assembly</c> to the <c>AdditionalAssemblies</c> of the
    /// <c>&lt;Router&gt;</c> in the host's <c>Routes.razor</c>. Otherwise the dashboard renders on the
    /// initial request but the interactive router replaces it with the application's not-found page
    /// once the circuit starts.
    /// </remarks>
    /// <example>
    /// <code>
    /// var blazor = endpoints.MapRazorComponents&lt;App&gt;()
    ///     .AddInteractiveServerRenderMode();
    ///
    /// endpoints.MapQuartzDashboard(blazor);
    /// </code>
    /// And in the host's <c>Routes.razor</c>:
    /// <code>
    /// &lt;Router AppAssembly="typeof(App).Assembly"
    ///         AdditionalAssemblies="new[] { typeof(Quartz.Dashboard.Components.QuartzDashboardApp).Assembly }"&gt;
    /// </code>
    /// </example>
    public static RazorComponentsEndpointConventionBuilder MapQuartzDashboard(
        this IEndpointRouteBuilder builder,
        RazorComponentsEndpointConventionBuilder existingComponents)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(existingComponents);

        // Register dashboard page components with the host's existing Blazor router
        existingComponents.AddAdditionalAssemblies(typeof(QuartzDashboardApp).Assembly);

        MapQuartzDashboardCore(builder, existingComponents, dashboardOwnsComponents: false);

        return existingComponents;
    }

    private static void MapQuartzDashboardCore(
        IEndpointRouteBuilder builder,
        RazorComponentsEndpointConventionBuilder components,
        bool dashboardOwnsComponents)
    {
        QuartzDashboardOptions options = builder.ServiceProvider.GetRequiredService<IOptions<QuartzDashboardOptions>>().Value;
        string dashboardPath = options.TrimmedDashboardPath;

        bool hasCustomPath = !string.Equals(dashboardPath, QuartzDashboardOptions.DefaultDashboardPath, StringComparison.OrdinalIgnoreCase);
        if (hasCustomPath && !dashboardOwnsComponents)
        {
            throw new InvalidOperationException(
                $"A custom QuartzDashboardOptions.DashboardPath ('{options.DashboardPath}') is not supported when integrating with an existing Blazor application " +
                $"because the dashboard page routes are fixed at '{QuartzDashboardOptions.DefaultDashboardPath}'. " +
                "Use the MapQuartzDashboard() overload without an existing components builder to serve the dashboard under a custom path.");
        }

        if (hasCustomPath)
        {
            // Re-root the dashboard page endpoints from the compile-time /quartz prefix to the
            // configured path so the initial page load and enhanced navigations resolve (#3093).
            // Interactive navigation is handled by the dashboard's own route matching in Routes.razor.
            components.Add(endpointBuilder =>
            {
                if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder
                    || GetComponentType(endpointBuilder)?.Assembly != typeof(QuartzDashboardApp).Assembly)
                {
                    return;
                }

                string? rawText = routeEndpointBuilder.RoutePattern.RawText;
                if (rawText is null
                    || !rawText.StartsWith(QuartzDashboardOptions.DefaultDashboardPath, StringComparison.OrdinalIgnoreCase)
                    || (rawText.Length > QuartzDashboardOptions.DefaultDashboardPath.Length && rawText[QuartzDashboardOptions.DefaultDashboardPath.Length] != '/'))
                {
                    return;
                }

                routeEndpointBuilder.RoutePattern = RoutePatternFactory.Parse(string.Concat(dashboardPath, rawText.AsSpan(QuartzDashboardOptions.DefaultDashboardPath.Length)));
            });
        }

        // Map SignalR hub under the dashboard path
        HubEndpointConventionBuilder hub = builder.MapHub<QuartzDashboardHub>(dashboardPath + "/hub")
            .DisableAntiforgery();

        // Serve dashboard static web assets via endpoint routing as a fallback
        // for hosts that don't configure UseStaticFiles() (e.g., API-only projects)
        RouteHandlerBuilder staticAssets = MapDashboardStaticAssets(builder);

        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
        {
            string policyName = options.AuthorizationPolicy;
            hub.RequireAuthorization(policyName);

            // Gate static assets with the same policy as the rest of the dashboard so the endpoint
            // carries explicit authorization metadata and keeps working in applications that use a
            // fail-closed FallbackPolicy (#3097).
            staticAssets.RequireAuthorization(policyName);

            if (dashboardOwnsComponents)
            {
                // Standalone mode: the components builder contains only dashboard endpoints (pages,
                // framework script and circuit endpoints), so the policy can safely cover them all.
                // This also keeps /_framework and /_blazor reachable in applications using a
                // fail-closed FallbackPolicy (#3097).
                components.RequireAuthorization(policyName);
            }
            else
            {
                // Integrated mode: apply the policy ONLY to dashboard page endpoints. Calling
                // RequireAuthorization on 'components' would otherwise apply to every endpoint owned
                // by the builder, including the host app's own pages (#3066).
                Assembly dashboardAssembly = typeof(QuartzDashboardApp).Assembly;
                components.Add(endpointBuilder =>
                {
                    if (GetComponentType(endpointBuilder)?.Assembly == dashboardAssembly)
                    {
                        endpointBuilder.Metadata.Add(new AuthorizeAttribute(policyName));
                    }
                });
            }
        }
        else
        {
            // Without a dashboard policy, static assets opt out of authorization so applications
            // enforcing a fail-closed FallbackPolicy can still load dashboard CSS/JS; the files are
            // public package content (#3097).
            staticAssets.AllowAnonymous();
        }
    }

    private static Type? GetComponentType(EndpointBuilder endpointBuilder)
    {
        foreach (object metadata in endpointBuilder.Metadata)
        {
            if (metadata is ComponentTypeMetadata componentTypeMetadata)
            {
                return componentTypeMetadata.Type;
            }
        }

        return null;
    }

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private static RouteHandlerBuilder MapDashboardStaticAssets(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("_content/Quartz.Dashboard/{**path}", async (HttpContext context, string path) =>
        {
            var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var fileInfo = env.WebRootFileProvider.GetFileInfo($"_content/Quartz.Dashboard/{path}");
            if (!fileInfo.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!ContentTypeProvider.TryGetContentType(path, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            context.Response.ContentType = contentType;
            context.Response.ContentLength = fileInfo.Length;

            var stream = fileInfo.CreateReadStream();
            await using (stream.ConfigureAwait(false))
            {
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            }
        }).ExcludeFromDescription();
    }
}
