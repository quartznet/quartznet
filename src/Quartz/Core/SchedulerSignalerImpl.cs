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
using Quartz.Extensibility;

namespace Quartz.Core;

/// <summary>
/// An interface to be used by <see cref="IJobStore" /> instances in order to
/// communicate signals back to the <see cref="QuartzScheduler" />.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class SchedulerSignalerImpl : ISchedulerSignaler
{
    private readonly ILogger<SchedulerSignalerImpl> logger = LogProvider.CreateLogger<SchedulerSignalerImpl>();
    private readonly QuartzScheduler scheduler;
    private readonly QuartzSchedulerThread schedThread;

    public SchedulerSignalerImpl(QuartzScheduler scheduler, QuartzSchedulerThread schedThread)
    {
        this.scheduler = scheduler;
        this.schedThread = schedThread;

        logger.LogInformation("Initialized Scheduler Signaller of type: {Type}", GetType());
    }


    /// <summary>
    /// Notifies the scheduler about misfired trigger.
    /// </summary>
    /// <param name="trigger">The trigger that misfired.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask NotifyTriggerListenersMisfired(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await scheduler.NotifyTriggerListenersMisfired(trigger, cancellationToken).ConfigureAwait(false);
        }
        catch (SchedulerException se)
        {
            logger.LogError(se, "Error notifying listeners of trigger misfire.");

            // The trigger travels with the failure: a listener that throws on one trigger's misfire
            // should not leave the report saying only that some misfire notification failed.
            SchedulerErrorContext error = new()
            {
                Message = "Error notifying listeners of trigger misfire.",
                Exception = se,
                TriggerKey = trigger.Key,
                JobKey = trigger.JobKey,
            };
            await scheduler.NotifySchedulerListenersError(error, cancellationToken).ConfigureAwait(false);
        }
    }


    /// <summary>
    /// Notifies the scheduler about finalized trigger.
    /// </summary>
    /// <param name="trigger">The trigger that has finalized.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifySchedulerListenersFinalized(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return scheduler.NotifySchedulerListenersFinalized(trigger, cancellationToken);
    }

    /// <summary>
    /// Signals the scheduling change.
    /// </summary>
    public ValueTask SignalSchedulingChange(
        DateTimeOffset? candidateNewNextFireTime,
        CancellationToken cancellationToken = default)
    {
        schedThread.SignalSchedulingChange(candidateNewNextFireTime);
        return default;
    }

    public ValueTask NotifySchedulerListenersJobDeleted(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.NotifySchedulerListenersJobDeleted(jobKey, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersTriggerInError(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.NotifySchedulerListenersTriggerInError(triggerKey, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersTriggersInError(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.NotifySchedulerListenersTriggersInError(jobKey, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersError(
        SchedulerErrorContext error,
        CancellationToken cancellationToken = default)
    {
        return scheduler.NotifySchedulerListenersError(error, cancellationToken);
    }
}