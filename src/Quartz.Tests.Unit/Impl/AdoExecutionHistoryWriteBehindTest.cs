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

using System.Diagnostics;

using FakeItEasy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The database history with <see cref="ExecutionHistoryOptions.WriteBehindInterval" /> set: completions
/// queued, flushed by interval, by batch, on shutdown and on disposal, folded into one status write per job,
/// and never lost without a word.
/// </summary>
public sealed partial class AdoExecutionHistoryStoreContractTest
{
    private static readonly TimeSpan writeBehind = TimeSpan.FromMinutes(1);
    private static readonly JobKey busy = new("busy", JobGroup);
    private static readonly JobKey quiet = new("quiet", JobGroup);

    /// <summary>
    /// A flush writes one status per job, whatever number of runs it folds into it, and the status is the
    /// one <see cref="JobRunStatusFold" /> gives for those runs in the order they completed.
    /// </summary>
    /// <remarks>
    /// The runs complete out of order and mix every kind of result, so a fold that only added the counters
    /// up would be caught on the last-run columns and on the failure streak.
    /// </remarks>
    [Test]
    public async Task EachJobsRunsAreFoldedIntoOneStatusWritePerFlush()
    {
        await CreateStore(_ => { });
        CountingDelegate counting = new();
        await using AdoExecutionHistoryStore history = WriteBehindStore(counting);

        ExecutionHistoryEntry[] firstFlush =
        [
            Named(busy, "b1", 0, JobRunResult.Succeeded),
            Named(quiet, "q1", 0, JobRunResult.Succeeded)
        ];

        ExecutionHistoryEntry[] secondFlush =
        [
            Named(busy, "b2", 10, JobRunResult.Failed) with { ExceptionMessage = "gave up" },
            Named(quiet, "q2", 3, JobRunResult.Skipped) with { Summary = "nothing to do" },
            Named(busy, "b3", 12, JobRunResult.Failed) with { RetryScheduled = true, ExceptionMessage = "retrying" },
            Named(busy, "b4", 5, JobRunResult.Succeeded) with { Summary = "completed late" },
            Named(busy, "b5", 14, JobRunResult.Failed) with { ExceptionMessage = "gave up again" },
            Named(quiet, "q3", 4, JobRunResult.Cancelled),
            Named(busy, "b6", 14, JobRunResult.Cancelled),
            Named(busy, "b7", 13, JobRunResult.Failed) with { ExceptionMessage = "older, completed last" }
        ];

        foreach (ExecutionHistoryEntry run in firstFlush)
        {
            await history.AddExecution(run);
        }

        await history.Flush();
        counting.StatusInserts.Should().Be(2, "a job's first flush inserts its status");

        foreach (ExecutionHistoryEntry run in secondFlush)
        {
            await history.AddExecution(run);
        }

        (await RowCount()).Should().Be(2, "the second batch waits in the queue until something flushes it");

        await history.Flush();

        counting.GuardedUpdates.Should().Equal([2],
            "one flush wrote both jobs' statuses, one update each, however many runs each folded in");
        counting.PerRunUpdates.Should().Be(0, "no status was written run by run");
        (await RowCount()).Should().Be(10, "every run has its row");

        foreach (JobKey job in new[] { busy, quiet })
        {
            JobRunStatus? expected = null;
            foreach (ExecutionHistoryEntry run in firstFlush.Concat(secondFlush).Where(run => run.JobName == job.Name))
            {
                expected = JobRunStatusFold.Apply(expected, run);
            }

            (await history.GetJobRunStatus(SchedulerName, job)).Should().BeEquivalentTo(expected,
                "the status a flush writes is the fold of {0}'s runs in the order they completed", job.Name);
        }
    }

