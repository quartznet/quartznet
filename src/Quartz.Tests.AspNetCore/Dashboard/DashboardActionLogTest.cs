using System.Security.Claims;

using Microsoft.Extensions.Logging;

using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Dashboard.Support;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// A mutating action taken from the dashboard reaches the application's logger, not only the page that
/// lists the last 250 of them.
/// </summary>
/// <remarks>
/// The Action Log was the whole record: in one process's memory, bounded, readable only from the
/// dashboard's own page and gone at the next restart — so "who paused this trigger last Tuesday" had
/// nowhere to be answered from. The page is unchanged; the events now also go wherever the application's
/// logs go.
/// </remarks>
public class DashboardActionLogTest
{
    [Test]
    public void ASuccessfulActionIsLoggedWithTheUserTheActionAndTheTarget()
    {
        RecordingLogger logger = new();
        DashboardActionLog log = Create(logger, userName: "ops@example.com");

        log.Record("acme", "PauseTrigger", "reports.nightly", succeeded: true);

        RecordedLog entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Id.Should().Be(9100);
        entry.Message.Should().Contain("ops@example.com")
            .And.Contain("PauseTrigger")
            .And.Contain("reports.nightly")
            .And.Contain("acme");
    }

    [Test]
    public void AFailedActionIsLoggedAsOneAndCarriesTheReason()
    {
        RecordingLogger logger = new();
        DashboardActionLog log = Create(logger, userName: "ops@example.com");

        log.Record("acme", "DeleteJob", "reports.nightly", succeeded: false, "the store said no");

        RecordedLog entry = logger.Entries.Should().ContainSingle().Which;
        entry.EventId.Id.Should().Be(9101);
        entry.Message.Should().Contain("failed").And.Contain("the store said no");
    }

    /// <summary>
    /// A dashboard nothing authenticated says so rather than naming nobody, which is the truth about an
    /// entry from a mapping that said <c>AllowAnonymous()</c>.
    /// </summary>
    [Test]
    public void AnUnauthenticatedVisitorIsLoggedAsAnonymous()
    {
        RecordingLogger logger = new();
        DashboardActionLog log = Create(logger, userName: null);

        log.Record("acme", "Standby", "acme", succeeded: true);

        logger.Entries.Should().ContainSingle().Which.Message.Should().Contain("(anonymous)");
    }

    [Test]
    public void TheEntryStillReachesThePagesOwnLog()
    {
        DashboardActionLogService store = new();
        DashboardActionLog log = Create(new RecordingLogger(), userName: "ops@example.com", store);

        log.Record("acme", "ResumeTrigger", "reports.nightly", succeeded: true);

        store.GetLatest().Should().ContainSingle()
            .Which.Action.Should().Be("ResumeTrigger", "the logging is in addition to the page, not instead of it");
    }

    private static DashboardActionLog Create(ILogger<DashboardActionLog> logger, string? userName, DashboardActionLogService? store = null)
    {
        TestAuthenticationStateProvider authentication = new();
        if (userName is not null)
        {
            authentication.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "test"));
        }

        return new DashboardActionLog(store ?? new DashboardActionLogService(), logger, authentication);
    }

    private sealed record RecordedLog(LogLevel Level, EventId EventId, string Message);

    private sealed class RecordingLogger : ILogger<DashboardActionLog>
    {
        public List<RecordedLog> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new RecordedLog(logLevel, eventId, formatter(state, exception)));
        }
    }
}
