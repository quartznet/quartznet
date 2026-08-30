using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Documentation.Samples;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/migration-guide.md.
/// </summary>
/// <remarks>
/// The guide is otherwise written in plain fences, because most of what it shows is 3.x code or a diff
/// between the two — neither of which can compile here. The blocks in this file are the 4.x half of an
/// answer, so they can, and therefore should.
/// </remarks>
public static class MigrationGuideSamples
{
    #region sample_migration_offset_time_provider

    /// <summary>
    /// A clock that runs at the system's speed, shifted by an offset that can be moved at will —
    /// forwards or backwards — without stopping.
    /// </summary>
    public sealed class OffsetTimeProvider(TimeProvider inner) : TimeProvider
    {
        private long offsetTicks;

        public TimeSpan Offset
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks));
            set => Interlocked.Exchange(ref offsetTicks, value.Ticks);
        }

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + Offset;

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override long GetTimestamp() => inner.GetTimestamp();

        public override long TimestampFrequency => inner.TimestampFrequency;

        // Left to the real clock deliberately: a timer that only fires when something advances the
        // offset would deadlock every wait inside the scheduler.
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => inner.CreateTimer(callback, state, dueTime, period);
    }

    #endregion

    public static void UsingTheOffsetClock(IServiceCollection services)
    {
        #region sample_migration_offset_time_provider_use

        OffsetTimeProvider clock = new(TimeProvider.System);
        services.AddQuartz(q => q.UseTimeProvider(clock));

        // ... and later, from the test or the diagnostic endpoint that owns it:
        clock.Offset = TimeSpan.FromHours(26);

        #endregion
    }

    #region sample_migration_late_bound_job_factory

    /// <summary>
    /// Holds something the container cannot supply yet, so that a component built at startup can be
    /// given the handle now and read the value later.
    /// </summary>
    public sealed class LateBound<T> where T : class
    {
        private T? value;

        public T Value => value ?? throw new InvalidOperationException($"{typeof(T).Name} is not available yet.");

        public void Set(T instance) => value = instance;
    }

    public sealed class BusAwareJobFactory(IServiceProvider provider, LateBound<IMessageBus> bus) : IJobFactory
    {
        public ValueTask<JobScope> CreateJob(
            TriggerFiredBundle bundle,
            IScheduler scheduler,
            CancellationToken cancellationToken = default)
        {
            IServiceScope scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();

            // Read per firing rather than captured per scheduler, which is what makes a dependency that
            // only exists once the bus has connected reachable from a factory built long before it.
            IJob job = (IJob) ActivatorUtilities.CreateInstance(
                scope.ServiceProvider,
                bundle.JobDetail.JobType.Type,
                bus.Value);

            return new ValueTask<JobScope>(new JobScope(job, scope));
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
        {
            (scope.State as IServiceScope)?.Dispose();
            return default;
        }
    }

    #endregion

    public static void UsingTheLateBoundFactory(IServiceCollection services)
    {
        #region sample_migration_late_bound_job_factory_use

        services.AddSingleton<LateBound<IMessageBus>>();
        services.AddQuartz(q => q.UseJobFactory<BusAwareJobFactory>());

        #endregion
    }
}

/// <summary>
/// Stands in for whatever a host's own scheduler-adjacent dependency is.
/// </summary>
public interface IMessageBus
{
    ValueTask Publish(object message, CancellationToken cancellationToken = default);
}
