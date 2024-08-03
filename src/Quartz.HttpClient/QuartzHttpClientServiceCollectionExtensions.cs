using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.HttpClient;
using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

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
        return services.AddQuartzHttpClient<IScheduler>(schedulerName, httpClient, jsonSerializerOptions);
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
        return services.AddQuartzHttpClient<IScheduler>(schedulerName, httpClientName, jsonSerializerOptions);
    }

    /// <summary>
    /// Register IScheduler which will call remote scheduler over HTTP
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection AddQuartzHttpClient(
        this IServiceCollection services,
        Action<HttpClientOptions> configure)
    {
        return services.AddQuartzHttpClient<IScheduler>(configure);
    }

    /// <summary>
    /// Register scheduler of given type which will call remote scheduler over HTTP
    /// </summary>
    /// <param name="services"></param>
    /// <param name="schedulerName">Name of the scheduler, must be same as the remote scheduler</param>
    /// <param name="httpClient">HttpClient to be used</param>
    /// <param name="jsonSerializerOptions">Optional json serializer options to be used by the HTTP scheduler</param>
    /// <typeparam name="TScheduler">Interface for the scheduler to be registered. Must inherit directly from IScheduler</typeparam>
    /// <returns></returns>
    public static IServiceCollection AddQuartzHttpClient<TScheduler>(
        this IServiceCollection services,
        string schedulerName,
        System.Net.Http.HttpClient httpClient,
        JsonSerializerOptions? jsonSerializerOptions = null) where TScheduler : class, IScheduler
    {
        return services.AddQuartzHttpClient<TScheduler>(options =>
        {
            options.SchedulerName = schedulerName;
            options.HttpClient = httpClient;
            options.JsonSerializerOptions = jsonSerializerOptions;
        });
    }

    /// <summary>
    /// Register scheduler of given type which will call remote scheduler over HTTP
    /// </summary>
    /// <param name="services"></param>
    /// <param name="schedulerName">Name of the scheduler, must be same as the remote scheduler</param>
    /// <param name="httpClientName">Name of the HttpClient, which will be fetched from IHttpClientFactory</param>
    /// <param name="jsonSerializerOptions">Optional json serializer options to be used by the HTTP scheduler</param>
    /// <typeparam name="TScheduler">Interface for the scheduler to be registered. Must inherit directly from IScheduler</typeparam>
    /// <returns></returns>
    public static IServiceCollection AddQuartzHttpClient<TScheduler>(
        this IServiceCollection services,
        string schedulerName,
        string httpClientName,
        JsonSerializerOptions? jsonSerializerOptions = null) where TScheduler : class, IScheduler
    {
        return services.AddQuartzHttpClient<TScheduler>(options =>
        {
            options.SchedulerName = schedulerName;
            options.HttpClientName = httpClientName;
            options.JsonSerializerOptions = jsonSerializerOptions;
        });
    }

    /// <summary>
    /// Register scheduler of given type which will call remote scheduler over HTTP
    /// </summary>
    /// <typeparam name="TScheduler">Interface for the scheduler to be registered. Must inherit directly from IScheduler</typeparam>
    /// <returns></returns>
    public static IServiceCollection AddQuartzHttpClient<TScheduler>(
        this IServiceCollection services,
        Action<HttpClientOptions> configure) where TScheduler : class, IScheduler
    {
        var options = new HttpClientOptions();
        configure(options);

        options.AssertValid();

        services.TryAddSingleton<ISchedulerRepository>(new SchedulerRepository());

        services.AddSingleton<TScheduler>(serviceProvider =>
        {
            var httpClient = options.HttpClient ?? serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(options.HttpClientName!);
            IScheduler scheduler = new HttpScheduler(options.SchedulerName, httpClient, options.JsonSerializerOptions);

            if (typeof(TScheduler) != typeof(IScheduler))
            {
                var schedulerType = SchedulerTypeBuilder.Create<TScheduler>();
                scheduler = (IScheduler) Activator.CreateInstance(schedulerType, scheduler)!;
            }

            serviceProvider.GetRequiredService<ISchedulerRepository>().Bind(scheduler);
            return (TScheduler) scheduler;
        });

        return services;
    }
}