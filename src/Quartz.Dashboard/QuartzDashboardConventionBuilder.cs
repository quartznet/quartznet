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

namespace Quartz.Dashboard;

/// <summary>
/// What <c>MapQuartzDashboard</c> hands back: the dashboard's pages and its live-events hub, as one
/// thing to say something about.
/// </summary>
/// <remarks>
/// <para>
/// The hub is mapped separately from the pages — SignalR needs its own route — and it carries the same
/// scheduler data the pages render, so a <c>RequireAuthorization()</c> that reached only the pages would
/// leave the interesting half open. It is the reason these overloads return this rather than the Razor
/// components builder they used to: what the caller means to say is about the dashboard, and the
/// dashboard is both.
/// </para>
/// <para>
/// In integrated hosting the components builder is the application's own, so a convention is applied
/// only to the endpoints whose component comes from this assembly — the host's pages are not the
/// dashboard's to authorize (#3066).
/// </para>
/// </remarks>
internal sealed class QuartzDashboardConventionBuilder : IEndpointConventionBuilder
{
    private readonly IEndpointConventionBuilder components;
    private readonly Func<EndpointBuilder, bool>? componentFilter;
    private readonly IEndpointConventionBuilder hub;

    /// <param name="components">The Razor components builder the dashboard pages live in.</param>
    /// <param name="componentFilter">
    /// Which of that builder's endpoints a convention reaches, or <see langword="null" /> for all of
    /// them — which is right in standalone hosting, where the builder holds nothing else.
    /// </param>
    /// <param name="hub">The dashboard's live-events hub.</param>
    public QuartzDashboardConventionBuilder(
        IEndpointConventionBuilder components,
        Func<EndpointBuilder, bool>? componentFilter,
        IEndpointConventionBuilder hub)
    {
        this.components = components;
        this.componentFilter = componentFilter;
        this.hub = hub;
    }

    public void Add(Action<EndpointBuilder> convention)
    {
        components.Add(Restrict(convention));
        hub.Add(convention);
    }

    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        components.Finally(Restrict(finallyConvention));
        hub.Finally(finallyConvention);
    }

    private Action<EndpointBuilder> Restrict(Action<EndpointBuilder> convention)
    {
        if (componentFilter is null)
        {
            return convention;
        }

        Func<EndpointBuilder, bool> filter = componentFilter;
        return endpointBuilder =>
        {
            if (filter(endpointBuilder))
            {
                convention(endpointBuilder);
            }
        };
    }
}
