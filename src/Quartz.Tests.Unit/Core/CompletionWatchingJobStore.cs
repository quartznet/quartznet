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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A record of the calls one collaborator member has received, which a test can await instead of
/// poll.
/// </summary>
/// <remarks>
/// This is what keeps the scheduler-loop tests off the wall clock. The assertions are about which
/// calls the loop made; awaiting a call only decides when it is safe to look at them, so the waits
/// carry a generous deadline and never a timing expectation.
/// </remarks>
public sealed class CallLog<T>
{
    private readonly object gate = new object();
    private readonly List<T> entries = new List<T>();
    private readonly List<(int Count, TaskCompletionSource<bool> Source)> waiters = new List<(int, TaskCompletionSource<bool>)>();

    /// <summary>
    /// The calls recorded so far, oldest first.
    /// </summary>
    public IReadOnlyList<T> Entries
    {
        get
        {
            lock (gate)
            {
                return entries.ToArray();
            }
        }
    }

    /// <summary>
    /// How many calls have been recorded so far.
    /// </summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    /// <summary>
    /// Records one call, releasing everyone waiting for the count it brings the log to.
    /// </summary>
    public void Record(T entry)
    {
        List<TaskCompletionSource<bool>> ready = null;
        lock (gate)
        {
            entries.Add(entry);
            for (int i = waiters.Count - 1; i >= 0; i--)
            {
                if (waiters[i].Count <= entries.Count)
                {
                    ready ??= new List<TaskCompletionSource<bool>>();
                    ready.Add(waiters[i].Source);
                    waiters.RemoveAt(i);
                }
            }
        }

        if (ready is null)
        {
            return;
        }

        // Completed outside the lock: a continuation that records another call would otherwise
        // re-enter it on this very thread.
        foreach (TaskCompletionSource<bool> source in ready)
        {
            source.TrySetResult(true);
        }
    }

    /// <summary>
    /// A task that completes once this member has been called <paramref name="count" /> times.
    /// </summary>
    public Task Reaches(int count)
    {
        TaskCompletionSource<bool> source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (entries.Count >= count)
            {
                return Task.CompletedTask;
            }

            waiters.Add((count, source));
        }

        return source.Task;
    }
}

/// <summary>
/// One <see cref="IJobStore.TriggeredJobComplete" /> call, as the scheduler thread made it.
/// </summary>
public sealed record CompletedFiring(TriggerKey Trigger, JobKey Job, SchedulerInstruction Instruction);

/// <summary>
/// Records how each firing was handed back to the store: through <c>TriggeredJobComplete</c>, with which
/// instruction, or through <c>ReleaseAcquiredTrigger</c>.
/// </summary>
/// <remarks>
/// <para>
/// The distinction is the one <c>AGENTS.md</c> records: after <c>TriggersFired</c> the scheduler must
/// complete a firing rather than release it, because releasing does not unblock the sibling triggers of
/// a <see cref="DisallowConcurrentExecutionAttribute" /> job. Nothing about that is visible from the
/// scheduler's own surface, so a test watches the store.
/// </para>
/// <para>
/// Both calls are recorded <em>after</em> the wrapped store has acted on them, so a test that waits on a
/// record and then asks the store a question is looking at a store that has already settled — which is
/// what keeps these assertions off the clock.
/// </para>
/// <para>
/// <see cref="StdSchedulerFactory" /> builds the store from its type name, so the instance it built is
/// published through <see cref="LastInstance" /> for the test that asked for it; a fixture using this
/// store is therefore <c>[NonParallelizable]</c>.
/// </para>
/// </remarks>
public sealed class CompletionWatchingJobStore : RAMJobStore
{
    public static CompletionWatchingJobStore LastInstance { get; private set; }

    public CompletionWatchingJobStore()
    {
        LastInstance = this;
    }

    /// <summary>The triggers handed back through <see cref="ReleaseAcquiredTrigger" />.</summary>
    public CallLog<TriggerKey> Releases { get; } = new CallLog<TriggerKey>();

    /// <summary>The completions the scheduler reported, instruction included.</summary>
    public CallLog<CompletedFiring> Completions { get; } = new CallLog<CompletedFiring>();

    public override async Task ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        await base.ReleaseAcquiredTrigger(trigger, cancellationToken).ConfigureAwait(false);
        Releases.Record(trigger.Key);
    }

    public override async Task TriggeredJobComplete(
        IOperableTrigger trigger,
        IJobDetail jobDetail,
        SchedulerInstruction triggerInstCode,
        CancellationToken cancellationToken = default)
    {
        await base.TriggeredJobComplete(trigger, jobDetail, triggerInstCode, cancellationToken).ConfigureAwait(false);
        Completions.Record(new CompletedFiring(trigger.Key, jobDetail.Key, triggerInstCode));
    }
}