    /// <summary>A queued run is written once the interval has passed, and not before.</summary>
    [Test]
    public async Task TheIntervalFlushesWhatIsQueued()
    {
        await CreateStore(_ => { });
        await using AdoExecutionHistoryStore history = WriteBehindStore(new SQLiteDelegate());

        await history.AddExecution(Named(busy, "first", 0, JobRunResult.Succeeded));
        await history.AddExecution(Named(busy, "second", 1, JobRunResult.Succeeded));

        (await RowCount()).Should().Be(0, "nothing is written until the window closes, and the clock has not moved");

        await Eventually(
            async () => await RowCount() == 2,
            "the writer flushes once a window of WriteBehindInterval has passed",
            () => Clock.Advance(writeBehind));

        (await history.GetJobRunStatus(SchedulerName, busy))!.RunCount.Should().Be(2);
    }

    /// <summary>A full batch is written at once, without waiting for the interval.</summary>
    [Test]
    public async Task AFullBatchIsWrittenWithoutWaitingForTheInterval()
    {
        await CreateStore(_ => { });
        await using AdoExecutionHistoryStore history = WriteBehindStore(new SQLiteDelegate(), batchSize: 3);

        foreach (int minutes in new[] { 0, 1, 2 })
        {
            await history.AddExecution(Named(busy, "run-" + minutes, minutes, JobRunResult.Succeeded));
        }

        await Eventually(async () => await RowCount() == 3, "three queued runs are a batch, which closes the window");
    }

    /// <summary>
    /// The scheduler's shutdown writes what is queued, through the recorder's own shutdown, before the job
    /// store closes the connections.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ShutdownWritesWhatIsQueued(bool waitForJobsToComplete)
    {
        IExecutionHistoryStore store = await CreateStore(options => options.WriteBehindInterval = writeBehind);

        await store.AddExecution(Named(busy, "first", 0, JobRunResult.Failed) with { ExceptionMessage = "refused" });
        await store.AddExecution(Named(busy, "second", 1, JobRunResult.Succeeded));
        (await RowCount()).Should().Be(0, "both runs are queued");

        await scheduler.Shutdown(waitForJobsToComplete);

        (await RowCount()).Should().Be(2, "the recorder's shutdown flushed the queue");
        JobRunStatus status = (await store.GetJobRunStatus(SchedulerName, busy))!;
        status.RunCount.Should().Be(2);
        status.FailureCount.Should().Be(1);
        status.ConsecutiveFailures.Should().Be(0, "the newer run succeeded");
    }

    /// <summary>Disposing the store writes what is queued, synchronously or not.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task DisposingTheStoreWritesWhatIsQueued(bool asynchronously)
    {
        await CreateStore(_ => { });
        AdoExecutionHistoryStore history = WriteBehindStore(new SQLiteDelegate());

        await history.AddExecution(Named(busy, "first", 0, JobRunResult.Succeeded));
        await history.AddExecution(Named(busy, "second", 1, JobRunResult.Succeeded));

        if (asynchronously)
        {
            await history.DisposeAsync();
        }
        else
        {
            history.Dispose();
        }

        (await RowCount()).Should().Be(2, "disposal is the store's last chance to record runs that happened");
        await history.Flush();
        history.Dispose();
    }

    /// <summary>
    /// A run that arrives after the store was disposed is not queued where nothing would write it: it is
    /// written as it arrives, which a disposed store refuses, and the loss is reported like any other.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ARunAfterDisposalIsNotQueued(bool queuedBefore)
    {
        await CreateStore(_ => { });
        RecordingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        AdoExecutionHistoryStore history = WriteBehindStore(new SQLiteDelegate(), loggerFactory: loggerFactory);

        if (queuedBefore)
        {
            await history.AddExecution(Named(busy, "before", 0, JobRunResult.Succeeded));
        }

        await history.DisposeAsync();

        Func<Task> late = async () => await history.AddExecution(Named(busy, "after", 1, JobRunResult.Succeeded));

        await late.Should().NotThrowAsync("a history write never fails the firing it records");
        logs.Entries.Should().ContainSingle(entry => entry.EventId.Id == 3160,
            "the late run was written as it arrived, which the disposed store refused and reported");
        (await RowCount()).Should().Be(queuedBefore ? 1 : 0, "only the run queued before the disposal was written");
    }

