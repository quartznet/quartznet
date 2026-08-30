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
/// The interface to be implemented by classes which represent a 'job' to be
/// performed.
/// </summary>
/// <remarks>
/// An instance is built for each fire by the scheduler's <see cref="Quartz.Extensibility.IJobFactory" />,
/// which by default resolves the job from the dependency-injection container — so a job's dependencies
/// are ordinary constructor parameters, and there need be no parameterless constructor.
/// <see cref="JobDataMap" /> provides a mechanism for 'instance member data' that may be required by
/// some implementations of this interface.
/// </remarks>
/// <seealso cref="IJobDetail" />
/// <seealso cref="JobBuilder" />
/// <seealso cref="DisallowConcurrentExecutionAttribute" />
/// <seealso cref="PersistJobDataAfterExecutionAttribute" />
/// <seealso cref="ITrigger" />
/// <seealso cref="IScheduler" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface IJob
{
    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// fires that is associated with the <see cref="IJob" />.
    /// </summary>
    /// <remarks>
    /// The implementation may wish to set a  result object on the
    /// JobExecutionContext before this method exits.  The result itself
    /// is meaningless to Quartz, but may be informative to
    /// <see cref="IJobListener" />s or
    /// <see cref="ITriggerListener" />s that are watching the job's
    /// execution.
    /// </remarks>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">
    /// Signalled when this execution is interrupted — either by
    /// <see cref="IScheduler.Interrupt(JobKey, CancellationToken)" /> or by the scheduler shutting
    /// down while configured to interrupt running jobs. A long-running job should pass this on to
    /// everything it awaits; a job that ignores it cannot be interrupted, and will hold up shutdown
    /// until it finishes on its own.
    /// <para>
    /// This is the same token as <see cref="IJobExecutionContext.CancellationToken" />, given as a
    /// parameter so that it is impossible to miss and so that the compiler can point out where it
    /// has not been forwarded.
    /// </para>
    /// </param>
    ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// A job that declares the type of the input it is scheduled with, so the payload arrives as a
/// parameter instead of being dug out of a <see cref="JobDataMap" /> by key.
/// </summary>
/// <remarks>
/// <para>
/// The input is put on the job or, more usually, on the trigger — <c>UsingInput</c> on either builder —
/// and the scheduler stores it as a string under <see cref="SchedulerConstants.JobInput" />. The
/// trigger's input wins over the job's, exactly as any other job data does.
/// </para>
/// <code>
/// public sealed record SendEmail(string To, string Subject);
///
/// public sealed class SendEmailJob : IJob&lt;SendEmail&gt;
/// {
///     public ValueTask Execute(IJobExecutionContext context, SendEmail input, CancellationToken cancellationToken = default)
///     {
///         // input.To, input.Subject
///         return default;
///     }
/// }
/// </code>
/// <para>
/// <typeparamref name="TInput" /> exists at compile time in exactly one place: the default
/// implementation of <see cref="IJob.Execute" /> below. Everything that runs a job — the job factory,
/// <c>JobRunShell</c>, the listeners — sees an <see cref="IJob" />, and dispatching to a typed job from
/// there would mean closing a generic method over a runtime type, which no trimmed or natively compiled
/// application can do. Being a default interface method rather than a base class also leaves a job free
/// to derive from whatever it likes.
/// </para>
/// <para>
/// A job whose input is missing fails the firing with a <see cref="SchedulerException" /> naming the
/// key, rather than running with a default payload.
/// </para>
/// </remarks>
/// <typeparam name="TInput">The type of the payload this job is scheduled with.</typeparam>
/// <seealso cref="JobInputBuilderExtensions" />
/// <seealso cref="JobExecutionContextInputExtensions.GetInput{TInput}" />
/// <seealso cref="Quartz.Extensibility.IJobInputSerializer" />
public interface IJob<TInput> : IJob
{
    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" /> fires that is associated
    /// with this job, with the input the job was scheduled with.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="input">The payload this firing was scheduled with.</param>
    /// <param name="cancellationToken">
    /// Signalled when this execution is interrupted. The same token as
    /// <see cref="IJobExecutionContext.CancellationToken" />, for the reason
    /// <see cref="IJob.Execute" /> gives.
    /// </param>
    ValueTask Execute(IJobExecutionContext context, TInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the stored input and hands it to <see cref="Execute(IJobExecutionContext, TInput, CancellationToken)" />.
    /// </summary>
    ValueTask IJob.Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return Execute(context, JobInput.Read<TInput>(context), cancellationToken);
    }
}