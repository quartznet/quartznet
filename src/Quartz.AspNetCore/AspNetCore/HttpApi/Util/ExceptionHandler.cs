using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;

namespace Quartz.AspNetCore.HttpApi.Util;

internal class ExceptionHandler
{
    private readonly bool includeStackTrace;
    private readonly ILogger logger;

    public ExceptionHandler(IOptions<QuartzHttpApiOptions> options, ILoggerFactory loggerFactory)
    {
        includeStackTrace = options.Value.IncludeStackTraceInProblemDetails;
        logger = loggerFactory.CreateLogger("Quartz.HttpApi");
    }

    public IResult HandleException(Exception exception, HttpContext context)
    {
        if (exception is BadRequestException)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (exception is NotFoundException)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }

        if (exception is SchedulerException)
        {
            var schedulerExceptionExtensions = new Dictionary<string, object?>
            {
                { HttpApiConstants.ProblemDetailsExceptionType, exception.GetType().Name }
            };

            if (includeStackTrace)
            {
                schedulerExceptionExtensions.Add(HttpApiConstants.ProblemDetailsStackTrace, exception.StackTrace);
            }

            logger.LogWarning(exception, "SchedulerException thrown when handling api request to url {Url}", context.Request.GetDisplayUrl());
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: schedulerExceptionExtensions
            );
        }

        Dictionary<string, object?>? extensions = null;
        if (includeStackTrace)
        {
            extensions = new Dictionary<string, object?>
            {
                { HttpApiConstants.ProblemDetailsStackTrace, exception.StackTrace }
            };
        }

        logger.LogError(exception, "Exception thrown when handling api request to url {Url}", context.Request.GetDisplayUrl());
        return Results.Problem(
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: extensions
        );
    }
}