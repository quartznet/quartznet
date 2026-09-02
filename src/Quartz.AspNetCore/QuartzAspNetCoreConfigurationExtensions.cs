using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore;
using Quartz.AspNetCore.HttpApi;
using Quartz.AspNetCore.HttpApi.Endpoints;
using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Serialization.SystemTextJson;

namespace Quartz;

/// <summary>
/// Registers and maps the Quartz HTTP API — the server half of <c>Quartz.HttpClient</c>.
/// </summary>
public static class QuartzAspNetCoreConfigurationExtensions
{
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

        // Refuses to start an application whose mapped API nothing authorizes. Registered here rather
        // than at the map site, because a hosted service added to a built application is too late.
        services.TryAddSingleton<QuartzMappedEndpoints>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, QuartzEndpointAuthorizationGuard>());

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

        builder.ServiceProvider.GetRequiredService<QuartzMappedEndpoints>().Track(builder);

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

        bool authorizedPerScheduler = !string.IsNullOrWhiteSpace(options.SchedulerAuthorizationPolicy);
        if (authorizedPerScheduler)
        {
            // Applied after the endpoints are built, so that every route added by any of the four groups
            // is covered by the one rule: a route that carries {schedulerName} is authorized against that
            // scheduler. The convention reads the pattern, so nothing here has to list which routes those
            // are. The scheduler listing carries no such parameter and filters its own answer instead.
            foreach (RouteHandlerBuilder endpoint in allEndpoints)
            {
                endpoint.RequireSchedulerAuthorization(options.SchedulerAuthorizationPolicy!);
            }
        }

        QuartzApiConventionBuilder conventionBuilder = new(allEndpoints);

        // Says "Quartz mapped this" on every route, which is what QuartzEndpointAuthorizationGuard reads
        // at startup. Added through the returned builder rather than to each group, so a route added to
        // any of the four later carries it by being one of them.
        QuartzEndpointMarker marker = new(HttpApiSurface, HttpApiRemedies, authorizedPerScheduler);
        conventionBuilder.Add(endpointBuilder => endpointBuilder.Metadata.Add(marker));

        return conventionBuilder;
    }

    private const string HttpApiSurface = "The Quartz HTTP API";

    private const string HttpApiRemedies = """
          - app.MapQuartzHttpApi().RequireAuthorization() authorizes the whole API;
          - services.AddQuartzHttpApi(options => options.SchedulerAuthorizationPolicy = "...") authorizes each scheduler on its own;
          - app.MapQuartzHttpApi().AllowAnonymous() serves it to anyone, deliberately.
        """;
}