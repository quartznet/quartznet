#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What every <see cref="IExecutionHistoryStore" /> answers, whichever of them is asked.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory store and the database-backed one are read by the same dashboard, the same HTTP API
/// and the same summary, so "what the store returns" is a contract rather than each implementation's
/// own business. Written once here and run against both, which is what makes the ADO store a
/// replacement rather than a second thing that nearly agrees.
/// </para>
/// <para>
/// Two differences are deliberate and are not asserted. A node filter is compared case-insensitively
/// in memory and by the database's collation in SQL — an instance id is generated rather than typed,
/// and comparing it as written is what lets the node index answer the filter with a seek. And the
/// count bound is applied on read in memory but by the sweep in the database, because a count over a
/// whole cluster's feed is not a property of one page; <see cref="ApplyBounds" /> is where a store
/// that sweeps gets to.
/// </para>
/// </remarks>
public abstract class ExecutionHistoryStoreContractTest
{
    protected const string SchedulerName = "ContractScheduler";
    protected const string JobGroup = "reports";
    protected const string TriggerGroup = "nightly";

    protected static readonly DateTimeOffset Start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    protected FakeTimeProvider Clock { get; private set; } = null!;

    [SetUp]
    public void StartTheClock()
    {
        Clock = new FakeTimeProvider(Start);
    }

    /// <summary>Builds the store under test, bounded as the case asks.</summary>
    protected abstract ValueTask<IExecutionHistoryStore> CreateStore(TimeSpan retention, int maxEntriesPerScheduler);

    /// <summary>
    /// Lets a store that keeps its bounds by sweeping do so, so that a count-bound case asserts the
    /// same thing about both implementations.
    /// </summary>
    protected virtual ValueTask ApplyBounds(IExecutionHistoryStore store) => default;

    private ValueTask<IExecutionHistoryStore> CreateStore() => CreateStore(TimeSpan.FromHours(24), 2000);

    [Test]
    public async Task AnExecutionIsReadBackWholeAfterItIsRecorded()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(new ExecutionHistoryEntry(
            SchedulerName: SchedulerName,
            SchedulerInstanceId: "node-a",
            JobGroup: JobGroup,
            JobName: "nightly-report",
            TriggerGroup: TriggerGroup,
            TriggerName: "at-midnight",
            FiredAtUtc: Start.AddMinutes(-3),
            Duration: TimeSpan.FromMilliseconds(1234.5678),
            Succeeded: false,
            ExceptionMessage: "the report source refused the connection"));

        ExecutionHistoryEntry entry = (await Executions(store)).Items.Should().ContainSingle().Subject;

