using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Quartz;

/// <summary>
/// Registers the Quartz scheduler health check.
/// </summary>
/// <remarks>
/// The check reads <see cref="IScheduler.Status" /> and probes the job store, and needs nothing from
/// ASP.NET Core to do either — so it ships in <c>Quartz</c> rather than in <c>Quartz.AspNetCore</c>, and
/// a worker on a <c>dotnet/runtime</c> image can register it. Serving the report over HTTP is
/// <c>MapHealthChecks</c>, which is still ASP.NET Core's.
/// </remarks>
public static class QuartzHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check for the default Quartz scheduler: healthy while it is running and can
    /// reach its store, degraded while it is in standby, and unhealthy otherwise.
    /// </summary>
    /// <remarks>
    /// Shorthand for <c>services.AddHealthChecks().AddQuartz(configure)</c>, for an application that has
    /// no other health checks to compose with.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional configuration for the health check registration, allowing the name, tags and failure
    /// status to be customized (for example to attach liveness/readiness probe tags).
    /// </param>
    public static IServiceCollection AddQuartzHealthChecks(
        this IServiceCollection services,
        Action<QuartzHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHealthChecks().AddQuartz(configure);
        return services;
    }

    /// <summary>
    /// Registers a health check for the scheduler this builder configures.
    /// </summary>
    /// <remarks>
    /// A named scheduler has its own health check, checking its own scheduler rather than the default
    /// one, and defaulting to a name of <c>quartz-scheduler-&lt;scheduler name&gt;</c> so several of
    /// them can be registered side by side.
    /// </remarks>
    /// <param name="builder">The scheduler's builder.</param>
    /// <param name="configure">
    /// Optional configuration for the health check registration, allowing the name, tags and failure
    /// status to be customized (for example to attach liveness/readiness probe tags).
    /// </param>
    public static IQuartzBuilder AddQuartzHealthChecks(
        this IQuartzBuilder builder,
        Action<QuartzHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IHealthChecksBuilder checks = builder.Services.AddHealthChecks();
        if (builder.SchedulerName.Length == 0)
        {
            checks.AddQuartz(configure);
        }
        else
        {
            checks.AddQuartz(builder.SchedulerName, configure);
        }

        return builder;
    }

    /// <summary>
    /// Adds a health check for the default Quartz scheduler, alongside an application's other checks.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="configure">Configures the check's name, tags and failure status.</param>
    public static IHealthChecksBuilder AddQuartz(
        this IHealthChecksBuilder builder,
        Action<QuartzHealthCheckOptions>? configure = null)
    {
        return AddQuartzCheck(builder, schedulerName: null, configure);
    }

    /// <summary>
    /// Adds a health check for one named Quartz scheduler.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="schedulerName">The name the scheduler was registered under with <c>AddQuartz(name, …)</c>.</param>
    /// <param name="configure">Configures the check's name, tags and failure status.</param>
    public static IHealthChecksBuilder AddQuartz(
        this IHealthChecksBuilder builder,
        string schedulerName,
        Action<QuartzHealthCheckOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        return AddQuartzCheck(builder, schedulerName, configure);
    }

    /// <summary>
    /// Registers the check, reading its settings from the options pipeline rather than from a callback
    /// applied on the spot.
    /// </summary>
    /// <remarks>
    /// The registration is built when the health check service is, by which time every source of
    /// <see cref="QuartzHealthCheckOptions"/> has had its say — so
    /// <c>services.Configure&lt;QuartzHealthCheckOptions&gt;(…)</c> and a configuration section bound to
    /// the type mean something, which they did not while the options object was constructed and read
    /// inside this method. A scheduler's check reads the options registered under that scheduler's name,
    /// like every other per-scheduler setting.
    /// </remarks>
    private static IHealthChecksBuilder AddQuartzCheck(
        IHealthChecksBuilder builder,
        string? schedulerName,
        Action<QuartzHealthCheckOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string optionsName = schedulerName ?? Options.DefaultName;

        if (configure is not null)
        {
            builder.Services.Configure(optionsName, configure);
        }

        builder.Services
            .AddOptions<HealthCheckServiceOptions>()
            .Configure<IOptionsMonitor<QuartzHealthCheckOptions>>((healthChecks, quartz) =>
            {
                QuartzHealthCheckOptions options = quartz.Get(optionsName);

                healthChecks.Registrations.Add(new HealthCheckRegistration(
                    options.Name ?? DefaultCheckName(schedulerName),
                    serviceProvider => ActivatorUtilities.CreateInstance<QuartzHealthCheck>(
                        serviceProvider,
                        new SchedulerHealthCheckTarget(schedulerName)),
                    options.FailureStatus,
                    options.Tags));
            });

        return builder;
    }

    /// <summary>
    /// What a scheduler's check is called when nothing named it: distinct per scheduler, so several can
    /// be registered side by side.
    /// </summary>
    private static string DefaultCheckName(string? schedulerName)
    {
        return schedulerName is null ? "quartz-scheduler" : $"quartz-scheduler-{schedulerName}";
    }
}
