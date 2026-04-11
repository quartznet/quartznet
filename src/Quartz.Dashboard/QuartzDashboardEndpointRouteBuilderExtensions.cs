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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

        MapQuartzDashboardCore(builder, components);

        return components;
    }

    /// <summary>
    /// Maps the Quartz.NET Dashboard endpoints into an existing Blazor Server application.
    /// Use this overload when the host application already calls
    /// <c>MapRazorComponents&lt;App&gt;().AddInteractiveServerRenderMode()</c>
    /// to avoid registering a second <c>/_blazor</c> SignalR endpoint.
    /// </summary>
    /// <example>
    /// <code>
    /// var blazor = endpoints.MapRazorComponents&lt;App&gt;()
    ///     .AddInteractiveServerRenderMode();
    ///
    /// endpoints.MapQuartzDashboard(blazor);
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

        MapQuartzDashboardCore(builder, existingComponents);

        return existingComponents;
    }

    private static void MapQuartzDashboardCore(
        IEndpointRouteBuilder builder,
        RazorComponentsEndpointConventionBuilder components)
    {
        QuartzDashboardOptions options = builder.ServiceProvider.GetRequiredService<IOptions<QuartzDashboardOptions>>().Value;
        string dashboardPath = options.TrimmedDashboardPath;
        if (string.IsNullOrWhiteSpace(dashboardPath))
        {
            dashboardPath = "/quartz";
        }

        // Map SignalR hub under the dashboard path
        HubEndpointConventionBuilder hub = builder.MapHub<QuartzDashboardHub>(dashboardPath + "/hub")
            .DisableAntiforgery();

        // Serve dashboard static web assets via endpoint routing as a fallback
        // for hosts that don't configure UseStaticFiles() (e.g., API-only projects)
        MapDashboardStaticAssets(builder);

        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
        {
            hub.RequireAuthorization(options.AuthorizationPolicy);
            components.RequireAuthorization(options.AuthorizationPolicy);
        }
    }

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private static void MapDashboardStaticAssets(IEndpointRouteBuilder builder)
    {
        builder.MapGet("_content/Quartz.Dashboard/{**path}", async (HttpContext context, string path) =>
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
