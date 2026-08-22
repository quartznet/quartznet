using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HttpApi;
using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The members an error body carries, and the one setting that adds another.
/// </summary>
/// <remarks>
/// The wire snapshots pin what these bodies look like with the defaults;
/// <see cref="QuartzHttpApiOptions.IncludeStackTraceInProblemDetails" /> is off by default and is not
/// something to discover the behaviour of in production, so it is exercised here.
/// </remarks>
public class ExceptionHandlerTest
{
    [Test]
    public async Task AStackTraceIsSentOnlyWhenItIsAskedFor()
    {
        SchedulerException thrown = Thrown();

        (await BodyOf(thrown, includeStackTrace: false))
            .TryGetProperty(HttpApiConstants.ProblemDetailsStackTrace, out _)
            .Should().BeFalse("a stack trace on every error body is what the option exists to keep off");

        JsonElement withTrace = await BodyOf(thrown, includeStackTrace: true);
        withTrace.GetProperty(HttpApiConstants.ProblemDetailsStackTrace).GetString()
            .Should().Contain(nameof(Thrown));
    }

    [Test]
    public async Task TheStackTraceJoinsTheExceptionTypeRatherThanReplacingIt()
    {
        JsonElement body = await BodyOf(Thrown(), includeStackTrace: true);

        body.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString()
            .Should().Be(nameof(SchedulerException),
                "the type is what a client rebuilds the exception from, and the option is about the trace");
        body.GetProperty("status").GetInt32().Should().Be(400);
    }

    /// <summary>
    /// A fault the caller cannot act on names no type, whether or not the trace is on.
    /// </summary>
    [Test]
    public async Task AServerFaultNamesNoExceptionTypeEvenWithStackTracesOn()
    {
        InvalidOperationException fault = new("something the API never promised");

        JsonElement body = await BodyOf(fault, includeStackTrace: true);

        body.GetProperty("status").GetInt32().Should().Be(500);
        body.TryGetProperty(HttpApiConstants.ProblemDetailsExceptionType, out _)
            .Should().BeFalse("a 500 is a fault, and naming the type behind it tells a caller nothing it can use");
    }

    private static SchedulerException Thrown()
    {
        try
        {
            throw new SchedulerException("the scheduler refused");
        }
        catch (SchedulerException caught)
        {
            return caught;
        }
    }

    private static async Task<JsonElement> BodyOf(Exception exception, bool includeStackTrace)
    {
        ExceptionHandler handler = new(
            Options.Create(new QuartzHttpApiOptions { IncludeStackTraceInProblemDetails = includeStackTrace }),
            NullLoggerFactory.Instance);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddProblemDetails();
        await using ServiceProvider provider = services.BuildServiceProvider();

        DefaultHttpContext context = new() { RequestServices = provider };
        using MemoryStream body = new();
        context.Response.Body = body;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");

        await handler.HandleException(exception, context).ExecuteAsync(context);

        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body);
        return document.RootElement.Clone();
    }
}
