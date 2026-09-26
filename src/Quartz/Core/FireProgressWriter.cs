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
/// hands it to the job store at most once every <see cref="WriteInterval" />, only when it has changed.
/// </summary>
/// <remarks>
/// <para>
/// One per firing, made the first time the firing reports and kept beside its context in a
/// <see cref="ConditionalWeakTable{TKey,TValue}" /> rather than in a field of it: every firing builds a
/// context, few report, and a field would make every context larger — which the allocation budget of a
/// firing that does not report is not allowed to pay for. The table lets the writer go with its context.
/// </para>
/// <para>
/// The job's thread never waits on the store. A write is queued to the thread pool without the job's
/// execution context — so it carries neither the firing's ambient state nor a transaction the job opened,
/// and enlists in nothing — and a report that arrives inside the interval only replaces the value a
/// pending tick will write. The first report is written at once; the last one is always written,
/// however quickly the reports came, because the tick that fires after it is what writes it.
/// </para>
/// <para>
/// A failed write is logged and forgotten; the next change tries again. Once the firing is over — the
/// ambient holder it was reported from no longer carries its context — nothing more is written, so a
/// report made in the last second of a job cannot reach a store that is shutting down.
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
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly string fireInstanceId;

    // The latest report.
    private int percent;
    private string? message;

    // The latest value a write was started for, which is what "only on a change" compares against.
    private bool written;
    private int writtenPercent;
    private string? writtenMessage;
    private long lastWriteTimestamp;

    private FireInstanceProgress? pending;
    private bool writing;
    private bool tickArmed;
    private ITimer? tick;
    private int writesStarted;

    private FireProgressWriter(JobExecutionContextImpl context, IJobStore? store, TimeProvider timeProvider, ILogger logger)
    {
        this.context = context;
        this.store = store;
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
    /// Gives a context a writer over a store and a clock of the caller's choosing, which is what a test
    /// of the throttle needs and what <see cref="For" /> otherwise works out from the scheduler.
    /// </summary>
    internal static FireProgressWriter Attach(JobExecutionContextImpl context, IJobStore store, TimeProvider timeProvider, ILogger logger)
    {
        FireProgressWriter writer = new(context, store, timeProvider, logger);
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
                resources.TimeProvider ?? TimeProvider.System,
                resources.LoggerFactory.CreateLogger<JobRunShell>());
        }

        // A context built by hand over a scheduler of somebody else's: the value is kept, and there is no
        // store to write it to.
        return new FireProgressWriter(context, store: null, TimeProvider.System, NullLogger.Instance);
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
    /// Whether a write is queued or in flight.
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

    void IThreadPoolWorkItem.Execute()
    {
        _ = Write();
    }

    private async Task Write()
    {
        FireInstanceProgress progress;
        lock (gate)
        {
            progress = pending!;
            pending = null;
        }

        try
        {
            // No token: the write is a record of something the job said, and the firing's token is the
            // job's to cancel, not this one's.
            await store!.UpdateFireInstanceProgress(fireInstanceId, progress, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.FireProgressWriteFailed(fireInstanceId, context.JobDetail.Key, e);
        }
        finally
        {
            lock (gate)
            {
                writing = false;
                if (!tickArmed)
                {
                    ScheduleNoLock();
                }
            }
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
        if (store is null)
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

        if (written && writtenPercent == percent && string.Equals(writtenMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        if (written)
        {
            TimeSpan since = timeProvider.GetElapsedTime(lastWriteTimestamp);
            if (since < WriteInterval)
            {
                ArmTickNoLock(WriteInterval - since);
                return;
            }
        }

        written = true;
        writtenPercent = percent;
        writtenMessage = message;
        lastWriteTimestamp = timeProvider.GetTimestamp();
        pending = new FireInstanceProgress { Percent = percent, Message = message };
        writing = true;
        writesStarted++;

        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
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
