using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HealthChecks;
using Quartz.AspNetCore.HttpApi;
using Quartz.AspNetCore.HttpApi.Endpoints;
using Quartz.AspNetCore.HttpApi.Util;

namespace Quartz.AspNetCore;

public static class QuartzServiceCollectionExtensions
{
    public static IServiceCollection AddQuartzHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddTypeActivatedCheck<QuartzHealthCheck>("quartz-scheduler");

        return services;
    }

    public static IServiceCollectionQuartzConfigurator AddHttpApi(
        this IServiceCollectionQuartzConfigurator configurator,
        Action<QuartzHttpApiOptions>? configure = null)
    {
        var optionsBuilder = configurator.Services
            .AddOptions<QuartzHttpApiOptions>()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiPath) && options.ApiPath.StartsWith('/'), "ApiPath is required and must start with '/'");

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        configurator.Services.TryAddSingleton<ExceptionHandler>();
        configurator.Services.TryAddSingleton<EndpointHelper>();

        // Add json converters into ASP.NET Core's default json options
        configurator.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => AddJsonConverters(options.SerializerOptions));

        return configurator;

        static void AddJsonConverters(JsonSerializerOptions? options)
        {
            if (options is null)
            {
                return;
            }

            options.AddQuartzConverters(newtonsoftCompatibilityMode: false);
        }
    }

    public static IEndpointConventionBuilder MapQuartzApi(this IEndpointRouteBuilder builder)
    {
        var handler = builder.ServiceProvider.GetService<ExceptionHandler>();
        if (handler is null)
        {
            throw new InvalidOperationException("HTTP API not configured. Call AddHttpApi() in AddQuartz(...)");
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