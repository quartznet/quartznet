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

using Quartz.Impl;

namespace Quartz.Extensibility;

/// <summary>
/// A job instance produced by an <see cref="IJobFactory" />, together with whatever per-fire state
/// the factory needs back when the job is returned.
/// </summary>
/// <remarks>
/// <para>
/// A factory that has to allocate something in order to build the job — a dependency injection
/// scope, a database connection, a tenant context — has nowhere to keep it between
/// <see cref="IJobFactory.CreateJob" /> and <see cref="IJobFactory.ReturnJob" /> unless it hides
/// it inside the job instance. <see cref="State" /> is that place, so the job stays the job.
/// </para>
/// <para>
/// This is a struct so that a synchronously-completed
/// <see cref="ValueTask{TResult}" /> carries it without allocating.
/// </para>
/// </remarks>
/// <seealso cref="IJobFactory" />
/// <seealso cref="SimpleJobFactory" />
public readonly struct JobScope
{
    /// <summary>
    /// Initializes a new <see cref="JobScope" />.
    /// </summary>
    /// <param name="job">The job to execute.</param>
    /// <param name="state">
    /// Optional per-fire state, handed back verbatim to <see cref="IJobFactory.ReturnJob" />.
    /// </param>
    public JobScope(IJob job, object? state = null)
    {
        ArgumentNullException.ThrowIfNull(job);

        Job = job;
        State = state;
    }

    /// <summary>
    /// The job to execute.
    /// </summary>
    /// <remarks>
    /// Always set when the scope was built with the constructor. <c>default(JobScope)</c> skips that
    /// constructor and leaves this null despite the annotation, so a factory must never return
    /// <see langword="default" /> — the scheduler rejects such a scope rather than executing it.
    /// </remarks>
    public IJob Job { get; }

    /// <summary>
    /// Opaque state the factory attached when it created the job.
    /// </summary>
    /// <remarks>
    /// Quartz never interprets this. <see cref="SimpleJobFactory.ReturnJob" /> disposes it if it
    /// happens to be <see cref="IAsyncDisposable" /> or <see cref="IDisposable" />, which is enough
    /// for the common case; anything else is the factory's own business.
    /// </remarks>
    public object? State { get; }
}
