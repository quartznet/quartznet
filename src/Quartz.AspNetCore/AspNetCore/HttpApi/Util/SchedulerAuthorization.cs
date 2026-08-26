using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Holds <see cref="QuartzHttpApiOptions.SchedulerAuthorizationPolicy" /> against the scheduler a request
/// names.
/// </summary>
/// <remarks>
/// The check is a filter over the whole route rather than a call inside each handler, so it runs before
/// anything is read: before the scheduler is looked up, and before a request body is bound. That ordering
/// is the point — a caller who fails the policy gets the same answer whether or not the scheduler exists,
/// so a <c>404</c> only ever answers a name the caller was allowed to ask about.
/// </remarks>
internal static class SchedulerAuthorization
{
    /// <summary>
    /// The route parameter every scheduler-scoped endpoint carries. A route without it — the scheduler
    /// listing — is about no single scheduler and filters its own answer instead.
    /// </summary>
    internal const string SchedulerNameRouteValue = "schedulerName";

    /// <summary>
    /// Puts <paramref name="policyName" /> in front of the endpoint, when the endpoint names a scheduler.
    /// </summary>
    /// <remarks>
    /// Whether it does is read from the route pattern rather than passed in, so an endpoint added later
    /// is covered by carrying <c>{schedulerName}</c> like every other one and by nothing else.
    /// </remarks>
    public static void RequireSchedulerAuthorization(this RouteHandlerBuilder builder, string policyName)
    {
        builder.Add(endpointBuilder =>
        {
            if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder || !NamesAScheduler(routeEndpointBuilder.RoutePattern))
            {
                return;
            }

            // Metadata rather than ProducesProblem(403) at the map site: the map site does not know
            // whether the route names a scheduler, and this convention does.
            endpointBuilder.Metadata.Add(new ProducesResponseTypeMetadata(
                StatusCodes.Status403Forbidden,
                typeof(ProblemDetails),
                ["application/problem+json"]));

            RequestDelegate next = endpointBuilder.RequestDelegate
                ?? throw new InvalidOperationException($"Endpoint {endpointBuilder.DisplayName} has null RequestDelegate");

            endpointBuilder.RequestDelegate = context => Authorize(context, policyName, next);
        });
    }

    private static bool NamesAScheduler(RoutePattern pattern)
    {
        foreach (RoutePatternParameterPart parameter in pattern.Parameters)
        {
            if (string.Equals(parameter.Name, SchedulerNameRouteValue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task Authorize(HttpContext context, string policyName, RequestDelegate next)
    {
        if (context.Request.RouteValues[SchedulerNameRouteValue] is string schedulerName
            && !await IsAuthorized(context, policyName, schedulerName).ConfigureAwait(false))
        {
            await Forbidden(schedulerName).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the request's user may act on <paramref name="schedulerName" />. A null or blank policy is
    /// the default configuration, where every caller who reached the endpoint may act on every scheduler.
    /// </summary>
    public static ValueTask<bool> IsAuthorized(
        HttpContext context,
        string? policyName,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            return new ValueTask<bool>(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(context, policyName, schedulerName);

        static async ValueTask<bool> Evaluate(HttpContext context, string policyName, string schedulerName)
        {
            // Resolved per request rather than injected: IAuthorizationService is transient, and an API
            // with no policy configured must keep working in a container that has none registered.
            IAuthorizationService authorizationService = context.RequestServices.GetRequiredService<IAuthorizationService>();
            AuthorizationResult result = await authorizationService
                .AuthorizeAsync(context.User, new SchedulerResource(schedulerName), policyName)
                .ConfigureAwait(false);

            return result.Succeeded;
        }
    }

    /// <summary>
    /// The refusal, in the problem-details shape every other error the API produces takes.
    /// </summary>
    /// <remarks>
    /// No <c>Quartz-ExceptionType</c>: that member names the exception a failure came from, and a policy
    /// that said no is a decision rather than a failure. The detail names the scheduler the caller asked
    /// for, which is the caller's own input, and says nothing about whether it exists.
    /// </remarks>
    private static IResult Forbidden(string schedulerName)
    {
        return Results.Problem(
            detail: $"Not authorized for scheduler {schedulerName}",
            statusCode: StatusCodes.Status403Forbidden);
    }
}
