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
using System.Reflection;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

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

        bool hasCustomPath = options.HasCustomDashboardPath;
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
                if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder)
                {
                    return;
                }

                string? rawText = routeEndpointBuilder.RoutePattern.RawText;
                if (string.IsNullOrEmpty(rawText) || rawText[0] != '/')
                {
                    return;
                }

                Type? componentType = GetComponentType(endpointBuilder);
                if (componentType is null)
                {
                    // The /_blazor circuit endpoints move under the dashboard path so a reverse proxy
                    // that forwards only the dashboard prefix can reach them (#3134); SignalR dispatch
                    // does not depend on the route pattern. Other framework-owned endpoints stay put:
                    // newer runtimes register the blazor.web.js script endpoint through this data
                    // source and it serves content keyed by its original path, so re-rooting it breaks
                    // it — the dashboard maps its own copy under the dashboard path instead.
                    // Standalone mode only: a custom path is rejected in integrated mode, so the
                    // builder never contains host application endpoints here.
                    if (rawText == "/_blazor" || rawText.StartsWith("/_blazor/", StringComparison.Ordinal))
                    {
                        routeEndpointBuilder.RoutePattern = RoutePatternFactory.Parse(dashboardPath + rawText);
                    }

                    return;
                }

                if (componentType.Assembly != typeof(QuartzDashboardApp).Assembly
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
        List<IEndpointConventionBuilder> assetEndpoints =
        [
            MapDashboardStaticAssets(builder, pathPrefix: string.Empty)
        ];

        if (hasCustomPath)
        {
            // With a custom path the shell renders a dashboard-rooted <base href>, so the browser
            // requests the static assets and the Blazor framework plumbing under the dashboard path.
            // Mirror them there so a reverse proxy that forwards only the dashboard prefix can reach
            // them (#3134); the root asset endpoint stays for backwards compatibility.
            assetEndpoints.Add(MapDashboardStaticAssets(builder, pathPrefix: dashboardPath));
            assetEndpoints.Add(MapDashboardFrameworkScript(builder, dashboardPath));
            assetEndpoints.Add(MapDashboardOpaqueRedirect(builder, dashboardPath));
        }

        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
        {
            string policyName = options.AuthorizationPolicy;
            hub.RequireAuthorization(policyName);

            // Gate static assets with the same policy as the rest of the dashboard so the endpoints
            // carry explicit authorization metadata and keep working in applications that use a
            // fail-closed FallbackPolicy (#3097).
            foreach (IEndpointConventionBuilder assetEndpoint in assetEndpoints)
            {
                assetEndpoint.RequireAuthorization(policyName);
            }

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
            // Without a dashboard policy the dashboard adds no authorization of its own. The static
            // asset endpoints opt out of authorization so applications enforcing a fail-closed
            // FallbackPolicy can still load dashboard CSS/JS; the files are public package content (#3097).
            foreach (IEndpointConventionBuilder assetEndpoint in assetEndpoints)
            {
                assetEndpoint.AllowAnonymous();
            }

            if (dashboardOwnsComponents)
            {
                // Standalone mode: the components builder contains only dashboard pages plus the
                // Blazor framework/circuit endpoints (e.g. /_blazor). Mark the non-page endpoints
                // AllowAnonymous so the circuit stays reachable under a fail-closed FallbackPolicy;
                // the dashboard pages and the live-events hub are intentionally left without
                // metadata so they remain governed by the host's policies and scheduler data is
                // never silently exposed to anonymous users (#3117).
                components.Add(endpointBuilder =>
                {
                    if (GetComponentType(endpointBuilder) is null)
                    {
                        endpointBuilder.Metadata.Add(new AllowAnonymousAttribute());
                    }
                });
            }

            // Integrated mode: the host owns its pages and Blazor plumbing, so the dashboard does
            // not touch the shared components builder here (#3066). Set
            // QuartzDashboardOptions.AuthorizationPolicy to make the dashboard reachable under a
            // fail-closed FallbackPolicy in that case.
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

    private static readonly string[] GetAndHeadMethods = ["GET", "HEAD"];

    private static RouteHandlerBuilder MapDashboardStaticAssets(IEndpointRouteBuilder builder, string pathPrefix)
    {
        string pattern = pathPrefix.Length == 0
            ? "_content/Quartz.Dashboard/{**path}"
            : pathPrefix + "/_content/Quartz.Dashboard/{**path}";

        return builder.MapMethods(pattern, GetAndHeadMethods, async (HttpContext context, string path) =>
        {
            var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            string assetPath = $"_content/Quartz.Dashboard/{path}";
            var fileInfo = env.WebRootFileProvider.GetFileInfo(assetPath);
            if (!fileInfo.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!ContentTypeProvider.TryGetContentType(path, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            await WriteFileAsync(context, assetPath, fileInfo, contentType).ConfigureAwait(false);
        }).ExcludeFromDescription();
    }

    private static readonly Lazy<IFileProvider?> FrameworkScriptFallbackProvider = new(static () =>
    {
        try
        {
            // .NET 8 and 9 embed blazor.web.js into Microsoft.AspNetCore.Components.Endpoints; this is
            // the same source the framework-owned /_framework/blazor.web.js endpoint serves it from
            return new ManifestEmbeddedFileProvider(typeof(RazorComponentsEndpointRouteBuilderExtensions).Assembly);
        }
        catch (InvalidOperationException)
        {
            // .NET 10+ ships the script as a static web asset instead of an embedded manifest;
            // the WebRootFileProvider lookup and the root-endpoint forwarding cover those hosts
            return null;
        }
    });

    private static IEndpointConventionBuilder MapDashboardFrameworkScript(IEndpointRouteBuilder builder, string dashboardPath)
    {
        const string scriptPath = "/_framework/blazor.web.js";
        return builder.MapMethods(dashboardPath + scriptPath, GetAndHeadMethods, async (HttpContext context) =>
        {
            var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            IFileInfo fileInfo = env.WebRootFileProvider.GetFileInfo("_framework/blazor.web.js");
            if (!fileInfo.Exists && FrameworkScriptFallbackProvider.Value is { } fallbackProvider)
            {
                fileInfo = fallbackProvider.GetFileInfo("_framework/blazor.web.js");
            }

            if (fileInfo.Exists)
            {
                // parity with the framework-owned script endpoint
                context.Response.Headers.CacheControl = "no-cache";
                await WriteFileAsync(context, "_framework/blazor.web.js", fileInfo, "text/javascript").ConfigureAwait(false);
                return;
            }

            // .NET 10+ hosts serve the script through MapStaticAssets endpoints rather than the web
            // root or an embedded manifest; forward to the framework-owned root endpoint when present
            if (!await TryForwardToRootEndpointAsync(context, scriptPath).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }).ExcludeFromDescription();
    }

    private static IEndpointConventionBuilder MapDashboardOpaqueRedirect(IEndpointRouteBuilder builder, string dashboardPath)
    {
        // The framework emits enhanced-navigation redirects as URLs relative to the document base,
        // which the dashboard-rooted <base href> resolves under the dashboard path; forward those
        // requests to the framework-owned root endpoint so the redirect flow completes (#3134)
        const string redirectPath = "/_framework/opaque-redirect";
        return builder.MapGet(dashboardPath + redirectPath, async (HttpContext context) =>
        {
            if (!await TryForwardToRootEndpointAsync(context, redirectPath).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }).ExcludeFromDescription();
    }

    private static async Task<bool> TryForwardToRootEndpointAsync(HttpContext context, string rootPath)
    {
        var dataSource = context.RequestServices.GetRequiredService<EndpointDataSource>();
        RouteEndpoint? rootEndpoint = null;
        foreach (Endpoint endpoint in dataSource.Endpoints)
        {
            if (endpoint is RouteEndpoint { RequestDelegate: not null } routeEndpoint)
            {
                string? rawText = routeEndpoint.RoutePattern.RawText;
                if (rawText is not null
                    && (rootPath.Equals(rawText, StringComparison.OrdinalIgnoreCase)
                        || (rawText.Length == rootPath.Length - 1 && rootPath.EndsWith(rawText, StringComparison.OrdinalIgnoreCase))))
                {
                    rootEndpoint = routeEndpoint;
                    break;
                }
            }
        }

        if (rootEndpoint is null)
        {
            return false;
        }

        // Handlers resolve content from the request path and the endpoint metadata, so both must
        // describe the root endpoint while its delegate runs
        PathString originalPath = context.Request.Path;
        Endpoint? originalEndpoint = context.GetEndpoint();
        context.Request.Path = rootPath;
        context.SetEndpoint(rootEndpoint);
        try
        {
            await rootEndpoint.RequestDelegate!(context).ConfigureAwait(false);
        }
        finally
        {
            context.Request.Path = originalPath;
            context.SetEndpoint(originalEndpoint);
        }

        return true;
    }

    private static readonly ConcurrentDictionary<string, (long Length, DateTimeOffset LastModified, EntityTagHeaderValue ETag)> ETagCache = new();

    private static async Task WriteFileAsync(HttpContext context, string assetPath, IFileInfo fileInfo, string contentType)
    {
        // Stable validator so browsers can revalidate with If-None-Match and get a 304
        // instead of re-downloading the body on every full page load
        EntityTagHeaderValue etag = await GetETagAsync(assetPath, fileInfo, context.RequestAborted).ConfigureAwait(false);
        context.Response.Headers.ETag = etag.ToString();

        foreach (EntityTagHeaderValue requestTag in context.Request.GetTypedHeaders().IfNoneMatch)
        {
            // RFC 9110: "*" matches any current representation
            if (EntityTagHeaderValue.Any.Equals(requestTag) || requestTag.Compare(etag, useStrongComparison: false))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }
        }

        context.Response.ContentType = contentType;
        context.Response.ContentLength = fileInfo.Length;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.SendFileAsync(fileInfo, context.RequestAborted).ConfigureAwait(false);
    }

    private static async ValueTask<EntityTagHeaderValue> GetETagAsync(string assetPath, IFileInfo fileInfo, CancellationToken cancellationToken)
    {
        // Content-derived so identical files validate identically across machines and restarts
        // (the embedded provider stamps LastModified from the assembly file's write time, which
        // differs per instance); Length + LastModified only guard the per-process cache entry
        if (ETagCache.TryGetValue(assetPath, out var cached)
            && cached.Length == fileInfo.Length
            && cached.LastModified == fileInfo.LastModified)
        {
            return cached.ETag;
        }

        byte[] hash;
        Stream stream = fileInfo.CreateReadStream();
        await using (stream.ConfigureAwait(false))
        {
            hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(hash)}\"");
        ETagCache[assetPath] = (fileInfo.Length, fileInfo.LastModified, etag);
        return etag;
    }
}
