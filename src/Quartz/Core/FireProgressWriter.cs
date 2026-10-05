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

using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Core;

/// <summary>
/// Keeps what one firing last reported through <see cref="IJobExecutionContext.ReportProgress" />, and
/// hands it to the job store and to the job listeners at most once every <see cref="WriteInterval" />,
/// only when it has changed.
/// </summary>
/// <remarks>
/// <para>
/// One per firing, made the first time the firing reports and kept beside its context in a
/// <see cref="ConditionalWeakTable{TKey,TValue}" /> rather than in a field of it: every firing builds a
/// context, few report, and a field would make every context larger — which the allocation budget of a
/// firing that does not report is not allowed to pay for. The table lets the writer go with its context.
/// </para>
/// <para>
/// The job's thread never waits on the store or on a listener. A write is queued to the thread pool
/// without the job's execution context — so it carries neither the firing's ambient state nor a
/// transaction the job opened, and enlists in nothing — and a report that arrives inside the interval
/// only replaces the value a pending tick will write. The first report is written at once; the last one
/// is always written while the job runs, however quickly the reports came, because the tick that fires
/// after it is what writes it. Each write then tells the job listeners, through
/// <see cref="IJobListener.JobProgressChanged" />, on the same work item: one announcement per write,
/// one at a time per firing, and in the order the values were written.
/// </para>
/// <para>
/// A failed write is logged and forgotten, and the listeners are told all the same; the next change
/// tries again. When the job returns, <see cref="Complete" /> ends it: nothing more is written, a write in
/// flight is waited for, and a last report the listeners have not heard is announced to them — not
/// written, because the firing's fire instance is about to go. The run shell awaits that before
/// <see cref="IJobListener.JobWasExecuted" />, so no announcement of a firing comes after it. A firing
/// that ends without reaching <see cref="Complete" /> stops once the ambient holder it was reported from
/// no longer carries its context, so a late report cannot reach a store that is shutting down.
/// </para>
/// </remarks>
internal sealed class FireProgressWriter : IThreadPoolWorkItem
{
    /// <summary>
    /// The least time between two writes of one firing's progress.
    /// </summary>
    internal static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(1);

    private static readonly ConditionalWeakTable<JobExecutionContextImpl, FireProgressWriter> writers = new();

    private readonly Lock gate = new();
    private readonly JobExecutionContextImpl context;
    private readonly AmbientJobExecution.Holder? holder;
    private readonly IJobStore? store;
    private readonly Func<IJobExecutionContext, FireInstanceProgress, CancellationToken, ValueTask>? announce;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly string fireInstanceId;

    // The latest report.
    private int percent;
    private string? message;

    // The latest value a write or an announcement was started for, which is what "only on a change"
    // compares against.
    private FireInstanceProgress? published;
    private long lastWriteTimestamp;

    private FireInstanceProgress? pending;
    private bool writing;
    private bool tickArmed;
    private ITimer? tick;
    private int writesStarted;

    // Set by Complete: the job has returned, and nothing more is written.
    private bool completed;
    private TaskCompletionSource? drained;

    private FireProgressWriter(
        JobExecutionContextImpl context,
        IJobStore? store,
        Func<IJobExecutionContext, FireInstanceProgress, CancellationToken, ValueTask>? announce,
        TimeProvider timeProvider,
        ILogger logger)
    {
        this.context = context;
        this.store = store;
        this.announce = announce;
        this.timeProvider = timeProvider;
        this.logger = logger;
        fireInstanceId = context.FireInstanceId;

        // Reported from the firing's own flow, which is the ordinary case, the holder is the one the run
        // shell empties when the firing ends. Reported from a flow that did not come from the firing,
        // there is nothing to watch, and a late write finds a fire instance that has gone.
        AmbientJobExecution.Holder? current = AmbientJobExecution.CurrentHolder;
        holder = current is not null && ReferenceEquals(current.Context, context) ? current : null;
    }

