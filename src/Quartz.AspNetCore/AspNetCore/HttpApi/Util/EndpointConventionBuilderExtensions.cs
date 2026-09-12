using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Marks a route as one that changes something, which is what
/// <see cref="QuartzHttpApiOptions.ReadOnly" /> refuses.
/// </summary>
/// <remarks>
/// Metadata on the endpoint rather than a list of route names held somewhere central: the marker sits
/// on the <c>Map*</c> call that adds the route, so a route added later is either marked where it is
/// written or is a read. A test sweeps the mapped endpoints and fails a non-<c>GET</c> route that
/// carries neither the marker nor one of the two deliberate exemptions, so "somebody forgot" is a build
/// failure rather than a hole.
/// </remarks>
internal sealed class QuartzMutationMetadata
{
    public static readonly QuartzMutationMetadata Instance = new();

    private QuartzMutationMetadata()
    {
    }
}

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

    /// <summary>
    /// Says this route changes something, so that
    /// <see cref="QuartzHttpApiOptions.ReadOnly" /> can refuse it.
    /// </summary>
    /// <remarks>
    /// The <c>403</c> is described only when the API is actually configured read-only, for the reason
    /// <see cref="ProducesJobTypeRefusal" /> describes its own: a response that cannot occur is a
    /// response a client writes a branch for.
    /// </remarks>
    public static RouteHandlerBuilder WithQuartzMutation(this RouteHandlerBuilder builder, QuartzHttpApiOptions options)
    {
        builder.WithMetadata(QuartzMutationMetadata.Instance);

        if (options.ReadOnly)
        {
            builder.ProducesProblem(StatusCodes.Status403Forbidden);
        }

        return builder;
    }

    private static async Task ExceptionHandlingWrapper(HttpContext context, RequestDelegate next)
    {
        try
        {
            RefuseWhenReadOnly(context);
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller went away. Everything behind this wrapper abandons its work on
            // HttpContext.RequestAborted, so this is the ordinary end of a request that was cancelled
            // rather than a fault: a closed tab on the event stream would otherwise be logged as a server
            // error and answered with a 500 written onto a response whose headers have already gone.
            context.RequestServices.GetService<ExceptionHandler>()?.HandleAbandonedRequest(context);
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

    /// <summary>
    /// Refuses a mutating route while <see cref="QuartzHttpApiOptions.ReadOnly" /> is set.
    /// </summary>
    /// <remarks>
    /// Inside the wrapper that already turns a <see cref="ForbiddenException" /> into the <c>403</c>
    /// problem details and the <c>9005</c> log line, so a refusal here is the same answer a refused job
    /// type gives — and it is made before the handler, so no body is read and no scheduler is looked up.
    /// A read pays one metadata lookup on an endpoint that has none of it.
    /// </remarks>
    private static void RefuseWhenReadOnly(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<QuartzMutationMetadata>() is null)
        {
            return;
        }

        IOptions<QuartzHttpApiOptions>? options = context.RequestServices.GetService<IOptions<QuartzHttpApiOptions>>();
        if (options?.Value.ReadOnly == true)
        {
            throw ForbiddenException.ForReadOnlyApi();
        }
    }
}