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

using System.Threading.Channels;

using Microsoft.Extensions.Logging;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The queue <see cref="AdoExecutionHistoryStore" /> buffers executions in when
/// <see cref="ExecutionHistoryOptions.WriteBehindInterval" /> is set, and the one writer that empties it.
/// </summary>
/// <remarks>
/// <para>
/// A window opens with the first execution queued and closes when the interval has passed, a batch is
/// waiting, a flush is asked for, or the queue is closed. The writer then takes everything queued, a batch at
/// a time, and hands each batch to the store. With nothing queued it waits for the next execution and holds no
/// timer.
/// </para>
/// <para>
/// Bounded: a full queue refuses an execution, and the store writes that one itself, as it does with
/// write-behind off. Nothing waits for room and nothing is dropped.
/// </para>
/// <para>
/// A batch the store cannot write is reported with event <c>3164</c>, naming how many rows were lost, and the
/// writer carries on with the next. The store has already retried what it counts as transient.
/// </para>
/// </remarks>
internal sealed class ExecutionHistoryWriteBehind : IAsyncDisposable
{
    /// <summary>How many executions make a batch, which closes the window early.</summary>
    internal const int DefaultBatchSize = 256;

    /// <summary>
    /// How many executions the queue holds before it refuses one: sixteen batches, which bounds what a
    /// slow database can make it hold.
    /// </summary>
    internal const int DefaultCapacity = 16 * DefaultBatchSize;

    private readonly Channel<BufferedWrite> queue;
    private readonly Func<List<ExecutionHistoryEntry>, ValueTask> write;
    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly int batchSize;
    private readonly Task writer;

    /// <summary>Executions queued and not yet taken by the writer. Can dip below zero for a moment.</summary>
    private int buffered;

    /// <summary>Flushes asked for whose marker the writer has not reached yet.</summary>
    private int flushesAsked;

    /// <summary>Set once by <see cref="DisposeAsync" />, which ends the window in progress.</summary>
    private int closing;

    /// <summary>Completed to end the window in progress early; a new one for every window.</summary>
    private TaskCompletionSource wake = NewSignal();

    /// <param name="interval">How long a window stays open at most.</param>
    /// <param name="timeProvider">The clock the window is measured on.</param>
    /// <param name="write">
    /// Writes one batch, in one transaction. Throws when it could not, after any retry of its own.
    /// </param>
    /// <param name="logger">Where a batch that could not be written is reported.</param>
    /// <param name="batchSize">How many executions make a batch.</param>
    /// <param name="capacity">How many executions the queue holds.</param>
    internal ExecutionHistoryWriteBehind(
        TimeSpan interval,
        TimeProvider timeProvider,
        Func<List<ExecutionHistoryEntry>, ValueTask> write,
        ILogger logger,
        int batchSize = DefaultBatchSize,
        int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        this.interval = interval;
        this.timeProvider = timeProvider;
        this.write = write;
        this.logger = logger;
        this.batchSize = batchSize;

        queue = Channel.CreateBounded<BufferedWrite>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        writer = Task.Run(Run);
    }

    /// <summary>
    /// Queues one execution, or answers <see langword="false" /> when the queue is full or closed and the
    /// caller has to write it itself.
    /// </summary>
    public bool TryEnqueue(ExecutionHistoryEntry entry)
    {
        if (!queue.Writer.TryWrite(new BufferedWrite(entry, Written: null)))
        {
            return false;
        }

        // A full fence before the signal is read, which pairs with the writer storing a new signal
        // before it reads this count: one of the two sees the other.
        if (Interlocked.Increment(ref buffered) >= batchSize)
        {
            Volatile.Read(ref wake).TrySetResult();
        }

        return true;
    }

    /// <summary>
    /// Writes everything queued before this call, and completes once it is written or reported lost.
    /// </summary>
    /// <remarks>
    /// A marker in the queue, which the writer reaches after every execution queued before it. Counted and
    /// signalled before it is queued: a full queue makes the marker wait for room, and only the writer,
    /// woken now rather than at the end of its window, makes any.
    /// </remarks>
    public async ValueTask Flush(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource written = NewSignal();
        Interlocked.Increment(ref flushesAsked);
        Volatile.Read(ref wake).TrySetResult();

        try
        {
            await queue.Writer.WriteAsync(new BufferedWrite(Entry: null, written), cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // Closed by DisposeAsync, whose writer empties the queue before it stops.
            Interlocked.Decrement(ref flushesAsked);
            await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref flushesAsked);
            throw;
        }

        Volatile.Read(ref wake).TrySetResult();
        await written.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the queue, ends the window in progress, and waits until everything queued is written or
    /// reported lost.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closing, 1) == 0)
        {
            queue.Writer.TryComplete();
            Volatile.Read(ref wake).TrySetResult();
        }

        await writer.ConfigureAwait(false);
    }

    private async Task Run()
    {
        ChannelReader<BufferedWrite> reader = queue.Reader;
        List<ExecutionHistoryEntry> batch = new(batchSize);

        // False once the queue is closed and empty.
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            await WaitForWindow().ConfigureAwait(false);

            while (reader.TryRead(out BufferedWrite item))
            {
                if (item.Entry is { } entry)
                {
                    Interlocked.Decrement(ref buffered);
                    batch.Add(entry);

                    if (batch.Count >= batchSize)
                    {
                        await Write(batch).ConfigureAwait(false);
                    }

                    continue;
                }

                // A flush: everything queued before it is in the batch, so writing the batch answers it.
                await Write(batch).ConfigureAwait(false);
                Interlocked.Decrement(ref flushesAsked);
                item.Written!.TrySetResult();
            }

            await Write(batch).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until the interval has passed, unless a batch is waiting, a flush was asked for or the queue
    /// is closing, or one of those happens first.
    /// </summary>
    private async ValueTask WaitForWindow()
    {
        TaskCompletionSource signal = NewSignal();

        // A full fence: the reads below cannot move before the store, so a producer that read the old
        // signal incremented a count this sees.
        Interlocked.Exchange(ref wake, signal);

        if (Volatile.Read(ref buffered) >= batchSize || Volatile.Read(ref flushesAsked) > 0 || Volatile.Read(ref closing) != 0)
        {
            return;
        }

        using CancellationTokenSource window = new();
        Task elapsed = Task.Delay(interval, timeProvider, window.Token);
        if (await Task.WhenAny(elapsed, signal.Task).ConfigureAwait(false) != elapsed)
        {
            // Woken early: the delay's timer goes now rather than at the end of the interval.
            await window.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Hands the batch to the store, reports it lost if the store could not write it, and empties it.</summary>
    private async ValueTask Write(List<ExecutionHistoryEntry> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await write(batch).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            // Nothing is left to try: the store retried what was transient. The firings these rows describe
            // happened, so the loss is reported and the writer carries on with the next batch.
            foreach (IGrouping<string, ExecutionHistoryEntry> scheduler in batch.GroupBy(static entry => entry.SchedulerName, StringComparer.Ordinal))
            {
                logger.ExecutionHistoryWriteBehindFailed(scheduler.Key, scheduler.Count(), failure);
            }
        }
        finally
        {
            batch.Clear();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>One item in the queue: an execution, or a flush to answer once those before it are written.</summary>
    private readonly record struct BufferedWrite(ExecutionHistoryEntry? Entry, TaskCompletionSource? Written);
}
