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
using Microsoft.Extensions.Logging;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Writes the line that says a request changed something, and who asked for it.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than a member of <see cref="ExceptionHandler" />, which is where everything else
/// the API logs about a request is written from: a mutation that succeeded is not an exception, and a
/// handler named for them would be the wrong place to look for it. Both write under
/// <see cref="HttpApiLog.Category" />, so an operator still filters the API by one name.
/// </para>
/// <para>
/// A singleton, because nothing here is a request's: what varies per request is read off the
/// <see cref="HttpContext" /> it is handed. The alternative — resolving <see cref="ILoggerFactory" />
/// per request and asking it for the category — takes a lock inside the factory on a path that runs for
/// every mutation.
/// </para>
/// </remarks>
internal sealed class MutationAudit
{
    private readonly ILogger logger;

    public MutationAudit(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        logger = loggerFactory.CreateLogger(HttpApiLog.Category);
    }

    /// <summary>
    /// Records that <paramref name="context" />'s request changed something.
    /// </summary>
    /// <remarks>
    /// Called once per successful mutating request, from the wrapper that already decides what a mutating
    /// request is — so a route added later is audited by carrying the same marker
    /// <see cref="QuartzHttpApiOptions.ReadOnly" /> refuses it by, and by nothing else.
    /// </remarks>
    public void Record(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        logger.MutationPerformed(UserName(context), Operation(context), SchedulerName(context), context.Request.Path.ToString());
    }

    /// <summary>
    /// Who made the request, or <c>(anonymous)</c> where nothing authenticated.
    /// </summary>
    /// <remarks>
    /// The name the dashboard's own action log uses for the same absence. An API mapped with
    /// <c>AllowAnonymous()</c> writes it on every line, which is the record such a deployment has: that
    /// somebody who could reach the endpoint did this.
    /// </remarks>
    private static string UserName(HttpContext context)
    {
        string? name = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "(anonymous)" : name;
    }

    /// <summary>
    /// What was done, as the endpoint's own name — <c>PauseTrigger</c>, <c>Shutdown</c>.
    /// </summary>
    /// <remarks>
    /// The name <c>WithName</c> gave the route rather than the path, because it is the same word in every
    /// deployment: the path carries whatever <see cref="QuartzHttpApiOptions.ApiPath" /> is and the keys
    /// of the request's target, so an operator matching on what was done would be matching on those too.
    /// </remarks>
    private static string Operation(HttpContext context)
    {
        Endpoint? endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
               ?? endpoint?.DisplayName
               ?? "(unknown)";
    }

    /// <summary>
    /// Which scheduler the request was for, read from the route.
    /// </summary>
    /// <remarks>
    /// Every mutating route the API maps names one — that is what makes the per-scheduler policy a
    /// convention over the route pattern rather than a call in each handler — so the fallback is for a
    /// route of somebody else's that carries the marker without naming a scheduler.
    /// </remarks>
    private static string SchedulerName(HttpContext context)
    {
        return context.Request.RouteValues[SchedulerAuthorization.SchedulerNameRouteValue] as string ?? "(none)";
    }
}
