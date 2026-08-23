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
/// Carries the execution context of the firing the current asynchronous flow belongs to, which is what
/// <see cref="IJobExecutionContextAccessor" /> reads.
/// </summary>
/// <remarks>
/// <para>
/// The state flows with <see cref="AsyncLocal{T}" />, the same mechanism <c>AmbientConnection</c> uses
/// for an enlisted connection — so it is a property of the logical flow rather than of the thread, and
/// a pooled thread picking up unrelated work inherits nothing.
/// </para>
/// <para>
/// The value is held behind a mutable holder rather than stored directly, which is what makes the end
/// of a firing reach flows that have already copied the execution context. Clearing
/// <c>current.Value</c> alone would only affect the flow doing the clearing: work started inside the
/// job and left running — <c>Task.Run</c>, a detached continuation — captured its own copy and would go
/// on reading a context whose dependency injection scope has been disposed and whose cancellation
/// handle is gone. Emptying the holder they all share is what turns that into
/// <see langword="null" />. It is the same reason <c>HttpContextAccessor</c> does it this way.
/// </para>
/// <para>
/// Static rather than one instance per container, because a logical flow is inside at most one firing
/// however many containers the process holds: two schedulers cannot be executing a job on one flow.
/// </para>
/// </remarks>
internal static class AmbientJobExecution
{
    private static readonly AsyncLocal<Holder?> current = new();

    /// <summary>
    /// The firing the current flow belongs to, or <see langword="null" /> when it belongs to none.
    /// </summary>
    internal static IJobExecutionContext? Current => current.Value?.Context;

    /// <summary>
    /// Makes the given execution context the current flow's until the returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// A fresh holder every time, so a firing can never be handed the holder of one that has ended —
    /// there is nothing to restore and nothing to nest.
    /// </remarks>
    internal static IDisposable Enter(IJobExecutionContext context)
    {
        Holder holder = new(context);
        current.Value = holder;
        return new Scope(holder);
    }

    private sealed class Holder
    {
        internal Holder(IJobExecutionContext context)
        {
            Context = context;
        }

        internal IJobExecutionContext? Context { get; set; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Holder holder;

        internal Scope(Holder holder)
        {
            this.holder = holder;
        }

        /// <summary>
        /// Ends the firing for every flow that captured it, including any it left running.
        /// </summary>
        /// <remarks>
        /// Only the holder is emptied. Assigning <c>current.Value</c> would publish a new execution
        /// context on the disposing thread for no benefit: reading a holder whose context is gone is
        /// already <see langword="null" />, and the flow this runs on is about to end anyway.
        /// </remarks>
        public void Dispose()
        {
            holder.Context = null;
        }
    }
}