    /// <summary>
    /// A run that finds the queue full writes itself, as with write-behind off: nothing waits for room and
    /// nothing is dropped, and the flush that follows folds onto the row that run wrote.
    /// </summary>
    [Test]
    public async Task ARunThatFindsTheQueueFullWritesItself()
    {
        await CreateStore(_ => { });
        await using AdoExecutionHistoryStore history = WriteBehindStore(new SQLiteDelegate(), batchSize: 10, capacity: 2);

        ExecutionHistoryEntry first = Named(busy, "first", 0, JobRunResult.Succeeded);
        ExecutionHistoryEntry second = Named(busy, "second", 1, JobRunResult.Failed) with { ExceptionMessage = "refused" };
        ExecutionHistoryEntry third = Named(busy, "third", 2, JobRunResult.Succeeded);

        await history.AddExecution(first);
        await history.AddExecution(second);
        await history.AddExecution(third);

        (await RowCount()).Should().Be(1, "the queue holds two, so the third run was written as it completed");
        (await history.GetExecution(SchedulerName, "third")).Should().NotBeNull();

        await history.Flush();

        (await RowCount()).Should().Be(3);
        (await history.GetJobRunStatus(SchedulerName, busy)).Should().BeEquivalentTo(
            JobRunStatusFold.Apply(JobRunStatusFold.Apply(JobRunStatusFold.Apply(null, third), first), second),
            "the flush read the status the third run wrote and folded the queued two onto it");
    }

    /// <summary>
    /// A flush that cannot be written is reported with event 3164, naming how many rows each scheduler lost,
    /// and the writer carries on with the next.
    /// </summary>
    [Test]
    public async Task AFlushThatCannotBeWrittenReportsTheRowsItLost()
    {
        await CreateStore(_ => { });
        RecordingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        FailingInsertsDelegate failing = new(failures: 1, static () => new InvalidOperationException("the history table refused the batch"));
        await using AdoExecutionHistoryStore history = WriteBehindStore(failing, loggerFactory: loggerFactory);

        await history.AddExecution(Named(busy, "first", 0, JobRunResult.Succeeded));
        await history.AddExecution(Named(busy, "second", 1, JobRunResult.Succeeded));
        await history.AddExecution(Named(busy, "elsewhere", 1, JobRunResult.Succeeded) with { SchedulerName = "OtherScheduler" });

        await history.Flush();

        (await RowCount()).Should().Be(0, "the batch failed as one transaction");
        List<LogEntry> lost = logs.Entries.Where(entry => entry.EventId.Id == 3164).ToList();
        lost.Should().HaveCount(2, "each scheduler's loss is reported under its own name");
        lost.Should().OnlyContain(entry => entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);
        lost.Select(entry => entry.Message).Should().ContainMatch($"*2 buffered*'{SchedulerName}'*")
            .And.ContainMatch("*1 buffered*'OtherScheduler'*");
        failing.Calls.Should().Be(1, "a failure that is not transient is not tried again");

        await history.AddExecution(Named(busy, "after", 2, JobRunResult.Succeeded));
        await history.Flush();

        (await RowCount()).Should().Be(1, "the writer carries on after a batch it could not write");
    }

    /// <summary>
    /// A transient failure runs the flush again, as many times as the job store retries its own writes,
    /// before the rows are reported lost.
    /// </summary>
    [TestCase(1, 0)]
    [TestCase(5, 1)]
    public async Task ATransientFailureIsRetriedAsTheJobStoreRetries(int failures, int reportedLost)
    {
        await CreateStore(_ => { });
        RecordingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        FailingInsertsDelegate failing = new(failures, static () => new TimeoutException("the database is busy"));
        AdoJobStoreOptions retries = new() { MaxTransientRetries = 2, TransientRetryInterval = TimeSpan.Zero };
        await using AdoExecutionHistoryStore history = WriteBehindStore(failing, loggerFactory: loggerFactory, storeOptions: retries);

        await history.AddExecution(Named(busy, "first", 0, JobRunResult.Succeeded));
        await history.Flush();

        failing.Calls.Should().Be(Math.Min(failures + 1, 3), "a transient failure is retried MaxTransientRetries times");
        logs.Entries.Count(entry => entry.EventId.Id == 3164).Should().Be(reportedLost);
        (await RowCount()).Should().Be(reportedLost == 0 ? 1 : 0);
    }

