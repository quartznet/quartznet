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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Runs the agent registry's sweep every heartbeat interval on the container's clock, while the
/// dashboard accepts agents.
/// </summary>
/// <remarks>
/// A hosted service rather than a timer the registry starts for itself, so that a container that never
/// starts a host — a test building the registry by hand — never has a timer running, and so that the
/// sweep stops with the host.
/// </remarks>
internal sealed class AgentRegistryJanitor : IHostedService, IDisposable
{
    private readonly IServiceProvider services;
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AgentRegistryJanitor> logger;

    private ITimer? timer;

    public AgentRegistryJanitor(IServiceProvider services, IOptions<QuartzDashboardOptions> options, ILogger<AgentRegistryJanitor> logger)
    {
        this.services = services;
        this.options = options;
        this.logger = logger;
        timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Agents is not { } agents)
        {
            return Task.CompletedTask;
        }

        AgentRegistry registry = services.GetRequiredService<AgentRegistry>();
        TimeSpan interval = agents.HeartbeatInterval;
        timer = timeProvider.CreateTimer(_ => Sweep(registry, interval), null, interval, interval);

        return Task.CompletedTask;
    }

    /// <summary>
    /// One sweep, which must not throw: an exception out of a timer callback ends the process, and the
    /// next tick is a better answer than that.
    /// </summary>
    private void Sweep(AgentRegistry registry, TimeSpan interval)
    {
        try
        {
            registry.Sweep();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.AgentSweepFailed(interval, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        timer?.Dispose();
        timer = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        timer?.Dispose();
    }
}
