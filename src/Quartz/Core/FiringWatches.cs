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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Core;

/// <summary>
/// The waits one scheduler's own completions answer: who waits on which trigger, and the answer the run shell
/// gives them (#4414).
/// </summary>
/// <remarks>
/// <para>
/// The shell <see cref="Settle" />s a key's waits just before it hands the store a firing's completion, and
/// <see cref="Release" />s them just after, in a <c>finally</c>. A wait registered between the two joins the
/// settling and is released with it. So a wait registered at any moment is either answered by that
/// completion, or reads the trigger gone only after it was settled, and when a wait answers the store has
/// committed: a one-off is gone, a <c>[DisallowConcurrentExecution]</c> block is lifted.
/// </para>
/// <para>
/// With nobody waiting the fire path pays one volatile read, <see cref="Any" />, and allocates nothing.
/// </para>
/// </remarks>
internal sealed class FiringWatches
{
    private readonly Lock gate = new();
    private readonly Dictionary<TriggerKey, Entry> entries = [];

    /// <summary>
    /// The open waits, written under <see cref="gate" /> and read without it by <see cref="Any" />.
    /// </summary>
    private int open;

    private bool shutDown;

    /// <summary>
    /// Whether anybody waits on any trigger: the fire path's whole cost when nobody does.
    /// </summary>
    public bool Any => Volatile.Read(ref open) > 0;

    /// <summary>
    /// Starts a wait on the next completion of <paramref name="triggerKey" />.
    /// </summary>
    /// <exception cref="SchedulerException">The scheduler has shut its waits down.</exception>
    public FiringSignal Watch(TriggerKey triggerKey)
    {
        lock (gate)
        {
            if (shutDown)
            {
                Throw.SchedulerException("The Scheduler has been Shutdown.");
            }

            if (!entries.TryGetValue(triggerKey, out Entry? entry))
            {
                entry = new Entry();
                entries.Add(triggerKey, entry);
            }

            // A wait that begins while a completion of the key is being stored is that completion's.
            FiringSignal signal = new(this, triggerKey, settled: entry.Settlings > 0);
            entry.Signals.Add(signal);
            Volatile.Write(ref open, open + 1);
            return signal;
        }
    }

    /// <summary>
    /// Marks the waits on <paramref name="triggerKey" /> as about to be answered, or answers
    /// <see langword="null" /> when nobody waits on it, and nothing needs building.
    /// </summary>
    /// <remarks>
    /// The caller puts the answer on <see cref="Settling.Result" /> and hands the settling to
    /// <see cref="Release" /> once the store has the completion, whatever the store did with it.
    /// </remarks>
    public Settling? Settle(TriggerKey triggerKey)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(triggerKey, out Entry? entry))
            {
                return null;
            }

            entry.Settlings++;
            foreach (FiringSignal signal in entry.Signals)
            {
                signal.MarkSettled();
            }

            return new Settling(triggerKey);
        }
    }

    /// <summary>
    /// Answers every wait on the settling's key registered up to now, the ones that began after the settle
    /// included.
    /// </summary>
    public void Release(Settling settling)
    {
        FiringSignal[] answered;
        lock (gate)
        {
            if (!entries.TryGetValue(settling.TriggerKey, out Entry? entry))
            {
                // The scheduler shut its waits down meanwhile, and they have had their answer.
                return;
            }

            entry.Settlings--;
            answered = [.. entry.Signals];
            entry.Signals.Clear();
            Volatile.Write(ref open, open - answered.Length);

            if (entry.Settlings == 0)
            {
                entries.Remove(settling.TriggerKey);
            }
        }

        // Outside the lock; each continuation runs asynchronously, so none runs on the fire path.
        FiringResult result = settling.Result ?? new FiringResult
        {
            TriggerKey = settling.TriggerKey,
            Status = FiringWaitStatus.Ended
        };

        foreach (FiringSignal signal in answered)
        {
            signal.Complete(result);
        }
    }

    /// <summary>
    /// Takes one wait out, answered or not.
    /// </summary>
    internal void Remove(FiringSignal signal)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(signal.TriggerKey, out Entry? entry) || !entry.Signals.Remove(signal))
            {
                return;
            }

            Volatile.Write(ref open, open - 1);
            if (entry.Signals.Count == 0 && entry.Settlings == 0)
            {
                entries.Remove(signal.TriggerKey);
            }
        }
    }

    /// <summary>
    /// Fails every wait still open, once the scheduler's in-flight completions have had their window: after it
    /// the job store closes, and nothing could answer them.
    /// </summary>
    public void ShutDown()
    {
        List<FiringSignal> failed = [];
        lock (gate)
        {
            shutDown = true;
            foreach (Entry entry in entries.Values)
            {
                failed.AddRange(entry.Signals);
            }

            entries.Clear();
            Volatile.Write(ref open, 0);
        }

        foreach (FiringSignal signal in failed)
        {
            signal.Fail(new SchedulerException($"The scheduler shut down before the firing of trigger '{signal.TriggerKey}' ended."));
        }
    }

    /// <summary>
    /// One completion of a key on its way to the store.
    /// </summary>
    internal sealed class Settling
    {
        public Settling(TriggerKey triggerKey)
        {
            TriggerKey = triggerKey;
        }

        public TriggerKey TriggerKey { get; }

        /// <summary>
        /// The answer, set by the run shell before it hands the store the completion.
        /// </summary>
        public FiringResult? Result { get; set; }
    }

    /// <summary>
    /// The waits on one key, and how many of its completions are on their way to the store.
    /// </summary>
    private sealed class Entry
    {
        public List<FiringSignal> Signals { get; } = [];

        public int Settlings { get; set; }
    }
}

