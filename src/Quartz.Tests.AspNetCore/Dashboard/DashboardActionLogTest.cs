using System.Security.Claims;

using FakeItEasy;

using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// Where the action landed is on the entry and at the end of the log line: the scheduler's origin, and
    /// the node the listing said was behind it.
    /// </summary>
    /// <remarks>
    /// An operator reading "PauseTrigger on acme" afterwards cannot tell from it whether the action ran in
    /// this process or in the one a remote registration points at, and the dashboard is the only thing that
    /// knows — the scheduler it drove is the one its picker last listed.
    /// </remarks>
    [Test]
    public void AnEntrySaysWhereTheSchedulerIsAndWhichNodeAnswered()
    {
        RecordingLogger logger = new();
        DashboardActionLogService store = new();
        DashboardActionLog log = Create(logger, userName: "ops@example.com", store, SchedulerOrigin.Remote);

        log.Record("acme", "PauseTrigger", "reports.nightly", succeeded: true);

        DashboardActionLogEntry entry = store.GetLatest().Should().ContainSingle().Which;
        entry.Origin.Should().Be(SchedulerOrigin.Remote);
        entry.SchedulerInstanceId.Should().Be(Node);
        entry.NodeLocal.Should().BeFalse("pausing a trigger writes the store, which binds every node");

        logger.Entries.Should().ContainSingle().Which.Message.Should()
            .Contain("origin Remote").And.Contain("node " + Node);
    }

    /// <summary>
    /// An action the page says is node-local is marked as one, and only then.
    /// </summary>
    /// <remarks>
    /// The distinction is what makes naming a node honest: an interrupt, a start, a stand-by and a shutdown
    /// reach the one node that answered, while pausing a trigger is the whole cluster's.
    /// </remarks>
    [Test]
    public void ANodeLocalActionIsMarkedAsOne()
    {
        DashboardActionLogService store = new();
        DashboardActionLog log = Create(new RecordingLogger(), userName: "ops@example.com", store);

        log.Record("acme", "InterruptFireInstance", "fire-1", succeeded: true, nodeLocal: true);

        store.GetLatest().Should().ContainSingle().Which.NodeLocal.Should().BeTrue();
    }

    /// <summary>
    /// A scheduler this circuit has not listed is recorded as one nothing is known about, rather than as
    /// one of this container's.
    /// </summary>
    [Test]
    public void ASchedulerTheListingDoesNotCarryIsRecordedAsUnknown()
    {
        RecordingLogger logger = new();
        DashboardActionLogService store = new();
        DashboardActionLog log = Create(logger, userName: "ops@example.com", store, origin: null);

        log.Record("acme", "PauseTrigger", "reports.nightly", succeeded: true);

        DashboardActionLogEntry entry = store.GetLatest().Should().ContainSingle().Which;
        entry.Origin.Should().BeNull("nothing said where this scheduler is, and an entry must not invent it");
        entry.SchedulerInstanceId.Should().BeNull();

        logger.Entries.Should().ContainSingle().Which.Message.Should()
            .Contain("origin (unknown)").And.Contain("node (unknown)");
    }

    private const string Node = "acme-node-1";

    private static DashboardActionLog Create(
        ILogger<DashboardActionLog> logger,
        string? userName,
        DashboardActionLogService? store = null,
        SchedulerOrigin? origin = SchedulerOrigin.Container)
    {
        TestAuthenticationStateProvider authentication = new();
        if (userName is not null)
        {
            authentication.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "test"));
        }

        // What the scheduler picker last listed, which is where the dashboard's answer to "where is this
        // scheduler" comes from. A state with no listing at all is a circuit that acted before anything
        // read one.
        SchedulerState schedulerState = new(A.Fake<IHttpContextAccessor>());
        if (origin is { } listed)
        {
            schedulerState.AvailableSchedulers = [new SchedulerHeaderDto("acme", Node, SchedulerStatus.Running, listed)];
        }

        return new DashboardActionLog(store ?? new DashboardActionLogService(), logger, authentication, schedulerState);
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
