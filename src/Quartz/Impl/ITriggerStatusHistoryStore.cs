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

namespace Quartz.Impl;

/// <summary>
/// A history store that keeps a <see cref="TriggerRunStatus" /> per trigger, and is told with each execution
/// whether its trigger was finalized by it.
/// </summary>
/// <remarks>
/// <para>
/// A firing after which the trigger may not fire again (a one-off's only firing, the last of a series, and so
/// every manual run, backfill and recovery) leaves no trigger status: its trigger is deleted as the firing
/// completes, so its status is one no listing would show, and a burst of them would push the statuses of the
/// recurring triggers out of a bounded store.
/// </para>
/// <para>
/// Internal, and asked for by <see cref="ExecutionHistoryPlugin" /> alone, as
/// <see cref="IBufferedExecutionHistoryStore" /> is: the fact lives on the firing's trigger, not on the public
/// <see cref="ExecutionHistoryEntry" />, where a write-only flag would read <see langword="false" /> on every row
/// read back. A store that wraps a shipped one is not told, and folds every firing it is handed.
/// </para>
/// </remarks>
internal interface ITriggerStatusHistoryStore
{
    /// <summary>
    /// Records one execution, as <see cref="Extensibility.IExecutionHistoryStore.AddExecution" /> does, folding it
    /// into its trigger's status unless <paramref name="triggerFinalized" /> or the run was manual.
    /// </summary>
    /// <param name="entry">The execution that finished.</param>
    /// <param name="triggerFinalized">Whether the trigger that fired may not fire again.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddExecution(ExecutionHistoryEntry entry, bool triggerFinalized, CancellationToken cancellationToken = default);
}
