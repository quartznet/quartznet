#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Serialization.SystemTextJson;
using Quartz.Impl;
using Quartz.Extensibility;

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
        HttpClient httpClient,
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
        HttpClient httpClient,
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

        // The repository the remote scheduler binds itself into is the container's, registered in exactly
        // one place. Creating one here would give a container that also calls AddQuartz two repositories,
        // and a scheduler registered in one would be invisible in the other.
        // This also registers the container-wide serializer registry the client reads below. A remote
        // scheduler's custom trigger and calendar serializers cannot be discovered over HTTP, so register
        // a custom serializer there to be able to read custom types from the remote scheduler.
        services.AddQuartzSharedServices();

        services.AddSingleton<TScheduler>(serviceProvider =>
        {
            var httpClient = options.HttpClient ?? serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(options.HttpClientName!);
            IScheduler scheduler = new HttpScheduler(
                options.SchedulerName,
                httpClient,
                options.JsonSerializerOptions,
                serviceProvider.GetRequiredService<SystemTextJsonSerializerRegistry>());

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