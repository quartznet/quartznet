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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// The target a request names its scheduler's key with — <c>…/schedulers/QuartzScheduler/jobs?target=w1</c>
/// is <c>w1/QuartzScheduler</c> — read once, checked once, and echoed on the answer.
/// </summary>
/// <remarks>
/// <para>
/// A query parameter rather than part of the path: a key cannot be one path segment, because <c>%2F</c>
/// stays escaped and routing does not unescape it, and a <c>targets/{target}/…</c> prefix would double the
/// route table. Every handler still binds it, so the generated OpenAPI document shows it on every route
/// that names a scheduler; this is what the wrappers in front of the handlers read, since they run before
/// anything is bound.
/// </para>
/// <para>
/// Absent, the request is for the bare key, exactly as before 4.9.
/// </para>
/// </remarks>
internal static class TargetedRequest
{
    /// <summary>
    /// The target the request names, or <see langword="null" /> for none. Read after
    /// <see cref="RequireTargetScope" /> has refused a malformed one, which is the order every wrapper and
    /// handler that asks runs in.
    /// </summary>
    public static string? Target(HttpContext context)
    {
        return context.Request.Query.TryGetValue(HttpApiConstants.TargetQueryParameter, out StringValues values)
               && Problem(values) is null
            ? values[0]
            : null;
    }

    /// <summary>
    /// The key the request names: the route's scheduler name, and the target when the query gives one — in the
    /// target's own spelling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Targets match ignoring case, so <c>?target=W1</c> reaches the scheduler behind <c>w1</c>. A policy is
    /// asked with this key, so it is asked about <c>w1</c> however the caller spelled it: one written as
    /// <c>Target != "w1"</c> cannot be stepped round by changing the case.
    /// </para>
    /// <para>
    /// The spelling is the registered target's, else that of the window or the proxy holding the scheduler
    /// under the target — the one resolution would act on. A target nothing holds stays as typed, so the answer
    /// a refused caller gets says nothing about which targets exist.
    /// </para>
    /// </remarks>
    public static SchedulerRef Key(HttpContext context, string schedulerName)
    {
        return new SchedulerRef(schedulerName, Canonical(context.RequestServices, schedulerName, Target(context)));
    }

    /// <summary>
    /// <paramref name="target" /> in the spelling the container holds it in, or as given when it holds none.
    /// </summary>
    internal static string? Canonical(IServiceProvider services, string schedulerName, string? target)
    {
        if (target is null)
        {
            return null;
        }

        if (services.GetService<SchedulerTargets>()?.Find(target) is { } registered)
        {
            return registered.Name;
        }

        if (services.GetService<SchedulerWindowRegistry>()?.TargetOf(schedulerName) is { } window
            && string.Equals(window, target, StringComparison.OrdinalIgnoreCase))
        {
            return window;
        }

        foreach (IScheduler scheduler in services.GetService<ISchedulerRepository>()?.LookupByName(schedulerName) ?? [])
        {
            if (scheduler is IProxyScheduler { Target: { } proxyTarget } && string.Equals(proxyTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                return proxyTarget;
            }
        }

        return target;
    }

    /// <summary>
    /// Puts the check and the echo in front of everything else the endpoint does, when it names a
    /// scheduler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Outermost, so that every answer to a targeted request carries <see cref="HttpApiConstants.TargetHeader" />
    /// — a refusal by a policy and the read-only refusal included — and a malformed target is refused before
    /// a policy is asked about a key that cannot exist. A client that sent a target refuses a success
    /// without the header, which is how it tells a 4.9 host from an older one that ignored the parameter
    /// and answered for its own scheduler of the bare name.
    /// </para>
    /// <para>
    /// The echo is the target percent-encoded, as it was sent: a response header carries ASCII only, and a
    /// value that is a caller's input is never written into one raw.
    /// </para>
    /// </remarks>
    public static void RequireTargetScope(this RouteHandlerBuilder builder)
    {
        builder.Add(endpointBuilder =>
        {
            if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder
                || !SchedulerAuthorization.NamesAScheduler(routeEndpointBuilder.RoutePattern))
            {
                return;
            }

            RequestDelegate next = endpointBuilder.RequestDelegate
                ?? throw new InvalidOperationException($"Endpoint {endpointBuilder.DisplayName} has null RequestDelegate");

            endpointBuilder.RequestDelegate = context => Scope(context, next);
        });
    }

    private static async Task Scope(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Query.TryGetValue(HttpApiConstants.TargetQueryParameter, out StringValues values))
        {
            if (Problem(values) is { } problem)
            {
                // The refusal every malformed request gets, through the handler that logs it: a 400 naming
                // the request refusal, which HttpScheduler raises as such.
                IResult? refusal = context.RequestServices.GetService<ExceptionHandler>()?.HandleException(new InvalidRequestException(problem), context);
                await (refusal ?? Results.BadRequest()).ExecuteAsync(context).ConfigureAwait(false);
                return;
            }

            context.Response.Headers[HttpApiConstants.TargetHeader] = Uri.EscapeDataString(values[0]!);
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Why the query's target cannot be one, or <see langword="null" /> when it can.
    /// </summary>
    /// <remarks>
    /// Blank and repeated are refused rather than read as absent: a request that says <c>?target=</c> asked
    /// for a target, and answering it for the bare key would act on a scheduler it did not name. A <c>/</c>
    /// is the separator of a key, which no target name contains.
    /// </remarks>
    internal static string? Problem(StringValues values)
    {
        if (values.Count != 1)
        {
            return $"{HttpApiConstants.TargetQueryParameter} is given {values.Count} times; a request names one target.";
        }

        string? target = values[0];
        if (string.IsNullOrWhiteSpace(target))
        {
            return $"{HttpApiConstants.TargetQueryParameter} is empty; leave it out for this host's own scheduler, or name a target.";
        }

        if (target.Contains(SchedulerRef.Separator, StringComparison.Ordinal))
        {
            return $"{HttpApiConstants.TargetQueryParameter} '{target}' contains '{SchedulerRef.Separator}', which no target name does: "
                   + "the route carries the scheduler's name and the query its target.";
        }

        return null;
    }
}
