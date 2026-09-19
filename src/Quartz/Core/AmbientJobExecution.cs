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
/// Carries what the current asynchronous flow's operation is: the identity it takes locks under, and
/// the execution context of the firing it belongs to — which is what
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
/// <para>
/// The caller id rides on the same holder, which is why there is one <see cref="AsyncLocal{T}" /> here
/// and not two. Writing an <see cref="AsyncLocal{T}" /> copies the execution context, so a firing that
/// announced its caller id and then its context paid for two copies and boxed a <c>Guid?</c> between
/// them (#3802). Both are written at the points they always were: the id when the run shell starts,
/// the context once the job exists.
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
    /// The identity the current flow's operation takes job-store locks under, or
    /// <see langword="null" /> when the flow belongs to no operation of Quartz's.
    /// </summary>
    internal static Guid? CurrentCallerId => current.Value?.CallerId;

    /// <summary>
    /// Begins an operation on this flow under an identity of its own, and hands back the holder its
    /// execution context is published on once there is one.
    /// </summary>
    /// <remarks>
    /// A fresh holder every time, so an operation can never be handed the holder of one that has
    /// ended — there is nothing to restore and nothing to nest.
    /// </remarks>
    internal static Holder Begin(Guid callerId)
    {
        Holder holder = new(callerId);
        current.Value = holder;
        return holder;
    }

    internal sealed class Holder
    {
        internal Holder(Guid callerId)
        {
            CallerId = callerId;
        }

        /// <summary>
        /// The identity every job-store lock this operation takes is taken under, which is what makes
        /// a nested acquisition recognisable as the same caller's.
        /// </summary>
        internal Guid CallerId { get; }

        internal IJobExecutionContext? Context { get; set; }

        /// <summary>
        /// Makes the given execution context the current flow's until the returned scope is disposed.
        /// Costs no execution context copy: this holder is already the flow's.
        /// </summary>
        internal IDisposable Enter(IJobExecutionContext context)
        {
            Context = context;
            return new Scope(this);
        }
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
