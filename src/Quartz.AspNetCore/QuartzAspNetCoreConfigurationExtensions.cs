using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HttpApi;
using Quartz.AspNetCore.HttpApi.Endpoints;
using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Serialization.Json;

namespace Quartz;

public static class QuartzAspNetCoreConfigurationExtensions
{
    /// <summary>
    /// Registers a health check for the default Quartz scheduler that reports unhealthy when the
    /// scheduler is not running or cannot reach its store.
    /// </summary>
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
        return AddCheck(services, schedulerName: null, configure);
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

        AddCheck(
            builder.Services,
            builder.SchedulerName.Length == 0 ? null : builder.SchedulerName,
            configure);

        return builder;
    }

    private static IServiceCollection AddCheck(
        IServiceCollection services,
        string? schedulerName,
        Action<QuartzHealthCheckOptions>? configure)
    {
        var options = new QuartzHealthCheckOptions
        {
            Name = schedulerName is null ? "quartz-scheduler" : $"quartz-scheduler-{schedulerName}",
        };

        configure?.Invoke(options);

        services
            .AddHealthChecks()
            .AddTypeActivatedCheck<QuartzHealthCheck>(
                options.Name,
                failureStatus: options.FailureStatus,
                tags: options.Tags,
                args: [new SchedulerHealthCheckTarget(schedulerName)]);

        return services;
    }

    /// <summary>
    /// Serves the schedulers in this container over HTTP, so a remote client can drive them.
    /// </summary>
    /// <remarks>
    /// Called on one scheduler's builder, but the API it configures serves every scheduler in the
    /// container — a request names the scheduler it is for. Call
    /// <c>MapQuartzHttpApi()</c> on the application to map the endpoints.
    /// </remarks>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Configures the API, including the path it is served under.</param>
    public static IQuartzBuilder AddQuartzHttpApi(
        this IQuartzBuilder builder,
        Action<QuartzHttpApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services
            .AddOptions<QuartzHttpApiOptions>()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiPath) && options.ApiPath.StartsWith('/'), "ApiPath is required and must start with '/'");

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        builder.Services.TryAddSingleton<ExceptionHandler>();
        builder.Services.TryAddSingleton<EndpointHelper>();

        // The HTTP API serves every scheduler in the container through one set of endpoints, so it cannot
        // read any single scheduler's serializers. It reads the container's registry instead: register a
        // custom trigger or calendar serializer there to have the API understand it.
        builder.Services.TryAddSingleton<SystemTextJsonSerializerRegistry>();

        // Add json converters into ASP.NET Core's default json options
        builder.Services
            .AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>()
            .Configure<SystemTextJsonSerializerRegistry>(AddJsonConverters);

        return builder;

        static void AddJsonConverters(Microsoft.AspNetCore.Http.Json.JsonOptions options, SystemTextJsonSerializerRegistry registry)
        {
            options.SerializerOptions?.AddQuartzConverters(registry, newtonsoftCompatibilityMode: false);
        }
    }

    /// <summary>
    /// Maps the Quartz HTTP API endpoints configured by <see cref="AddQuartzHttpApi" />.
    /// </summary>
    public static IEndpointConventionBuilder MapQuartzHttpApi(this IEndpointRouteBuilder builder)
    {
        var handler = builder.ServiceProvider.GetService<ExceptionHandler>();
        if (handler is null)
        {
            throw new InvalidOperationException("HTTP API not configured. Call AddQuartzHttpApi() in AddQuartz(...)");
        }

        var options = builder.ServiceProvider.GetRequiredService<IOptions<QuartzHttpApiOptions>>().Value;

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
            .Union(triggerEndpoints);

        return new QuartzApiConventionBuilder(allEndpoints);
    }
}