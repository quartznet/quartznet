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
using Quartz.Util;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl;

/// <summary>
/// The default JobFactory used by Quartz - simply calls
/// <see cref="ObjectUtils.InstantiateType{T}" /> on the job class.
/// </summary>
/// <seealso cref="IJobFactory" />
/// <seealso cref="PropertySettingJobFactory" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleJobFactory : IJobFactory
{
    private readonly ILogger<SimpleJobFactory> logger;

    /// <param name="loggerFactory">
    /// Where this factory and its derived types create their loggers. A factory the container builds is
    /// handed the application's; one constructed by hand — <c>UseJobFactory(instance)</c> — is handed
    /// nothing and reads <see cref="LogProvider" />, as before. A factory rather than a logger, because
    /// <see cref="PropertySettingJobFactory" /> logs under its own category and has to be able to pass
    /// the same source down.
    /// </param>
    public SimpleJobFactory(ILoggerFactory? loggerFactory = null)
    {
        logger = loggerFactory?.CreateLogger<SimpleJobFactory>() ?? LogProvider.CreateLogger<SimpleJobFactory>();
    }

    /// <summary>
    /// Called by the scheduler at the time of the trigger firing, in order to
    /// produce a <see cref="IJob" /> instance on which to call Execute.
    /// </summary>
    /// <remarks>
    /// It should be extremely rare for this method to throw an exception -
    /// basically only the case where there is no way at all to instantiate
    /// and prepare the Job for execution.  When the exception is thrown, the
    /// Scheduler will move all triggers associated with the Job into the
    /// <see cref="TriggerState.Error" /> state, which will require human
    /// intervention (e.g. an application restart after fixing whatever
    /// configuration problem led to the issue with instantiating the Job).
    /// </remarks>
    /// <param name="bundle">The TriggerFiredBundle from which the <see cref="IJobDetail" />
    ///   and other info relating to the trigger firing can be obtained.</param>
    /// <param name="scheduler">The scheduler the job will run under, made available to the job through its execution context.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the newly instantiated job, together with any per-fire state, in a <see cref="JobScope" /></returns>
    /// <throws>  SchedulerException if there is a problem instantiating the Job. </throws>
    public virtual ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return new ValueTask<JobScope>(new JobScope(InstantiateJobCore(bundle)));
    }

    /// <summary>
    /// Synchronous core of <see cref="CreateJob" /> that derived classes can call when
    /// they need a job instance without driving the asynchronous code path.
    /// </summary>
    protected IJob InstantiateJobCore(TriggerFiredBundle bundle)
    {
        IJobDetail jobDetail = bundle.JobDetail;
        Type jobType = jobDetail.JobType.ResolvedType;
        try
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Producing instance of Job '{JobKey}', class={JobFullName}", jobDetail.Key, jobType.FullName);
            }

            return ObjectUtils.InstantiateType<IJob>(jobType);
        }
        catch (Exception e)
        {
            SchedulerException se = new SchedulerException($"Problem instantiating class '{jobDetail.JobType.FullName}': {e.Message}", e);
            throw se;
        }
    }

    /// <summary>
    /// Allows the job factory to destroy/cleanup the job once it has finished executing.
    /// </summary>
    /// <remarks>
    /// Disposes the job, then any state the factory attached to the scope, preferring
    /// <see cref="IAsyncDisposable" /> over <see cref="IDisposable" /> for each. Anything that is
    /// neither is left alone. The state is disposed even when disposing the job throws, since
    /// whatever the factory had to allocate is usually the more expensive thing to leak.
    /// </remarks>
    public virtual async ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
    {
        try
        {
            await DisposeIfDisposable(scope.Job, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeIfDisposable(scope.State, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes <paramref name="target" /> if it is disposable, preferring
    /// <see cref="IAsyncDisposable" /> over <see cref="IDisposable" />. Anything else is left alone.
    /// </summary>
    protected static ValueTask DisposeIfDisposable(object? target, CancellationToken cancellationToken = default)
    {
        if (target is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        if (target is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return default;
    }
}