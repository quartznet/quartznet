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

using Quartz.Core;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Impl;

/// <summary>
/// The part of the scheduler facade that consults <see cref="AdoJobStoreOptions.EnlistmentSource" />.
/// </summary>
/// <remarks>
/// <para>
/// The facade is the one place the hook can live. Every <see cref="IScheduler" /> call an application
/// makes ends here, and so does every call a job makes through <see cref="IJobExecutionContext.Scheduler" />;
/// the scheduler's own work - the scheduling thread, the misfire handler, cluster check-in, recovery and
/// completing a trigger - calls <see cref="QuartzScheduler" /> and the store directly, so it never reaches
/// the source.
/// </para>
/// <para>
/// A member that is a plain pass-through must not set the enlistment: it is an
/// <see cref="AsyncLocal{T}" />, so a non-<c>async</c> member that set it and returned the store's task
/// would leave it in the <em>caller's</em> execution context, and every later call on that flow would run
/// enlisted. <see cref="WithEnlistment{TState, TResult}" /> is an <c>async</c> method, and the enlistment is set and
/// ended inside it, so the caller's context is restored when it returns - whether the call completed
/// synchronously or not.
/// </para>
/// </remarks>
internal sealed partial class StdScheduler
{
    /// <summary>
    /// The persistent store underneath the decorators, when it has a source to consult. Only its name is
    /// read, to tell whether the flow has already enlisted something for this scheduler.
    /// </summary>
    private readonly AdoJobStoreBase? enlistingStore;

    /// <summary>
    /// What the members branch on: <see langword="null" /> costs one check and nothing else.
    /// </summary>
    private readonly Func<EnlistmentSourceContext, IDisposable?>? enlistmentSource;

    /// <summary>
    /// The state of a call that has no arguments to carry.
    /// </summary>
    private readonly struct NoState;

    /// <summary>
    /// Runs a call that answers with a value inside whatever the enlistment source enlists for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The call is a <see langword="static" /> lambda and its arguments travel as <paramref name="state" />,
    /// so nothing is allocated on the path that has no source: a lambda that captured the arguments would
    /// allocate its closure as the member was entered, before the branch.
    /// </para>
    /// <para>
    /// The source is consulted first, synchronously on the caller's thread - this method's synchronous
    /// prefix - and an exception from it faults the returned task before any store work. When the call
    /// succeeds, the enlistment is ended and a failure to end it reaches the caller. When it fails or is
    /// cancelled, the enlistment is ended best effort: the exception that reaches the caller is the
    /// call's own, as it is in <c>AmbientConnection.EnlistmentScope</c>.
    /// </para>
    /// </remarks>
    private async ValueTask<TResult> WithEnlistment<TState, TResult>(
        Func<QuartzScheduler, TState, CancellationToken, ValueTask<TResult>> call,
        TState state,
        CancellationToken cancellationToken)
    {
        IDisposable? enlistment = Consult();

        TResult result;
        try
        {
            result = await call(scheduler, state, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            EndAfterFailure(enlistment);
            throw;
        }

        enlistment?.Dispose();
        return result;
    }

    /// <summary>
    /// Runs a call that answers with nothing inside whatever the enlistment source enlists for it.
    /// </summary>
    /// <inheritdoc cref="WithEnlistment{TState, TResult}" path="/remarks" />
    private async ValueTask WithEnlistment<TState>(
        Func<QuartzScheduler, TState, CancellationToken, ValueTask> call,
        TState state,
        CancellationToken cancellationToken)
    {
        IDisposable? enlistment = Consult();

        try
        {
            await call(scheduler, state, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            EndAfterFailure(enlistment);
            throw;
        }

        enlistment?.Dispose();
    }

    /// <summary>
    /// Asks the source, unless the flow has already enlisted something for this scheduler.
    /// </summary>
    /// <remarks>
    /// An explicit <see cref="SchedulerEnlistmentExtensions.EnlistTransaction" /> is the caller saying
    /// which transaction this is, and a source second-guessing it would be a second opinion about a
    /// question that has been answered. The context is built here and nowhere else, so a scheduler with no
    /// source builds none. It carries this facade, which is what <see cref="SchedulerEnlistmentExtensions" />
    /// resolves a scheduler to, so every refusal those methods make applies to what the source enlists.
    /// </remarks>
    private IDisposable? Consult()
    {
        if (AmbientConnection.Get(enlistingStore!.InstanceName) is not null)
        {
            return null;
        }

        return enlistmentSource!(new EnlistmentSourceContext { Scheduler = this });
    }

    /// <summary>
    /// Ends an enlistment after a call that failed, without letting a failure of its own replace the
    /// call's exception: that one is the error the caller needs to see.
    /// </summary>
    private static void EndAfterFailure(IDisposable? enlistment)
    {
        try
        {
            enlistment?.Dispose();
        }
        catch (Exception)
        {
            // Best effort, as AmbientConnection.EnlistmentScope.Dispose is. Letting this out would replace
            // whatever the call was unwinding with - typically the database error the caller needs.
        }
    }
}
