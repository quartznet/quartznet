using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What the shipped execution history keeps, and what it forgets.
/// </summary>
/// <remarks>
/// It used to be bounded by count alone, which says nothing about a scheduler that has gone quiet: it
/// keeps whatever it last recorded, so a page shows executions from an arbitrary distance in the past
/// with nothing to say how old they are. Both bounds are exercised here, on a clock the test moves —
/// a retention window measured against the wall clock is a test that passes for the wrong reason.
/// </remarks>
public class InMemoryExecutionHistoryStoreTest
{
    private const string SchedulerName = "TestScheduler";
    private static readonly DateTimeOffset Start = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AnExecutionOlderThanTheRetentionWindowIsForgotten()
    {
        FakeTimeProvider clock = new(Start);
        InMemoryExecutionHistoryStore store = Store(clock, retention: TimeSpan.FromHours(1));

        await store.AddExecution(Entry(Start, "nightly"));

        clock.Advance(TimeSpan.FromMinutes(59));
        (await Page(store)).Items.Should().ContainSingle(
            "the window has not closed yet, and an execution inside it is what the page is for");

        clock.Advance(TimeSpan.FromMinutes(2));
        (await Page(store)).Items.Should().BeEmpty(
            "an hour was the whole window, and a row nothing can date is worse than no row");
    }

    [Test]
    public async Task ASchedulerThatHasStoppedRunningJobsStillForgets()
    {
        FakeTimeProvider clock = new(Start);
        InMemoryExecutionHistoryStore store = Store(clock, retention: TimeSpan.FromHours(1));

        await store.AddExecution(Entry(Start, "nightly"));
        clock.Advance(TimeSpan.FromHours(2));

        // nothing is added in between: this scheduler has gone quiet, which is exactly the case a
        // trim-on-write-only store cannot answer
        (await Page(store)).TotalCount.Should().Be(0,
            "reading has to apply the window too, or a scheduler that never writes again keeps its "
            + "history forever");
    }

    [Test]
    public async Task OnlyTheNewestExecutionsSurviveTheCountBound()
    {
        FakeTimeProvider clock = new(Start);
        InMemoryExecutionHistoryStore store = Store(clock, maxEntriesPerScheduler: 3);

        for (int index = 0; index < 5; index++)
        {
            await store.AddExecution(Entry(Start.AddSeconds(index), "job" + index));
        }

        PagedResult<ExecutionHistoryEntry> page = await Page(store);

        page.Items.Select(entry => entry.JobName).Should().Equal(["job4", "job3", "job2"],
            "the cap drops the oldest and the page reads newest first");
    }

    [Test]
    public async Task TheTwoBoundsApplyTogether()
    {
        FakeTimeProvider clock = new(Start);
        InMemoryExecutionHistoryStore store = Store(clock, retention: TimeSpan.FromMinutes(10), maxEntriesPerScheduler: 100);

        await store.AddExecution(Entry(Start.AddMinutes(-30), "old"));
        await store.AddExecution(Entry(Start, "fresh"));

        PagedResult<ExecutionHistoryEntry> page = await Page(store);

        page.Items.Select(entry => entry.JobName).Should().Equal(["fresh"],
            "the count bound had room for both, so it is the age bound that dropped the old one");
    }

    [Test]
    public async Task ExecutionsCanBeReadForOneNode()
    {
        InMemoryExecutionHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.AddExecution(Entry(Start, "on-a", node: "node-a"));
        await store.AddExecution(Entry(Start, "on-b", node: "node-b"));

        PagedResult<ExecutionHistoryEntry> everywhere = await Page(store);
        everywhere.Items.Should().HaveCount(2, "an unfiltered query is every node's");

        PagedResult<ExecutionHistoryEntry> onB = await Page(store, node: "node-b");
        onB.Items.Should().ContainSingle().Which.JobName.Should().Be("on-b",
            "a cluster's history is unreadable until it can be narrowed to one machine");
    }

