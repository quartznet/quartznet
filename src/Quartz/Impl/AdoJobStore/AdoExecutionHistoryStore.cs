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

using System.Collections.Concurrent;
using System.Data.Common;
using System.Transactions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The execution history kept in the scheduler's own database, so that a cluster has one history
/// rather than one per node and a dashboard reading through any of them sees all of it.
/// </summary>
/// <remarks>
/// <para>
/// Selected by <c>UsePersistentStore(store =&gt; store.UseExecutionHistory())</c>, which also puts the
/// three tables and the columns the optional migrations create — <c>4.2/add_execution_history</c>,
/// <c>4.3/add_execution_log</c> and <c>add_misfire_reason</c>, <c>4.4/add_execution_outcome</c> — into the
/// schema the store validates at startup. Without that call nothing here runs and the tables may be
/// absent, which is what makes the migrations optional.
/// </para>
/// <para>
/// Every statement runs on a connection of this store's own, outside any ambient transaction: a
/// history write is a record of something that already happened, so it must not be rolled back with
/// the job's work, must not hold the trigger lock, and must never be the reason a firing fails. A
/// write that cannot reach the database is logged and dropped. An execution is one transaction on that
/// connection — its row and its job's <c>QRTZ_JOB_STATUS</c> row commit together or not at all.
/// </para>
/// <para>
/// Reading applies the longest age bound as well as the sweep does, as the in-memory history does and
/// for the same reason: a scheduler that has stopped running jobs never writes again, and it is that
/// scheduler whose page would otherwise go on showing executions from days ago. The shorter tiers of
/// <see cref="ExecutionHistoryOptions.RetentionByResult" />, the per-job cap and the count bound are the
/// sweep's alone — each is a property of the whole cluster's feed rather than of one page, and answering
/// it on every read would mean a window function six dialects spell differently.
/// </para>
/// </remarks>
internal sealed class AdoExecutionHistoryStore : IExecutionHistoryStore, IDisposable
{
    /// <summary>
    /// How many rows one <c>DELETE</c> of the retention sweep removes, so that a long-idle store
    /// cannot lock the table for minutes on the first pass.
    /// </summary>
    internal const int SweepBatchSize = 1000;

    /// <summary>
    /// How many batches one sweep runs before leaving the rest to the next. A bound rather than "until
    /// it is finished", so that a store that has been down for a week gives its connection back.
    /// </summary>
    /// <remarks>
    /// One budget for the whole pass, spent on every bound in turn. A pass that stops on it brings the
    /// next one forward to <see cref="MinimumSweepInterval" />, which is what keeps the sweep ahead of a
    /// busy scheduler.
    /// </remarks>
    internal const int SweepBatchesPerPass = 20;

    /// <summary>
    /// How many jobs over <see cref="ExecutionHistoryOptions.MaxEntriesPerJob" /> one pass trims. More
    /// bring the next pass forward, as a spent batch budget does.
    /// </summary>
    internal const int JobsCappedPerPass = 50;

    /// <summary>
    /// The floor under the sweep interval, whatever the retention windows are — and the interval a pass
    /// that ran out of batches brings the next one forward to.
    /// </summary>
    internal static readonly TimeSpan MinimumSweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The ceiling over the sweep interval, so that the count bounds are applied at least daily, and a
    /// window of <see cref="TimeSpan.MaxValue" /> never asks a timer for a period it cannot hold.
    /// </summary>
    internal static readonly TimeSpan MaximumSweepInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// The shortest check-in interval a live node is judged by: <c>ClusterCheckinMisfireThreshold</c>'s
    /// default, as <c>CalcFailedIfAfter</c> allows for it.
    /// </summary>
    internal static readonly TimeSpan MinimumCheckinWindow = TimeSpan.FromSeconds(7.5);

    /// <summary>Every result this version writes, each of which has a retention window.</summary>
    private static readonly JobRunResult[] knownResults =
        [JobRunResult.Succeeded, JobRunResult.Failed, JobRunResult.Cancelled, JobRunResult.Skipped];

    private readonly IDbProvider dbProvider;
    private readonly IDriverDelegate driverDelegate;
    private readonly IOptions<ExecutionHistoryOptions> historyOptions;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AdoExecutionHistoryStore> logger;

    /// <summary>
    /// The scheduler this store was built for: the one its driver delegate reads the check-ins of, and
    /// so the one whose sweep is elected.
    /// </summary>
    private readonly string ownSchedulerName;

    /// <summary>
    /// The scheduler names this store has rows for, which is what the sweep walks.
    /// </summary>
    /// <remarks>
    /// Seeded with the scheduler this store was built for and added to by every write, so a store
    /// shared by several schedulers sweeps each of them. A node that only reads sweeps its own
    /// scheduler and leaves the rest to whoever writes them.
    /// </remarks>
    private readonly ConcurrentDictionary<string, byte> schedulers = new(StringComparer.Ordinal);