    /// <summary>
    /// A status another writer changed between the flush's read and its update is folded again from the row
    /// as it stands, and the rolled-back rows are written once.
    /// </summary>
    [Test]
    public async Task AStatusChangedSinceTheReadIsFoldedAgain()
    {
        await CreateStore(_ => { });
        StaleReadsDelegate stale = new();
        await using AdoExecutionHistoryStore history = WriteBehindStore(stale);

        ExecutionHistoryEntry[] runs =
        [
            Named(busy, "first", 0, JobRunResult.Succeeded),
            Named(busy, "second", 1, JobRunResult.Failed) with { ExceptionMessage = "refused" },
            Named(busy, "third", 2, JobRunResult.Failed) with { ExceptionMessage = "refused again" }
        ];

        await history.AddExecution(runs[0]);
        await history.Flush();

        stale.StaleReads = 1;
        await history.AddExecution(runs[1]);
        await history.AddExecution(runs[2]);
        await history.Flush();

        stale.Reads.Should().Be(3, "the first flush read once, and the second read again after its update missed");
        stale.PerRunUpdates.Should().Be(0, "one miss is folded again, not written run by run");
        (await RowCount()).Should().Be(3, "the rolled-back attempt took its rows with it");
        (await history.GetJobRunStatus(SchedulerName, busy)).Should().BeEquivalentTo(Folded(runs));
    }

    /// <summary>
    /// A status that changes under every folded attempt is written run by run, which adds to the row as it
    /// stands and so cannot miss.
    /// </summary>
    [Test]
    public async Task AStatusThatKeepsChangingIsFoldedRunByRun()
    {
        await CreateStore(_ => { });
        StaleReadsDelegate stale = new();
        await using AdoExecutionHistoryStore history = WriteBehindStore(stale);

        ExecutionHistoryEntry[] runs =
        [
            Named(busy, "first", 0, JobRunResult.Succeeded),
            Named(busy, "second", 1, JobRunResult.Failed) with { ExceptionMessage = "refused" },
            Named(busy, "third", 2, JobRunResult.Succeeded)
        ];

        await history.AddExecution(runs[0]);
        await history.Flush();

        stale.StaleReads = int.MaxValue;
        await history.AddExecution(runs[1]);
        await history.AddExecution(runs[2]);
        await history.Flush();

        stale.Reads.Should().Be(1 + AdoExecutionHistoryStore.FoldedFlushAttempts, "each folded attempt read once and missed");
        stale.PerRunUpdates.Should().Be(2, "the attempt after them folded each run on its own");
        (await RowCount()).Should().Be(3);
        (await history.GetJobRunStatus(SchedulerName, busy)).Should().BeEquivalentTo(Folded(runs));
    }

    /// <summary>
    /// A flush that found no status inserts one, and if another writer inserted it first the insert fails
    /// on the key and the flush folds onto that writer's row instead.
    /// </summary>
    [Test]
    public async Task AFirstStatusAnotherWriterInsertedFirstIsFoldedOnto()
    {
        IExecutionHistoryStore other = await CreateStore(_ => { });
        BlindFirstReadDelegate blind = new();
        await using AdoExecutionHistoryStore history = WriteBehindStore(blind);

        ExecutionHistoryEntry otherNodes = Named(busy, "other-node", 0, JobRunResult.Succeeded) with { SchedulerInstanceId = "node-b" };
        ExecutionHistoryEntry ours = Named(busy, "ours", 1, JobRunResult.Failed) with { ExceptionMessage = "refused" };

        await other.AddExecution(otherNodes);
        await history.AddExecution(ours);
        await history.Flush();

        (await RowCount()).Should().Be(2, "the attempt whose insert failed was rolled back, rows and all");
        (await history.GetJobRunStatus(SchedulerName, busy)).Should().BeEquivalentTo(Folded([otherNodes, ours]),
            "the insert lost the race, so the flush read the row the other node wrote and folded onto it");
    }