    /// <summary>
    /// A <c>Contains</c> filter matches the group, the name, or the two joined — the reading the
    /// dashboard's history search has always had, and the one the HTTP API's query parameters carry.
    /// </summary>
    [Test]
    public async Task ExecutionsCanBeNarrowedByJobAndByTrigger()
    {
        InMemoryExecutionHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.AddExecution(Entry(Start, "nightly-report"));
        await store.AddExecution(Entry(Start, "hourly-sweep"));

        PagedResult<ExecutionHistoryEntry> byName = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            JobContains = "nightly"
        });
        byName.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly-report");

        PagedResult<ExecutionHistoryEntry> byQualifiedKey = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            JobContains = "DummyGroup.hourly"
        });
        byQualifiedKey.Items.Should().ContainSingle().Which.JobName.Should().Be("hourly-sweep",
            "group.name is how a key is written, so it is how a reader searches for one");

        PagedResult<ExecutionHistoryEntry> byTrigger = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            TriggerContains = "no-such-trigger"
        });
        byTrigger.Items.Should().BeEmpty();
    }

    [Test]
    public async Task AMisfireIsRecordedBesideTheExecutionsAndReadBack()
    {
        InMemoryExecutionHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.AddExecution(Entry(Start, "ran"));
        await store.AddMisfire(Misfire(Start, "nightly"));

        (await Page(store)).Items.Should().ContainSingle(
            "a misfire is not an execution — nothing ran — so it must not appear in the history");

        PagedResult<MisfireHistoryEntry> misfires = await store.QueryMisfires(
            new MisfireHistoryQuery { SchedulerName = SchedulerName, IncludeTotalCount = true });

        MisfireHistoryEntry misfire = misfires.Items.Should().ContainSingle().Subject;
        misfire.TriggerName.Should().Be("nightly");
        misfire.SchedulerInstanceId.Should().Be("node-a");
        misfire.ScheduledFireTimeUtc.Should().Be(Start.AddMinutes(-5),
            "the missed firing is the point of the row: it says what did not happen and when");
    }

    [Test]
    public async Task MisfiresAreBoundedTheWayExecutionsAre()
    {
        FakeTimeProvider clock = new(Start);
        InMemoryExecutionHistoryStore store = Store(clock, retention: TimeSpan.FromHours(1), maxEntriesPerScheduler: 2);

        await store.AddMisfire(Misfire(Start, "one"));
        await store.AddMisfire(Misfire(Start.AddSeconds(1), "two"));
        await store.AddMisfire(Misfire(Start.AddSeconds(2), "three"));

        PagedResult<MisfireHistoryEntry> capped = await store.QueryMisfires(
            new MisfireHistoryQuery { SchedulerName = SchedulerName });
        capped.Items.Select(entry => entry.TriggerName).Should().Equal(["three", "two"],
            "the cap is per feed, and the misfire feed is not exempt from it");

        clock.Advance(TimeSpan.FromHours(2));
        PagedResult<MisfireHistoryEntry> aged = await store.QueryMisfires(
            new MisfireHistoryQuery { SchedulerName = SchedulerName });
        aged.Items.Should().BeEmpty("the retention window covers misfires too");
    }

    [Test]
    public async Task MisfiresAreCountedOverAWindow()
    {
        InMemoryExecutionHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.AddMisfire(Misfire(Start.AddMinutes(-30), "old"));
        await store.AddMisfire(Misfire(Start.AddMinutes(-5), "recent"));
        await store.AddMisfire(Misfire(Start.AddMinutes(-1), "newest"));

        int lastTenMinutes = await store.CountMisfires(SchedulerName, Start.AddMinutes(-10));

        lastTenMinutes.Should().Be(2,
            "a summary asks how bad it is right now, which is a count over a window rather than a page");
    }

    [Test]
    public async Task MisfiresCanBeReadForOneNode()
    {
        InMemoryExecutionHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.AddMisfire(Misfire(Start, "on-a", node: "node-a"));
        await store.AddMisfire(Misfire(Start, "on-b", node: "node-b"));

        PagedResult<MisfireHistoryEntry> onA = await store.QueryMisfires(
            new MisfireHistoryQuery { SchedulerName = SchedulerName, SchedulerInstanceId = "node-a" });

        onA.Items.Should().ContainSingle().Which.TriggerName.Should().Be("on-a");
    }

    /// <summary>
    /// A page is a page here as it is over a job store: <c>skip</c>, <c>take</c> and a <c>hasMore</c>
    /// that says whether anything was left out.
    /// </summary>
    [Test]
    public async Task AHistoryIsReadAPageAtATime()
    {
        InMemoryExecutionHistoryStore store = Store(new FakeTimeProvider(Start));

        for (int index = 0; index < 5; index++)
        {
            await store.AddExecution(Entry(Start.AddSeconds(index), "job" + index));
        }

        PagedResult<ExecutionHistoryEntry> first = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            Take = 2,
            IncludeTotalCount = true
        });

        first.Items.Select(entry => entry.JobName).Should().Equal(["job4", "job3"]);
        first.HasMore.Should().BeTrue();
        first.TotalCount.Should().Be(5);

        PagedResult<ExecutionHistoryEntry> last = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            Skip = 4,
            Take = 2
        });

        last.Items.Select(entry => entry.JobName).Should().Equal(["job0"]);
        last.HasMore.Should().BeFalse();
    }

    private static InMemoryExecutionHistoryStore Store(
        FakeTimeProvider clock,
        TimeSpan? retention = null,
        int? maxEntriesPerScheduler = null)
    {
        ExecutionHistoryOptions options = new();
        if (retention is not null)
        {
            options.Retention = retention.Value;
        }

        if (maxEntriesPerScheduler is not null)
        {
            options.MaxEntriesPerScheduler = maxEntriesPerScheduler.Value;
        }

        return new InMemoryExecutionHistoryStore(Options.Create(options), clock);
    }

    private static ValueTask<PagedResult<ExecutionHistoryEntry>> Page(InMemoryExecutionHistoryStore store, string node = null)
    {
        return store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            SchedulerInstanceId = node,
            IncludeTotalCount = true
        });
    }

    private static ExecutionHistoryEntry Entry(DateTimeOffset firedAt, string jobName, string node = "node-a") => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: node,
        JobGroup: "DummyGroup",
        JobName: jobName,
        TriggerGroup: "DummyTriggerGroup",
        TriggerName: "DummyTrigger",
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null);

    private static MisfireHistoryEntry Misfire(DateTimeOffset misfiredAt, string triggerName, string node = "node-a") => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: node,
        TriggerGroup: "DummyTriggerGroup",
        TriggerName: triggerName,
        JobKey: new JobKey("DummyJob", "DummyGroup"),
        MisfiredAtUtc: misfiredAt,
        ScheduledFireTimeUtc: misfiredAt.AddMinutes(-5));
}
