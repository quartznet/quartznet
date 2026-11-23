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

namespace Quartz.Core;

/// <summary>
/// The one algorithm every wait for a firing follows, in process, on a cluster and over HTTP (#4414).
/// </summary>
/// <remarks>
/// <para>
/// The awaited occurrence is the trigger's next final completion after the wait begins; for a trigger already
/// gone, its newest final row. A first look reads the trigger's newest history rows as the
/// <see cref="Baseline" />, then its state: the history before the state, so a one-off that completes between
/// the two reads lands on the "gone" side with its row visible.
/// </para>
/// <para>
/// Then the first of: the local signal; a poll, 200 ms and doubling to 5 s; the deadline; the token. A poll
/// where a firing can end without the local signal hearing it reads the history for a final row the baseline
/// does not hold. Every poll reads the state, and a trigger gone ends the wait with its row, a misfire row, or
/// after <see cref="FiringWatch.RowGrace" />, <see cref="FiringWaitStatus.Unknown" />.
/// </para>
/// <para>
/// Every interval is measured on the system clock, as the scheduler's shutdown window is: a scheduler on a
/// clock nobody advances would otherwise never time out (#3892).
/// </para>
/// </remarks>
internal static class FiringWait
{
    /// <summary>
    /// How many of the trigger's newest rows a look reads.
    /// </summary>
    internal const int RowsRead = 50;