        entry.SchedulerName.Should().Be(SchedulerName);
        entry.SchedulerInstanceId.Should().Be("node-a");
        entry.JobGroup.Should().Be(JobGroup);
        entry.JobName.Should().Be("nightly-report");
        entry.TriggerGroup.Should().Be(TriggerGroup);
        entry.TriggerName.Should().Be("at-midnight");
        entry.FiredAtUtc.Should().Be(Start.AddMinutes(-3));
        entry.Succeeded.Should().BeFalse();
        entry.ExceptionMessage.Should().Be("the report source refused the connection",
            "what a job threw is the one thing a reader of a failed execution came for");
        entry.Duration.Should().Be(TimeSpan.FromMilliseconds(1234.5678),
            "a job's run time is whatever the clock measured, and a store that rounded it would report "
            + "a duration nobody observed");
    }

    [Test]
    public async Task ExecutionsAreReadNewestFirst()
    {
        IExecutionHistoryStore store = await CreateStore();

        foreach (int index in Enumerable.Range(0, 4))
        {
            await store.AddExecution(Execution(Start.AddMinutes(-index), "job" + index));
        }

        (await Executions(store)).Items.Select(entry => entry.JobName).Should()
            .Equal(["job0", "job1", "job2", "job3"],
                "a history page is read newest first, which is the one ordering it is ever read in");
    }

    [Test]
    public async Task ExecutionsCanBeReadForOneNode()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Execution(Start, "on-a", node: "node-a"));
        await store.AddExecution(Execution(Start, "on-b", node: "node-b"));

        (await Executions(store)).Items.Should().HaveCount(2, "an unfiltered query is every node's");

        (await Executions(store, node: "node-b")).Items.Should().ContainSingle()
            .Which.JobName.Should().Be("on-b",
                "a cluster's history is unreadable until it can be narrowed to one machine");
    }

    [Test]
    public async Task ExecutionsCanBeNarrowedByJobAndByTrigger()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Execution(Start, "nightly-report"));
        await store.AddExecution(Execution(Start.AddMinutes(-1), "hourly-sweep"));

        (await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            JobContains = "nightly"
        })).Items.Should().ContainSingle().Which.JobName.Should().Be("nightly-report");

        (await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            JobContains = JobGroup + ".hourly"
        })).Items.Should().ContainSingle().Which.JobName.Should().Be("hourly-sweep",
            "group.name is how a key is written, so it is how a reader searches for one");

        (await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            JobContains = "NIGHTLY-REPORT"
        })).Items.Should().ContainSingle(
            "a search box is case-insensitive, and an operator who typed a name in capitals is searching "
            + "for the same job");

        (await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            TriggerContains = "no-such-trigger"
        })).Items.Should().BeEmpty();
    }

    /// <summary>
    /// A filter is matched literally, wildcards and all.
    /// </summary>
    /// <remarks>
    /// A database store writes the filter into a <c>LIKE</c> pattern, where <c>%</c> and <c>_</c> are
    /// the engine's own and <c>[</c> is T-SQL's. An unescaped one would answer a search for a job
    /// called <c>a_b</c> with every three-letter job there is.
    /// </remarks>
    [Test]
    public async Task AFilterWithWildcardsInItMatchesThemLiterally()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Execution(Start, "a_b"));
        await store.AddExecution(Execution(Start.AddMinutes(-1), "axb"));

        (await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            JobContains = "a_b"
        })).Items.Should().ContainSingle().Which.JobName.Should().Be("a_b",
            "the underscore is part of the name the reader typed, not a wildcard they meant");
    }

    [Test]
    public async Task AHistoryIsReadAPageAtATime()
    {
        IExecutionHistoryStore store = await CreateStore();

        foreach (int index in Enumerable.Range(0, 5))
        {
            await store.AddExecution(Execution(Start.AddSeconds(index), "job" + index));
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

    /// <summary>
    /// The count idiom: a take of nothing with the total asked for, which every paged read here
    /// answers without loading a page it would throw away.
    /// </summary>
    [Test]
    public async Task TheCountIdiomAnswersWithNoPage()
    {
        IExecutionHistoryStore store = await CreateStore();

        foreach (int index in Enumerable.Range(0, 3))
        {
            await store.AddExecution(Execution(Start.AddSeconds(index), "job" + index));
        }

        PagedResult<ExecutionHistoryEntry> count = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            Take = 0,
            IncludeTotalCount = true
        });

        count.Items.Should().BeEmpty();
        count.TotalCount.Should().Be(3);
        count.HasMore.Should().BeTrue("three rows were left unread");
    }

    [Test]
    public async Task AnExecutionOlderThanTheRetentionWindowIsForgotten()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(1), maxEntriesPerScheduler: 2000);

        await store.AddExecution(Execution(Start, "nightly"));

        Clock.Advance(TimeSpan.FromMinutes(59));
        (await Executions(store)).Items.Should().ContainSingle(
            "the window has not closed yet, and an execution inside it is what the page is for");

        Clock.Advance(TimeSpan.FromMinutes(2));
        await ApplyBounds(store);

        (await Executions(store)).Items.Should().BeEmpty(
            "an hour was the whole window, and reading has to apply it too — a scheduler that has "
            + "stopped running jobs never writes again, and it is that one whose page would otherwise "
            + "keep showing days-old executions");
    }

    [Test]
    public async Task OnlyTheNewestExecutionsSurviveTheCountBound()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(24), maxEntriesPerScheduler: 3);

        foreach (int index in Enumerable.Range(0, 5))
        {
            await store.AddExecution(Execution(Start.AddSeconds(index), "job" + index));
        }

        await ApplyBounds(store);

        (await Executions(store)).Items.Select(entry => entry.JobName).Should()
            .Equal(["job4", "job3", "job2"], "the cap drops the oldest and the page reads newest first");
    }

    [Test]
    public async Task AMisfireIsRecordedBesideTheExecutionsAndReadBack()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Execution(Start, "ran"));
        await store.AddMisfire(Misfire(Start, "at-midnight"));

        (await Executions(store)).Items.Should().ContainSingle(
            "a misfire is not an execution — nothing ran — so it must not appear in the history");

        MisfireHistoryEntry misfire = (await Misfires(store)).Items.Should().ContainSingle().Subject;

        misfire.SchedulerName.Should().Be(SchedulerName);
        misfire.SchedulerInstanceId.Should().Be("node-a");
        misfire.TriggerGroup.Should().Be(TriggerGroup);
        misfire.TriggerName.Should().Be("at-midnight");
        misfire.JobKey.Should().Be(new JobKey("nightly-report", JobGroup));
        misfire.MisfiredAtUtc.Should().Be(Start);
        misfire.ScheduledFireTimeUtc.Should().Be(Start.AddMinutes(-5),
            "the missed firing is the point of the row: it says what did not happen and when");
    }

    [Test]
    public async Task AMisfireOfATriggerThatNamesNoJobIsReadBackWithoutOne()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddMisfire(new MisfireHistoryEntry(
            SchedulerName: SchedulerName,
            SchedulerInstanceId: "node-a",
            TriggerGroup: TriggerGroup,
            TriggerName: "orphan",
            JobKey: null,
            MisfiredAtUtc: Start,
            ScheduledFireTimeUtc: null));

        MisfireHistoryEntry misfire = (await Misfires(store)).Items.Should().ContainSingle().Subject;

        misfire.JobKey.Should().BeNull("a trigger need not name a job, and a row cannot invent one");
        misfire.ScheduledFireTimeUtc.Should().BeNull(
            "a trigger with no firing left to name has nothing to put here");
    }

    [Test]
    public async Task MisfiresCanBeReadForOneNode()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddMisfire(Misfire(Start, "on-a", node: "node-a"));
        await store.AddMisfire(Misfire(Start.AddMinutes(-1), "on-b", node: "node-b"));

        (await Misfires(store, node: "node-a")).Items.Should().ContainSingle()
            .Which.TriggerName.Should().Be("on-a");
    }

    [Test]
    public async Task MisfiresAreCountedOverAWindow()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddMisfire(Misfire(Start.AddMinutes(-30), "old"));
        await store.AddMisfire(Misfire(Start.AddMinutes(-5), "recent"));
        await store.AddMisfire(Misfire(Start.AddMinutes(-1), "newest"));

        (await store.CountMisfires(SchedulerName, Start.AddMinutes(-10))).Should().Be(2,
            "a summary asks how bad it is right now, which is a count over a window rather than a page");
    }

    [Test]
    public async Task MisfiresAreBoundedTheWayExecutionsAre()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(1), maxEntriesPerScheduler: 2);

        await store.AddMisfire(Misfire(Start, "one"));
        await store.AddMisfire(Misfire(Start.AddSeconds(1), "two"));
        await store.AddMisfire(Misfire(Start.AddSeconds(2), "three"));

        await ApplyBounds(store);

        (await Misfires(store)).Items.Select(entry => entry.TriggerName).Should().Equal(["three", "two"],
            "the cap is per feed, and the misfire feed is not exempt from it");

        Clock.Advance(TimeSpan.FromHours(2));
        await ApplyBounds(store);

        (await Misfires(store)).Items.Should().BeEmpty("the retention window covers misfires too");
    }

    [Test]
    public async Task OneSchedulersHistoryIsNotAnothersHistory()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Execution(Start, "ours"));
        await store.AddExecution(Execution(Start, "theirs") with { SchedulerName = "OtherScheduler" });

        (await Executions(store)).Items.Should().ContainSingle().Which.JobName.Should().Be("ours",
            "a store keeps every scheduler's rows together and a query names the one it wants");

        (await store.CountMisfires("OtherScheduler", Start.AddDays(-1))).Should().Be(0);
    }

    [Test]
    public async Task TheAttemptAndWhetherAnotherWasScheduledAreReadBack()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Execution(Start, "retried") with
        {
            Succeeded = false,
            ExceptionMessage = "the upstream system is down",
            RetryAttempt = 2,
            RetryScheduled = true
        });

        ExecutionHistoryEntry entry = (await Executions(store)).Items.Should().ContainSingle().Subject;

        entry.RetryAttempt.Should().Be(2, "which attempt at the occurrence a row is of is what makes a page of "
            + "repeated failures readable as one occurrence rather than three");
        entry.RetryScheduled.Should().BeTrue("the trigger answered this failure with another attempt, so the row "
            + "is not the occurrence's last word");
    }

    [Test]
    public async Task AnExecutionWithNoRetryPolicyBehindItRecordsTheDefaults()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Execution(Start, "plain"));

        ExecutionHistoryEntry entry = (await Executions(store)).Items.Should().ContainSingle().Subject;

        entry.RetryAttempt.Should().Be(0);
        entry.RetryScheduled.Should().BeFalse("nothing was going to try again, which is exactly what the defaults say");
    }

    [Test]
    public async Task TheFinalFailureFilterSeparatesGivingUpFromStillTrying()
    {
        IExecutionHistoryStore store = await CreateStore();

        // One occurrence: two attempts that were answered with a retry, and a third that was not.
        await store.AddExecution(Failed(Start.AddSeconds(1), "nightly", retryAttempt: 0, retryScheduled: true));
        await store.AddExecution(Failed(Start.AddSeconds(2), "nightly", retryAttempt: 1, retryScheduled: true));
        await store.AddExecution(Failed(Start.AddSeconds(3), "nightly", retryAttempt: 2, retryScheduled: false));

        // And a firing that simply worked.
        await store.AddExecution(Execution(Start.AddSeconds(4), "hourly"));

        PagedResult<ExecutionHistoryEntry> gaveUp = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            FailedFinally = true,
            IncludeTotalCount = true
        });

        gaveUp.Items.Should().ContainSingle("a filter on failure alone would show one occurrence three times over")
            .Which.RetryAttempt.Should().Be(2);
        gaveUp.TotalCount.Should().Be(1, "the count has to agree with the page, or the pager lies about it");

        PagedResult<ExecutionHistoryEntry> everythingElse = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            FailedFinally = false,
            IncludeTotalCount = true
        });

        everythingElse.Items.Should().HaveCount(3,
            "the complement is the successes and the failures that are going to be tried again");
        everythingElse.Items.Should().OnlyContain(x => x.Succeeded || x.RetryScheduled);

        (await Executions(store)).Items.Should().HaveCount(4, "an unasked filter narrows nothing");
    }

    [Test]
    public async Task TheFinalFailureFilterComposesWithTheOtherFilters()
    {
        IExecutionHistoryStore store = await CreateStore();

        await store.AddExecution(Failed(Start.AddSeconds(1), "nightly", retryAttempt: 1, retryScheduled: false));
        await store.AddExecution(Failed(Start.AddSeconds(2), "hourly", retryAttempt: 1, retryScheduled: false));

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            JobContains = "nightly",
            FailedFinally = true
        });

        page.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly",
            "the outcome filter is one predicate among the others rather than a second query");
    }

    // ---------------------------------------------------------------------------------------------
    // Building the entries
    // ---------------------------------------------------------------------------------------------

    /// <summary>One failed attempt at an occurrence, which may or may not have another coming.</summary>
    protected static ExecutionHistoryEntry Failed(
        DateTimeOffset firedAt,
        string jobName,
        int retryAttempt,
        bool retryScheduled) => Execution(firedAt, jobName) with
    {
        Succeeded = false,
        ExceptionMessage = "the upstream system is down",
        RetryAttempt = retryAttempt,
        RetryScheduled = retryScheduled
    };

    protected static ExecutionHistoryEntry Execution(
        DateTimeOffset firedAt,
        string jobName,
        string node = "node-a") => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: node,
        JobGroup: JobGroup,
        JobName: jobName,
        TriggerGroup: TriggerGroup,
        TriggerName: "at-midnight",
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null);

    protected static MisfireHistoryEntry Misfire(
        DateTimeOffset misfiredAt,
        string triggerName,
        string node = "node-a") => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: node,
        TriggerGroup: TriggerGroup,
        TriggerName: triggerName,
        JobKey: new JobKey("nightly-report", JobGroup),
        MisfiredAtUtc: misfiredAt,
        ScheduledFireTimeUtc: misfiredAt.AddMinutes(-5));

    protected static ValueTask<PagedResult<ExecutionHistoryEntry>> Executions(
        IExecutionHistoryStore store,
        string? node = null)
    {
        return store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            SchedulerInstanceId = node,
            IncludeTotalCount = true
        });
    }

    protected static ValueTask<PagedResult<MisfireHistoryEntry>> Misfires(
        IExecutionHistoryStore store,
        string? node = null)
    {
        return store.QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = SchedulerName,
            SchedulerInstanceId = node,
            IncludeTotalCount = true
        });
    }
}

