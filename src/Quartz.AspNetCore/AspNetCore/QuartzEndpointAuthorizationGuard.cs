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

using System.Text;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Quartz.AspNetCore;

/// <summary>
/// Refuses to start an application whose Quartz endpoints nothing authorizes.
/// </summary>
/// <remarks>
/// <para>
/// Both HTTP surfaces are fully mutating and neither adds authentication of its own, and either can
/// schedule a job whose type is named by a string the request carries — which, with <c>Quartz.Jobs</c> on
/// the host's probing path, reaches <c>NativeJob</c> and its process. An open one is therefore remote
/// code execution rather than an information leak, and the mistake that opens it is a mapping call that
/// says nothing. So a mapping that says nothing is the thing that fails, at startup, where it is cheap.
/// </para>
/// <para>
/// The check runs in <see cref="IHostedLifecycleService.StartingAsync" />, which every hosted service
/// completes before any of them is started — so before the web host binds its listeners and before a
/// single request can arrive. An endpoint passes if it carries <see cref="IAuthorizeData" /> (someone
/// called <c>RequireAuthorization</c>, whether on the returned builder or on a group above it) or
/// <see cref="IAllowAnonymous" /> (someone said so on purpose); the whole check passes if the host has a
/// non-null <see cref="AuthorizationOptions.FallbackPolicy" />, which covers every endpoint that states
/// nothing.
/// </para>
/// <para>
/// Registered by <c>AddQuartzHttpApi</c> and by <c>AddQuartzDashboard</c>, once for both. Adding either
/// without mapping it leaves nothing marked and nothing to refuse, which is right: an application that
/// registered the services and never mapped the endpoints serves nothing.
/// </para>
/// </remarks>
internal sealed class QuartzEndpointAuthorizationGuard : IHostedLifecycleService
{
    private readonly QuartzMappedEndpoints mappedEndpoints;
    private readonly IOptions<AuthorizationOptions>? authorizationOptions;
    private readonly EndpointDataSource? containerEndpoints;

    public QuartzEndpointAuthorizationGuard(
        QuartzMappedEndpoints mappedEndpoints,
        IOptions<AuthorizationOptions>? authorizationOptions = null,
        EndpointDataSource? containerEndpoints = null)
    {
        this.mappedEndpoints = mappedEndpoints;
        this.authorizationOptions = authorizationOptions;
        this.containerEndpoints = containerEndpoints;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        Verify(mappedEndpoints.Endpoints());
        return Task.CompletedTask;
    }

    /// <summary>
    /// The same check once the pipeline exists, for the hosting shapes that map their endpoints from
    /// inside it.
    /// </summary>
    /// <remarks>
    /// Two of them. A <c>Startup.Configure</c> class, or anything else that maps inside
    /// <c>UseEndpoints</c>, builds its routes while the web host is starting — after
    /// <see cref="StartingAsync" /> has already run and found nothing. A map onto a <c>MapGroup</c> is
    /// the other: its endpoints only carry the group's prefix and the group's conventions once the
    /// group has been asked to finish them, which is what the container's
    /// <see cref="EndpointDataSource" /> holds by now. Both are caught here instead: later than is
    /// ideal, since the listener is bound by then, but the host stops on the exception and the
    /// alternative is passing them in silence.
    /// </remarks>
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        List<Endpoint> endpoints = mappedEndpoints.Endpoints();
        if (containerEndpoints is not null)
        {
            // Endpoint does not override Equals, so the default comparer is reference equality - which is
            // what is wanted: the same endpoint instance reached through two data sources is one endpoint.
            HashSet<Endpoint> seen = new(endpoints);
            foreach (Endpoint endpoint in containerEndpoints.Endpoints)
            {
                if (seen.Add(endpoint))
                {
                    endpoints.Add(endpoint);
                }
            }
        }

        Verify(endpoints);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Verify(List<Endpoint> endpoints)
    {
        if (authorizationOptions?.Value.FallbackPolicy is not null)
        {
            // A fallback policy is evaluated for every endpoint that states nothing itself, so the
            // application has already answered the question this guard asks.
            return;
        }

        Dictionary<string, (QuartzEndpointMarker Marker, List<string> Routes)> unguarded = new(StringComparer.Ordinal);
        foreach (Endpoint endpoint in endpoints)
        {
            QuartzEndpointMarker? marker = endpoint.Metadata.GetMetadata<QuartzEndpointMarker>();
            if (marker is null || marker.AuthorizedByOptions)
            {
                continue;
            }

            if (endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
                || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                continue;
            }

            if (!unguarded.TryGetValue(marker.Surface, out var surface))
            {
                surface = (marker, []);
                unguarded[marker.Surface] = surface;
            }

            surface.Routes.Add(Describe(endpoint));
        }

        if (unguarded.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(BuildMessage(unguarded));
    }

    private static string Describe(Endpoint endpoint)
    {
        return endpoint is RouteEndpoint routeEndpoint && !string.IsNullOrEmpty(routeEndpoint.RoutePattern.RawText)
            ? routeEndpoint.RoutePattern.RawText
            : endpoint.DisplayName ?? "(unnamed endpoint)";
    }

    private static string BuildMessage(Dictionary<string, (QuartzEndpointMarker Marker, List<string> Routes)> unguarded)
    {
        StringBuilder message = new();
        foreach ((QuartzEndpointMarker marker, List<string> routes) in unguarded.Values)
        {
            if (message.Length > 0)
            {
                message.AppendLine().AppendLine();
            }

            routes.Sort(StringComparer.Ordinal);
            message.Append(marker.Surface)
                .Append(" is mapped with no authorization: ")
                .Append(routes.Count)
                .Append(routes.Count == 1 ? " endpoint answers" : " endpoints answer")
                .AppendLine(" anonymously, and they can schedule, pause and shut down every scheduler in this process - including a job whose type is named by a string the request carries. Quartz refuses to start rather than serve that by accident. Say which you meant:")
                .AppendLine(marker.Remedies)
                .Append("A non-null AuthorizationOptions.FallbackPolicy satisfies this too, since it covers every endpoint that states nothing. Unauthorized: ")
                .Append(string.Join(", ", routes.Take(MaxRoutesNamed)))
                .Append(routes.Count > MaxRoutesNamed ? $", and {routes.Count - MaxRoutesNamed} more." : ".");
        }

        return message.ToString();
    }

    private const int MaxRoutesNamed = 5;
}
