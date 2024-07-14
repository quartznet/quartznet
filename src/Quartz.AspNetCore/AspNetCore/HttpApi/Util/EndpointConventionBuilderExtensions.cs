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