    /// <summary>
    /// The first poll's interval, doubled at every poll.
    /// </summary>
    internal static readonly TimeSpan FirstPoll = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The longest interval between two polls.
    /// </summary>
    internal static readonly TimeSpan LongestPoll = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a shared history is looked at again for the row of a trigger already gone, before write-behind
    /// is added.
    /// </summary>
    internal static readonly TimeSpan MinimumRowGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Refuses a timeout no wait can mean.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout" /> is negative and is not <see cref="Timeout.InfiniteTimeSpan" />.
    /// </exception>
    internal static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "A wait's timeout is zero or more, or Timeout.InfiniteTimeSpan for no limit.");
        }
    }

    /// <summary>
    /// Takes the first look at the history, once.
    /// </summary>
    internal static async ValueTask Begin(FiringWatch watch, CancellationToken cancellationToken)
    {
        if (watch.Baseline is not null)
        {
            return;
        }

        if (watch.History is null)
        {
            watch.Baseline = Baseline.Empty;
            return;
        }

        List<ExecutionHistoryEntry> executions = await Executions(watch, cancellationToken).ConfigureAwait(false);
        List<MisfireHistoryEntry> misfires = await Misfires(watch, cancellationToken).ConfigureAwait(false);
        watch.Baseline = new Baseline(executions, misfires);
    }

    internal static async ValueTask<FiringResult> Result(FiringWatch watch, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();

        FiringSignal? signal = watch.Signal;
        if (signal is { Task.IsCompleted: true })
        {
            return await signal.Task.ConfigureAwait(false);
        }

        TimeProvider clock = TimeProvider.System;
        long started = clock.GetTimestamp();

        try
        {
            await Begin(watch, cancellationToken).ConfigureAwait(false);
            Baseline baseline = watch.Baseline!;

            TriggerState state = await watch.Scheduler.GetTriggerState(watch.TriggerKey, cancellationToken).ConfigureAwait(false);
            bool existed = !IsGone(state);

            TimeSpan poll = FirstPoll;
            long? graceEnds = null;

            while (true)
            {
                if (signal is { Task.IsCompleted: true })
                {
                    return await signal.Task.ConfigureAwait(false);
                }

                if (IsGone(state))
                {
                    if (signal is { Settled: true })
                    {
                        // The completion that removed it is on its way here: the store has committed, and the
                        // release follows.
                        return await Released(signal, watch.TriggerKey, Remaining(clock, started, timeout), cancellationToken).ConfigureAwait(false);
                    }

                    if (await FromHistory(watch, baseline, existed, cancellationToken).ConfigureAwait(false) is { } recorded)
                    {
                        return recorded;
                    }

                    if (watch.History is null || watch.RowGrace <= TimeSpan.Zero)
                    {
                        return Unknown(watch.TriggerKey);
                    }

                    graceEnds ??= clock.GetTimestamp() + (long) (watch.RowGrace.TotalSeconds * clock.TimestampFrequency);
                    if (clock.GetTimestamp() >= graceEnds)
                    {
                        return Unknown(watch.TriggerKey);
                    }
                }
                else if (state == TriggerState.Error)
                {
                    if (signal is { Settled: true })
                    {
                        // The shell's own answer names the failure; the state only says there was one.
                        return await Released(signal, watch.TriggerKey, Remaining(clock, started, timeout), cancellationToken).ConfigureAwait(false);
                    }

                    return new FiringResult
                    {
                        TriggerKey = watch.TriggerKey,
                        Status = FiringWaitStatus.Ended,
                        Outcome = ExecutionOutcome.NotExecuted,
                        Message = "The trigger is in the ERROR state: its job could not be built."
                    };
                }

                TimeSpan remaining = Remaining(clock, started, timeout);
                if (remaining == TimeSpan.Zero)
                {
                    return new FiringResult
                    {
                        TriggerKey = watch.TriggerKey,
                        Status = FiringWaitStatus.Pending
                    };
                }

                TimeSpan wait = remaining == Timeout.InfiniteTimeSpan || poll < remaining ? poll : remaining;
                await Wait(signal, wait, cancellationToken).ConfigureAwait(false);
                poll = (poll + poll) < LongestPoll ? poll + poll : LongestPoll;

                if (signal is { Task.IsCompleted: true } || Remaining(clock, started, timeout) == TimeSpan.Zero)
                {
                    continue;
                }

                if (IsGone(state))
                {
                    // Still in the grace: the next pass reads the history again.
                    continue;
                }

                if (watch.PollsHistory)
                {
                    List<ExecutionHistoryEntry> rows = await Executions(watch, cancellationToken).ConfigureAwait(false);
                    if (NewestFinal(rows, baseline, skipBaseline: true) is { } row)
                    {
                        return Ended(watch.TriggerKey, row);
                    }
                }

                state = await watch.Scheduler.GetTriggerState(watch.TriggerKey, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) when (signal is { Task.IsFaulted: true })
        {
            // The scheduler shut down under a read: the signal says so in the words that name the trigger.
            return await signal.Task.ConfigureAwait(false);
        }
        catch (SchedulerException) when (signal is not null && watch.Scheduler.Status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
        {
            // A scheduler on its way down refuses reads while its running firings still have their window to
            // complete in. The signal is the answer then: completed by a firing that ends inside the window,
            // failed once the window is over.
            return await Released(signal, watch.TriggerKey, Remaining(clock, started, timeout), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The answer of a signal whose completion is being stored, within what is left of the timeout.
    /// </summary>
    private static async ValueTask<FiringResult> Released(FiringSignal signal, TriggerKey triggerKey, TimeSpan remaining, CancellationToken cancellationToken)
    {
        try
        {
            return await signal.Task.WaitAsync(remaining, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new FiringResult
            {
                TriggerKey = triggerKey,
                Status = FiringWaitStatus.Pending
            };
        }
    }

    /// <summary>
    /// Whether the trigger will never fire again: removed, or left complete.
    /// </summary>
    private static bool IsGone(TriggerState state) => state is TriggerState.None or TriggerState.Complete;

    /// <summary>
    /// The answer the history gives for a trigger seen gone, read after it was seen gone so that every row
    /// written before its removal is there; <see langword="null" /> when the history holds none yet.
    /// </summary>
    /// <remarks>
    /// A trigger that was there at the first look (<paramref name="existed" />) is answered only by a row the
    /// baseline does not hold. A trigger already gone then is answered by its newest row.
    /// </remarks>
    private static async ValueTask<FiringResult?> FromHistory(FiringWatch watch, Baseline baseline, bool existed, CancellationToken cancellationToken)
    {
        if (watch.History is null)
        {
            return null;
        }

        List<ExecutionHistoryEntry> executions = await Executions(watch, cancellationToken).ConfigureAwait(false);
        if (NewestFinal(executions, baseline, skipBaseline: existed) is { } row)
        {
            return Ended(watch.TriggerKey, row);
        }

        List<MisfireHistoryEntry> misfires = await Misfires(watch, cancellationToken).ConfigureAwait(false);
        foreach (MisfireHistoryEntry misfire in misfires)
        {
            if (!existed || !baseline.Misfires.Contains(misfire))
            {
                return Ended(watch.TriggerKey, misfire);
            }
        }

        return null;
    }

    /// <summary>
    /// The newest row that is an occurrence's last word, and, when <paramref name="skipBaseline" />, one the
    /// first look did not see.
    /// </summary>
    /// <remarks>
    /// A row without an <see cref="ExecutionHistoryEntry.EntryId" /> — only a store written before 4.3 returns
    /// one — cannot be told from the baseline's, so it answers only a trigger already gone at the first look.
    /// </remarks>
    private static ExecutionHistoryEntry? NewestFinal(List<ExecutionHistoryEntry> rows, Baseline baseline, bool skipBaseline)
    {
        foreach (ExecutionHistoryEntry row in rows)
        {
            if (row.RetryScheduled)
            {
                continue;
            }

            if (!skipBaseline)
            {
                return row;
            }

            if (row.EntryId is { } entryId && !baseline.ExecutionIds.Contains(entryId))
            {
                return row;
            }
        }

        return null;
    }

    private static FiringResult Ended(TriggerKey triggerKey, ExecutionHistoryEntry row)
    {
        return new FiringResult
        {
            TriggerKey = triggerKey,
            Status = FiringWaitStatus.Ended,
            Outcome = OutcomeOf(row),
            Execution = row
        };
    }

    private static FiringResult Ended(TriggerKey triggerKey, MisfireHistoryEntry misfire)
    {
        bool vetoed = misfire.Reason == MisfireReason.Vetoed;
        return new FiringResult
        {
            TriggerKey = triggerKey,
            Status = FiringWaitStatus.Ended,
            Outcome = vetoed ? ExecutionOutcome.Vetoed : ExecutionOutcome.NotExecuted,
            Misfire = misfire,
            Message = misfire.Reason switch
            {
                MisfireReason.Vetoed => null,
                MisfireReason.Overlap => "The trigger's overlap policy skipped the firing: the previous one was still running.",
                _ => "The trigger misfired, and its misfire instruction did not fire it."
            }
        };
    }

    /// <summary>
    /// What the run shell would have said of the run a row records: a job that reported a failure without
    /// throwing succeeded as far as the scheduler saw.
    /// </summary>
    internal static ExecutionOutcome OutcomeOf(ExecutionHistoryEntry row)
    {
        return row.EffectiveResult switch
        {
            JobRunResult.Cancelled => ExecutionOutcome.Cancelled,
            JobRunResult.Failed when row.ExceptionMessage is not null => ExecutionOutcome.Failed,
            _ => ExecutionOutcome.Succeeded
        };
    }

    private static FiringResult Unknown(TriggerKey triggerKey)
    {
        return new FiringResult
        {
            TriggerKey = triggerKey,
            Status = FiringWaitStatus.Unknown
        };
    }

    private static TimeSpan Remaining(TimeProvider clock, long started, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return Timeout.InfiniteTimeSpan;
        }

        TimeSpan left = timeout - clock.GetElapsedTime(started);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Waits for the local signal or the interval, whichever comes first; the token throws.
    /// </summary>
    private static async ValueTask Wait(FiringSignal? signal, TimeSpan interval, CancellationToken cancellationToken)
    {
        if (signal is null)
        {
            await Task.Delay(interval, TimeProvider.System, cancellationToken).ConfigureAwait(false);
            return;
        }

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delay = Task.Delay(interval, TimeProvider.System, stop.Token);
        await Task.WhenAny(signal.Task, delay).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async ValueTask<List<ExecutionHistoryEntry>> Executions(FiringWatch watch, CancellationToken cancellationToken)
    {
        PagedResult<ExecutionHistoryEntry> page = await watch.History!.QueryExecutions(
            new ExecutionHistoryQuery
            {
                SchedulerName = watch.SchedulerName,
                Trigger = watch.TriggerKey,
                Take = RowsRead
            },
            cancellationToken).ConfigureAwait(false);

        return [.. page.Items];
    }

    private static async ValueTask<List<MisfireHistoryEntry>> Misfires(FiringWatch watch, CancellationToken cancellationToken)
    {
        PagedResult<MisfireHistoryEntry> page = await watch.History!.QueryMisfires(
            new MisfireHistoryQuery
            {
                SchedulerName = watch.SchedulerName,
                Trigger = watch.TriggerKey,
                Take = RowsRead
            },
            cancellationToken).ConfigureAwait(false);

        return [.. page.Items];
    }

    /// <summary>
    /// What the history held for the trigger when the wait began: its final rows, by entry id, and its
    /// misfire rows, by value.
    /// </summary>
    internal sealed class Baseline
    {
        public static readonly Baseline Empty = new([], []);

        public Baseline(List<ExecutionHistoryEntry> executions, List<MisfireHistoryEntry> misfires)
        {
            foreach (ExecutionHistoryEntry row in executions)
            {
                if (!row.RetryScheduled && row.EntryId is { } entryId)
                {
                    ExecutionIds.Add(entryId);
                }
            }

            Misfires = [.. misfires];
        }

        public HashSet<string> ExecutionIds { get; } = new(StringComparer.Ordinal);

        public HashSet<MisfireHistoryEntry> Misfires { get; }
    }
}
