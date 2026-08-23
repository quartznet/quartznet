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
/// The job execution the calling code is part of, for code that cannot be handed an
/// <see cref="IJobExecutionContext" />: a scoped service, a logging enricher, a repository three calls
/// below <c>Execute</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>AddQuartz</c> as a singleton, so any container with Quartz in it has one. It
/// answers for the firing of whichever scheduler is running in this asynchronous flow, which is why it
/// is not one of a scheduler's own parts: a flow is inside at most one firing, whatever container owns
/// the scheduler that started it.
/// </para>
/// <para>
/// <strong>When it is set.</strong> From the moment the execution context exists — before the trigger
/// and job listeners are notified — until the job has been returned to the job factory. Outside that
/// window it is <see langword="null" />: on the scheduler's own threads, in application code that
/// merely schedules something, and in a <see cref="ISchedulerListener" /> reacting to a scheduling
/// call rather than to a firing.
/// </para>
/// <para>
/// <strong>It is never another firing's.</strong> The value travels with
/// <see cref="System.Threading.ExecutionContext" />, so it is a property of the logical flow rather
/// than of the thread, and a pooled thread picking up unrelated work does not inherit it. The end of a
/// firing additionally clears the value <em>through</em> every flow that captured it — so work started
/// inside a job and left running past the end of the execution, with <c>Task.Run</c> or a detached
/// continuation, reads <see langword="null" /> from that point rather than a context whose scope has
/// been disposed and whose cancellation handle is gone. There is deliberately no setter: an ambient
/// context anyone can assign is an ambient context that can be left pointing at a firing that is over.
/// </para>
/// <para>
/// <strong>What it is not.</strong> It is not available while the job is being <em>constructed</em>:
/// the execution context takes the job instance, so it does not exist until the job factory has
/// produced one. Seeding a job's dependency injection scope is still <c>ConfigureJobScope</c>'s job,
/// which is handed the <see cref="Extensibility.TriggerFiredBundle" /> before anything is resolved —
/// populate a scoped holder object from it, or set an <see cref="System.Threading.AsyncLocal{T}" />.
/// Code that reads the tenant when it is <em>used</em> rather than when it is constructed can read it
/// from here instead and needs neither.
/// </para>
/// <para>
/// <strong>What it exposes.</strong> The whole <see cref="IJobExecutionContext" />, rather than a
/// tenant-shaped projection of it. Quartz has no tenant concept and is not going to get one, so a
/// narrower type here would be inventing exactly that — and it would have to grow a member every time
/// somebody needed one more fact the context already carries.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class TenantConnectionFactory(IJobExecutionContextAccessor accessor)
/// {
///     public string ConnectionString =>
///         connectionStrings[accessor.Current?.Trigger.Key.Group ?? throw new InvalidOperationException(
///             "there is no job running on this flow to take a tenant from")];
/// }
/// </code>
/// </example>
public interface IJobExecutionContextAccessor
{
    /// <summary>
    /// The firing the calling code is part of, or <see langword="null" /> when it is not part of one.
    /// </summary>
    IJobExecutionContext? Current { get; }
}
