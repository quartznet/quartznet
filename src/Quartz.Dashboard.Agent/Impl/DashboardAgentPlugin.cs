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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// The plugin <c>UseDashboardAgent</c> installs: dials the scheduler out to a dashboard when the scheduler
/// starts, keeps the connection for as long as it runs, and says goodbye when it shuts down.
/// </summary>
/// <remarks>
/// Container-built, as <see cref="SchedulerEventPlugin" /> is, and handed the scheduler's own options,
/// broker and clock. Starting never blocks the scheduler and never fails it: a dashboard that is down
/// when the worker starts is dialled again on <see cref="Reconnection" />'s backoff, for as long as the
/// worker runs.
/// </remarks>
internal sealed class DashboardAgentPlugin : ISchedulerPlugin
{
    private readonly DashboardAgentOptions options;
    private readonly SchedulerEventBroker broker;
    private readonly IServiceProvider services;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly CancellationTokenSource stopping = new();

    private AgentConnection? connection;
    private Task? running;
    private int stopped;

    public DashboardAgentPlugin(
        IOptions<DashboardAgentOptions> options,
        SchedulerEventBroker broker,
        IServiceProvider services,
        TimeProvider timeProvider,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options.Value;
        this.broker = broker ?? throw new ArgumentNullException(nameof(broker));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        logger = loggerFactory?.CreateLogger(DashboardAgentLog.Category) ?? DashboardAgentLog.Fallback();
    }

    /// <summary>
    /// The name the scheduler knows the plugin by.
    /// </summary>
    public string Name { get; private set; } = DashboardAgentConfigurationExtensions.PluginName;

    /// <summary>
    /// Captures the scheduler and builds the carrier over it. The options are checked here as well as by
    /// the options validator, so a scheduler whose options nothing resolved through <c>IOptions</c> is
    /// refused with the same words before it dials anything.
    /// </summary>
    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        Name = pluginName;

        ValidateOptionsResult validation = new DashboardAgentOptionsValidator().Validate(Options.DefaultName, options);
        if (validation.Failed)
        {
            Throw.SchedulerConfigException($"UseDashboardAgent on scheduler '{scheduler.SchedulerName}': {validation.FailureMessage}");
        }

        AgentCarrier carrier = new(scheduler, services, options, logger);
        connection = new AgentConnection(options, scheduler, carrier, broker, timeProvider, logger);
        return default;
    }

    /// <summary>
    /// Starts the connection loop on the thread pool and returns at once.
    /// </summary>
    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            Throw.SchedulerException("The dashboard agent was started before it was initialized.");
        }

        running = Task.Run(() => connection.RunAsync(stopping.Token), stopping.Token);
        return default;
    }

    /// <summary>
    /// Says goodbye to the dashboard, stops the loop and lets the connection go.
    /// </summary>
    public async ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            return;
        }

        await connection.SayGoodbye(cancellationToken).ConfigureAwait(false);
        await Stop().ConfigureAwait(false);
    }

    /// <summary>
    /// For a test: drops the connection without a goodbye and stops dialling, the way a process that was
    /// killed looks to the dashboard.
    /// </summary>
    internal async ValueTask Vanish()
    {
        if (connection is null)
        {
            return;
        }

        await Stop().ConfigureAwait(false);
    }

    /// <summary>
    /// The agent's own connection, for a test that asks what it is doing.
    /// </summary>
    internal AgentConnection? Connection => connection;

    private async ValueTask Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 1)
        {
            return;
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        await connection!.Drop().ConfigureAwait(false);

        if (running is not null)
        {
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The loop was told to stop, which is what this is.
            }
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        stopping.Dispose();
    }
}