    /// <summary>
    /// Holds one sweep at a time. The deletes are idempotent, so two nodes sweeping at once is
    /// harmless — but two sweeps in one process would only be two connections doing the same work.
    /// </summary>
    /// <remarks>
    /// Never disposed. Disposing the timer does not wait for a pass it already started, so a pass can
    /// still hold this when the store is disposed, and it releases the gate on its way out. It holds
    /// nothing that needs releasing unless its wait handle is asked for, which nothing here does.
    /// </remarks>
    private readonly SemaphoreSlim sweepGate = new(1, 1);

    /// <summary>
    /// Cancelled when the store is disposed, which stops a pass the timer started at its next statement
    /// rather than letting it run on against a store that is shutting down.
    /// </summary>
    /// <remarks>
    /// Cancelled and then disposed. A pass reads <see cref="stoppingToken" />, taken at construction,
    /// never the source itself: a token whose source was cancelled before it was disposed still answers
    /// every question a pass asks of it.
    /// </remarks>
    private readonly CancellationTokenSource stopping = new();

    private readonly CancellationToken stoppingToken;

    /// <summary>
    /// This node's instance id in <see cref="ownSchedulerName" />, learned from the rows it writes:
    /// <see langword="null" /> until it has written one, and until then it sweeps without an election.
    /// </summary>
    private volatile string? ownInstanceId;

    /// <summary>
    /// The node this one last left its sweep to, so that it says so once rather than every pass. Read
    /// and written by a pass alone, under <see cref="sweepGate" />.
    /// </summary>
    private string? deferringTo;

    private ITimer? sweepTimer;
    private int sweepScheduled;
    private volatile bool disposed;

    /// <param name="dbProvider">The database this scheduler reads and writes through.</param>
    /// <param name="driverDelegate">
    /// The dialect the statements are issued in, already initialized, and this store's alone: a scheduler's
    /// store is handed a copy of the scheduler's delegate rather than the job store's own, which the job
    /// store initializes only when the scheduler is built (see <c>ExecutionHistoryRegistration</c>), and
    /// an attached store's is its probe's. It has to be a <see cref="StdAdoDelegate" />: the history's
    /// statements live there, beside the paging and parameter binding every dialect delegate already
    /// inherits.
    /// </param>
    /// <param name="historyOptions">The bounds the history is kept under.</param>
    /// <param name="schedulerOptions">Names the scheduler this store was built for.</param>
    /// <param name="timeProvider">
    /// The clock the retention window is measured on and the sweep runs on — this scheduler's, so a
    /// test that moves it forward sees the store forget.
    /// </param>
    /// <param name="loggerFactory">Where a failed write or sweep is reported.</param>
    public AdoExecutionHistoryStore(
        IDbProvider dbProvider,
        IDriverDelegate driverDelegate,
        IOptions<ExecutionHistoryOptions> historyOptions,
        IOptions<QuartzSchedulerOptions> schedulerOptions,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(dbProvider);
        ArgumentNullException.ThrowIfNull(driverDelegate);
        ArgumentNullException.ThrowIfNull(historyOptions);
        ArgumentNullException.ThrowIfNull(schedulerOptions);

        if (driverDelegate is not StdAdoDelegate)
        {
            Throw.SchedulerConfigException(
                $"{driverDelegate.GetType().FullName} does not derive from {nameof(StdAdoDelegate)}, so it cannot serve "
                + "the execution history: the history statements, the dialect's paging and its parameter binding all "
                + "live there. Derive the delegate from StdAdoDelegate, or leave UseExecutionHistory() uncalled and "
                + "register an IExecutionHistoryStore of your own.");
        }

        this.dbProvider = dbProvider;
        this.driverDelegate = driverDelegate;
        this.historyOptions = historyOptions;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        stoppingToken = stopping.Token;
        logger = loggerFactory is null
            ? LogProvider.CreateLogger<AdoExecutionHistoryStore>()
            : loggerFactory.CreateLogger<AdoExecutionHistoryStore>();

        ownSchedulerName = schedulerOptions.Value.InstanceName;
        schedulers.TryAdd(ownSchedulerName, 0);
    }

    private StdAdoDelegate Delegate => (StdAdoDelegate) driverDelegate;

    /// <remarks>
    /// <para>
    /// One transaction on this store's own connection: the row, then the job's status — updated, or
    /// inserted on the job's first recorded run — then one commit. The commit is the one a row alone
    /// cost; the status adds a statement, and a second on a job's first run.
    /// </para>
    /// <para>
    /// Two nodes recording a job's first run at once both find no status row, and the second insert
    /// fails on the key. That transaction is rolled back, which takes its execution row with it, and run
    /// once more, when the update finds the row; nothing is counted twice.
    /// </para>
    /// </remarks>
    public async ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Remember(entry.SchedulerName, entry.SchedulerInstanceId);

