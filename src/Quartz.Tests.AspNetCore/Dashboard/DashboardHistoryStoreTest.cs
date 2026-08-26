using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// What the dashboard's own history store keeps, and what it forgets.
/// </summary>
/// <remarks>
/// It used to be bounded by count alone, which says nothing about a scheduler that has gone quiet: it
/// keeps whatever it last recorded, so the page shows executions from an arbitrary distance in the past
/// with nothing to say how old they are. Both bounds are exercised here, on a clock the test moves —
/// a retention window measured against the wall clock is a test that passes for the wrong reason.
/// </remarks>
public class DashboardHistoryStoreTest
{
    private const string SchedulerName = "TestScheduler";
    private static readonly DateTimeOffset Start = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AnExecutionOlderThanTheRetentionWindowIsForgotten()
    {
        FakeTimeProvider clock = new(Start);
        DashboardHistoryStore store = Store(clock, retention: TimeSpan.FromHours(1));

        await store.Add(Entry(Start, "nightly"));

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
        DashboardHistoryStore store = Store(clock, retention: TimeSpan.FromHours(1));

        await store.Add(Entry(Start, "nightly"));
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
        DashboardHistoryStore store = Store(clock, maxEntriesPerScheduler: 3);

        for (int index = 0; index < 5; index++)
        {
            await store.Add(Entry(Start.AddSeconds(index), "job" + index));
        }

        PagedResult<DashboardHistoryEntry> page = await Page(store);

        page.Items.Select(entry => entry.JobName).Should().Equal(["job4", "job3", "job2"],
            "the cap drops the oldest and the page reads newest first");
    }

    [Test]
    public async Task TheTwoBoundsApplyTogether()
    {
        FakeTimeProvider clock = new(Start);
        DashboardHistoryStore store = Store(clock, retention: TimeSpan.FromMinutes(10), maxEntriesPerScheduler: 100);

        await store.Add(Entry(Start.AddMinutes(-30), "old"));
        await store.Add(Entry(Start, "fresh"));

        PagedResult<DashboardHistoryEntry> page = await Page(store);

        page.Items.Select(entry => entry.JobName).Should().Equal(["fresh"],
            "the count bound had room for both, so it is the age bound that dropped the old one");
    }

    [Test]
    public async Task ExecutionsCanBeReadForOneNode()
    {
        DashboardHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.Add(Entry(Start, "on-a", node: "node-a"));
        await store.Add(Entry(Start, "on-b", node: "node-b"));

        PagedResult<DashboardHistoryEntry> everywhere = await Page(store);
        everywhere.Items.Should().HaveCount(2, "an unfiltered query is every node's");

        PagedResult<DashboardHistoryEntry> onB = await Page(store, node: "node-b");
        onB.Items.Should().ContainSingle().Which.JobName.Should().Be("on-b",
            "a cluster's history is unreadable until it can be narrowed to one machine");
    }

    [Test]
    public async Task AMisfireIsRecordedBesideTheExecutionsAndReadBack()
    {
        DashboardHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.Add(Entry(Start, "ran"));
        await store.AddMisfire(Misfire(Start, "nightly"));

        (await Page(store)).Items.Should().ContainSingle(
            "a misfire is not an execution — nothing ran — so it must not appear in the history");

        PagedResult<DashboardMisfireEntry> misfires = await store.GetMisfires(
            new DashboardMisfireQuery { SchedulerName = SchedulerName, IncludeTotalCount = true });

        DashboardMisfireEntry misfire = misfires.Items.Should().ContainSingle().Subject;
        misfire.TriggerName.Should().Be("nightly");
        misfire.SchedulerInstanceId.Should().Be("node-a");
        misfire.ScheduledFireTimeUtc.Should().Be(Start.AddMinutes(-5),
            "the missed firing is the point of the row: it says what did not happen and when");
    }

    [Test]
    public async Task MisfiresAreBoundedTheWayExecutionsAre()
    {
        FakeTimeProvider clock = new(Start);
        DashboardHistoryStore store = Store(clock, retention: TimeSpan.FromHours(1), maxEntriesPerScheduler: 2);

        await store.AddMisfire(Misfire(Start, "one"));
        await store.AddMisfire(Misfire(Start.AddSeconds(1), "two"));
        await store.AddMisfire(Misfire(Start.AddSeconds(2), "three"));

        PagedResult<DashboardMisfireEntry> capped = await store.GetMisfires(
            new DashboardMisfireQuery { SchedulerName = SchedulerName });
        capped.Items.Select(entry => entry.TriggerName).Should().Equal(["three", "two"],
            "the cap is per feed, and the misfire feed is not exempt from it");

        clock.Advance(TimeSpan.FromHours(2));
        PagedResult<DashboardMisfireEntry> aged = await store.GetMisfires(
            new DashboardMisfireQuery { SchedulerName = SchedulerName });
        aged.Items.Should().BeEmpty("the retention window covers misfires too");
    }

    [Test]
    public async Task MisfiresAreCountedOverAWindow()
    {
        DashboardHistoryStore store = Store(new FakeTimeProvider(Start));

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
        DashboardHistoryStore store = Store(new FakeTimeProvider(Start));

        await store.AddMisfire(Misfire(Start, "on-a", node: "node-a"));
        await store.AddMisfire(Misfire(Start, "on-b", node: "node-b"));

        PagedResult<DashboardMisfireEntry> onA = await store.GetMisfires(
            new DashboardMisfireQuery { SchedulerName = SchedulerName, SchedulerInstanceId = "node-a" });

        onA.Items.Should().ContainSingle().Which.TriggerName.Should().Be("on-a");
    }

    private static DashboardHistoryStore Store(
        FakeTimeProvider clock,
        TimeSpan? retention = null,
        int? maxEntriesPerScheduler = null)
    {
        return TestData.Dashboard.HistoryStore(clock, retention, maxEntriesPerScheduler);
    }

    private static ValueTask<PagedResult<DashboardHistoryEntry>> Page(DashboardHistoryStore store, string? node = null)
    {
        return store.GetPage(new DashboardHistoryQuery
        {
            SchedulerName = SchedulerName,
            SchedulerInstanceId = node,
            IncludeTotalCount = true
        });
    }

    private static DashboardHistoryEntry Entry(DateTimeOffset firedAt, string jobName, string node = "node-a") => new(
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

    private static DashboardMisfireEntry Misfire(DateTimeOffset misfiredAt, string triggerName, string node = "node-a") => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: node,
        TriggerGroup: "DummyTriggerGroup",
        TriggerName: triggerName,
        JobKey: new JobKeyDto("DummyGroup", "DummyJob"),
        MisfiredAtUtc: misfiredAt,
        ScheduledFireTimeUtc: misfiredAt.AddMinutes(-5));
}