    /// <summary>
    /// The writer of this context's firing, made on the first call.
    /// </summary>
    internal static FireProgressWriter For(JobExecutionContextImpl context)
    {
        return writers.GetValue(context, static c => FromScheduler(c));
    }

    /// <summary>
    /// Gives a context a writer over a store, listeners and a clock of the caller's choosing, which is
    /// what a test of the throttle needs and what <see cref="For" /> otherwise works out from the
    /// scheduler.
    /// </summary>
    internal static FireProgressWriter Attach(
        JobExecutionContextImpl context,
        IJobStore store,
        TimeProvider timeProvider,
        ILogger logger,
        Func<IJobExecutionContext, FireInstanceProgress, CancellationToken, ValueTask>? announce = null)
    {
        FireProgressWriter writer = new(context, store, announce, timeProvider, logger);
        writers.AddOrUpdate(context, writer);
        return writer;
    }

    private static FireProgressWriter FromScheduler(JobExecutionContextImpl context)
    {
        if (context.Scheduler is StdScheduler std)
        {
            QuartzSchedulerResources resources = std.scheduler.resources;
            return new FireProgressWriter(
                context,
                resources.JobStore,
                std.scheduler.JobProgressAnnouncer,
                resources.TimeProvider ?? TimeProvider.System,
                resources.LoggerFactory.CreateLogger<JobRunShell>());
        }

        // A context built by hand over a scheduler of somebody else's: the value is kept, and there is no
        // store to write it to and no listener to tell.
        return new FireProgressWriter(context, store: null, announce: null, TimeProvider.System, NullLogger.Instance);
    }

    /// <summary>
    /// The writer of this context's firing, or <see langword="null" /> when the firing has not reported.
    /// </summary>
    internal static FireProgressWriter? Find(JobExecutionContextImpl context)
    {
        return writers.TryGetValue(context, out FireProgressWriter? writer) ? writer : null;
    }

    /// <summary>
    /// The latest report, which is what the next write carries.
    /// </summary>
    internal (int Percent, string? Message) Latest
    {
        get
        {
            lock (gate)
            {
                return (percent, message);
            }
        }
    }

    /// <summary>
    /// How many writes have been started, counted when one is queued rather than when it lands, so a
    /// test can say how many there were without racing the thread pool.
    /// </summary>
    internal int WritesStarted
    {
        get
        {
            lock (gate)
            {
                return writesStarted;
            }
        }
    }

    /// <summary>
    /// Whether a write, or the announcement that follows it, is queued or in flight.
    /// </summary>
    internal bool Writing
    {
        get
        {
            lock (gate)
            {
                return writing;
            }
        }
    }

    /// <summary>
    /// Keeps a report and, when the interval allows and the value has changed, starts writing it.
    /// </summary>
    internal void Report(int percent, string? message)
    {
        string? truncated = FireInstanceProgress.Truncate(message);

        lock (gate)
        {
            this.percent = percent;
            this.message = truncated;

            // A write in flight reschedules when it finishes, and an armed tick writes whatever is latest
            // when it fires: either way this value is the one that will go.
            if (!writing && !tickArmed)
            {
                ScheduleNoLock();
            }
        }
    }

    /// <summary>
    /// Ends the writer's part in the firing, once the job has returned: nothing more is written, a write
    /// in flight is waited for, and a last report the listeners have not heard is announced to them.
    /// </summary>
    /// <remarks>
    /// The announcement goes through the same work item as every other, so it too is made outside the
    /// firing's execution context, and the run shell's awaiting it is what puts it before
    /// <see cref="IJobListener.JobWasExecuted" />. It is not written to the store: the fire instance is
    /// about to be completed, and a write now would be a round trip for a row that is going.
    /// </remarks>
    internal async ValueTask Complete()
    {
        Task? inFlight;
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            tick?.Dispose();
            tick = null;
            tickArmed = false;
            inFlight = writing ? DrainNoLock() : null;
        }

        if (inFlight is not null)
        {
            await inFlight.ConfigureAwait(false);
        }

