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
using Quartz.HttpApiContract;

namespace Quartz.Impl;

/// <summary>
/// Publishes what one scheduler does into the process's <see cref="SchedulerEventBroker" />, which is
/// what makes a live view of it possible.
/// </summary>
/// <remarks>
/// <para>
/// All three listener kinds at once, because a live view draws all three: a job starting and finishing,
/// a trigger firing, completing and misfiring, and the scheduler's own lifecycle. Registered against
/// every scheduler in the container by <c>AddQuartzSchedulerEvents()</c> and told its own scheduler's
/// name when it is initialized, which is the name every event it publishes is keyed by.
/// </para>
/// <para>
/// It is the one publisher. Two of them on one scheduler would put every event on the stream twice, which
/// is why the registration is idempotent.
/// </para>
/// </remarks>
internal sealed class SchedulerEventPlugin : ISchedulerPlugin, IJobListener, ITriggerListener, ISchedulerListener
{
    /// <summary>
    /// The name <c>AddQuartzSchedulerEvents()</c> registers this under.
    /// </summary>
    internal const string PluginName = "quartzSchedulerEvents";

    private readonly SchedulerEventBroker broker;
    private readonly TimeProvider timeProvider;

    private string schedulerName = "";
    private string schedulerInstanceId = "";

    /// <summary>
    /// Takes the broker to publish into and the clock the events are stamped with.
    /// </summary>
    /// <remarks>
    /// The clock is this scheduler's: a named scheduler is built through a provider that resolves its own
    /// parts, so a scheduler given a <see cref="System.TimeProvider" /> of its own stamps its events with
    /// it. The broker is the container's — one process, one set of streams.
    /// </remarks>
    public SchedulerEventPlugin(SchedulerEventBroker broker, TimeProvider timeProvider)
    {
        this.broker = broker;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name { get; private set; } = PluginName;

    /// <summary>
    /// Registers this as all three kinds of listener, and reads the scheduler's identity once.
    /// </summary>
    /// <remarks>
    /// Once, because it cannot change and because reading it per event would read a property. The
    /// scheduler is one this process runs — a plugin is only ever initialized by the scheduler it belongs
    /// to — so both properties are fields behind the facade rather than anything that could block.
    /// </remarks>
    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        Name = pluginName;
        schedulerName = scheduler.SchedulerName;
        schedulerInstanceId = scheduler.SchedulerInstanceId;

        scheduler.ListenerManager.AddJobListener(this, Matchers.AllJobs());
        scheduler.ListenerManager.AddTriggerListener(this, Matchers.AllTriggers());
        scheduler.ListenerManager.AddSchedulerListener(this);

        return default;
    }

    /// <inheritdoc />
    public ValueTask Start(CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;

    // ---------------------------------------------------------------------------------------------
    // IJobListener

    /// <inheritdoc />
    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(new SchedulerEvent
        {
            Kind = SchedulerEventKind.JobExecuting,
            SchedulerName = schedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            JobKey = KeyDto.Create(context.JobDetail.Key),
            TriggerKey = KeyDto.Create(context.Trigger.Key),
            FireTimeUtc = context.FireTimeUtc,
            FireInstanceId = context.FireInstanceId
        });

        return default;
    }

