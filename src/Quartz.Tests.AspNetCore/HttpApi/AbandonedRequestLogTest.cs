using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HttpApi;
using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The Debug line the API writes when the caller goes away before a request finishes. Guarded by
/// <c>ILogger.IsEnabled</c>, because the URL is built by a method call, so asserted with the level on.
/// </summary>
public class AbandonedRequestLogTest
{
    private const int RequestAbandonedEventId = 9006;

    [Test]
    public void AnAbandonedRequestIsLoggedWithItsUrl()
    {
        RecordingLoggerProvider logs = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        ExceptionHandler handler = new(Options.Create(new QuartzHttpApiOptions()), factory);

        DefaultHttpContext context = new();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("scheduler.example");
        context.Request.Path = "/schedulers/acme/events";

        handler.HandleAbandonedRequest(context);

        RecordedLogEntry entry = logs.WithEventId(RequestAbandonedEventId).Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Debug);
        entry.Properties["Url"].Should().Be("https://scheduler.example/schedulers/acme/events");
    }
}
