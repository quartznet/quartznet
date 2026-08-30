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

namespace Quartz;

/// <summary>
/// The rest of the job execution pipeline, as a middleware sees it: the middleware registered after
/// this one, ending in the job itself.
/// </summary>
/// <remarks>
/// The cancellation token has no default. A middleware is handed one and is expected to pass it on, so
/// leaving it out would have to be written out rather than being what happens when nothing is said.
/// </remarks>
/// <param name="context">The firing being executed.</param>
/// <param name="cancellationToken">
/// Signalled when this execution is interrupted. The same token
/// <see cref="IJobExecutionContext.CancellationToken" /> carries.
/// </param>
public delegate ValueTask JobExecutionDelegate(IJobExecutionContext context, CancellationToken cancellationToken);

/// <summary>
/// Wraps every job execution a scheduler performs, so a cross-cutting concern has somewhere to live
/// that is neither the job nor a listener.
/// </summary>
/// <remarks>
/// <para>
/// A log scope, a tenant context, a timeout, a translation of what a third-party library throws: each
/// of these has to <em>surround</em> the call to the job, and a listener cannot. Listeners are
/// notification-only — they are told a job is about to run and told what it did, but the job runs
/// between the two notifications rather than inside them, so a listener cannot open an
/// <c>await using</c> around it, cannot decline to run it, and cannot catch what it threw. Before this
/// existed the only place to put such code was a job that wrapped another job, which is why several
/// frameworks built on Quartz ship exactly that adapter.
/// </para>
/// <para>
/// <strong>What a middleware may do.</strong> Run code before and after <c>next</c>; not
/// call it at all, which means the job does not run; catch what it threw and rethrow something else;
/// and set ambient state that the job and everything it calls will read. What it may <em>not</em> do is
/// decide what the scheduler does with the trigger afterwards, except in the way a job decides it — by
/// throwing a <see cref="JobExecutionException" />, whose <see cref="JobExecutionException.RefireImmediately" />
/// and unschedule flags are honoured exactly as they are when the job raises one itself.
/// </para>
/// <para>
/// <strong>Where it runs.</strong> Inside the execution span and the duration measurement, so what a
/// middleware costs is part of what the firing cost, and outside the run shell's exception handling, so
/// an exception a middleware lets out is classified as though the job had thrown it. It runs after the
/// trigger and job listeners have been notified that the job is about to execute, which means a fire a
/// listener vetoed never reaches the pipeline at all.
/// </para>
/// <para>
/// <strong>Order.</strong> Middleware is registered on the scheduler's builder and runs in registration
/// order, outermost first: the first registered sees the firing before the second, and sees the result
/// after it. The chain is composed once when the scheduler is built, so a middleware instance is shared
/// by every firing of that scheduler and must not keep per-firing state in a field. Per-firing state
/// belongs in an <see cref="System.Threading.AsyncLocal{T}" />, or in the job's dependency-injection
/// scope — which is prepared by <c>ConfigureJobScope</c> and read back through
/// <see cref="IJobExecutionContextAccessor" />. No <c>IServiceScope</c> is threaded through this
/// signature, because the scope belongs to the firing rather than to any one middleware.
/// </para>
/// <para>
/// <strong>The token.</strong> Forward the one you were given. Passing a different token to
/// <c>next</c> changes what the job's <c>Execute</c> parameter is without changing
/// <see cref="IJobExecutionContext.CancellationToken" />, so the two stop being the same token and a
/// job that reads the context sees the wrong one — which is the trap in writing a timeout as a
/// middleware.
/// </para>
/// <para>
/// <strong>Not a retry mechanism.</strong> Catching a failure and awaiting a delay before calling
/// <c>next</c> again holds a thread-pool slot for the whole wait and loses the attempt if
/// the process stops; a trigger's retry policy is the tool for that.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class TenantScopeMiddleware(TenantContext tenants) : IJobExecutionMiddleware
/// {
///     public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
///     {
///         using IDisposable scope = tenants.Enter(context.Trigger.Key.Group);
///         await next(context, cancellationToken);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IJobListener" />
/// <seealso cref="IJobExecutionContextAccessor" />
public interface IJobExecutionMiddleware
{
    /// <summary>
    /// Executes this stage of the pipeline.
    /// </summary>
    /// <param name="context">The firing being executed.</param>
    /// <param name="next">
    /// The rest of the pipeline. Await it to run the job; do not, and the job does not run.
    /// </param>
    /// <param name="cancellationToken">
    /// Signalled when this execution is interrupted. Pass it on to <paramref name="next" /> and to
    /// anything else awaited here.
    /// </param>
    // CA1716: the parameter is called next because that is what this shape has been called since
    // ASP.NET Core named it that on IMiddleware.InvokeAsync, and a name every reader of a middleware
    // already knows is worth more here than sparing a Visual Basic implementation a pair of brackets.
#pragma warning disable CA1716
    ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default);
#pragma warning restore CA1716
}
