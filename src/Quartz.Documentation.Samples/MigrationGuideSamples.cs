using System.Data.Common;

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

    /// <summary>
    /// Lists the stored cron expressions that a crontab-derived dialect would read differently.
    /// </summary>
    /// <remarks>
    /// C# rather than SQL because what it does is split a field and look at the characters in it, and
    /// six database dialects spell that six ways. This one runs anywhere the application's own
    /// connection does.
    /// </remarks>
    public static async Task AuditCronDialect(DbConnection connection)
    {
        #region sample_cron_dialect_audit

        // Both shapes below are valid Quartz expressions, so nothing rejects them and nothing logs.
        // What this lists is the expressions worth reading again if they were carried over from a
        // crontab-derived library such as Cronos or NCrontab.
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_NAME, TRIGGER_GROUP, CRON_EXPRESSION FROM QRTZ_CRON_TRIGGERS";

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string name = reader.GetString(0);
            string group = reader.GetString(1);
            string expression = reader.GetString(2);

            // A stored expression is always the canonical six-field Quartz form: seconds, minutes,
            // hours, day-of-month, month, day-of-week, and optionally a year.
            string[] fields = expression.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 6)
            {
                continue;
            }

            string dayOfMonth = fields[3];
            string dayOfWeek = fields[5];

            // Quartz numbers the days 1-7 from Sunday; the other dialects number them 0-6 (Unix: 0-7),
            // also from Sunday. So a day written as a number names a different day in each, in every
            // form that carries one: '1', '1-5', '*/2', and the '6#3' and '6L' forms with it.
            if (IsNumericDay(dayOfWeek))
            {
                Console.WriteLine($"{group}.{name}: numeric day-of-week '{dayOfWeek}' — write it as a name");
            }

            // Quartz fires on the union of the two day fields, as crontab does; Cronos intersects them.
            if (!IsUnrestricted(dayOfMonth) && !IsUnrestricted(dayOfWeek))
            {
                Console.WriteLine($"{group}.{name}: both day fields restricted — this fires on their union");
            }
        }

        // A day named by number rather than by name, whatever decorates it. A letter anywhere says the
        // days were written as names and there is nothing to renumber — except 'L', which is a
        // position rather than a name and shifts with the digit in front of it.
        static bool IsNumericDay(string field)
            => field.Any(char.IsDigit) && !field.Any(c => char.IsLetter(c) && c != 'L');

        // '*' and '?' both mean "this field restricts nothing"; either one leaves the other in charge.
        static bool IsUnrestricted(string field) => field is "*" or "?";

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
