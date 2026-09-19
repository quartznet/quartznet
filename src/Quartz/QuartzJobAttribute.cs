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
/// Declares this class as a job to register, where the job is written. The analyzer that ships inside
/// <c>Quartz.nupkg</c> generates the <c>AddJob&lt;T&gt;</c> call for it, which
/// <c>builder.AddDeclaredJobs()</c> runs.
/// </summary>
/// <remarks>
/// <para>
/// Nothing reads this attribute at run time. It is read by the compiler, and what reaches the
/// scheduler is ordinary generated C# calling the same <c>AddJob&lt;T&gt;</c> and
/// <c>AddTrigger&lt;T&gt;</c> a hand-written registration calls — so a declared job is trimmable and
/// native-AOT clean for the reason a hand-written one is, and everything a registration can do that
/// an attribute cannot say is still available beside it.
/// </para>
/// <para>
/// The class has to be a concrete, non-generic, accessible <see cref="IJob" /> —
/// <see cref="IJob{TInput}" /> counts, since it is one — or the generator reports <c>QZ1001</c>
/// rather than emitting a call that would not compile. Two declarations resolving to the same key
/// are <c>QZ1002</c>.
/// </para>
/// <para>
/// A declared job's schedule is <see cref="CronTriggerAttribute" />, as many times as it has
/// schedules. A job declaring none is registered durably, because a job with no trigger is the shape
/// a store discards otherwise.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [QuartzJob(Name = "cleanup", Group = "maintenance")]
/// [CronTrigger("0 0 0/6 * * ?")]
/// public sealed class CleanupJob : IJob
/// {
///     public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
///     {
///         return default;
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="CronTriggerAttribute" />
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class QuartzJobAttribute : Attribute
{
    /// <summary>
    /// The job key's name. Defaults to the class's own name.
    /// </summary>
    /// <remarks>
    /// The default is the name alone, not the namespace-qualified one: a job key is what an operator
    /// reads in a dashboard and what a scheduling file names.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// The job key's group. Defaults to <see cref="Key{T}.DefaultGroup" />.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// What the job is for, carried on the job detail. Defaults to none.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the job stays in the store when no trigger points at it.
    /// </summary>
    /// <remarks>
    /// Forced on for a job that declares no <see cref="CronTriggerAttribute" />: a non-durable job
    /// with no trigger is deleted as soon as it is stored, so declaring one would be declaring
    /// nothing.
    /// </remarks>
    public bool Durable { get; init; }

    /// <summary>
    /// Whether a firing interrupted by a hard shutdown is re-fired when this node — or another one —
    /// recovers.
    /// </summary>
    public bool RequestRecovery { get; init; }

    /// <summary>
    /// Which scheduler this job belongs to, by the name it was registered under with
    /// <c>AddQuartz(name, …)</c>. Defaults to every scheduler <c>AddDeclaredJobs()</c> is called on.
    /// </summary>
    /// <remarks>
    /// The generated registration is wrapped in a check on <c>IQuartzBuilder.SchedulerName</c>, so a
    /// job naming a scheduler is skipped by every other one — the unnamed scheduler included, whose
    /// name is the empty string.
    /// </remarks>
    public string? Scheduler { get; init; }
}
