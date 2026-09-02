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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Extensibility;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Plugins.History;

/// <summary>
/// Logs a history of all job executions (and execution vetoes) via structured logging.
/// </summary>
/// <remarks>
/// <para>
/// This is a structured logging alternative to <see cref="LoggingJobHistoryPlugin"/>.
/// Unlike <see cref="LoggingJobHistoryPlugin"/>, message templates use named parameters
/// (e.g. <c>{JobName}</c>, <c>{TriggerGroup}</c>) instead of index-based placeholders.
/// This makes log output compatible with structured logging sinks like Serilog and NLog,
/// and avoids template cache memory leaks.
/// </para>
/// <para>
/// Message templates can be customized via properties. The parameter names in the
/// templates must match the default names exactly (they are positionally mapped).
/// </para>
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
[SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification =
    "The template is the user's, configured at run time, and its named placeholders are what a structured sink "
    + "resolves into properties - which is the whole reason this plugin exists. A [LoggerMessage] template is "
    + "fixed at compile time, and rendering this one first would flatten exactly what is being preserved. Every "
    + "call is already behind an IsEnabled check, so the allocation CA1848 exists to avoid is already avoided. "
    + "LogCallSiteTest.Allowed records the same decision from the source side.")]
public sealed class StructuredLoggingJobHistoryPlugin : ISchedulerPlugin, IJobListener
{
    private readonly ILogger<StructuredLoggingJobHistoryPlugin> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Creates the plugin with the static logger and the system clock, for a plugin the loader built
    /// from a <c>quartz.plugin.&lt;name&gt;.type</c> key and so had nothing to inject into.
    /// </summary>
    public StructuredLoggingJobHistoryPlugin()
        : this(LogProvider.CreateLogger<StructuredLoggingJobHistoryPlugin>(), TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the plugin with the logger and clock a container resolved, which is what
    /// <c>UseStructuredJobLogging</c> uses.
    /// </summary>
    /// <param name="logger">Where the history events go.</param>
    /// <param name="timeProvider">The clock the events are stamped with.</param>
    public StructuredLoggingJobHistoryPlugin(
        ILogger<StructuredLoggingJobHistoryPlugin> logger,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets or sets the message that is logged when a Job is about to be executed.
    /// </summary>
    public string JobToBeFiredMessage { get; internal set; } = "Job {JobGroup}.{JobName} fired by trigger {TriggerGroup}.{TriggerName} at {FireTime} scheduled at {ScheduledFireTime} next fire at {NextFireTime} refire count {RefireCount}";

    /// <summary>
    /// Gets or sets the message that is logged when a Job successfully completes its execution.
    /// </summary>
    public string JobSuccessMessage { get; internal set; } = "Job {JobGroup}.{JobName} execution complete at {FireTime} triggered by {TriggerGroup}.{TriggerName} with result {Result}";

    /// <summary>
    /// Gets or sets the message that is logged when a Job fails its execution.
    /// </summary>
    public string JobFailedMessage { get; internal set; } = "Job {JobGroup}.{JobName} execution failed at {FireTime} triggered by {TriggerGroup}.{TriggerName} with exception message {ExceptionMessage}";

    /// <summary>
    /// Gets or sets the message that is logged when a Job execution is vetoed by a trigger listener.
    /// </summary>
    public string JobWasVetoedMessage { get; internal set; } = "Job {JobGroup}.{JobName} was vetoed. It was to be fired by trigger {TriggerGroup}.{TriggerName} at {FireTime}";

    /// <summary>
    /// Get the name of the <see cref="IJobListener" />.
    /// </summary>
    public string Name { get; private set; } = "Structured Logging Job History Plugin";

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
        scheduler.ListenerManager.AddJobListener(this, Matchers.AllJobs());
        return default;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" /> is
    /// about to be executed (an associated <see cref="ITrigger" /> has occurred).
    /// <para>
    /// This method will not be invoked if the execution of the Job was vetoed by a
    /// <see cref="ITriggerListener" />.
    /// </para>
    /// </summary>
    /// <seealso cref="JobExecutionVetoed"/>
    public ValueTask JobToBeExecuted(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return default;
        }

        ITrigger trigger = context.Trigger;

#pragma warning disable CA2254
        logger.LogInformation(
            JobToBeFiredMessage,
            context.JobDetail.Key.Group,
            context.JobDetail.Key.Name,
            trigger.Key.Group,
            trigger.Key.Name,
            timeProvider.GetUtcNow(),
            trigger.PreviousFireTimeUtc,
            trigger.NextFireTimeUtc,
            context.RefireCount);
#pragma warning restore CA2254

        return default;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> after a <see cref="IJobDetail" />
    /// has been executed, and before the associated <see cref="ITrigger" />'s
    /// <see cref="IOperableTrigger.Triggered" /> method has been called.
    /// </summary>
    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        ITrigger trigger = context.Trigger;

        if (jobException is not null)
        {
            if (!logger.IsEnabled(LogLevel.Warning))
            {
                return default;
            }

#pragma warning disable CA2254
            logger.LogWarning(
                jobException,
                JobFailedMessage,
                context.JobDetail.Key.Group,
                context.JobDetail.Key.Name,
                timeProvider.GetUtcNow(),
                trigger.Key.Group,
                trigger.Key.Name,
                jobException.Message);
#pragma warning restore CA2254
        }
        else
        {
            if (!logger.IsEnabled(LogLevel.Information))
            {
                return default;
            }

            string result = Convert.ToString(context.Result, CultureInfo.InvariantCulture) ?? "NULL";

#pragma warning disable CA2254
            logger.LogInformation(
                JobSuccessMessage,
                context.JobDetail.Key.Group,
                context.JobDetail.Key.Name,
                timeProvider.GetUtcNow(),
                trigger.Key.Group,
                trigger.Key.Name,
                result);
#pragma warning restore CA2254
        }

        return default;
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// was about to be executed (an associated <see cref="ITrigger" />
    /// has occurred), but a <see cref="ITriggerListener" /> vetoed it's
    /// execution.
    /// </summary>
    /// <seealso cref="JobToBeExecuted"/>
    public ValueTask JobExecutionVetoed(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return default;
        }

        ITrigger trigger = context.Trigger;

#pragma warning disable CA2254
        logger.LogInformation(
            JobWasVetoedMessage,
            context.JobDetail.Key.Group,
            context.JobDetail.Key.Name,
            trigger.Key.Group,
            trigger.Key.Name,
            timeProvider.GetUtcNow());
#pragma warning restore CA2254

        return default;
    }
}
