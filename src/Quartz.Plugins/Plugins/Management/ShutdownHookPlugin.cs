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

using Quartz.Diagnostics;
using Quartz.Extensibility;

namespace Quartz.Plugins.Management;

/// <summary>
/// This plugin catches the event of the VM terminating (such as upon a CRTL-C)
/// and tells the scheduler to Shutdown.
/// </summary>
/// <seealso cref="IScheduler.Shutdown(bool, CancellationToken)" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class ShutdownHookPlugin : ISchedulerPlugin
{
    private readonly ILogger<ShutdownHookPlugin> logger;

    public ShutdownHookPlugin()
    {
        logger = LogProvider.CreateLogger<ShutdownHookPlugin>();
        CleanShutdown = true;
    }

    /// <summary>
    /// Determine whether or not the plug-in is configured to cause a clean
    /// Shutdown of the scheduler.
    /// <para>
    /// The default value is <see langword="true" />.
    /// </para>
    /// </summary>
    /// <seealso cref="IScheduler.Shutdown(bool, CancellationToken)" />
    public bool CleanShutdown { get; internal set; }

    /// <summary>
    /// Called during creation of the <see cref="IScheduler" /> in order to give
    /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
    /// </summary>
    public ValueTask Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        logger.ShutdownHookRegistered(pluginName);
        AppDomain.CurrentDomain.ProcessExit += async (sender, ea) =>
        {
            logger.ShuttingDown();
            try
            {
                await scheduler.Shutdown(CleanShutdown, cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException e)
            {
                logger.ShutdownFailed(e.Message, e);
            }
        };
        return default;
    }

    // Start and Shutdown are the interface's defaults: the hook is registered in Initialize, and by the
    // time Shutdown runs the scheduler is already shutting down, which is the whole of what this plugin
    // was going to do about it.
}