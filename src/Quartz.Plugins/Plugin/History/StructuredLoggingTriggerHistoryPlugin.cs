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

using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Plugin.History;

/// <summary>
/// Logs a history of all trigger firings via structured logging.
/// </summary>
/// <remarks>
/// <para>
/// This is a structured logging alternative to <see cref="LoggingTriggerHistoryPlugin"/>.
/// Unlike <see cref="LoggingTriggerHistoryPlugin"/>, message templates use named parameters
/// (e.g. <c>{TriggerName}</c>, <c>{JobGroup}</c>) instead of index-based placeholders.
/// This makes log output compatible with structured logging sinks like Serilog and NLog,
/// and avoids template cache memory leaks.
/// </para>
/// <para>
/// Message templates can be customized via properties. The parameter names in the
/// templates must match the default names exactly (they are positionally mapped).
/// </para>
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public sealed class StructuredLoggingTriggerHistoryPlugin : ISchedulerPlugin, ITriggerListener
{
    private readonly ILog log = LogProvider.GetLogger(typeof(StructuredLoggingTriggerHistoryPlugin));

    /// <summary>
    /// Gets or sets the message that is logged when a trigger fires.
    /// </summary>
    public string TriggerFiredMessage { get; set; } = "Trigger {TriggerGroup}.{TriggerName} fired job {JobGroup}.{JobName} at {FireTime} scheduled at {ScheduledFireTime} next fire at {NextFireTime} refire count {RefireCount}";

    /// <summary>
    /// Gets or sets the message that is logged when a trigger misfires.
    /// </summary>
    public string TriggerMisfiredMessage { get; set; } = "Trigger {TriggerGroup}.{TriggerName} misfired job {JobGroup}.{JobName} at {FireTime}. Should have fired at {ScheduledFireTime} next fire at {NextFireTime}";

    /// <summary>
    /// Gets or sets the message that is logged when a trigger completes.
    /// </summary>
    public string TriggerCompleteMessage { get; set; } = "Trigger {TriggerGroup}.{TriggerName} completed firing job {JobGroup}.{JobName} at {CompletedTime} scheduled at {ScheduledFireTime} next fire at {NextFireTime} with instruction {TriggerInstructionCode}";

    /// <summary>
    /// Get the name of the <see cref="ITriggerListener" />.
    /// </summary>
    public string Name { get; private set; } = "Structured Logging Trigger History Plugin";

    /// <summary>
    /// Called during creation of the <see cref="IScheduler" /> in order to give
    /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
    /// </summary>
    public Task Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        scheduler.ListenerManager.AddTriggerListener(this, EverythingMatcher<TriggerKey>.AllTriggers());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the associated <see cref="IScheduler" /> is started, in order
    /// to let the plug-in know it can now make calls into the scheduler if it
    /// needs to.
    /// </summary>
    public Task Start(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
    /// should free up all of it's resources because the scheduler is shutting
    /// down.
    /// </summary>
    public Task Shutdown(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// has fired, and it's associated <see cref="IJobDetail" />
    /// is about to be executed.
    /// <para>
    /// It is called before the <see cref="VetoJobExecution" /> method of this
    /// interface.
    /// </para>
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> that has fired.</param>
    /// <param name="context">The <see cref="IJobExecutionContext" /> that will be passed to the <see cref="IJob" />'s <see cref="IJob.Execute" /> method.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public Task TriggerFired(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!log.IsInfoEnabled())
        {
            return Task.CompletedTask;
        }

        log.InfoFormat(
            TriggerFiredMessage,
            trigger.Key.Group,
            trigger.Key.Name,
            context.JobDetail.Key.Group,
            context.JobDetail.Key.Name,
            SystemTime.UtcNow(),
            trigger.GetPreviousFireTimeUtc(),
            trigger.GetNextFireTimeUtc(),
            context.RefireCount);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// has misfired.
    /// <para>
    /// Consideration should be given to how much time is spent in this method,
    /// as it will affect all triggers that are misfiring. If you have lots
    /// of triggers misfiring at once, it could be an issue if this method
    /// does a lot.
    /// </para>
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> that has misfired.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public Task TriggerMisfired(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        if (!log.IsInfoEnabled())
        {
            return Task.CompletedTask;
        }

        log.InfoFormat(
            TriggerMisfiredMessage,
            trigger.Key.Group,
            trigger.Key.Name,
            trigger.JobKey.Group,
            trigger.JobKey.Name,
            SystemTime.UtcNow(),
            trigger.GetPreviousFireTimeUtc(),
            trigger.GetNextFireTimeUtc());

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// has fired, it's associated <see cref="IJobDetail" />
    /// has been executed, and it's <see cref="IOperableTrigger.Triggered" /> method has been
    /// called.
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> that was fired.</param>
    /// <param name="context">The <see cref="IJobExecutionContext" /> that was passed to the
    /// <see cref="IJob" />'s <see cref="IJob.Execute" /> method.</param>
    /// <param name="triggerInstructionCode">The result of the call on the <see cref="IOperableTrigger" />'s <see cref="IOperableTrigger.Triggered" />  method.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public Task TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        if (!log.IsInfoEnabled())
        {
            return Task.CompletedTask;
        }

        log.InfoFormat(
            TriggerCompleteMessage,
            trigger.Key.Group,
            trigger.Key.Name,
            context.JobDetail.Key.Group,
            context.JobDetail.Key.Name,
            SystemTime.UtcNow(),
            trigger.GetPreviousFireTimeUtc(),
            trigger.GetNextFireTimeUtc(),
            triggerInstructionCode);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// has fired, and it's associated <see cref="IJobDetail" />
    /// is about to be executed.
    /// <para>
    /// It is called after the <see cref="TriggerFired" /> method of this
    /// interface.
    /// </para>
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> that has fired.</param>
    /// <param name="context">The <see cref="IJobExecutionContext" /> that will be passed to
    /// the <see cref="IJob" />'s <see cref="IJob.Execute" /> method.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public Task<bool> VetoJobExecution(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
