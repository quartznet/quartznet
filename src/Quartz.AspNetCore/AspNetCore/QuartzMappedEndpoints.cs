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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Quartz.AspNetCore;

/// <summary>
/// Where the startup guard reads endpoints from: the route builders <c>MapQuartzHttpApi</c> and
/// <c>MapQuartzDashboard</c> were called on.
/// </summary>
/// <remarks>
/// <para>
/// Not the container's <see cref="EndpointDataSource" />, although that is the obvious place to look. It
/// is a composite over the data sources of the route builder the middleware pipeline owns, and that
/// pipeline is built inside the web host's <c>StartAsync</c> — after every hosted service's
/// <c>StartingAsync</c> has run. Read there, it answers zero endpoints and the guard would pass
/// everything in silence. The collection a <c>Map</c> call was made on is populated as the call is made,
/// which for a <c>WebApplication</c> is before the host is started at all.
/// </para>
/// <para>
/// The collections are held rather than their contents: an <see cref="EndpointDataSource" /> builds its
/// endpoints when it is enumerated, so what is captured here is where to look and the looking happens at
/// startup.
/// </para>
/// </remarks>
internal sealed class QuartzMappedEndpoints
{
    private readonly List<ICollection<EndpointDataSource>> sources = [];
    private readonly Lock gate = new();

    /// <summary>
    /// Remembers where <paramref name="builder" /> collects its endpoints.
    /// </summary>
    /// <remarks>
    /// A <see cref="RouteGroupBuilder" /> is passed over. Its own data sources answer with the endpoints
    /// as they were mapped — before the group's prefix and the group's conventions, and
    /// <c>MapGroup("/ops").RequireAuthorization()</c> is one of those conventions — so reading them here
    /// would refuse a mapping the application had authorized. The application's own builder holds the
    /// group as a single data source that answers with the finished endpoints, and that is what the
    /// container's <see cref="EndpointDataSource" /> is composed of by the time the host has started.
    /// </remarks>
    public void Track(IEndpointRouteBuilder builder)
    {
        if (builder is RouteGroupBuilder)
        {
            return;
        }

        ICollection<EndpointDataSource> dataSources = builder.DataSources;
        lock (gate)
        {
            foreach (ICollection<EndpointDataSource> tracked in sources)
            {
                if (ReferenceEquals(tracked, dataSources))
                {
                    return;
                }
            }

            sources.Add(dataSources);
        }
    }

    /// <summary>
    /// Every endpoint reachable from a tracked route builder, built as this is read.
    /// </summary>
    public List<Endpoint> Endpoints()
    {
        ICollection<EndpointDataSource>[] snapshot;
        lock (gate)
        {
            snapshot = sources.ToArray();
        }

        List<Endpoint> endpoints = [];
        foreach (ICollection<EndpointDataSource> dataSources in snapshot)
        {
            foreach (EndpointDataSource dataSource in dataSources.ToArray())
            {
                endpoints.AddRange(dataSource.Endpoints);
            }
        }

        return endpoints;
    }
}
