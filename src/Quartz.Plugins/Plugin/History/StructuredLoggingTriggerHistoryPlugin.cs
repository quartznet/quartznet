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

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Impl.Matchers;
using Quartz.Spi;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

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
    private readonly ILogger<StructuredLoggingTriggerHistoryPlugin> logger;
    private readonly TimeProvider timeProvider;

    public StructuredLoggingTriggerHistoryPlugin()
        : this(LogProvider.CreateLogger<StructuredLoggingTriggerHistoryPlugin>(), TimeProvider.System)
    {
    }

    public StructuredLoggingTriggerHistoryPlugin(
        ILogger<StructuredLoggingTriggerHistoryPlugin> logger,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

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
    public ValueTask Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        scheduler.ListenerManager.AddTriggerListener(this, EverythingMatcher<TriggerKey>.AllTriggers());
        return default;
    }

    /// <summary>
    /// Called when the associated <see cref="IScheduler" /> is started, in order
    /// to let the plug-in know it can now make calls into the scheduler if it
    /// needs to.
    /// </summary>
    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <summary>
    /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
    /// should free up all of it's resources because the scheduler is shutting
    /// down.
    /// </summary>
    public ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <inheritdoc />
    public ValueTask TriggerFired(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return default;
        }

#pragma warning disable CA2254
        logger.LogInformation(
            TriggerFiredMessage,
            trigger.Key.Group,
            trigger.Key.Name,
            context.JobDetail.Key.Group,
            context.JobDetail.Key.Name,
            timeProvider.GetUtcNow(),
            trigger.GetPreviousFireTimeUtc(),
            trigger.GetNextFireTimeUtc(),
            context.RefireCount);
#pragma warning restore CA2254

        return default;
    }

    /// <inheritdoc />
    public ValueTask TriggerMisfired(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return default;
        }

#pragma warning disable CA2254
        logger.LogInformation(
            TriggerMisfiredMessage,
            trigger.Key.Group,
            trigger.Key.Name,
            trigger.JobKey.Group,
            trigger.JobKey.Name,
            timeProvider.GetUtcNow(),
            trigger.GetPreviousFireTimeUtc(),
            trigger.GetNextFireTimeUtc());
#pragma warning restore CA2254

        return default;
    }

    /// <inheritdoc />
    public ValueTask TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return default;
        }

#pragma warning disable CA2254
        logger.LogInformation(
            TriggerCompleteMessage,
            trigger.Key.Group,
            trigger.Key.Name,
            context.JobDetail.Key.Group,
            context.JobDetail.Key.Name,
            timeProvider.GetUtcNow(),
            trigger.GetPreviousFireTimeUtc(),
            trigger.GetNextFireTimeUtc(),
            triggerInstructionCode);
#pragma warning restore CA2254

        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> VetoJobExecution(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<bool>(false);
    }
}
