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

using Quartz.Configuration;

namespace Quartz.Impl;

/// <summary>
/// The job type every delegate job is stored as. A firing runs the handler its scheduler registered
/// under the key being fired.
/// </summary>
/// <remarks>
/// <para>
/// A handler is code, and a job store keeps a type name. So every delegate job is stored as this one
/// type — its <c>JOB_CLASS_NAME</c> reads <c>Quartz.Impl.DelegateJob, Quartz</c> — and what tells one
/// from another is its key. The type resolves on every node, which is what lets a delegate job be
/// persisted and clustered like any other; the handler itself is not stored, so it has to be
/// registered on every node that may run the job.
/// </para>
/// <para>
/// A node that was never given the handler — an older deployment, a misspelled name, a job added by
/// type name through the HTTP API — fails the firing with a <see cref="JobExecutionException" /> that
/// names the key and the scheduler, so the trigger's <see cref="RetryPolicy" /> and the scheduler's
/// error reporting treat it as the failed job it is.
/// </para>
/// </remarks>
internal sealed class DelegateJob : IJob
{
    private readonly IServiceProvider services;

    /// <param name="services">
    /// The firing's scope, which is what the handler's services are resolved from.
    /// </param>
    public DelegateJob(IServiceProvider services)
    {
        this.services = services;
    }

    /// <summary>
    /// Runs the handler registered for this firing's scheduler and key.
    /// </summary>
    /// <remarks>
    /// The registry is asked for rather than injected, so a node whose container holds no delegate job
    /// at all can still build this type out of the store and report what is missing, instead of failing
    /// to construct it and leaving the trigger in error without a word about why.
    /// </remarks>
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        string schedulerName = context.Scheduler.SchedulerName;
        JobKey key = context.JobDetail.Key;

        if (services.GetService<DelegateJobRegistry>()?.Find(schedulerName, key) is not { } binding)
        {
            throw new JobExecutionException(
                $"Delegate job '{key}' has no handler on scheduler '{schedulerName}'. A delegate job's code is not "
                + "stored with it, only its key: every node that runs this scheduler has to register it with "
                + $"AddJob(\"{key.Name}\", …) or ScheduleJob(\"{key.Name}\", …).");
        }

        return binding.Invoke(context, services, cancellationToken);
    }
}