/// <summary>
/// One wait's local signal: completed when a completion of its trigger is stored here, failed when the
/// scheduler shuts down first.
/// </summary>
internal sealed class FiringSignal : IDisposable
{
    private readonly FiringWatches owner;
    private readonly TaskCompletionSource<FiringResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool settled;

    internal FiringSignal(FiringWatches owner, TriggerKey triggerKey, bool settled)
    {
        this.owner = owner;
        TriggerKey = triggerKey;
        this.settled = settled;
    }

    public TriggerKey TriggerKey { get; }

    /// <summary>
    /// The answer, once a completion of the trigger has been stored.
    /// </summary>
    public Task<FiringResult> Task => completion.Task;

    /// <summary>
    /// Whether a completion of the trigger is on its way to the store, and will answer this wait.
    /// </summary>
    public bool Settled => settled;

    internal void MarkSettled() => settled = true;

    internal void Complete(FiringResult result) => completion.TrySetResult(result);

    internal void Fail(Exception exception) => completion.TrySetException(exception);

    public void Dispose() => owner.Remove(this);
}

/// <summary>
/// One wait for a firing of one trigger: the local signal, when its scheduler runs here, and the reads that
/// answer it from the execution history and the trigger's state otherwise.
/// </summary>
/// <remarks>
/// <see cref="IFiringWaitingScheduler.Watch" /> starts it before the trigger is stored, and disposing it ends
/// the wait without touching the firing.
/// </remarks>
internal sealed class FiringWatch : IAsyncDisposable
{
    /// <param name="triggerKey">The trigger whose firing is waited for.</param>
    /// <param name="scheduler">Where the trigger's state is read.</param>
    /// <param name="schedulerName">The name the history keys the scheduler's rows by.</param>
    /// <param name="history">The history the scheduler's recorder writes, or <see langword="null" /> for none.</param>
    /// <param name="pollsHistory">
    /// Whether a firing may end where no local signal hears it, on another node or another process, so every
    /// poll reads the history for a new final row.
    /// </param>
    /// <param name="signal">The local signal, or <see langword="null" /> when no completion here can answer.</param>
    internal FiringWatch(
        TriggerKey triggerKey,
        IScheduler scheduler,
        string schedulerName,
        IExecutionHistoryStore? history,
        bool pollsHistory,
        FiringSignal? signal)
    {
        TriggerKey = triggerKey;
        Scheduler = scheduler;
        SchedulerName = schedulerName;
        History = history;
        PollsHistory = pollsHistory;
        Signal = signal;

        // Rows only lag their firing where the history is not this process's own synchronous record: a
        // database history with write-behind, here or on any other node, and a host over HTTP.
        if (history is not null and not InMemoryExecutionHistoryStore)
        {
            TimeSpan writeBehind = (history as IBufferedExecutionHistoryStore)?.WriteBehindInterval ?? TimeSpan.Zero;
            RowGrace = FiringWait.MinimumRowGrace + writeBehind + writeBehind;
        }
    }

    public TriggerKey TriggerKey { get; }

    internal IScheduler Scheduler { get; }

    internal string SchedulerName { get; }

    internal IExecutionHistoryStore? History { get; }

    internal bool PollsHistory { get; }

    internal FiringSignal? Signal { get; }

    /// <summary>
    /// How long a trigger seen gone is looked at again for its row before the wait answers
    /// <see cref="FiringWaitStatus.Unknown" />: zero where a row is written before its trigger is removed.
    /// </summary>
    /// <remarks>
    /// <c>init</c>, so a test can wait out a short one rather than five seconds.
    /// </remarks>
    internal TimeSpan RowGrace { get; init; }

    /// <summary>
    /// What the first look at the history found, or <see langword="null" /> before it was taken.
    /// </summary>
    internal FiringWait.Baseline? Baseline { get; set; }

    /// <summary>
    /// Reads what the history already holds for the trigger, so that only a later row answers. Taken before the
    /// trigger is stored by <c>TriggerJobAndWait</c>, so a history that refuses to be read refuses before
    /// anything runs.
    /// </summary>
    public ValueTask Begin(CancellationToken cancellationToken = default) => FiringWait.Begin(this, cancellationToken);

    /// <summary>
    /// Waits up to <paramref name="timeout" /> for the firing to end.
    /// </summary>
    public ValueTask<FiringResult> Result(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return FiringWait.Result(this, timeout, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Signal?.Dispose();
        return default;
    }
}
