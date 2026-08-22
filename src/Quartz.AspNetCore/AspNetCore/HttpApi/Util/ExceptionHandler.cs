using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;

namespace Quartz.AspNetCore.HttpApi.Util;

internal sealed class ExceptionHandler
{
    private readonly bool includeStackTrace;
    private readonly ILogger logger;

    public ExceptionHandler(IOptions<QuartzHttpApiOptions> options, ILoggerFactory loggerFactory)
    {
        includeStackTrace = options.Value.IncludeStackTraceInProblemDetails;
        logger = loggerFactory.CreateLogger("Quartz.HttpApi");
    }

    /// <summary>
    /// Turns an exception into the one problem-details shape the API answers errors with.
    /// </summary>
    /// <remarks>
    /// Which layer raised the exception decides the status code and the wording, never the members:
    /// every error body carries <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c> and
    /// <see cref="HttpApiConstants.ProblemDetailsExceptionType" />. The exception type used to ride
    /// only on the <see cref="SchedulerException" /> path, so a client could not tell a body it was
    /// missing from a body that never carries it, and a 400 meant two different shapes.
    /// </remarks>
    public IResult HandleException(Exception exception, HttpContext context)
    {
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            logger.LogDebug(exception, "BadHttpRequestException thrown");
            return Problem(exception, GetMessageWithInnerExceptionMessage(exception), badHttpRequestException.StatusCode);
        }

        if (exception is JsonSerializationException)
        {
            logger.LogDebug(exception, "Failed to deserialize request");
            return Problem(exception, GetMessageWithInnerExceptionMessage(exception), StatusCodes.Status400BadRequest);
        }

        if (exception is NotFoundException)
        {
            logger.LogDebug(exception, "NotFoundException thrown");
            return Problem(exception, GetMessageWithInnerExceptionMessage(exception), StatusCodes.Status404NotFound);
        }

        if (exception is SchedulerException)
        {
            logger.LogWarning(exception, "SchedulerException thrown when handling api request to url {Url}", context.Request.GetDisplayUrl());
            return Problem(exception, exception.Message, StatusCodes.Status400BadRequest);
        }

        logger.LogError(exception, "Exception thrown when handling api request to url {Url}", context.Request.GetDisplayUrl());
        return Problem(exception, exception.Message, StatusCodes.Status500InternalServerError);

        static string GetMessageWithInnerExceptionMessage(Exception exception)
        {
            return exception.InnerException is not null ? $"{exception.Message} {exception.InnerException.Message}" : exception.Message;
        }
    }

    private IResult Problem(Exception exception, string detail, int statusCode)
    {
        Dictionary<string, object?> extensions = new()
        {
            { HttpApiConstants.ProblemDetailsExceptionType, exception.GetType().Name }
        };

        if (includeStackTrace)
        {
            extensions.Add(HttpApiConstants.ProblemDetailsStackTrace, exception.StackTrace);
        }

        return Results.Problem(detail: detail, statusCode: statusCode, extensions: extensions);
    }
}