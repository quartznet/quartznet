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

using Quartz.Core;
using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// Shuts a scheduler down when the container that built it is disposed.
/// </summary>
/// <remarks>
/// <para>
/// The hosted service shuts every scheduler down inside the host's shutdown window, and
/// <see cref="StandaloneSchedulerFactory" /> shuts its own down before it disposes its container. An
/// application that builds and disposes a container itself, with no host, had nothing to do it unless
/// it had resolved <see cref="IScheduler" /> and used it: a scheduler built through
/// <see cref="ISchedulerFactory.GetScheduler" /> went on firing into the disposed container, because
/// none of its parts is disposable. This is the step that was missing, as
/// <see cref="SchedulerRuntime.DisposeAsync" /> is for the schedulers added at runtime.
/// </para>
/// <para>
/// <see cref="DefaultSchedulerFactory" /> resolves one as the last step of building a scheduler. The
/// container disposes in the reverse order of creation, so this is disposed before every part of the
/// scheduler it shuts down, and before the logger factory. A scheduler that was never built has none,
/// so nothing is built only to be torn down.
/// </para>
/// <para>
/// The shutdown does not wait for running jobs, as the hosted service's default does not. A scheduler
/// that was already shut down is left alone, because a second shutdown returns at once.
/// </para>
/// </remarks>
internal sealed class ShutdownOnContainerDisposal : IAsyncDisposable, IDisposable
{
    private readonly QuartzScheduler scheduler;
    private readonly ILogger<ShutdownOnContainerDisposal> logger;

    public ShutdownOnContainerDisposal(QuartzScheduler scheduler, ILogger<ShutdownOnContainerDisposal> logger)
    {
        this.scheduler = scheduler;
        this.logger = logger;
    }

    /// <summary>
    /// Shuts the scheduler down without waiting for its jobs, through <see cref="DisposeScheduler" />.
    /// </summary>
    public ValueTask DisposeAsync() => DisposeScheduler(scheduler.Scheduler, logger);

    /// <summary>
    /// Disposes a scheduler, and logs a failed shutdown rather than throwing it.
    /// </summary>
    /// <remarks>
    /// Microsoft's container stops at the first service whose disposal throws, and leaves the services
    /// created before it undisposed: the application's own as well as the scheduler's parts. So a failed
    /// shutdown is logged as event 4033 and the disposal goes on. <see cref="DeferredScheduler" />, the
    /// <see cref="IScheduler" /> a container injects, disposes through this too. It is usually first
    /// resolved after the scheduler is built, so the container disposes it before this.
    /// </remarks>
    internal static async ValueTask DisposeScheduler(IScheduler scheduler, ILogger logger)
    {
        try
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.SchedulerDisposalFailed(scheduler.SchedulerName, e);
        }
    }

    /// <inheritdoc cref="DisposeAsync" />
    /// <remarks>
    /// Present because <c>ServiceProvider.Dispose()</c> throws on a service that is only
    /// <see cref="IAsyncDisposable" />. It blocks on the asynchronous shutdown, which awaits with
    /// <c>ConfigureAwait(false)</c> throughout, as <see cref="SchedulerRuntime.Dispose" /> does.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
