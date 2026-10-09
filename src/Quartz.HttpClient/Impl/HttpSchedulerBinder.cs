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
/// The service keys <c>AddQuartzHttpClient</c> has registered under — a scheduler name, or the target
/// when one was given — so the binder knows which remote schedulers the container holds.
/// </summary>
/// <remarks>
/// Held as a registered instance rather than resolved from the built container, because the keys are
/// collected while registration is still going on.
/// </remarks>
internal sealed class HttpSchedulerRegistry
{
    private readonly List<string> keys = [];

    /// <summary>
    /// The service keys, in registration order.
    /// </summary>
    public IReadOnlyList<string> Names => keys;

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
    /// Records a remote scheduler's service key, refusing one already recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is the registration's identity: the keyed <see cref="IScheduler" /> registration is
    /// appended, so a second <c>AddQuartzHttpClient</c> under the same key would be last-wins and the
    /// first target would simply disappear — silently, and with the repository holding one entry under
    /// the key either way.
    /// </para>
    /// <para>
    /// An untargeted registration's key is its scheduler name, and a second one under the same name is
    /// refused as it always was: two processes fronted under one bare name is what a target is for. A
    /// targeted registration's key is its target, and a second target of that name is a configuration
    /// error of the kind every other duplicate target is.
    /// </para>
    /// </remarks>
    /// <param name="key">The service key: the target, or the scheduler name when there is no target.</param>
    /// <param name="targeted">Whether the registration gave a target.</param>
    /// <exception cref="InvalidOperationException">An untargeted registration already holds the scheduler name.</exception>
    /// <exception cref="SchedulerConfigException">A registration already holds the target.</exception>
    public void Add(string key, bool targeted)
    {
        foreach (string registered in keys)
        {
            if (!string.Equals(registered, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (targeted)
            {
                Throw.SchedulerConfigException(
                    $"An HTTP target named '{key}' is already registered. A target's name is half of every key its "
                    + "scheduler is shown under, so two registrations under one target would give two schedulers one "
                    + "spelling; give the second one a Target of its own.");
            }

            throw new InvalidOperationException(
                $"A remote scheduler named '{key}' is already registered. AddQuartzHttpClient registers one "
                + "target per scheduler name, and a second registration under the same name would replace the "
                + "first rather than add to it. Give the registrations different scheduler names, or set "
                + "HttpClientOptions.Target on each to front several schedulers that share a name — the fleet "
                + "model of https://github.com/quartznet/quartznet/issues/3387.");
        }

        keys.Add(key);
    }
}

/// <summary>
/// Builds the container's remote schedulers when the application starts, which is what binds them into
/// <c>ISchedulerRepository</c>.
/// </summary>
/// <remarks>
/// A remote scheduler is otherwise built the first time something injects it, so a dashboard or an HTTP
/// API listing the container's schedulers would not show one until an unrelated piece of code happened to
/// use it. Resolving it is all this does: binding is part of building one, and so is claiming its target.
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
        foreach (string key in registry.Names)
        {
            serviceProvider.GetRequiredKeyedService<IScheduler>(key);
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
