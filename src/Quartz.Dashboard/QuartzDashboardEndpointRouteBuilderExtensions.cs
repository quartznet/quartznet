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
using Quartz.Dashboard.Plugins;

namespace Quartz;

public static class QuartzDashboardEndpointRouteBuilderExtensions
{
    public static RazorComponentsEndpointConventionBuilder MapQuartzDashboard(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        QuartzDashboardOptions options = builder.ServiceProvider.GetRequiredService<IOptions<QuartzDashboardOptions>>().Value;
        DashboardLiveEventsPlugin.ServiceProvider = builder.ServiceProvider;
        DashboardHistoryPlugin.ServiceProvider = builder.ServiceProvider;
        string dashboardPath = options.TrimmedDashboardPath;
        if (string.IsNullOrWhiteSpace(dashboardPath))
        {
            dashboardPath = "/quartz";
        }

        // Map SignalR hub under the dashboard path
        HubEndpointConventionBuilder hub = builder.MapHub<QuartzDashboardHub>(dashboardPath + "/hub");

        // Map Blazor components at root level — pages already have /quartz prefix in @page directives
        RazorComponentsEndpointConventionBuilder components = builder
            .MapRazorComponents<QuartzDashboardApp>()
            .AddInteractiveServerRenderMode();

        // Serve dashboard static web assets via endpoint routing as a fallback
        // for hosts that don't configure UseStaticFiles() (e.g., API-only projects)
        MapDashboardStaticAssets(builder);

        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
        {
            hub.RequireAuthorization(options.AuthorizationPolicy);
            components.RequireAuthorization(options.AuthorizationPolicy);
        }

        return components;
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

            await using var stream = fileInfo.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
        }).ExcludeFromDescription();
    }
}
