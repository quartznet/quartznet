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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Holds <see cref="QuartzHttpApiOptions.OperationAuthorizationPolicy" /> against the operation a request
/// is, on the scheduler it names.
/// </summary>
/// <remarks>
/// <para>
/// A filter over the whole route, as <see cref="SchedulerAuthorization" /> is, and inside it: a caller who
/// may not see a scheduler is told that first, and one who may is told what their role does not allow
/// before a body is read or the scheduler looked up. The level is the route's, from
/// <see cref="SchedulerRoutes" />; the one route whose level depends on its body, a trigger-now carrying
/// job data, asks again through <see cref="RefuseJobData" /> once the body is read.
/// </para>
/// <para>
/// The refusal writes its own <c>403</c> rather than raising <see cref="ForbiddenException" />, so it is
/// logged once, as <c>9009</c> naming the user, and not again as the <c>9005</c> that a refusal of
/// everybody is.
/// </para>
/// </remarks>
internal static class OperationAuthorization
{
    /// <summary>
    /// Puts <paramref name="policyName" /> in front of the endpoint at the level its route needs, when the
    /// endpoint names a scheduler. The listing names none and filters its own answer.
    /// </summary>
    public static void RequireOperationAuthorization(this RouteHandlerBuilder builder, string policyName)
    {
        builder.Add(endpointBuilder =>
        {
            if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder
                || !SchedulerAuthorization.NamesAScheduler(routeEndpointBuilder.RoutePattern))
            {
                return;
            }

            WireRoute route = endpointBuilder.Metadata.OfType<WireRoute>().LastOrDefault()
                ?? throw new InvalidOperationException($"Endpoint {endpointBuilder.DisplayName} names no route of the wire contract.");

            endpointBuilder.Metadata.Add(new ProducesResponseTypeMetadata(
                StatusCodes.Status403Forbidden,
                typeof(ProblemDetails),
                ["application/problem+json"]));

            RequestDelegate next = endpointBuilder.RequestDelegate
                ?? throw new InvalidOperationException($"Endpoint {endpointBuilder.DisplayName} has null RequestDelegate");

            endpointBuilder.RequestDelegate = context => Authorize(context, policyName, route, next);
        });
    }

    private static async Task Authorize(HttpContext context, string policyName, WireRoute route, RequestDelegate next)
    {
        if (context.Request.RouteValues[SchedulerAuthorization.SchedulerNameRouteValue] is string schedulerName
            && await Refusal(context, policyName, schedulerName, route, route.Access, route.Name, context.RequestAborted).ConfigureAwait(false) is { } refusal)
        {
            await refusal.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// The <c>403</c> a trigger-now carrying job data is answered with when the caller's role is below
    /// <see cref="SchedulerAccess.Administer" />, or <see langword="null" /> when it may go ahead.
    /// </summary>
    /// <remarks>
    /// Asked from the handler, once the body is bound: the route filter has already let the caller through
    /// at <see cref="SchedulerAccess.Operate" />, which is a trigger-now without data. With no policy
    /// configured this asks nothing.
    /// </remarks>
    public static ValueTask<IResult?> RefuseJobData(HttpContext context, string schedulerName, JobDataMap? jobData, CancellationToken cancellationToken)
    {
        string? policyName = context.RequestServices.GetService<IOptions<QuartzHttpApiOptions>>()?.Value.OperationAuthorizationPolicy;
        if (string.IsNullOrWhiteSpace(policyName) || !SchedulerRoutes.CarriesJobData(jobData))
        {
            return new ValueTask<IResult?>((IResult?) null);
        }

        WireRoute route = SchedulerRoutes.TriggerJob;
        return Refusal(context, policyName, schedulerName, route, SchedulerRoutes.RequiredAccess(route, carriesJobData: true), route.Name + " with job data", cancellationToken);
    }

    /// <summary>
    /// Whether the request's user may call <paramref name="route" /> on <paramref name="schedulerName" />
    /// at <paramref name="access" />. A null or blank policy is the default configuration, where every caller
    /// who reached the endpoint may.
    /// </summary>
    public static ValueTask<bool> IsAllowed(
        HttpContext context,
        string? policyName,
        string schedulerName,
        WireRoute route,
        SchedulerAccess access,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            return new ValueTask<bool>(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(context, policyName, schedulerName, route, access);

        static async ValueTask<bool> Evaluate(HttpContext context, string policyName, string schedulerName, WireRoute route, SchedulerAccess access)
        {
            // Resolved per request, as SchedulerAuthorization does: IAuthorizationService is transient.
            IAuthorizationService authorizationService = context.RequestServices.GetRequiredService<IAuthorizationService>();
            SchedulerResource resource = new(schedulerName) { Operation = route.Name, Access = access };
            AuthorizationResult result = await authorizationService.AuthorizeAsync(context.User, resource, policyName).ConfigureAwait(false);
            return result.Succeeded;
        }
    }

    private static async ValueTask<IResult?> Refusal(
        HttpContext context,
        string policyName,
        string schedulerName,
        WireRoute route,
        SchedulerAccess access,
        string operation,
        CancellationToken cancellationToken)
    {
        if (await IsAllowed(context, policyName, schedulerName, route, access, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        context.RequestServices.GetService<MutationAudit>()?.Refused(context, operation, schedulerName, access);
        return Forbidden(operation, schedulerName, access);
    }

    /// <summary>
    /// The refusal, in the problem-details shape every other error the API produces takes, and without a
    /// <c>Quartz-ExceptionType</c>: a rule said no, nothing failed.
    /// </summary>
    /// <remarks>
    /// The detail names the operation, the scheduler as the caller spelled it, and the role the operation
    /// needs — all of which the caller sent or can read in the documentation — and nothing about the
    /// roles the caller holds.
    /// </remarks>
    internal static IResult Forbidden(string operation, string schedulerName, SchedulerAccess access)
    {
        return ContractProblem.Problem(Detail(operation, schedulerName, access), StatusCodes.Status403Forbidden);
    }

    internal static string Detail(string operation, string schedulerName, SchedulerAccess access)
    {
        return $"Not authorized for {operation} on scheduler {schedulerName}: it needs {access}";
    }
}
