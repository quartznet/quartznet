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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Impl;

/// <summary>
/// The names <c>AddQuartzHttpClient</c> has been called with, so the binder knows which remote schedulers
/// the container holds.
/// </summary>
/// <remarks>
/// Held as a registered instance rather than resolved from the built container, because the names are
/// collected while registration is still going on.
/// </remarks>
internal sealed class HttpSchedulerRegistry
{
    private readonly List<string> names = [];

    public IReadOnlyList<string> Names => names;

    public static HttpSchedulerRegistry For(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(HttpSchedulerRegistry)
                && descriptor.ImplementationInstance is HttpSchedulerRegistry existing)
            {
                return existing;
            }
        }

        HttpSchedulerRegistry registry = new();
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>
    /// Records a remote scheduler's name, refusing one already recorded.
    /// </summary>
    /// <remarks>
    /// A scheduler's name is its key: the keyed <see cref="IScheduler" /> registration is appended, so a
    /// second <c>AddQuartzHttpClient("X", …)</c> was last-wins and the first target simply disappeared —
    /// silently, and with the repository holding one entry under the name either way. Two processes
    /// fronted under one name is a fleet, which is what
    /// <see href="https://github.com/quartznet/quartznet/issues/3387" /> is for; until it ships, saying
    /// so is better than dropping one of them.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The name is already registered.</exception>
    public void Add(string name)
    {
        foreach (string registered in names)
        {
            if (string.Equals(registered, name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"A remote scheduler named '{name}' is already registered. AddQuartzHttpClient registers one "
                    + "target per scheduler name, and a second registration under the same name would replace the "
                    + "first rather than add to it. Give the targets different scheduler names; fronting several "
                    + "schedulers that share a name is the fleet model of "
                    + "https://github.com/quartznet/quartznet/issues/3387.");
            }
        }

        names.Add(name);
    }
}

/// <summary>
/// Builds the container's remote schedulers when the application starts, which is what binds them into
/// <c>ISchedulerRepository</c>.
/// </summary>
/// <remarks>
/// A remote scheduler is otherwise built the first time something injects it, so a dashboard or an HTTP
/// API listing the container's schedulers would not show one until an unrelated piece of code happened to
/// use it. Resolving it is all this does: binding is part of building one.
/// </remarks>
internal sealed class HttpSchedulerBinder : IHostedService
{
    private readonly IServiceProvider serviceProvider;
    private readonly HttpSchedulerRegistry registry;

    public HttpSchedulerBinder(IServiceProvider serviceProvider, HttpSchedulerRegistry registry)
    {
        this.serviceProvider = serviceProvider;
        this.registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (string name in registry.Names)
        {
            serviceProvider.GetRequiredKeyedService<IScheduler>(name);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Nothing to stop: a remote scheduler is a handle to a scheduler running elsewhere, and shutting it
    /// down here would shut down somebody else's.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
