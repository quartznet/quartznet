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
    /// Turns an exception into the problem details the API answers errors with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client-actionable error names the exception type it came from; a server fault does not. A
    /// <c>400</c> and a <c>404</c> are things the caller can reconstruct and handle — that is what
    /// <see cref="HttpApiConstants.ProblemDetailsExceptionType" /> is read for — so every one of them
    /// carries it whichever layer raised it. It used to ride only on the
    /// <see cref="SchedulerException" /> path, so a <c>400</c> from request validation and a
    /// <c>400</c> from the scheduler were two different shapes and a client could not tell a body the
    /// member was missing from one that never carries it.
    /// </para>
    /// <para>
    /// A <c>500</c> is a fault the caller cannot act on, and naming the type that produced it buys
    /// nothing it does not also leak. Nor does its message: an <c>ArgumentOutOfRangeException</c> from a
    /// driver names the server, the database, the login or the constraint as readily as it names a
    /// number, and the caller can do nothing with any of it. So the detail on that path is
    /// <see cref="ServerFaultDetail" />, one fixed sentence, and the real message goes to the log where
    /// the operator reading it is the one entitled to it.
    /// <see cref="QuartzHttpApiOptions.IncludeStackTraceInProblemDetails" /> — the switch that already
    /// says "I am debugging this" — puts it back.
    /// </para>
    /// </remarks>
    public IResult HandleException(Exception exception, HttpContext context)
    {
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            logger.BadHttpRequest(exception);
            return Problem(exception, GetMessageWithInnerExceptionMessage(exception), badHttpRequestException.StatusCode);
        }

        if (exception is JsonSerializationException)
        {
            logger.RequestDeserializationFailed(exception);
            return Problem(exception, GetMessageWithInnerExceptionMessage(exception), StatusCodes.Status400BadRequest);
        }

        if (exception is NotFoundException)
        {
            logger.NotFound(exception);
            return Problem(exception, GetMessageWithInnerExceptionMessage(exception), StatusCodes.Status404NotFound);
        }

        if (exception is SchedulerException)
        {
            logger.SchedulerExceptionHandlingRequest(context.Request.GetDisplayUrl(), exception);
            return Problem(exception, exception.Message, StatusCodes.Status400BadRequest);
        }

        logger.ExceptionHandlingRequest(context.Request.GetDisplayUrl(), exception);
        return Problem(
            exception,
            includeStackTrace ? exception.Message : ServerFaultDetail,
            StatusCodes.Status500InternalServerError,
            nameTheExceptionType: false);
    }

    /// <summary>
    /// What a <c>500</c> says instead of the exception's message.
    /// </summary>
    /// <remarks>
    /// Fixed text rather than an empty detail, so a client that renders the detail has something to
    /// render and one that matches on it matches on a constant rather than on whichever driver failed.
    /// </remarks>
    internal const string ServerFaultDetail = "The scheduler failed to handle the request. The failure is recorded in the server's log.";

    private static string GetMessageWithInnerExceptionMessage(Exception exception)
    {
        return exception.InnerException is not null ? $"{exception.Message} {exception.InnerException.Message}" : exception.Message;
    }

    private IResult Problem(Exception exception, string detail, int statusCode, bool nameTheExceptionType = true)
    {
        Dictionary<string, object?> extensions = new();

        if (nameTheExceptionType)
        {
            extensions.Add(HttpApiConstants.ProblemDetailsExceptionType, exception.GetType().Name);
        }

        if (includeStackTrace)
        {
            extensions.Add(HttpApiConstants.ProblemDetailsStackTrace, exception.StackTrace);
        }

        return Results.Problem(detail: detail, statusCode: statusCode, extensions: extensions);
    }
}