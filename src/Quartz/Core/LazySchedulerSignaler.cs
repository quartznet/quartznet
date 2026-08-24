using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Core;

/// <summary>
/// Breaks the cycle between a job store and the scheduler that signals it.
/// </summary>
/// <remarks>
/// <para>
/// A job store needs an <see cref="ISchedulerSignaler"/>, and the signaler belongs to the scheduler,
/// which is built from the job store. That is a genuine cycle in the object graph, and it is the reason
/// job stores were historically handed their signaler by a late <c>Initialize</c> call rather than
/// through their constructor.
/// </para>
/// <para>
/// Nothing signals during construction, so the indirection only has to be deferred, not broken: the
/// scheduler is resolved the first time a signal is actually sent, by which point it exists. This
/// keeps <see cref="IServiceProvider"/> confined to one adapter instead of being threaded through
/// every job store as a service locator.
/// </para>
/// </remarks>
internal sealed class LazySchedulerSignaler : ISchedulerSignaler
{
    private readonly Lazy<ISchedulerSignaler> signaler;

    public LazySchedulerSignaler(IServiceProvider provider, SchedulerKey schedulerKey)
    {
        signaler = new Lazy<ISchedulerSignaler>(
            () => provider.GetScheduler<QuartzScheduler>(schedulerKey.Key).SchedulerSignaler,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return signaler.Value.NotifyTriggerListenersMisfired(trigger, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return signaler.Value.NotifySchedulerListenersFinalized(trigger, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return signaler.Value.NotifySchedulerListenersJobDeleted(jobKey, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersTriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return signaler.Value.NotifySchedulerListenersTriggerInError(triggerKey, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersTriggersInError(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return signaler.Value.NotifySchedulerListenersTriggersInError(jobKey, cancellationToken);
    }

    public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default)
    {
        return signaler.Value.SignalSchedulingChange(candidateNewNextFireTimeUtc, cancellationToken);
    }

    public ValueTask NotifySchedulerListenersError(SchedulerErrorContext error, CancellationToken cancellationToken = default)
    {
        return signaler.Value.NotifySchedulerListenersError(error, cancellationToken);
    }
}