        // The recorder's key when it named the row, which is what makes the row findable by the key a
        // reader was given; a key of this store's own when nothing did. The status names the same one.
        ExecutionHistoryEntry named = entry.EntryId is null ? entry with { EntryId = NewEntryId() } : entry;

        try
        {
            await Record(named, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            // The execution happened; only the record of it did not. Failing the firing over a history
            // row would turn an observability feature into an outage.
            logger.ExecutionHistoryWriteFailed(entry.SchedulerName, failure);
        }

        StartSweeping();
    }

    public async ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Remember(entry.SchedulerName, entry.SchedulerInstanceId);

        try
        {
            await Execute(
                conn => Delegate.InsertMisfireHistory(conn, NewEntryId(), entry, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            logger.ExecutionHistoryWriteFailed(entry.SchedulerName, failure);
        }

        StartSweeping();
    }

    public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(
        ExecutionHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Execute(
            conn => Delegate.SelectExecutionHistory(conn, query, ExecutionFloor(), cancellationToken),
            cancellationToken);
    }

    /// <remarks>
    /// One statement by the row's key, and the only one that reads <c>EXECUTION_LOG</c>: the listing
    /// leaves it out. The age bound applies here as it does to the listing.
    /// </remarks>
    public ValueTask<ExecutionHistoryEntry?> GetExecution(string schedulerName, string entryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        return Execute(
            conn => Delegate.SelectExecutionHistoryEntry(conn, schedulerName, entryId, ExecutionFloor(), cancellationToken),
            cancellationToken);
    }

    public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(
        MisfireHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Execute(
            conn => Delegate.SelectMisfireHistory(conn, query, MisfireFloor(), cancellationToken),
            cancellationToken);
    }

    public ValueTask<int> CountMisfires(
        string schedulerName,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        // The later of the two bounds, so that the count never includes a row a read would not show.
        DateTimeOffset floor = MisfireFloor();
        DateTimeOffset from = since > floor ? since : floor;

        return Execute(
            conn => Delegate.CountMisfireHistorySince(conn, schedulerName, from, cancellationToken),
            cancellationToken);
    }

    /// <remarks>
    /// Read from <c>QRTZ_JOB_STATUS</c>, which the store keeps beside the rows. Not bounded by age: a
    /// status is what says a job has been failing since before the history reaches.
    /// </remarks>
    public ValueTask<PagedResult<JobRunStatus>> QueryJobRunStatuses(JobRunStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Execute(
            conn => Delegate.SelectJobRunStatuses(conn, query, cancellationToken),
            cancellationToken);
    }

    /// <remarks>One statement by the table's key.</remarks>
    public ValueTask<JobRunStatus?> GetJobRunStatus(string schedulerName, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentNullException.ThrowIfNull(jobKey);

        return Execute(
            conn => Delegate.SelectJobRunStatus(conn, schedulerName, jobKey, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Deletes what has fallen out of any bound, for every scheduler this store has seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internal so that a test can run one pass rather than wait for the timer. In a cluster, one node
    /// sweeps: the live node with the lowest instance id (see <see cref="Sweeper" />). The deletes are
    /// idempotent, so two nodes that each believe they are it do the same work twice at worst — never the
    /// wrong work.
    /// </para>
    /// <para>
    /// A pass that stops on its batch budget, or leaves capped jobs for later, brings the next pass
    /// forward; see <see cref="CatchUp" />.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal async ValueTask Sweep(CancellationToken cancellationToken = default)
    {
        if (!await sweepGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            ExecutionHistoryOptions bounds = historyOptions.Value;
            SweepBudget budget = new(SweepBatchesPerPass);
            bool finished = true;

            foreach (string schedulerName in schedulers.Keys)
            {
                // Not short-circuited: a scheduler whose sweep stopped on the budget still has its
                // election read, and the budget decides what is left.
                finished &= await SweepScheduler(schedulerName, bounds, budget, cancellationToken).ConfigureAwait(false);
            }

            if (!finished)
            {
                CatchUp();
            }
        }
        catch (Exception) when (disposed)
        {
            // Disposed under the pass: its token was cancelled, or its next statement found the store
            // closed, or the database was torn down around it. Shutting down is not a sweep that failed,
            // and what this pass left is the next one's — on another node, or in whatever runs next.
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            logger.ExecutionHistorySweepFailed(failure);
        }
        finally
        {
            sweepGate.Release();
        }
    }

    /// <summary>
    /// The pass the timer starts, stopped by the store's disposal rather than by any caller.
    /// </summary>
    /// <remarks>
    /// Started and not awaited: a timer callback returns void. Everything inside the pass is handled
    /// by <see cref="Sweep" />; the one thing that is not is a pass refused at the gate by a token the
    /// disposal has already cancelled, which is the same shutdown and is dropped as quietly.
    /// </remarks>
    private async Task SweepOnTimer()
    {
        try
        {
            await Sweep(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Disposed before the pass began.
        }
    }

    /// <summary>
    /// Waits for the pass in progress, if there is one, to finish.
    /// </summary>
    /// <remarks>
    /// For a test that moves a fake clock: the timer starts its pass and does not wait for it, so the
    /// pass a clock move caused may still be running when the move returns. It has taken
    /// <see cref="sweepGate" /> by then — that happens before its first await — so taking the gate here
    /// waits exactly as long as the pass does.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal async ValueTask WaitForSweep(CancellationToken cancellationToken = default)
    {
        await sweepGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        sweepGate.Release();
    }

    /// <remarks>
    /// <para>
    /// Stops the timer and cancels a pass it started, and waits for nothing: a pass in flight stops at
    /// its next statement and gives the gate back itself, which is why the gate is not disposed here.
    /// <see cref="disposed" /> is set first, so a pass that fails because of any of this sees that it did.
    /// </para>
    /// <para>
    /// The timer is taken with a full fence after <see cref="disposed" /> is written, which pairs with
    /// <see cref="StartSweeping" /> storing its timer before it looks at <see cref="disposed" /> again: a
    /// timer being created while this runs is disposed by one of the two.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Interlocked.Exchange(ref sweepTimer, null)?.Dispose();
        stopping.Cancel();
        stopping.Dispose();
    }

    /// <summary>Whether <see cref="Dispose" /> has run, read afresh on every call.</summary>
    private bool IsDisposed() => disposed;

    /// <summary>
    /// Adds a scheduler to the ones the sweep walks, and learns this node's instance id in its own.
    /// </summary>
    private void Remember(string schedulerName, string schedulerInstanceId)
    {
        schedulers.TryAdd(schedulerName, 0);

        if (string.Equals(schedulerName, ownSchedulerName, StringComparison.Ordinal))
        {
            ownInstanceId = schedulerInstanceId;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Retention
    // ---------------------------------------------------------------------------------------------

    /// <summary>The instant an execution stops being part of the history, whatever its result.</summary>
    private DateTimeOffset ExecutionFloor() => Cutoff(timeProvider.GetUtcNow(), LongestWindow(historyOptions.Value));

    /// <summary>The instant a misfire stops being part of the history.</summary>
    private DateTimeOffset MisfireFloor() => Cutoff(timeProvider.GetUtcNow(), MisfireWindow(historyOptions.Value));

    /// <summary>How long executions of <paramref name="result" /> are kept.</summary>
    private static TimeSpan WindowOf(ExecutionHistoryOptions bounds, JobRunResult result)
    {
        return bounds.RetentionByResult.TryGetValue(result, out TimeSpan age) ? age : bounds.Retention;
    }

    /// <summary>The age of the result kept longest: every row past it has expired, whatever its result.</summary>
    internal static TimeSpan LongestWindow(ExecutionHistoryOptions bounds)
    {
        TimeSpan longest = TimeSpan.Zero;
        foreach (JobRunResult result in knownResults)
        {
            TimeSpan age = WindowOf(bounds, result);
            longest = age > longest ? age : longest;
        }

        return longest;
    }

    private static TimeSpan MisfireWindow(ExecutionHistoryOptions bounds) => bounds.MisfireRetention ?? bounds.Retention;

    /// <summary>
    /// The oldest instant an age keeps. An age reaching past <see cref="DateTimeOffset.MinValue" /> —
    /// <see cref="TimeSpan.MaxValue" />, to keep a result for good — keeps everything rather than
    /// overflowing.
    /// </summary>
    internal static DateTimeOffset Cutoff(DateTimeOffset now, TimeSpan age)
    {
        return age >= now - DateTimeOffset.MinValue ? DateTimeOffset.MinValue : now - age;
    }

    /// <summary>
    /// The windows shorter than the longest, each with the results kept for it, longest first.
    /// </summary>
    /// <remarks>
    /// <see cref="ExecutionHistoryOptions.Retention" /> is one of them for every result
    /// <see cref="ExecutionHistoryOptions.RetentionByResult" /> does not name, so the results a tier
    /// leaves to the default are trimmed by their own predicate too.
    /// </remarks>
    internal static List<(TimeSpan Age, List<JobRunResult> Results)> ShorterWindows(ExecutionHistoryOptions bounds)
    {
        TimeSpan longest = LongestWindow(bounds);
        List<(TimeSpan Age, List<JobRunResult> Results)> windows = [];

        foreach (JobRunResult result in knownResults)
        {
            TimeSpan age = WindowOf(bounds, result);
            if (age >= longest)
            {
                continue;
            }

            int index = windows.FindIndex(window => window.Age == age);
            if (index < 0)
            {
                windows.Add((age, [result]));
            }
            else
            {
                windows[index].Results.Add(result);
            }
        }

        windows.Sort(static (left, right) => right.Age.CompareTo(left.Age));
        return windows;
    }

    /// <summary>
    /// Applies every bound to one scheduler's feeds and statuses, unless another node is the sweeper.
    /// </summary>
    /// <returns>
    /// Whether the scheduler is done with for this pass — <see langword="false" /> when the budget ran out,
    /// or capped jobs were left, with rows still to go.
    /// </returns>
    private async ValueTask<bool> SweepScheduler(
        string schedulerName,
        ExecutionHistoryOptions bounds,
        SweepBudget budget,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        (int deleted, bool finished) = await Execute(async conn =>
        {
            if (!await IsSweeper(conn, schedulerName, now, cancellationToken).ConfigureAwait(false))
            {
                return (0, true);
            }

            int removed = 0;
            bool done = true;

            // 1. The longest window, whatever the result.
            DateTimeOffset longest = Cutoff(now, LongestWindow(bounds));
            Tally(await DeleteBelow(conn, misfires: false, schedulerName, longest, budget, cancellationToken).ConfigureAwait(false));

            // 2. Each shorter window, for the results it keeps.
            foreach ((TimeSpan age, List<JobRunResult> results) in ShorterWindows(bounds))
            {
                Tally(await DeleteSliceBelow(
                    conn, schedulerName, new ExecutionHistorySlice { Results = results }, Cutoff(now, age), budget, cancellationToken).ConfigureAwait(false));
            }

            // 3. The per-job cap, failures exempt.
            if (bounds.MaxEntriesPerJob > 0)
            {
                Tally(await CapJobs(conn, schedulerName, bounds.MaxEntriesPerJob, budget, cancellationToken).ConfigureAwait(false));
            }

            // 4. The misfire feed, for its own window.
            Tally(await DeleteBelow(
                conn, misfires: true, schedulerName, Cutoff(now, MisfireWindow(bounds)), budget, cancellationToken).ConfigureAwait(false));

            // 5. The statuses of deleted jobs, once they are past the longest window too.
            if (longest > DateTimeOffset.MinValue)
            {
                if (budget.TryTake())
                {
                    removed += await Delegate.DeleteOrphanedJobRunStatuses(conn, schedulerName, longest, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    done = false;
                }
            }

            // 6. The count bound on each feed, applied to what the age bounds left, so the boundary
            // row is looked up against a feed already inside its windows.
            Tally(await DeleteOverCount(conn, misfires: false, schedulerName, bounds, budget, cancellationToken).ConfigureAwait(false));
            Tally(await DeleteOverCount(conn, misfires: true, schedulerName, bounds, budget, cancellationToken).ConfigureAwait(false));

            return (removed, done);

            void Tally((int Deleted, bool Finished) step)
            {
                removed += step.Deleted;
                done &= step.Finished;
            }
        }, cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.ExecutionHistorySwept(schedulerName, deleted);
        }

        return finished;
    }

    /// <summary>
    /// Whether this node sweeps <paramref name="schedulerName" /> this pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The live node with the lowest instance id sweeps, read from <c>QRTZ_SCHEDULER_STATE</c>: no lock
    /// row, because a non-clustered store never writes one and there is no lock to try. With no live row
    /// lower than this node's — a single node, a store that is not clustered, SQLite — this node sweeps.
    /// </para>
    /// <para>
    /// Only the scheduler this store was built for is elected, because its delegate reads that
    /// scheduler's check-ins; another scheduler recorded here is swept as before. So is this one until
    /// this node has written a row and so knows its own instance id.
    /// </para>
    /// </remarks>
    private async ValueTask<bool> IsSweeper(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(schedulerName, ownSchedulerName, StringComparison.Ordinal) || ownInstanceId is not { } self)
        {
            return true;
        }

        List<SchedulerStateRecord> states = await Delegate.SelectSchedulerStateRecords(conn, instanceId: null, cancellationToken).ConfigureAwait(false);

        if (Sweeper(states, self, now) is not { } sweeper)
        {
            deferringTo = null;
            return true;
        }

        if (!string.Equals(deferringTo, sweeper, StringComparison.Ordinal))
        {
            deferringTo = sweeper;
            logger.ExecutionHistorySweepDeferred(schedulerName, sweeper);
        }

        return false;
    }

    /// <summary>
    /// The live node, other than <paramref name="self" />, whose instance id is ordinally lowest and
    /// lower than <paramref name="self" />'s; or <see langword="null" /> when there is none, and
    /// <paramref name="self" /> sweeps.
    /// </summary>
    internal static string? Sweeper(IEnumerable<SchedulerStateRecord> states, string self, DateTimeOffset now)
    {
        string? lowest = null;

        foreach (SchedulerStateRecord state in states)
        {
            if (!IsLive(state, now) || string.CompareOrdinal(state.SchedulerInstanceId, self) >= 0)
            {
                continue;
            }

            if (lowest is null || string.CompareOrdinal(state.SchedulerInstanceId, lowest) < 0)
            {
                lowest = state.SchedulerInstanceId;
            }
        }

        return lowest;
    }

    /// <summary>
    /// Whether a node's last check-in is recent enough that it is still sweeping: within twice its
    /// check-in interval, and never less than twice <see cref="MinimumCheckinWindow" />.
    /// </summary>
    internal static bool IsLive(SchedulerStateRecord state, DateTimeOffset now)
    {
        TimeSpan interval = state.CheckinInterval > MinimumCheckinWindow ? state.CheckinInterval : MinimumCheckinWindow;
        return state.CheckinTimestamp + interval + interval >= now;
    }

    /// <summary>
    /// Deletes everything in one feed below an instant, a bounded batch per statement.
    /// </summary>
    /// <remarks>
    /// The batch is bounded by an instant rather than by a row limit, because <c>DELETE … LIMIT</c> is
    /// spelled six different ways and two of them need a correlated subquery. Paging to the instant the
    /// batch ends at costs one indexed seek and leaves the delete a plain range predicate. The boundary
    /// row is included, so a batch always removes at least one row and a feed whose rows share one
    /// instant cannot stall the sweep.
    /// </remarks>
    /// <returns>
    /// The rows deleted, and whether that was all of them: <see langword="false" /> when the batch budget
    /// ran out while every batch was still finding a full batch to delete.
    /// </returns>
    private async ValueTask<(int Deleted, bool Finished)> DeleteBelow(
        ConnectionAndTransactionHolder conn,
        bool misfires,
        string schedulerName,
        DateTimeOffset cutoff,
        SweepBudget budget,
        CancellationToken cancellationToken)
    {
        if (cutoff == DateTimeOffset.MinValue)
        {
            // Kept for good: there is nothing below the start of time.
            return (0, true);
        }

        int deleted = 0;

        while (budget.TryTake())
        {
            DateTimeOffset? boundary = await Delegate.SelectHistoryBatchBoundary(
                conn, misfires, schedulerName, cutoff, SweepBatchSize, cancellationToken).ConfigureAwait(false);

            deleted += await Delegate.DeleteHistoryBefore(
                conn, misfires, schedulerName, boundary?.AddTicks(1) ?? cutoff, cancellationToken).ConfigureAwait(false);

            if (boundary is null)
            {
                // Fewer than a batch were left, so that statement finished the job.
                return (deleted, true);
            }
        }

        return (deleted, false);
    }

    /// <summary>
    /// <see cref="DeleteBelow" /> for a slice of the execution feed: a tier's results, or a capped job.
    /// </summary>
    private async ValueTask<(int Deleted, bool Finished)> DeleteSliceBelow(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        ExecutionHistorySlice slice,
        DateTimeOffset cutoff,
        SweepBudget budget,
        CancellationToken cancellationToken)
    {
        if (cutoff == DateTimeOffset.MinValue)
        {
            return (0, true);
        }

        int deleted = 0;

        while (budget.TryTake())
        {
            DateTimeOffset? boundary = await Delegate.SelectExecutionHistorySliceBoundary(
                conn, schedulerName, slice, cutoff, SweepBatchSize, cancellationToken).ConfigureAwait(false);

            deleted += await Delegate.DeleteExecutionHistorySlice(
                conn, schedulerName, slice, boundary?.AddTicks(1) ?? cutoff, cancellationToken).ConfigureAwait(false);

            if (boundary is null)
            {
                return (deleted, true);
            }
        }

        return (deleted, false);
    }

    /// <summary>
    /// Trims each job holding more than <paramref name="cap" /> executions that did not fail to its
    /// newest <paramref name="cap" />, at most <see cref="JobsCappedPerPass" /> jobs a pass.
    /// </summary>
    /// <remarks>
    /// The jobs over the cap are found with one <c>GROUP BY</c>; each job's boundary is then the first row
    /// past the cap, newest first, on <c>IDX_QRTZ_EH_JOB_TIME</c>, and its older rows go in batches. Rows
    /// sharing the boundary's instant go with it, so a job can be left below the cap rather than over it.
    /// </remarks>
    private async ValueTask<(int Deleted, bool Finished)> CapJobs(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        int cap,
        SweepBudget budget,
        CancellationToken cancellationToken)
    {
        if (budget.Exhausted)
        {
            return (0, false);
        }

        List<JobKey> jobs = await Delegate.SelectJobsOverHistoryCap(
            conn, schedulerName, cap, JobsCappedPerPass + 1, cancellationToken).ConfigureAwait(false);

        bool finished = jobs.Count <= JobsCappedPerPass;
        int deleted = 0;

        foreach (JobKey job in jobs.Take(JobsCappedPerPass))
        {
            DateTimeOffset? boundary = await Delegate.SelectJobHistoryCapBoundary(
                conn, schedulerName, job, cap, cancellationToken).ConfigureAwait(false);

            if (boundary is not { } firstToGo)
            {
                continue;
            }

            (int removed, bool done) = await DeleteSliceBelow(
                conn, schedulerName, new ExecutionHistorySlice { CappedJob = job }, firstToGo.AddTicks(1), budget, cancellationToken).ConfigureAwait(false);

            deleted += removed;
            finished &= done;
        }

        return (deleted, finished);
    }

    /// <summary>
    /// Applies <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" /> to one feed: the newest rows
    /// stay, whatever their result.
    /// </summary>
    private async ValueTask<(int Deleted, bool Finished)> DeleteOverCount(
        ConnectionAndTransactionHolder conn,
        bool misfires,
        string schedulerName,
        ExecutionHistoryOptions bounds,
        SweepBudget budget,
        CancellationToken cancellationToken)
    {
        if (budget.Exhausted)
        {
            return (0, false);
        }

        DateTimeOffset? countBoundary = await Delegate.SelectHistoryCountBoundary(
            conn, misfires, schedulerName, bounds.MaxEntriesPerScheduler, cancellationToken).ConfigureAwait(false);

        if (countBoundary is not { } boundary)
        {
            return (0, true);
        }

        // The boundary row is the first one to go, so the cutoff is one tick past it: the instants are
        // stored as ticks, which makes "at or below" a strictly-below predicate and keeps one statement
        // shape for both bounds. Rows sharing the boundary's instant go with it, so a feed whose rows
        // arrived together can be left shorter than the bound — which is the right way to be wrong
        // about a bound that says "at most".
        return await DeleteBelow(conn, misfires, schedulerName, boundary.AddTicks(1), budget, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings the next pass forward to <see cref="MinimumSweepInterval" />, after a pass that stopped on
    /// its batch budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pass is bounded so that it gives its connection back, and at the long interval that bound was
    /// also a ceiling on the rate: <see cref="SweepBatchesPerPass" /> × <see cref="SweepBatchSize" />
    /// rows every interval — some 20,000 rows in 2.4 hours at the defaults, under three a second. A
    /// scheduler recording faster than that grew the tables without limit while every pass did all it
    /// was allowed to. Coming back a minute later instead lifts the ceiling to that many rows a minute,
    /// and the connection is still given back between passes.
    /// </para>
    /// <para>
    /// Only the due time changes; the period the timer carries on with is still the long one, so the
    /// first pass that finishes puts the store back on it with nothing to undo. A pass that
    /// <em>failed</em> does not hurry — a database that is refusing the deletes is not helped by being
    /// asked every minute.
    /// </para>
    /// </remarks>
    private void CatchUp()
    {
        ITimer? timer = sweepTimer;
        if (timer is null || disposed)
        {
            return;
        }

        try
        {
            timer.Change(MinimumSweepInterval, SweepInterval());
        }
        catch (ObjectDisposedException)
        {
            // Disposed while this pass ran: there is no next pass to bring forward.
        }
    }

    /// <summary>
    /// How often the store sweeps while it is keeping up: a tenth of the shortest retention window, never
    /// more often than <see cref="MinimumSweepInterval" /> and never less often than
    /// <see cref="MaximumSweepInterval" />.
    /// </summary>
    internal TimeSpan SweepInterval()
    {
        ExecutionHistoryOptions bounds = historyOptions.Value;

        TimeSpan shortest = MisfireWindow(bounds);
        foreach (JobRunResult result in knownResults)
        {
            TimeSpan age = WindowOf(bounds, result);
            shortest = age < shortest ? age : shortest;
        }

        TimeSpan tenth = shortest / 10;
        if (tenth < MinimumSweepInterval)
        {
            return MinimumSweepInterval;
        }

        return tenth > MaximumSweepInterval ? MaximumSweepInterval : tenth;
    }

    /// <summary>
    /// Starts the sweep timer on the first write, and never again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the first write rather than in the constructor: a process that resolves this store and
    /// records nothing — one that maps the HTTP API to read another node's history — has nothing to
    /// sweep, and a timer it never needs is a connection it opens for no reason. The first tick is due
    /// immediately, which is the "sweep on the first write after startup" the retention rule asks for.
    /// </para>
    /// <para>
    /// Created stopped and started once it is in <see cref="sweepTimer" />, because the first pass can
    /// run before <c>CreateTimer</c> returns — a timer due at once may fire on another thread straight
    /// away, and a fake clock fires it inside the call — and a pass that has to <see cref="CatchUp" />
    /// needs the timer it is bringing forward.
    /// </para>
    /// <para>
    /// A disposal can land between the check at the top and the timer being stored, and it finds no timer
    /// to stop. So the store is asked again once the timer is in place, and a timer it cannot keep is
    /// disposed here. The store and the read are ordered against <see cref="Dispose" />'s own pair by full
    /// fences on both sides, so at least one of the two sees the other; if both do, the timer is disposed
    /// twice, which a timer allows.
    /// </para>
    /// </remarks>
    private void StartSweeping()
    {
        if (disposed || Interlocked.Exchange(ref sweepScheduled, 1) != 0)
        {
            return;
        }

        ITimer timer = timeProvider.CreateTimer(
            static state => _ = ((AdoExecutionHistoryStore) state!).SweepOnTimer(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        Interlocked.Exchange(ref sweepTimer, timer);

        // Read again, through a call: Dispose may have run on another thread since the check above.
        if (IsDisposed())
        {
            timer.Dispose();
            return;
        }

        try
        {
            timer.Change(TimeSpan.Zero, SweepInterval());
        }
        catch (ObjectDisposedException)
        {
            // Disposed between the second look and the start: the disposal stopped it, which is the
            // outcome the second look is for.
        }
    }

    /// <summary>
    /// How many more <c>DELETE</c> statements one pass may run, across every bound and scheduler.
    /// </summary>
    private sealed class SweepBudget(int batches)
    {
        private int remaining = batches;

        public bool Exhausted => remaining == 0;

        public bool TryTake()
        {
            if (remaining == 0)
            {
                return false;
            }

            remaining--;
            return true;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Reaching the database
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The key of one history row. A value this store mints, as <c>QRTZ_FIRED_TRIGGERS.ENTRY_ID</c> is:
    /// no two dialects spell an identity column the same way, and nothing reads the number.
    /// </summary>
    private static string NewEntryId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Records one execution and folds it into its job's status, in one transaction of this store's own.
    /// </summary>
    private async ValueTask Record(ExecutionHistoryEntry entry, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        DbConnection connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Twice at most: the second attempt's status insert is not caught, so it commits or throws.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                // Disposed uncommitted, which rolls it back, on every path but the commit.
                DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using (transaction.ConfigureAwait(false))
                {
                    ConnectionAndTransactionHolder holder = new(connection, transaction, ownsResources: false);

                    if (await RecordIn(holder, entry, lastAttempt: attempt > 0, cancellationToken).ConfigureAwait(false))
                    {
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    // Another node inserted the status first. Its row is committed, so the update finds it
                    // when the unit runs again; the execution row goes with this rollback.
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <returns>
    /// <see langword="false" /> when the status insert failed on a first attempt, which is another node
    /// recording the job's first run at the same moment; the caller rolls back and runs the unit again.
    /// </returns>
    private async ValueTask<bool> RecordIn(
        ConnectionAndTransactionHolder conn,
        ExecutionHistoryEntry entry,
        bool lastAttempt,
        CancellationToken cancellationToken)
    {
        await Delegate.InsertExecutionHistory(conn, entry.EntryId!, entry, cancellationToken).ConfigureAwait(false);

        if (await Delegate.UpdateJobRunStatus(conn, entry, cancellationToken).ConfigureAwait(false) > 0)
        {
            return true;
        }

        try
        {
            await Delegate.InsertJobRunStatus(conn, entry, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbException) when (!lastAttempt)
        {
            // Every provider spells a duplicate key differently, and a deadlock between two first runs
            // on MySQL is the same race; either way the unit runs once more.
            return false;
        }
    }

    private async ValueTask<T> Execute<T>(
        Func<ConnectionAndTransactionHolder, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        DbConnection connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Owning nothing: the connection is disposed above, and there is no transaction to commit.
            // Each statement is its own unit of work, which is what "never inside the job's
            // transaction" comes to in practice.
            ConnectionAndTransactionHolder holder = new(connection, transaction: null, ownsResources: false);
            return await action(holder).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens a connection that is this store's alone, outside whatever transaction is in progress.
    /// </summary>
    /// <remarks>
    /// An ambient <see cref="Transaction" /> would enlist this connection in the job's own
    /// transaction, so a rolled-back job would take its history row with it — and a second connection
    /// in that transaction needs a distributed one, which not every provider has. Suppressed exactly
    /// as <c>AdoJobStoreBase.OpenOwnConnection</c> suppresses it, and only around the open, which is
    /// where enlistment happens.
    /// </remarks>
    private async ValueTask<DbConnection> OpenConnection(CancellationToken cancellationToken)
    {
        using TransactionScope? suppression = Transaction.Current is not null
            ? new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled)
            : null;

        DbConnection connection = dbProvider.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
