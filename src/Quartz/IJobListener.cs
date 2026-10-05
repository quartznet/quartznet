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

using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// The interface to be implemented by classes that want to be informed when a
/// <see cref="IJobDetail" /> executes. In general,  applications that use a
/// <see cref="IScheduler" /> will not have use for this mechanism.
/// </summary>
/// <remarks>
/// Every member has a default implementation, so an implementation only has to write the
/// notifications it cares about. <see cref="Name" /> defaults to the implementing type's name.
/// </remarks>
/// <seealso cref="IListenerManager.AddJobListener(Quartz.IJobListener,System.Collections.Generic.IReadOnlyCollection{Quartz.IMatcher{Quartz.JobKey}})" />
/// <seealso cref="IMatcher{T}" />
/// <seealso cref="IJob" />
/// <seealso cref="IJobExecutionContext" />
/// <seealso cref="JobExecutionException" />
/// <seealso cref="ITriggerListener" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface IJobListener
{
    /// <summary>
    /// The name this listener is registered and removed under.
    /// </summary>
    /// <remarks>
    /// Defaults to the implementing type's name, which is the right answer whenever a scheduler
    /// has at most one listener of a given type. Override it when several instances of one type
    /// are registered with the same scheduler, because the later registration would otherwise
    /// replace the earlier one.
    /// </remarks>
    string Name => GetType().Name;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// is about to be executed (an associated <see cref="ITrigger" />
    /// has occurred).
    /// <para>
    /// This method will not be invoked if the execution of the Job was vetoed
    /// by a <see cref="ITriggerListener" />.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The default implementation does nothing.
    /// </remarks>
    /// <seealso cref="JobExecutionVetoed" />
    ValueTask JobToBeExecuted(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// was about to be executed (an associated <see cref="ITrigger" />
    /// has occurred), but a <see cref="ITriggerListener" /> vetoed it's
    /// execution.
    /// </summary>
    /// <remarks>
    /// The default implementation does nothing.
    /// </remarks>
    /// <seealso cref="JobToBeExecuted" />
    ValueTask JobExecutionVetoed(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> after a <see cref="IJobDetail" />
    /// has been executed, and be for the associated <see cref="IOperableTrigger" />'s
    /// <see cref="IOperableTrigger.Triggered" /> method has been called.
    /// </summary>
    /// <remarks>
    /// The default implementation does nothing.
    /// </remarks>
    ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when what a running <see cref="IJob" /> reports through
    /// <see cref="IJobExecutionContext.ReportProgress" /> has changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised at the cadence the scheduler writes progress to the job store: the first report at once,
    /// then at most once a second per firing, and only when the value has changed. Reports in between
    /// are coalesced, so <paramref name="progress" /> is the latest. A report the listeners have not
    /// heard by the time the job returns is raised then, before <see cref="JobWasExecuted" />; every
    /// call for a firing has returned before that one is made.
    /// </para>
    /// <para>
    /// Never made on the job's own flow, so <see cref="IJobExecutionContext.ReportProgress" /> stays
    /// cheap: the call comes from the thread pool, outside the firing's execution context, and one call
    /// for a firing is made at a time. Only listeners whose matchers match the job hear it. A listener
    /// that throws is logged, and the job, the job store and the other listeners carry on.
    /// </para>
    /// <para>
    /// In-process only: a listener hears the firings this scheduler runs, not those another node of a
    /// cluster runs. <see cref="IScheduler.QueryFireInstances" />, the HTTP API and the dashboard read
    /// progress from anywhere.
    /// </para>
    /// <para>
    /// The default implementation does nothing.
    /// </para>
    /// </remarks>
    /// <param name="context">The running firing.</param>
    /// <param name="progress">
    /// What the job reported, with its message already cut to
    /// <see cref="FireInstanceProgress.MaxMessageLength" />: the value the job store is handed.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask JobProgressChanged(
        IJobExecutionContext context,
        FireInstanceProgress progress,
        CancellationToken cancellationToken = default) => default;
}