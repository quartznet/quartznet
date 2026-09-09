using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.AspNetCore.HttpApi.Util;

internal static class EndpointConventionBuilderExtensions
{
    public static RouteHandlerBuilder WithQuartzDefaults(this RouteHandlerBuilder builder, string name, string displayName)
    {
        builder
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithName(name)
            .WithDisplayName(displayName)
            .Add(endpoint =>
            {
                var requestDelegateToWrap = endpoint.RequestDelegate;
                if (requestDelegateToWrap is null)
                {
                    throw new InvalidOperationException($"Endpoint {endpoint.DisplayName} has null RequestDelegate");
                }

                endpoint.RequestDelegate = context => ExceptionHandlingWrapper(context, requestDelegateToWrap);
            });

        return builder;
    }

    /// <summary>
    /// Declares the <c>403</c> a job type <see cref="QuartzHttpApiOptions.IsJobTypeAllowed" /> refuses
    /// produces, on the three endpoints that take a job in their body.
    /// </summary>
    /// <remarks>
    /// Only where one can be refused, which is why it is not part of <see cref="WithQuartzDefaults" />:
    /// a described response that cannot occur is a described response a client writes a branch for. It is
    /// the rule <see cref="SchedulerAuthorization" /> follows for its own <c>403</c>, which is added to a
    /// route only when a policy is configured and only when the route names a scheduler.
    /// </remarks>
    public static RouteHandlerBuilder ProducesJobTypeRefusal(this RouteHandlerBuilder builder, QuartzHttpApiOptions options)
    {
        if (options.IsJobTypeAllowed is not null)
        {
            builder.ProducesProblem(StatusCodes.Status403Forbidden);
        }

        return builder;
    }

    private static async Task ExceptionHandlingWrapper(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            var result = context.RequestServices.GetService<ExceptionHandler>()?.HandleException(e, context);
            if (result is null)
            {
                throw;
            }

            await result.ExecuteAsync(context).ConfigureAwait(false);
        }
    }
}