        Task last;
        lock (gate)
        {
            if (announce is null || IsPublishedNoLock())
            {
                return;
            }

            PublishNoLock();
            last = DrainNoLock();
        }

        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        await last.ConfigureAwait(false);
    }

    void IThreadPoolWorkItem.Execute()
    {
        _ = Write();
    }

    private async Task Write()
    {
        FireInstanceProgress progress;
        bool toStore;
        lock (gate)
        {
            progress = pending!;
            pending = null;
            toStore = !completed;
        }

        try
        {
            if (toStore)
            {
                try
                {
                    // No token: the write is a record of something the job said, and the firing's token is
                    // the job's to cancel, not this one's.
                    await store!.UpdateFireInstanceProgress(fireInstanceId, progress, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.FireProgressWriteFailed(fireInstanceId, context.JobDetail.Key, e);
                }
            }

            // After the write, so a listener that reads the fire instance finds the value it was told; and
            // whether or not the write worked, because what the job said does not depend on the store. The
            // announcer logs a listener that throws, so nothing it does reaches the job or the next write.
            if (announce is not null)
            {
                await announce(context, progress, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            TaskCompletionSource? done = null;
            lock (gate)
            {
                writing = false;
                if (completed)
                {
                    done = drained;
                    drained = null;
                }
                else if (!tickArmed)
                {
                    ScheduleNoLock();
                }
            }

            done?.TrySetResult();
        }
    }

    private void OnTick()
    {
        lock (gate)
        {
            tickArmed = false;
            if (!writing)
            {
                ScheduleNoLock();
            }
        }
    }

    /// <summary>
    /// Starts a write of the latest value now, arms the tick that will write it once the interval has
    /// passed, or does nothing because there is nothing new to write.
    /// </summary>
    private void ScheduleNoLock()
    {
        if (store is null || completed)
        {
            return;
        }

        if (holder is not null && !ReferenceEquals(holder.Context, context))
        {
            // The firing is over and its fire instance with it.
            tick?.Dispose();
            tick = null;
            return;
        }

        if (IsPublishedNoLock())
        {
            return;
        }

        if (published is not null)
        {
            TimeSpan since = timeProvider.GetElapsedTime(lastWriteTimestamp);
            if (since < WriteInterval)
            {
                ArmTickNoLock(WriteInterval - since);
                return;
            }
        }

        PublishNoLock();
        lastWriteTimestamp = timeProvider.GetTimestamp();
        writesStarted++;

        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    /// <summary>
    /// Whether the latest report is the value the last write or announcement was started for.
    /// </summary>
    private bool IsPublishedNoLock()
    {
        return published is not null
            && published.Percent == percent
            && string.Equals(published.Message, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hands the latest report to the work item, which <see cref="writing" /> then says is queued.
    /// </summary>
    private void PublishNoLock()
    {
        published = new FireInstanceProgress { Percent = percent, Message = message };
        pending = published;
        writing = true;
    }

    /// <summary>
    /// What the work item queued or in flight completes once it is over, which is what
    /// <see cref="Complete" /> waits on.
    /// </summary>
    private Task DrainNoLock()
    {
        drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return drained.Task;
    }

    private void ArmTickNoLock(TimeSpan dueTime)
    {
        tickArmed = true;

        if (tick is not null)
        {
            tick.Change(dueTime, Timeout.InfiniteTimeSpan);
            return;
        }

        // Created with the flow suppressed, so the tick carries none of the firing's execution context
        // for as long as it lives — it is armed from the job's own flow the first time.
        if (ExecutionContext.IsFlowSuppressed())
        {
            tick = CreateTick(dueTime);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                tick = CreateTick(dueTime);
            }
        }
    }

    private ITimer CreateTick(TimeSpan dueTime)
    {
        return timeProvider.CreateTimer(
            static state => ((FireProgressWriter) state!).OnTick(),
            this,
            dueTime,
            Timeout.InfiniteTimeSpan);
    }
}