    /// <summary>Two nodes flushing runs of one job at once count every run once.</summary>
    [Test]
    public async Task TwoNodesFlushingOneJobAtOnceCountEveryRun()
    {
        await CreateStore(_ => { });
        await using AdoExecutionHistoryStore nodeA = WriteBehindStore(new SQLiteDelegate(), instanceId: "node-a");
        await using AdoExecutionHistoryStore nodeB = WriteBehindStore(new SQLiteDelegate(), instanceId: "node-b");

        int failures = 0;
        for (int round = 0; round < 4; round++)
        {
            for (int i = 0; i < 10; i++)
            {
                bool failed = i % 3 == 0;
                failures += failed ? 2 : 0;
                JobRunResult result = failed ? JobRunResult.Failed : JobRunResult.Succeeded;

                await nodeA.AddExecution(Named(busy, $"a-{round}-{i}", round * 10 + i, result));
                await nodeB.AddExecution(Named(busy, $"b-{round}-{i}", round * 10 + i, result) with { SchedulerInstanceId = "node-b" });
            }

            await Task.WhenAll(nodeA.Flush().AsTask(), nodeB.Flush().AsTask());
        }

        JobRunStatus status = (await nodeA.GetJobRunStatus(SchedulerName, busy))!;
        status.RunCount.Should().Be(80, "each node's flush counted its own runs once, whichever wrote first");
        status.FailureCount.Should().Be(failures);
        (await RowCount()).Should().Be(80);
    }

    /// <summary>With write-behind off there is no queue, and a flush or an asynchronous disposal has nothing to do.</summary>
    [Test]
    public async Task WithoutWriteBehindAFlushHasNothingToDo()
    {
        await CreateStore(_ => { });
        AdoExecutionHistoryStore history = StoreOver(new SQLiteDelegate());

        await history.AddExecution(Named(busy, "first", 0, JobRunResult.Succeeded));
        (await RowCount()).Should().Be(1, "with write-behind off a run is written as it completes");

        await ((IBufferedExecutionHistoryStore) history).Flush();
        await history.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>A store over the contract's database with write-behind on, sized as the case needs.</summary>
    private AdoExecutionHistoryStore WriteBehindStore(
        StdAdoDelegate driverDelegate,
        int batchSize = ExecutionHistoryWriteBehind.DefaultBatchSize,
        int capacity = ExecutionHistoryWriteBehind.DefaultCapacity,
        ILoggerFactory? loggerFactory = null,
        AdoJobStoreOptions? storeOptions = null,
        string instanceId = "node-a")
    {
        IDbProvider dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = AdoConstants.DefaultTablePrefix,
            SchedulerName = SchedulerName,
            InstanceId = instanceId,
            DbProvider = dbProvider,
            TypeLoader = new SimpleTypeLoader(),
        });

