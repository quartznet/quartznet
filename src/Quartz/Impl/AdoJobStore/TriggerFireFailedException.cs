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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Thrown out of a <c>TriggersFired</c> batch when firing one of its triggers failed for a reason that
/// will not go away on a retry. The batch's transaction is rolled back whole — a statement that failed
/// partway has left the earlier writes of that fire in it, and on PostgreSQL has doomed every statement
/// after it — and the batch runs again without the trigger, which is reported
/// <see cref="Extensibility.TriggerFiredResult.Failed" /> with <see cref="Exception.InnerException" />
/// as the reason.
/// </summary>
/// <remarks>
/// A <see cref="JobPersistenceException" /> for the reason <see cref="CompletionNeedsLockException" />
/// is one: the store's transaction wrapper rolls back and rethrows a persistence exception as itself,
/// and it retries only a transient one. Nothing about this one is transient — the failure it carries was
/// classified before it was wrapped — so the wrapper releases the lock and hands it straight back to the
/// batch loop.
/// </remarks>
internal sealed class TriggerFireFailedException : JobPersistenceException
{
    public TriggerFireFailedException(int index, TriggerKey triggerKey, Exception failure)
        : base($"Firing trigger '{triggerKey}' failed; the batch is rolled back and fired again without it.", failure)
    {
        Index = index;
        TriggerKey = triggerKey;
    }

    /// <summary>
    /// The position of the failed trigger in the batch that was being fired — the list the attempt was
    /// handed, which after the first failure is shorter than the one the scheduler asked for.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// The trigger whose fire failed.
    /// </summary>
    public TriggerKey TriggerKey { get; }

    /// <summary>
    /// The failed trigger's previous fire time as it was acquired, which the failure is counted against.
    /// Set by a round that acquires and fires in one transaction, whose rollback takes the acquired
    /// trigger with it; <c>TriggersFired</c> still holds the trigger it was handed.
    /// </summary>
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }

    /// <summary>
    /// How many triggers the rolled-back attempt was firing, for the log line. Set by a round that
    /// acquires and fires in one transaction, as <see cref="PreviousFireTimeUtc" /> is.
    /// </summary>
    public int BatchSize { get; init; }
}