    /// <summary>
    /// A vetoed execution is reported as one that finished, because that is what a reader watching the
    /// job has to be told: it will hear no completion otherwise.
    /// </summary>
    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return PublishExecuted(context, vetoed: true, jobException: null);
    }

    /// <inheritdoc />
    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        return PublishExecuted(context, vetoed: false, jobException);
    }

    // ---------------------------------------------------------------------------------------------
    // ITriggerListener

    /// <inheritdoc />
    public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(new SchedulerEvent
        {
            Kind = SchedulerEventKind.TriggerFired,
            SchedulerName = schedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            TriggerKey = KeyDto.Create(trigger.Key),
            JobKey = KeyDto.Create(context.JobDetail.Key),
            FireTimeUtc = context.FireTimeUtc,
            FireInstanceId = context.FireInstanceId
        });

        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return new ValueTask<bool>(false);
    }

    /// <summary>
    /// A missed firing, which carries no fire time: there was no firing to time.
    /// </summary>
    public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(new SchedulerEvent
        {
            Kind = SchedulerEventKind.TriggerMisfired,
            SchedulerName = schedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            TriggerKey = KeyDto.Create(trigger.Key),
            JobKey = trigger.JobKey is null ? null : KeyDto.Create(trigger.JobKey)
        });

        return default;
    }

    /// <inheritdoc />
    public ValueTask TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(new SchedulerEvent
        {
            Kind = SchedulerEventKind.TriggerCompleted,
            SchedulerName = schedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            TriggerKey = KeyDto.Create(trigger.Key),
            JobKey = KeyDto.Create(context.JobDetail.Key),
            FireTimeUtc = context.FireTimeUtc,
            FireInstanceId = context.FireInstanceId
        });

        return default;
    }

    // ---------------------------------------------------------------------------------------------
    // ISchedulerListener

    /// <inheritdoc />
    public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return PublishTrigger(SchedulerEventKind.TriggerPaused, triggerKey);
    }

    /// <inheritdoc />
    public ValueTask TriggerResumed(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return PublishTrigger(SchedulerEventKind.TriggerResumed, triggerKey);
    }

    /// <inheritdoc />
    public ValueTask JobPaused(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return PublishJob(SchedulerEventKind.JobPaused, jobKey);
    }

    /// <inheritdoc />
    public ValueTask JobResumed(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return PublishJob(SchedulerEventKind.JobResumed, jobKey);
    }

    /// <summary>
    /// One firing that was interrupted, named by its fire instance id.
    /// </summary>
    /// <remarks>
    /// The overload that says <em>which</em> firing, because a job without
    /// <see cref="DisallowConcurrentExecutionAttribute" /> can have several in flight and a reader told
    /// only the key cannot say which execution it just saw cancelled. The key-only overload is left at its
    /// default, so nothing is published twice.
    /// </remarks>
    public ValueTask JobInterrupted(
        IScheduler scheduler,
        JobKey jobKey,
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(new SchedulerEvent
        {
            Kind = SchedulerEventKind.JobInterrupted,
            SchedulerName = schedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            JobKey = KeyDto.Create(jobKey),
            FireInstanceId = fireInstanceId
        });

        return default;
    }

    /// <inheritdoc />
    public ValueTask TriggerInError(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return PublishTrigger(SchedulerEventKind.TriggerInError, triggerKey);
    }

    /// <inheritdoc />
    public ValueTask SchedulerError(
        IScheduler scheduler,
        SchedulerErrorContext errorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(errorContext);

        if (!Watched())
        {
            return default;
        }

        Publish(new SchedulerEvent
        {
            Kind = SchedulerEventKind.SchedulerError,
            SchedulerName = schedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Message = errorContext.Message,
            Cause = errorContext.Exception.Message,
            TriggerKey = errorContext.TriggerKey is null ? null : KeyDto.Create(errorContext.TriggerKey),
            JobKey = errorContext.JobKey is null ? null : KeyDto.Create(errorContext.JobKey),
            FireInstanceId = errorContext.FireInstanceId
        });

        return default;
    }

    /// <inheritdoc />
    public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return PublishState(SchedulerStatus.Running);
    }

    /// <inheritdoc />
    public ValueTask SchedulerInStandbyMode(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return PublishState(SchedulerStatus.Standby);
    }

    /// <inheritdoc />
    public ValueTask SchedulerShuttingDown(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return PublishState(SchedulerStatus.ShuttingDown);
    }

    /// <inheritdoc />
    public ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return PublishState(SchedulerStatus.Shutdown);
    }

    /// <summary>
    /// Nothing is published: a scheduler that is starting is an event, not a state it is in.
    /// </summary>
    /// <remarks>
    /// The state it will be in arrives a moment later as <see cref="SchedulerStarted" />, and publishing
    /// "starting" first only gives a reader a value that is not a <see cref="SchedulerStatus" /> to
    /// render in the meantime. It is stated rather than left to the default interface member because it
    /// is a decision.
    /// </remarks>
    public ValueTask SchedulerStarting(IScheduler scheduler, CancellationToken cancellationToken = default) => default;

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether anything is watching this scheduler. Asked before an event is built, because a process
    /// that serves the event route with nobody connected should pay nothing for it.
    /// </summary>
    private bool Watched() => broker.HasSubscribers(schedulerName);

    private void Publish(SchedulerEvent schedulerEvent) => broker.Publish(schedulerEvent);

    private ValueTask PublishExecuted(IJobExecutionContext context, bool vetoed, JobExecutionException? jobException)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(new SchedulerEvent
        {
            Kind = SchedulerEventKind.JobExecuted,
            SchedulerName = schedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            JobKey = KeyDto.Create(context.JobDetail.Key),
            TriggerKey = KeyDto.Create(context.Trigger.Key),
            FireTimeUtc = context.FireTimeUtc,
            FireInstanceId = context.FireInstanceId,
            RunTime = context.JobRunTime,
            Vetoed = vetoed,
            ExceptionMessage = jobException?.Message
        });

        return default;
    }

    private ValueTask PublishTrigger(SchedulerEventKind kind, TriggerKey triggerKey)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(Blank(kind) with { TriggerKey = KeyDto.Create(triggerKey) });
        return default;
    }

    private ValueTask PublishJob(SchedulerEventKind kind, JobKey jobKey)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(Blank(kind) with { JobKey = KeyDto.Create(jobKey) });
        return default;
    }

    private ValueTask PublishState(SchedulerStatus status)
    {
        if (!Watched())
        {
            return default;
        }

        Publish(Blank(SchedulerEventKind.SchedulerStateChanged) with { Status = status });
        return default;
    }

    /// <summary>
    /// An event with the four things every event carries and nothing else.
    /// </summary>
    private SchedulerEvent Blank(SchedulerEventKind kind) => new()
    {
        Kind = kind,
        SchedulerName = schedulerName,
        SchedulerInstanceId = schedulerInstanceId,
        OccurredAtUtc = timeProvider.GetUtcNow()
    };
}