        return new AdoExecutionHistoryStore(
            dbProvider,
            driverDelegate,
            Options.Create(new ExecutionHistoryOptions { WriteBehindInterval = writeBehind }),
            Options.Create(new QuartzSchedulerOptions { InstanceName = SchedulerName }),
            new ClockWithoutHistorySweeps(Clock),
            loggerFactory,
            storeOptions is null ? null : Options.Create(storeOptions))
        {
            WriteBehindBatchSize = batchSize,
            WriteBehindCapacity = capacity
        };
    }

    /// <summary>One run of <paramref name="job" />, named, and <paramref name="minutes" /> after <c>Start</c>.</summary>
    private static ExecutionHistoryEntry Named(JobKey job, string entryId, int minutes, JobRunResult result)
    {
        return Run(Start.AddMinutes(minutes), job.Name, result) with { EntryId = entryId };
    }

    private static JobRunStatus? Folded(IEnumerable<ExecutionHistoryEntry> runs)
    {
        JobRunStatus? status = null;
        foreach (ExecutionHistoryEntry run in runs)
        {
            status = JobRunStatusFold.Apply(status, run);
        }

        return status;
    }

    /// <summary>
    /// Waits for something the writer does on its own task, nudging it on each look when it needs the clock
    /// moved.
    /// </summary>
    private static async Task Eventually(Func<Task<bool>> condition, string because, Action? nudge = null)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (!await condition())
        {
            waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), because);
            nudge?.Invoke();
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    /// <summary>The shipped SQLite delegate, counting how the statuses were written.</summary>
    private sealed class CountingDelegate : SQLiteDelegate
    {
        private readonly List<int> guardedUpdates = [];
        private int perRunUpdates;
        private int statusInserts;

        public List<int> GuardedUpdates
        {
            get
            {
                lock (guardedUpdates)
                {
                    return [.. guardedUpdates];
                }
            }
        }

        public int PerRunUpdates => Volatile.Read(ref perRunUpdates);

        public int StatusInserts => Volatile.Read(ref statusInserts);

        internal override ValueTask<int> UpdateJobRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref perRunUpdates);
            return base.UpdateJobRunStatus(conn, entry, cancellationToken);
        }

        internal override ValueTask<int> UpdateJobRunStatusesIfUnchanged(
            ConnectionAndTransactionHolder conn,
            IReadOnlyList<(JobRunStatus Status, long ReadRunCount)> writes,
            CancellationToken cancellationToken = default)
        {
            lock (guardedUpdates)
            {
                guardedUpdates.Add(writes.Count);
            }

            return base.UpdateJobRunStatusesIfUnchanged(conn, writes, cancellationToken);
        }

        internal override ValueTask<int> InsertJobRunStatus(
            ConnectionAndTransactionHolder conn,
            JobRunStatus status,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref statusInserts);
            return base.InsertJobRunStatus(conn, status, cancellationToken);
        }
    }

    /// <summary>The shipped SQLite delegate, whose first flushes fail to insert their rows.</summary>
    private sealed class FailingInsertsDelegate(int failures, Func<Exception> failure) : SQLiteDelegate
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        internal override ValueTask InsertExecutionHistories(
            ConnectionAndTransactionHolder conn,
            IReadOnlyList<ExecutionHistoryEntry> entries,
            CancellationToken cancellationToken = default)
        {
            return Interlocked.Increment(ref calls) <= failures
                ? throw failure()
                : base.InsertExecutionHistories(conn, entries, cancellationToken);
        }
    }

    /// <summary>
    /// The shipped SQLite delegate, whose status reads can be made to answer one run behind: the read a
    /// flush took just before another writer's update committed.
    /// </summary>
    private sealed class StaleReadsDelegate : SQLiteDelegate
    {
        private int reads;
        private int perRunUpdates;

        public int StaleReads { get; set; }

        public int Reads => Volatile.Read(ref reads);

        public int PerRunUpdates => Volatile.Read(ref perRunUpdates);

        internal override async ValueTask<Dictionary<JobKey, JobRunStatus>> SelectJobRunStatusesByKey(
            ConnectionAndTransactionHolder conn,
            string schedulerName,
            IReadOnlyCollection<JobKey> jobs,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref reads);
            Dictionary<JobKey, JobRunStatus> read = await base.SelectJobRunStatusesByKey(conn, schedulerName, jobs, cancellationToken);

            if (read.Count == 0 || StaleReads <= 0)
            {
                return read;
            }

            StaleReads--;
            return read.ToDictionary(static pair => pair.Key, static pair => pair.Value with { RunCount = pair.Value.RunCount - 1 });
        }

        internal override ValueTask<int> UpdateJobRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref perRunUpdates);
            return base.UpdateJobRunStatus(conn, entry, cancellationToken);
        }
    }

    /// <summary>
    /// The shipped SQLite delegate, whose first status read finds nothing: the moment before another node's
    /// insert of the job's first status has committed.
    /// </summary>
    private sealed class BlindFirstReadDelegate : SQLiteDelegate
    {
        private int reads;

        internal override ValueTask<Dictionary<JobKey, JobRunStatus>> SelectJobRunStatusesByKey(
            ConnectionAndTransactionHolder conn,
            string schedulerName,
            IReadOnlyCollection<JobKey> jobs,
            CancellationToken cancellationToken = default)
        {
            return Interlocked.Increment(ref reads) == 1
                ? new ValueTask<Dictionary<JobKey, JobRunStatus>>(new Dictionary<JobKey, JobRunStatus>())
                : base.SelectJobRunStatusesByKey(conn, schedulerName, jobs, cancellationToken);
        }
    }
}

