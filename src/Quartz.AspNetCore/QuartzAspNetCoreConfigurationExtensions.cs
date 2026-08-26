using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HttpApi;
using Quartz.AspNetCore.HttpApi.Endpoints;
using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Serialization.SystemTextJson;

namespace Quartz;

public static class QuartzAspNetCoreConfigurationExtensions
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

        var checks = builder.Services.AddHealthChecks();
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

        var optionsName = schedulerName ?? Options.DefaultName;

        if (configure is not null)
        {
            builder.Services.Configure(optionsName, configure);
        }

        builder.Services
            .AddOptions<HealthCheckServiceOptions>()
            .Configure<IOptionsMonitor<QuartzHealthCheckOptions>>((healthChecks, quartz) =>
            {
                var options = quartz.Get(optionsName);

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

    /// <summary>
    /// Serves the schedulers in this container over HTTP, so a remote client can drive them.
    /// </summary>
    /// <remarks>
    /// The API is container-wide rather than a scheduler's — a request names the scheduler it is for — so
    /// this is where it belongs, and there is deliberately no <see cref="IQuartzBuilder"/> form: written
    /// inside <c>AddQuartz(name, …)</c> it would look like that scheduler's API while configuring
    /// everybody's, and two of them with different <see cref="QuartzHttpApiOptions.ApiPath"/>s would be
    /// last-writer-wins. Call <c>MapQuartzHttpApi()</c> on the application to map the endpoints.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the API, including the path it is served under.</param>
    public static IServiceCollection AddQuartzHttpApi(
        this IServiceCollection services,
        Action<QuartzHttpApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<QuartzHttpApiOptions>, QuartzHttpApiOptionsValidator>());

        var optionsBuilder = services
            .AddOptions<QuartzHttpApiOptions>()
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<ExceptionHandler>();
        services.TryAddSingleton<EndpointHelper>();

        // The HTTP API serves every scheduler in the container through one set of endpoints, so it cannot
        // read any single scheduler's serializers. It reads the container's registry instead: register a
        // custom trigger or calendar serializer there to have the API understand it.
        services.TryAddSingleton<SystemTextJsonSerializerRegistry>();

        // Add json converters into ASP.NET Core's default json options. Those options belong to the whole
        // container, not to one scheduler, so the setup is registered by type: calling AddQuartzHttpApi
        // for a second scheduler must not stack the same converters onto them twice.
        services.AddOptions();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>, QuartzJsonOptionsSetup>());

        return services;
    }

    /// <summary>
    /// Maps the Quartz HTTP API endpoints under the path
    /// <see cref="AddQuartzHttpApi(IServiceCollection, Action{QuartzHttpApiOptions})" /> configured,
    /// which defaults to <c>/quartz-api</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapQuartzHttpApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return MapApiEndpoints(builder, pattern: null);
    }

    /// <summary>
    /// Maps the Quartz HTTP API endpoints under the given route pattern.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Naming the path where the endpoints are mapped is how the rest of ASP.NET Core reads —
    /// <c>MapHealthChecks("/health")</c> — and it puts the route beside an application's other routes
    /// rather than in a registration callback somewhere else.
    /// </para>
    /// <para>
    /// The pattern given here wins over <see cref="QuartzHttpApiOptions.ApiPath" />, whichever way that
    /// was set. It has to start with <c>/</c>, the same rule the option is validated against.
    /// </para>
    /// </remarks>
    /// <param name="builder">The endpoint route builder.</param>
    /// <param name="pattern">The path the API is served under, for example <c>/quartz-api</c>.</param>
    public static IEndpointConventionBuilder MapQuartzHttpApi(this IEndpointRouteBuilder builder, string pattern)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!QuartzHttpApiOptions.IsRoutableApiPath(pattern))
        {
            throw new ArgumentException($"The route pattern is required and must start with '/', was '{pattern}'.", nameof(pattern));
        }

        return MapApiEndpoints(builder, pattern);
    }

    private static QuartzApiConventionBuilder MapApiEndpoints(IEndpointRouteBuilder builder, string? pattern)
    {
        var handler = builder.ServiceProvider.GetService<ExceptionHandler>();
        if (handler is null)
        {
            throw new InvalidOperationException("HTTP API not configured. Call services.AddQuartzHttpApi() first.");
        }

        var options = builder.ServiceProvider.GetRequiredService<IOptions<QuartzHttpApiOptions>>().Value;
        if (pattern is not null)
        {
            // Written onto the resolved options rather than kept locally, so that anything reading them
            // afterwards is told where the API actually is rather than where it was once asked to be.
            options.ApiPath = pattern;
        }

        var calendarEndpoints = CalendarEndpoints.MapEndpoints(builder, options).ToArray();
        foreach (var endpoint in calendarEndpoints)
        {
            endpoint.WithTags("Calendar");
        }

        var jobEndpoints = JobEndpoints.MapEndpoints(builder, options).ToArray();
        foreach (var endpoint in jobEndpoints)
        {
            endpoint.WithTags("Job");
        }

        var schedulerEndpoints = SchedulerEndpoints.MapEndpoints(builder, options).ToArray();
        foreach (var endpoint in schedulerEndpoints)
        {
            endpoint.WithTags("Scheduler");
        }

        var triggerEndpoints = TriggerEndpoints.MapEndpoints(builder, options).ToArray();
        foreach (var endpoint in triggerEndpoints)
        {
            endpoint.WithTags("Trigger");
        }

        var allEndpoints = calendarEndpoints
            .Union(jobEndpoints)
            .Union(schedulerEndpoints)
            .Union(triggerEndpoints)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(options.SchedulerAuthorizationPolicy))
        {
            // Applied after the endpoints are built, so that every route added by any of the four groups
            // is covered by the one rule: a route that carries {schedulerName} is authorized against that
            // scheduler. The convention reads the pattern, so nothing here has to list which routes those
            // are. The scheduler listing carries no such parameter and filters its own answer instead.
            foreach (RouteHandlerBuilder endpoint in allEndpoints)
            {
                endpoint.RequireSchedulerAuthorization(options.SchedulerAuthorizationPolicy);
            }
        }

        return new QuartzApiConventionBuilder(allEndpoints);
    }
}