/// <summary>The contract, against the store Quartz keeps when nothing else is registered.</summary>
public sealed class InMemoryExecutionHistoryStoreContractTest : ExecutionHistoryStoreContractTest
{
    protected override ValueTask<IExecutionHistoryStore> CreateStore(TimeSpan retention, int maxEntriesPerScheduler)
    {
        ExecutionHistoryOptions options = new()
        {
            Retention = retention,
            MaxEntriesPerScheduler = maxEntriesPerScheduler
        };

        return new ValueTask<IExecutionHistoryStore>(
            new InMemoryExecutionHistoryStore(Options.Create(options), Clock));
    }
}

/// <summary>
/// The same contract, against the history kept in a database.
/// </summary>
/// <remarks>
/// On a SQLite file, which is a whole empty database for the price of a temporary path: the schema is
/// the one <c>ProvisionSchema()</c> creates, the statements are the ones every dialect runs, and the
/// store is the one <c>UseExecutionHistory()</c> registers. The other five dialects are the
/// integration legs' business; what is here is everything that does not need a container.
/// </remarks>
public sealed class AdoExecutionHistoryStoreContractTest : ExecutionHistoryStoreContractTest
{
    private SqliteTestDatabase database = null!;
    private ServiceProvider? container;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("history-contract");
    }

    [TearDown]
    public async Task DisposeTheContainer()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }

        database.Dispose();
    }

    protected override async ValueTask<IExecutionHistoryStore> CreateStore(TimeSpan retention, int maxEntriesPerScheduler)
    {
        ServiceCollection services = new();

        services.AddSingleton<TimeProvider>(Clock);
        services.AddQuartzExecutionHistory(options =>
        {
            options.Retention = retention;
            options.MaxEntriesPerScheduler = maxEntriesPerScheduler;
        });

        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "node-a";
            });

            quartz.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.UseExecutionHistory();
            });
        });

        container = services.BuildServiceProvider();

        // The scheduler is what initializes the job store, which is what tells the driver delegate its
        // table prefix. Built and not started: nothing here fires a trigger.
        await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        return container.GetRequiredService<IExecutionHistoryStore>();
    }

    protected override ValueTask ApplyBounds(IExecutionHistoryStore store)
    {
        return ((AdoExecutionHistoryStore) store).Sweep();
    }

    /// <summary>
    /// <c>UseExecutionHistory()</c> is what puts the ADO store in the slot the in-memory one holds.
    /// </summary>
    [Test]
    public async Task TheRegisteredStoreIsTheDatabaseBackedOne()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(1), 10);

        store.Should().BeOfType<AdoExecutionHistoryStore>(
            "UseExecutionHistory() replaces the shipped in-memory default, and the recorder, the "
            + "dashboard and the HTTP API all resolve the store without a key");
    }

    /// <summary>
    /// A write that cannot reach the database is a lost row, never a failed firing.
    /// </summary>
    /// <remarks>
    /// The execution has already happened by the time the recorder is told about it, so there is
    /// nothing to undo and nobody to report it to. The table is dropped under the store, which is the
    /// bluntest version of every way a write can fail.
    /// </remarks>
    [Test]
    public async Task AWriteThatFailsDoesNotFailTheFiring()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(1), 10);

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE QRTZ_EXECUTION_HISTORY";
            await drop.ExecuteNonQueryAsync();
        }

        Func<Task> act = async () => await store.AddExecution(Execution(Start, "nightly"));

        await act.Should().NotThrowAsync(
            "the job ran; only the record of it was lost, and an observability feature that can fail a "
            + "firing is worse than no observability at all");
    }

    /// <summary>
    /// The misfire feed's writer is as forgiving, for the same reason.
    /// </summary>
    /// <remarks>
    /// A misfire is reported from inside the scheduler's misfire sweep, so a write that threw would
    /// fail the pass that was recovering the backlog — the worst possible moment to stop.
    /// </remarks>
    [Test]
    public async Task AMisfireWriteThatFailsDoesNotFailTheSweepThatReportedIt()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(1), 10);

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE QRTZ_MISFIRE_HISTORY";
            await drop.ExecuteNonQueryAsync();
        }

        Func<Task> act = async () => await store.AddMisfire(Misfire(Start, "at-midnight"));

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// The sweep really deletes, rather than the read merely hiding what is past its bounds.
    /// </summary>
    /// <remarks>
    /// The read applies the age bound too, so a page alone cannot tell a swept table from an unswept
    /// one — and an unswept table is a table that grows for ever. This one counts the rows.
    /// </remarks>
    [Test]
    public async Task TheSweepDeletesTheRowsThePageStopsShowing()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(1), maxEntriesPerScheduler: 5);

        foreach (int index in Enumerable.Range(0, 12))
        {
            await store.AddExecution(Execution(Start.AddSeconds(index), "job" + index));
        }

        await ((AdoExecutionHistoryStore) store).Sweep();

        (await RowCount()).Should().Be(5, "the count bound is what the sweep leaves in the table");

        Clock.Advance(TimeSpan.FromHours(2));
        await ((AdoExecutionHistoryStore) store).Sweep();

        (await RowCount()).Should().Be(0, "and the age bound takes the rest of them");
    }

    /// <summary>
    /// A batch of rows that share one instant does not stall the sweep.
    /// </summary>
    /// <remarks>
    /// A sweep batch is bounded by an instant rather than by a row limit — <c>DELETE … LIMIT</c> is
    /// spelled six different ways — and a batch whose boundary row shares its instant with the oldest
    /// row would delete nothing at all if the boundary were exclusive. It is inclusive, so the tie
    /// group goes as a whole: this leaves fewer rows than the count bound rather than more, and the
    /// sweep always makes progress. A batch firing is what produces the tie.
    /// </remarks>
    [Test]
    public async Task RowsSharingAnInstantDoNotStallTheSweep()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(1), maxEntriesPerScheduler: 5);

        foreach (int index in Enumerable.Range(0, 12))
        {
            await store.AddExecution(Execution(Start, "job" + index));
        }

        await ((AdoExecutionHistoryStore) store).Sweep();

        (await RowCount()).Should().Be(0,
            "twelve rows on one instant cannot be cut to five, so the whole tie group goes — never "
            + "fewer than the bound asked to be removed, and never a sweep that removes nothing");
    }

    /// <summary>
    /// A pass that runs out of batches brings the next one forward, so the sweep keeps up with a
    /// scheduler that records faster than one pass per interval can delete.
    /// </summary>
    /// <remarks>
    /// A pass stops after <see cref="AdoExecutionHistoryStore.SweepBatchesPerPass" /> batches of
    /// <see cref="AdoExecutionHistoryStore.SweepBatchSize" />, so that it gives its connection back —
    /// and at the long interval, <c>Retention / 10</c>, that bound was also the most the store could
    /// ever delete: a busy scheduler grew the table without limit while every pass did all it was
    /// allowed to. The backlog is written in one statement, because what is being measured is what the
    /// timer does about it rather than how long 25,000 writes take.
    /// </remarks>
    [Test]
    public async Task ASweepThatRunsOutOfBatchesComesBackAMinuteLater()
    {
        IExecutionHistoryStore store = await CreateStore(TimeSpan.FromHours(24), maxEntriesPerScheduler: 5);
        AdoExecutionHistoryStore history = (AdoExecutionHistoryStore) store;

        await InsertExecutionBacklog(25_000);

        // The first write starts the timer, and its first pass is due at once.
        await store.AddExecution(Execution(Start, "latest"));
        await history.WaitForSweep();

        (await RowCount()).Should().BeGreaterThan(5,
            "one pass deletes at most SweepBatchesPerPass x SweepBatchSize rows a bound, and gives its "
            + "connection back rather than holding it until a backlog of any size is gone");

        Clock.Advance(AdoExecutionHistoryStore.MinimumSweepInterval);
        await history.WaitForSweep();

        (await RowCount()).Should().Be(5,
            "a pass that stopped on its batch budget brings the next one forward to a minute, rather than "
            + "leaving the rest for Retention / 10 - 2.4 hours here - which a busy scheduler outruns");

        foreach (int index in Enumerable.Range(1, 10))
        {
            await store.AddExecution(Execution(Start.AddSeconds(index), "later" + index));
        }

        Clock.Advance(AdoExecutionHistoryStore.MinimumSweepInterval);
        await history.WaitForSweep();

        (await RowCount()).Should().Be(15,
            "the pass that finished put the store back on the long interval, so a minute later is not a "
            + "pass - hurrying is for a backlog, not for every table");

        Clock.Advance(TimeSpan.FromHours(2.4));
        await history.WaitForSweep();

        (await RowCount()).Should().Be(5, "and the long interval still comes round");
    }

    /// <summary>
    /// The history can be read before anything has built the scheduler it belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dashboard or the HTTP API resolves the store at its first request, and nothing about that
    /// request builds the scheduler — a registered scheduler is listed without being built, on purpose.
    /// The store used the job store's driver delegate, which the job store initializes when the
    /// scheduler is built, so a read before then threw a <see cref="NullReferenceException" /> out of
    /// <c>StdAdoDelegate.PrepareCommand</c>.
    /// </para>
    /// <para>
    /// A table prefix of its own, so that a delegate initialized with the default one would find no
    /// table: the reader has to read with the scheduler's prefix, not merely with some prefix. A named
    /// scheduler, so the scheduler's options are found by name as its job store would find them.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheHistoryCanBeReadBeforeTheSchedulerIsBuilt()
    {
        const string Scheduler = "reporting";

        // The process that runs the scheduler: building it provisions the schema.
        await using ServiceProvider node = HistoryContainer(Scheduler);
        await node.GetRequiredKeyedService<ISchedulerFactory>(Scheduler).GetScheduler();

        // A process that only reads: its container is built and its scheduler never is.
        await using ServiceProvider reader = HistoryContainer(Scheduler);
        IExecutionHistoryStore history = reader.GetRequiredKeyedService<IExecutionHistoryStore>(Scheduler);

        ExecutionHistoryQuery query = new() { SchedulerName = Scheduler, IncludeTotalCount = true };

        PagedResult<ExecutionHistoryEntry> empty = await history.QueryExecutions(query);
        empty.Items.Should().BeEmpty("nothing has run yet, and that is an answer rather than a failure");
        empty.TotalCount.Should().Be(0);

        (await history.QueryMisfires(new MisfireHistoryQuery { SchedulerName = Scheduler })).Items.Should().BeEmpty();
        (await history.CountMisfires(Scheduler, Start.AddDays(-1))).Should().Be(0);

        await node.GetRequiredKeyedService<IExecutionHistoryStore>(Scheduler).AddExecution(
            Execution(Start, "nightly") with { SchedulerName = Scheduler });

        (await history.QueryExecutions(query)).Items.Should().ContainSingle()
            .Which.JobName.Should().Be("nightly",
                "the reader's statements are the scheduler's own - its dialect and its table prefix - "
                + "whether or not the scheduler has been built in this process");

        reader.GetService<ISchedulerRepository>()!.Lookup(Scheduler).Should().BeNull(
            "reading the history is not a reason to build the scheduler, and did not");
    }

    /// <summary>
    /// A container holding one named scheduler that keeps its history in the database, under a table
    /// prefix of its own.
    /// </summary>
    private ServiceProvider HistoryContainer(string schedulerName)
    {
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(Clock);

        services.AddQuartz(schedulerName, quartz => quartz.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
            store.ProvisionSchema();
            store.UseExecutionHistory();
            store.ConfigureStore(options => options.TablePrefix = "HIST_");
        }));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Writes <paramref name="count" /> executions of <see cref="ExecutionHistoryStoreContractTest.SchedulerName" />
    /// in one statement, a millisecond apart and all before <see cref="ExecutionHistoryStoreContractTest.Start" />.
    /// </summary>
    private async Task InsertExecutionBacklog(int count)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < @count) "
            + "INSERT INTO QRTZ_EXECUTION_HISTORY (SCHED_NAME, ENTRY_ID, INSTANCE_NAME, JOB_NAME, JOB_GROUP, "
            + "TRIGGER_NAME, TRIGGER_GROUP, FIRED_TIME, RUN_TIME, SUCCEEDED) "
            + "SELECT @scheduler, 'backlog-' || i, 'node-a', 'backlog', @jobGroup, 'at-midnight', @triggerGroup, "
            + "@start - i * @millisecond, @millisecond, 1 FROM n";
        command.Parameters.AddWithValue("@count", count);
        command.Parameters.AddWithValue("@scheduler", SchedulerName);
        command.Parameters.AddWithValue("@jobGroup", JobGroup);
        command.Parameters.AddWithValue("@triggerGroup", TriggerGroup);
        command.Parameters.AddWithValue("@start", Start.UtcTicks);
        command.Parameters.AddWithValue("@millisecond", TimeSpan.TicksPerMillisecond);

        (await command.ExecuteNonQueryAsync()).Should().Be(count);
    }

    private async Task<long> RowCount()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_EXECUTION_HISTORY";

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
