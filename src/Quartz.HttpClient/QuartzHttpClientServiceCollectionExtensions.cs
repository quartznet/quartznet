using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpClient;
using Quartz.Impl;

namespace Quartz;

public static class QuartzHttpClientServiceCollectionExtensions
{
    /// <summary>
    /// Register IScheduler which will call remote scheduler over HTTP
    /// </summary>
    /// <param name="services"></param>
    /// <param name="schedulerName">Name of the scheduler, must be same as the remote scheduler</param>
    /// <param name="httpClient">HttpClient to be used</param>
    /// <param name="jsonSerializerOptions">Optional json serializer options to be used by the HTTP scheduler</param>
    /// <returns></returns>
    public static IServiceCollection AddQuartzHttpClient(
        this IServiceCollection services,
        string schedulerName,
        System.Net.Http.HttpClient httpClient,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = schedulerName;
            options.HttpClient = httpClient;
            options.JsonSerializerOptions = jsonSerializerOptions;
        });
    }

    /// <summary>
    /// Register IScheduler which will call remote scheduler over HTTP
    /// </summary>
    /// <param name="services"></param>
    /// <param name="schedulerName">Name of the scheduler, must be same as the remote scheduler</param>
    /// <param name="httpClientName">Name of the HttpClient, which will be fetched from IHttpClientFactory</param>
    /// <param name="jsonSerializerOptions">Optional json serializer options to be used by the HTTP scheduler</param>
    /// <returns></returns>
    public static IServiceCollection AddQuartzHttpClient(
        this IServiceCollection services,
        string schedulerName,
        string httpClientName,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = schedulerName;
            options.HttpClientName = httpClientName;
            options.JsonSerializerOptions = jsonSerializerOptions;
        });
    }

    /// <summary>
    /// Register IScheduler which will call remote scheduler over HTTP
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection AddQuartzHttpClient(
        this IServiceCollection services,
        Action<HttpClientOptions> configure)
    {
        var options = new HttpClientOptions();
        configure(options);

        options.AssertValid();

        services.AddSingleton<IScheduler>(serviceProvider =>
        {
            var httpClient = options.HttpClient ?? serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(options.HttpClientName!);

            var scheduler = new HttpScheduler(options.SchedulerName, httpClient);
            SchedulerRepository.Instance.Bind(scheduler);

            return scheduler;
        });

        return services;
    }
}