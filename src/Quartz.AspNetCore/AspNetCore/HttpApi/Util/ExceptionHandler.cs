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

    public IResult HandleException(Exception exception, HttpContext context)
    {
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            logger.LogDebug(exception, "BadHttpRequestException thrown");
            return Results.Problem(
                detail: GetMessageWithInnerExceptionMessage(exception),
                statusCode: badHttpRequestException.StatusCode,
                extensions: GetCommonExtensions(exception)
            );
        }

        if (exception is JsonSerializationException)
        {
            logger.LogDebug(exception, "Failed to deserialize request");
            return Results.Problem(
                detail: GetMessageWithInnerExceptionMessage(exception),
                statusCode: StatusCodes.Status400BadRequest,
                extensions: GetCommonExtensions(exception)
            );
        }

        if (exception is NotFoundException)
        {
            logger.LogDebug(exception, "NotFoundException thrown");
            return Results.Problem(
                detail: GetMessageWithInnerExceptionMessage(exception),
                statusCode: StatusCodes.Status404NotFound,
                extensions: GetCommonExtensions(exception)
            );
        }

        if (exception is SchedulerException)
        {
            var schedulerExceptionExtensions = GetCommonExtensions(exception) ?? new Dictionary<string, object?>();
            schedulerExceptionExtensions.Add(HttpApiConstants.ProblemDetailsExceptionType, exception.GetType().Name);

            logger.LogWarning(exception, "SchedulerException thrown when handling api request to url {Url}", context.Request.GetDisplayUrl());
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: schedulerExceptionExtensions
            );
        }

        logger.LogError(exception, "Exception thrown when handling api request to url {Url}", context.Request.GetDisplayUrl());
        return Results.Problem(
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: GetCommonExtensions(exception)
        );

        static string GetMessageWithInnerExceptionMessage(Exception exception)
        {
            return exception.InnerException is not null ? $"{exception.Message} {exception.InnerException.Message}" : exception.Message;
        }
    }

    private Dictionary<string, object?>? GetCommonExtensions(Exception exception)
    {
        if (!includeStackTrace)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            { HttpApiConstants.ProblemDetailsStackTrace, exception.StackTrace }
        };
    }
}