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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;

namespace Quartz;

/// <summary>
/// Registers schedulers that live in another process and are driven over Quartz's HTTP API.
/// </summary>
/// <remarks>
/// A remote scheduler is registered the same way a local one is: keyed by its name, so
/// <c>GetRequiredKeyedService&lt;IScheduler&gt;("reporting")</c> and
/// <c>[FromKeyedServices("reporting")] IScheduler</c> reach it, and unkeyed as well while it is the only
/// scheduler in the container. Before 4.0 a second remote scheduler needed a marker interface of its own,
/// implemented by a type emitted at runtime; the service key says the same thing without the reflection.
/// </remarks>
public static class QuartzHttpClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers a scheduler that drives a remote one over HTTP.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="schedulerName">The scheduler's name, which must match the remote scheduler's.</param>
    /// <param name="httpClient">The client to call the remote scheduler with.</param>
    /// <param name="jsonSerializerOptions">Optional serializer options for the HTTP scheduler.</param>
    public static IServiceCollection AddQuartzHttpClient(
        this IServiceCollection services,
        string schedulerName,
        HttpClient httpClient,
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
    /// Registers a scheduler that drives a remote one over HTTP, using a named
    /// <see cref="IHttpClientFactory"/> client.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="schedulerName">The scheduler's name, which must match the remote scheduler's.</param>
    /// <param name="httpClientName">The name the client is registered under with <c>AddHttpClient</c>.</param>
    /// <param name="jsonSerializerOptions">Optional serializer options for the HTTP scheduler.</param>
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
    /// Registers a scheduler that drives a remote one over HTTP.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Configures the client: the scheduler's name and how to reach it.</param>
    public static IServiceCollection AddQuartzHttpClient(
        this IServiceCollection services,
        Action<HttpClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HttpClientOptions();
        configure(options);

        HttpClientOptionsValidator.ThrowIfInvalid(options);

        // The repository the remote scheduler binds itself into is the container's, registered in exactly
        // one place. Creating one here would give a container that also calls AddQuartz two repositories,
        // and a scheduler registered in one would be invisible in the other.
        // This also registers the container-wide serializer registry the client reads below. A remote
        // scheduler's custom trigger and calendar serializers cannot be discovered over HTTP, so register
        // a custom serializer there to be able to read custom types from the remote scheduler.
        services.AddQuartzSharedServices();

        services.AddKeyedSingleton<IScheduler>(options.SchedulerName, (serviceProvider, _) =>
        {
            var httpClient = options.HttpClient ?? serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(options.HttpClientName!);
            IScheduler scheduler = new HttpScheduler(
                options.SchedulerName,
                httpClient,
                options.JsonSerializerOptions,
                serviceProvider.GetRequiredService<SystemTextJsonSerializerRegistry>());

            // Bound under its own name rather than under an instance id read from the remote scheduler:
            // that property costs a request, and one registration is one remote scheduler, so the name
            // tells the repository's entries apart on its own.
            serviceProvider.GetRequiredService<ISchedulerRepository>().Bind(scheduler, options.SchedulerName);
            return scheduler;
        });

        // Keyed by name like any other scheduler, and unkeyed as well so that a container holding one
        // remote scheduler and nothing else answers GetRequiredService<IScheduler>() with it. TryAdd,
        // because a second remote scheduler must not quietly take over what "the scheduler" means.
        services.TryAddSingleton<IScheduler>(
            serviceProvider => serviceProvider.GetRequiredKeyedService<IScheduler>(options.SchedulerName));

        // Remote schedulers are otherwise built on first injection, which leaves them missing from the
        // repository - and so from LookupAll, the dashboard and the HTTP API - until something happens to
        // ask for one. A container with no host never runs this, and is exactly as lazy as it was.
        HttpSchedulerRegistry.For(services).Add(options.SchedulerName);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HttpSchedulerBinder>());

        return services;
    }
}