/// <summary>
/// The write-behind queue on its own: what it does once it is closed, and with a flush nobody waits for.
/// </summary>
public sealed class ExecutionHistoryWriteBehindTest
{
    [Test]
    public async Task AClosedQueueRefusesARunAndAFlushReturnsOnceTheWriterHasStopped()
    {
        List<int> written = [];
        ExecutionHistoryWriteBehind queue = new(
            TimeSpan.FromMinutes(1),
            new FakeTimeProvider(),
            batch =>
            {
                written.Add(batch.Count);
                return default;
            },
            NullLogger.Instance);

        queue.TryEnqueue(Entry()).Should().BeTrue();
        await queue.DisposeAsync();

        written.Should().Equal([1], "closing the queue wrote what it held, without waiting for the interval");
        queue.TryEnqueue(Entry()).Should().BeFalse("a closed queue refuses a run, which its caller then writes itself");

        Func<Task> flush = async () => await queue.Flush();
        await flush.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10), "a flush after the close has nothing left to wait for");
        await queue.DisposeAsync();
    }

    [Test]
    public async Task AFlushWhoseTokenIsCancelledThrowsAndOwesNothing()
    {
        int writes = 0;
        await using ExecutionHistoryWriteBehind queue = new(
            TimeSpan.FromMinutes(1),
            new FakeTimeProvider(),
            _ =>
            {
                Interlocked.Increment(ref writes);
                return default;
            },
            NullLogger.Instance);

        Func<Task> flush = async () => await queue.Flush(new CancellationToken(canceled: true));

        await flush.Should().ThrowAsync<OperationCanceledException>("the caller stopped waiting before the flush was queued");
        await queue.Flush();
        writes.Should().Be(0, "neither flush found anything to write");
    }

    private static ExecutionHistoryEntry Entry() => new(
        SchedulerName: "queue",
        SchedulerInstanceId: "node-a",
        JobGroup: "group",
        JobName: "job",
        TriggerGroup: "group",
        TriggerName: "trigger",
        FiredAtUtc: DateTimeOffset.UnixEpoch,
        Duration: TimeSpan.Zero,
        Succeeded: true,
        ExceptionMessage: null);
}

/// <summary>
/// The recorder's shutdown leaves a store that does not buffer alone, and a container already gone.
/// </summary>
/// <remarks>
/// That it flushes a store that does buffer is <c>AdoExecutionHistoryStoreContractTest.ShutdownWritesWhatIsQueued</c>,
/// through a real scheduler's shutdown.
/// </remarks>
public sealed class ExecutionHistoryPluginShutdownTest
{
    [Test]
    public async Task ShutdownLeavesAStoreThatDoesNotBufferAlone()
    {
        IServiceProvider services = A.Fake<IServiceProvider>();
        A.CallTo(() => services.GetService(typeof(IExecutionHistoryStore)))
            .Returns(new InMemoryExecutionHistoryStore(Options.Create(new ExecutionHistoryOptions())));

        Func<Task> shutdown = async () => await new ExecutionHistoryPlugin(services, TimeProvider.System).Shutdown();

        await shutdown.Should().NotThrowAsync("the in-memory history writes as it is handed each run, so there is nothing to flush");
    }

    [Test]
    public async Task ShutdownAfterTheContainerIsDisposedDoesNothing()
    {
        IServiceProvider services = A.Fake<IServiceProvider>();
        A.CallTo(() => services.GetService(typeof(IExecutionHistoryStore))).Throws(new ObjectDisposedException("container"));

        Func<Task> shutdown = async () => await new ExecutionHistoryPlugin(services, TimeProvider.System).Shutdown();

        await shutdown.Should().NotThrowAsync("disposing the container disposed the store, which wrote what it held");
    }
}
