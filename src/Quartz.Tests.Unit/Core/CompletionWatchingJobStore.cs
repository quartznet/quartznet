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

namespace Quartz.Tests.Unit.Core;

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
/// </remarks>
public sealed class CompletionWatchingJobStore : DelegatingJobStore
{
    public CompletionWatchingJobStore(IJobStore jobStore) : base(jobStore)
    {
    }

    /// <summary>The triggers handed back through <see cref="ReleaseAcquiredTrigger" />.</summary>
    public CallLog<TriggerKey> Releases { get; } = new();

    /// <summary>The completions the scheduler reported, instruction included.</summary>
    public CallLog<CompletedFiring> Completions { get; } = new();

    /// <summary>
    /// Runs immediately before the wrapped store is told a firing is over, so that a test can read the
    /// state the completion is about to change. Nothing is holding the store's lock at that point, so
    /// the hook may ask it questions.
    /// </summary>
    public Func<ValueTask> BeforeCompletion { get; set; }

    public override async ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        await base.ReleaseAcquiredTrigger(trigger, cancellationToken).ConfigureAwait(false);
        Releases.Record(trigger.Key);
    }

    public override async ValueTask TriggeredJobComplete(
        IOperableTrigger trigger,
        IJobDetail jobDetail,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        Func<ValueTask> before = BeforeCompletion;
        if (before is not null)
        {
            await before().ConfigureAwait(false);
        }

        await base.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken).ConfigureAwait(false);
        Completions.Record(new CompletedFiring(trigger.Key, jobDetail.Key, triggerInstructionCode));
    }
}
