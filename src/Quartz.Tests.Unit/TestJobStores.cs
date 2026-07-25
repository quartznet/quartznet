#nullable enable

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests;

/// <summary>
/// Builds job stores for tests that construct one directly rather than through a container.
/// </summary>
/// <remarks>
/// Job stores take their collaborators through their constructors, so tests that new one up have to
/// supply them. This keeps that noise in one place, and lets a test pass only the collaborator it
/// actually cares about — usually a signaler it wants to observe.
/// </remarks>
public static class TestJobStores
{
    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;

    public static IOptions<QuartzSchedulerOptions> SchedulerOptions(
        string instanceName = "TestScheduler",
        string instanceId = "TestInstance")
    {
        return Options.Create(new QuartzSchedulerOptions
        {
            InstanceName = instanceName,
            InstanceId = instanceId,
        });
    }

    public static ISchedulerSignaler Signaler() => new NoOpSchedulerSignaler();

    public static ITypeLoadHelper TypeLoader() => new SimpleTypeLoadHelper();

    public static RAMJobStore Ram(ISchedulerSignaler? signaler = null, TimeProvider? timeProvider = null)
    {
        return new RAMJobStore(
            Logger<RAMJobStore>(),
            signaler ?? new NoOpSchedulerSignaler(),
            timeProvider ?? TimeProvider.System);
    }

    public static JobStoreTX Tx(
        ISchedulerSignaler? signaler = null,
        ITypeLoadHelper? typeLoadHelper = null,
        TimeProvider? timeProvider = null,
        string instanceName = "TestScheduler",
        string instanceId = "TestInstance")
    {
        return new JobStoreTX(
            signaler ?? new NoOpSchedulerSignaler(),
            typeLoadHelper ?? new SimpleTypeLoadHelper(),
            timeProvider ?? TimeProvider.System,
            SchedulerOptions(instanceName, instanceId));
    }

    public static JobStoreCMT Cmt(
        ISchedulerSignaler? signaler = null,
        ITypeLoadHelper? typeLoadHelper = null,
        TimeProvider? timeProvider = null)
    {
        return new JobStoreCMT(
            signaler ?? new NoOpSchedulerSignaler(),
            typeLoadHelper ?? new SimpleTypeLoadHelper(),
            timeProvider ?? TimeProvider.System,
            SchedulerOptions());
    }
}

/// <summary>
/// A signaler that does nothing, for tests that do not care about scheduling signals.
/// </summary>
public sealed class NoOpSchedulerSignaler : ISchedulerSignaler
{
    public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

    public ValueTask NotifySchedulerListenersError(string message, SchedulerException jpe, CancellationToken cancellationToken = default) => default;
}
