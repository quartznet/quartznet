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
/// two tables <c>database/migrations/4.2/add_execution_history_&lt;dialect&gt;.sql</c> creates into the
/// schema the store validates at startup. Without that call nothing here runs and the tables may be
/// absent, which is what makes the migration optional.
/// </para>
/// <para>
/// Every statement runs on a connection of this store's own, outside any ambient transaction: a
/// history write is a record of something that already happened, so it must not be rolled back with
/// the job's work, must not hold the trigger lock, and must never be the reason a firing fails. A
/// write that cannot reach the database is logged and dropped.
/// </para>
/// <para>
/// Reading applies the age bound as well as the sweep does, exactly as the in-memory history does and
/// for the same reason: a scheduler that has stopped running jobs never writes again, and it is that
/// scheduler whose page would otherwise go on showing executions from days ago. The count bound is
/// the sweep's alone — it is a property of the whole cluster's feed rather than of one page, and
/// answering it on every read would mean a window function six dialects spell differently.
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
    /// The bound is per pass, not per interval: a pass that stops on it brings the next one forward to
    /// <see cref="MinimumSweepInterval" />, which is what keeps the sweep ahead of a busy scheduler.
    /// </remarks>
    internal const int SweepBatchesPerPass = 20;

    /// <summary>
    /// The floor under the sweep interval, whatever the retention window is — and the interval a pass
    /// that ran out of batches brings the next one forward to.
    /// </summary>
    internal static readonly TimeSpan MinimumSweepInterval = TimeSpan.FromMinutes(1);

    private readonly IDbProvider dbProvider;
    private readonly IDriverDelegate driverDelegate;
    private readonly IOptions<ExecutionHistoryOptions> historyOptions;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AdoExecutionHistoryStore> logger;

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

        schedulers.TryAdd(schedulerOptions.Value.InstanceName, 0);
    }

    private StdAdoDelegate Delegate => (StdAdoDelegate) driverDelegate;

    public async ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        schedulers.TryAdd(entry.SchedulerName, 0);

        try
        {
            await Execute(
                conn => Delegate.InsertExecutionHistory(conn, NewEntryId(), entry, cancellationToken),
                cancellationToken).ConfigureAwait(false);
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

        schedulers.TryAdd(entry.SchedulerName, 0);

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
            conn => Delegate.SelectExecutionHistory(conn, query, RetentionFloor(), cancellationToken),
            cancellationToken);
    }

    public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(
        MisfireHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Execute(
            conn => Delegate.SelectMisfireHistory(conn, query, RetentionFloor(), cancellationToken),
            cancellationToken);
    }

    public ValueTask<int> CountMisfires(
        string schedulerName,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        // The later of the two bounds, so that the count never includes a row a read would not show.
        DateTimeOffset from = since > RetentionFloor() ? since : RetentionFloor();

        return Execute(
            conn => Delegate.CountMisfireHistorySince(conn, schedulerName, from, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Deletes what has fallen out of either bound, for every scheduler this store has seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internal so that a test can run one pass rather than wait for the timer. Each node sweeps
    /// independently and the deletes are idempotent, so a cluster sweeping in parallel does the same
    /// work twice at worst — never the wrong work.
    /// </para>
    /// <para>
    /// A pass that stops on its batch budget on either bound of either feed brings the next pass
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
            bool finished = true;

            foreach (string schedulerName in schedulers.Keys)
            {
                // Not short-circuited: a feed that ran out of batches is no reason to leave the next one
                // unswept this pass.
                finished &= await SweepFeed(schedulerName, misfires: false, bounds, cancellationToken).ConfigureAwait(false);
                finished &= await SweepFeed(schedulerName, misfires: true, bounds, cancellationToken).ConfigureAwait(false);
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

    // ---------------------------------------------------------------------------------------------
    // Retention
    // ---------------------------------------------------------------------------------------------

    /// <summary>The instant a row stops being part of the history.</summary>
    private DateTimeOffset RetentionFloor() => timeProvider.GetUtcNow() - historyOptions.Value.Retention;

    /// <summary>
    /// Applies both bounds to one scheduler's feed.
    /// </summary>
    /// <returns>
    /// Whether both bounds finished inside their batch budget — <see langword="false" /> when either one
    /// stopped on it with rows still to go.
    /// </returns>
    private async ValueTask<bool> SweepFeed(
        string schedulerName,
        bool misfires,
        ExecutionHistoryOptions bounds,
        CancellationToken cancellationToken)
    {
        (int deleted, bool finished) = await Execute(async conn =>
        {
            (int removed, bool finishedAge) = await DeleteBelow(
                conn, misfires, schedulerName, RetentionFloor(), cancellationToken).ConfigureAwait(false);

            // The count bound is applied to what the age bound left, so the boundary row is looked up
            // once against a feed that is already inside its window.
            DateTimeOffset? countBoundary = await Delegate.SelectHistoryCountBoundary(
                conn, misfires, schedulerName, bounds.MaxEntriesPerScheduler, cancellationToken).ConfigureAwait(false);

            bool finishedCount = true;
            if (countBoundary is { } boundary)
            {
                // The boundary row is the first one to go, so the cutoff is one tick past it: the
                // instants are stored as ticks, which makes "at or below" a strictly-below predicate
                // and keeps one statement shape for both bounds. Rows sharing the boundary's instant
                // go with it, so a feed whose rows arrived together can be left shorter than the
                // bound — which is the right way to be wrong about a bound that says "at most".
                (int removedByCount, finishedCount) = await DeleteBelow(
                    conn, misfires, schedulerName, boundary.AddTicks(1), cancellationToken).ConfigureAwait(false);

                removed += removedByCount;
            }

            return (removed, finishedAge && finishedCount);
        }, cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.ExecutionHistorySwept(schedulerName, deleted);
        }

        return finished;
    }

    /// <summary>
    /// Deletes everything below an instant, a bounded batch per statement.
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
        CancellationToken cancellationToken)
    {
        int deleted = 0;

        for (int batch = 0; batch < SweepBatchesPerPass; batch++)
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
    /// Brings the next pass forward to <see cref="MinimumSweepInterval" />, after a pass that stopped on
    /// its batch budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pass is bounded so that it gives its connection back, and at the long interval that bound was
    /// also a ceiling on the rate: <see cref="SweepBatchesPerPass" /> × <see cref="SweepBatchSize" />
    /// rows a bound a feed every <c>Retention / 10</c> — some 20,000 rows in 2.4 hours at the defaults,
    /// under three a second. A scheduler recording faster than that grew the tables without limit while
    /// every pass did all it was allowed to. Coming back a minute later instead lifts the ceiling to that
    /// many rows a minute, and the connection is still given back between passes.
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
    /// How often the store sweeps while it is keeping up: a tenth of the retention window, and never
    /// more often than <see cref="MinimumSweepInterval" />.
    /// </summary>
    private TimeSpan SweepInterval()
    {
        TimeSpan tenth = historyOptions.Value.Retention / 10;
        return tenth > MinimumSweepInterval ? tenth : MinimumSweepInterval;
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

    // ---------------------------------------------------------------------------------------------
    // Reaching the database
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The key of one history row. A value this store mints, as <c>QRTZ_FIRED_TRIGGERS.ENTRY_ID</c> is:
    /// no two dialects spell an identity column the same way, and nothing reads the number.
    /// </summary>
    private static string NewEntryId() => Guid.NewGuid().ToString("N");

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
