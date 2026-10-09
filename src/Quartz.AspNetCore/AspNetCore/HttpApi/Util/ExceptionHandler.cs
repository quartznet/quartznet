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
        logger = loggerFactory.CreateLogger(HttpApiLog.Category);
    }

    /// <summary>
    /// Turns an exception into the problem details the API answers errors with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a failure is to the caller is <see cref="ProblemDetailsFactory" />'s decision, shared with
    /// every other carrier of the contract so that one failure is one body whichever carrier answers it;
    /// this writes the <c>IResult</c> and the log line, and the level of the line says who has to act. A
    /// <c>BadHttpRequestException</c> is ASP.NET Core's own, raised by its binding and by the endpoints'
    /// validation, and is answered here with its own status before the factory is asked.
    /// </para>
    /// </remarks>
    public IResult HandleException(Exception exception, HttpContext context)
    {
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            logger.BadHttpRequest(exception);
            return Problem(exception, GetMessageWithInnerExceptionMessage(exception), badHttpRequestException.StatusCode);
        }

        ProblemClassification problem = ProblemDetailsFactory.Classify(exception, includeStackTrace);

        switch (problem.Kind)
        {
            case ProblemKind.RequestRefused:
                logger.BadHttpRequest(exception);
                break;
            case ProblemKind.Deserialization:
                logger.RequestDeserializationFailed(exception);
                break;
            case ProblemKind.NotFound:
                logger.NotFound(exception);
                break;
            case ProblemKind.NotServed:
                logger.NotServed(exception);
                break;
            case ProblemKind.Forbidden:
                // Warning, not Debug: the caller got the request right and a rule the operator configured
                // said no, which is the one thing on this path an operator asked to be told about.
                logger.Forbidden(exception.Message);
                break;
            case ProblemKind.SchedulerFault:
                logger.SchedulerExceptionHandlingRequest(context.Request.GetDisplayUrl(), exception);
                break;
            default:
                logger.ExceptionHandlingRequest(context.Request.GetDisplayUrl(), exception);
                break;
        }

        return Problem(exception, problem.Detail, (int) problem.Status, nameTheExceptionType: problem.ExceptionType is not null, problem.ExceptionType);
    }

    /// <summary>
    /// Records that the caller of <paramref name="context" /> went away before the request finished.
    /// </summary>
    /// <remarks>
    /// Here rather than in the wrapper that catches it, so that everything the API logs about a request
    /// goes through one logger under one category. Nothing is written to the response: its headers are
    /// long gone by the time a stream is abandoned, and there is nobody to read a body either way.
    /// </remarks>
    public void HandleAbandonedRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        logger.RequestAbandoned(context.Request.GetDisplayUrl());
    }

    /// <summary>
    /// What a <c>500</c> says instead of the exception's message: <see cref="ProblemDetailsFactory.ServerFaultDetail" />.
    /// </summary>
    internal const string ServerFaultDetail = ProblemDetailsFactory.ServerFaultDetail;

    private static string GetMessageWithInnerExceptionMessage(Exception exception)
    {
        return exception.InnerException is not null ? $"{exception.Message} {exception.InnerException.Message}" : exception.Message;
    }

    private IResult Problem(Exception exception, string detail, int statusCode, bool nameTheExceptionType = true, string? exceptionType = null)
    {
        Dictionary<string, object?> extensions = new();

        if (nameTheExceptionType)
        {
            extensions.Add(HttpApiConstants.ProblemDetailsExceptionType, exceptionType ?? exception.GetType().Name);
        }

        if (includeStackTrace)
        {
            extensions.Add(HttpApiConstants.ProblemDetailsStackTrace, exception.StackTrace);
        }

        return Results.Problem(detail: detail, statusCode: statusCode, extensions: extensions);
    }
}
