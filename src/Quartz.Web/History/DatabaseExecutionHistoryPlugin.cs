#region License

/*
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved.
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

using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.Web.History;

/// <summary>
/// Logs a history of all job and trigger executions.
/// </summary>
/// <author>Marko Lahma</author>
public class DatabaseExecutionHistoryPlugin : ISchedulerPlugin, IJobListener
{
    public static JobHistoryDelegate Delegate { get; private set; } = null!;

    /// <summary>
    /// Get the name of the <see cref="IJobListener" />.
    /// </summary>
    /// <value></value>
    public virtual string Name { get; private set; } = null!;

    public string TablePrefix { get; set; } = null!;
    public string DataSource { get; set; } = null!;
    public string DriverDelegateType { get; set; } = null!;

    /// <summary>
    /// Called during creation of the <see cref="IScheduler" /> in order to give
    /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
    /// </summary>
    public virtual ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken)
    {
        Name = pluginName;
        Delegate = new JobHistoryDelegate(DataSource, DriverDelegateType, TablePrefix);
        scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
        return default;
    }

    /// <summary>
    /// Called when the associated <see cref="IScheduler" /> is started, in order
    /// to let the plug-in know it can now make calls into the scheduler if it
    /// needs to.
    /// </summary>
    public virtual ValueTask Start(CancellationToken cancellationToken)
    {
        // do nothing...
        return default;
    }

    /// <summary>
    /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
    /// should free up all of it's resources because the scheduler is shutting
    /// down.
    /// </summary>
    public virtual ValueTask Shutdown(CancellationToken cancellationToken)
    {
        // nothing to do...
        return default;
    }

    /// <summary>
    ///     Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/> is
    ///     about to be executed (an associated <see cref="ITrigger"/> has occurred).
    ///     <para>
    ///         This method will not be invoked if the execution of the Job was vetoed by a
    ///         <see cref="ITriggerListener"/>.
    ///     </para>
    /// </summary>
    /// <seealso cref="JobExecutionVetoed(IJobExecutionContext, CancellationToken)"/>
    public virtual ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return default;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> after a <see cref="IJobDetail" />
    /// has been executed, and be for the associated <see cref="ITrigger" />'s
    /// <see cref="IOperableTrigger.Triggered" /> method has been called.
    /// </summary>
    public virtual ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken)
    {
        return Delegate.InsertJobHistoryEntry(context, jobException, cancellationToken);
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// was about to be executed (an associated <see cref="ITrigger" />
    /// has occurred), but a <see cref="ITriggerListener" /> vetoed it's
    /// execution.
    /// </summary>
    /// <seealso cref="JobToBeExecuted(IJobExecutionContext, CancellationToken)"/>
    public virtual ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return default;
    }
}