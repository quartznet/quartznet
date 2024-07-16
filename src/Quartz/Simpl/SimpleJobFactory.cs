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
using Quartz.Spi;
using Quartz.Util;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Simpl;

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

    public SimpleJobFactory()
    {
        logger = LogProvider.CreateLogger<SimpleJobFactory>();
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
    /// <param name="scheduler"></param>
    /// <returns>the newly instantiated Job</returns>
    /// <throws>  SchedulerException if there is a problem instantiating the Job. </throws>
    public virtual IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        IJobDetail jobDetail = bundle.JobDetail;
        Type jobType = jobDetail.JobType;
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
            SchedulerException se = new SchedulerException($"Problem instantiating class '{jobDetail.JobType.FullName}: {e.Message}'", e);
            throw se;
        }
    }

    /// <summary>
    /// Allows the job factory to destroy/cleanup the job if needed.
    /// No-op when using SimpleJobFactory.
    /// </summary>
    public virtual ValueTask ReturnJob(IJob job)
    {
        if (job is IAsyncDisposable asyncDisposableJob)
        {
            return asyncDisposableJob.DisposeAsync();
        }
        if (job is IDisposable disposableJob)
        {
            disposableJob.Dispose();
        }
        // check for wrapped jobs only if the current job is not disposable
        // disposable wrappers should handle inner disposal on it's own
        else if (job is IJobWrapper jobWrapper)
        {
            return ReturnJob(jobWrapper.Target);
        }

        return default;
    }
}