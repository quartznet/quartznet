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

using System.Diagnostics.CodeAnalysis;

namespace Quartz;

/// <summary>
/// Scheduling a job by its type, the way the container-configured builder does.
/// </summary>
/// <remarks>
/// An extension rather than an <see cref="IScheduler" /> member: it composes
/// <see cref="JobBuilder" /> and <see cref="IScheduler.ScheduleJob(IJobDetail, ITrigger, CancellationToken)" />
/// and needs nothing an implementation could do better, so every scheduler — including one written
/// outside this repository — gets it without having to write it.
/// </remarks>
public static class SchedulerJobExtensions
{
    /// <summary>
    /// Schedule a job of the given type with the given trigger, building the
    /// <see cref="IJobDetail" /> along the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The imperative twin of <c>q.ScheduleJob&lt;TJob&gt;(...)</c> on <see cref="IQuartzBuilder" />,
    /// so that scheduling a job at run time reads like declaring one at start-up and neither has to
    /// name a <see cref="JobBuilder" />.
    /// </para>
    /// <para>
    /// The job's identity is the first of these that exists: the one
    /// <paramref name="configure" /> gave it, the one <paramref name="trigger" /> already points at
    /// through <see cref="ITrigger.JobKey" />, or the trigger's own key. Naming neither is therefore
    /// enough to schedule the pair, and a trigger built with <c>ForJob</c> still gets the job it
    /// asked for.
    /// </para>
    /// </remarks>
    /// <typeparam name="TJob">the type of the job to schedule.</typeparam>
    /// <param name="scheduler">the scheduler to schedule the job with.</param>
    /// <param name="trigger">the trigger that fires the job.</param>
    /// <param name="configure">configures the job; omit to take everything from the type and the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the first time at which the trigger will fire.</returns>
    public static ValueTask<DateTimeOffset> ScheduleJob<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        this IScheduler scheduler,
        ITrigger trigger,
        Action<IJobConfigurator<TJob>>? configure = null,
        CancellationToken cancellationToken = default) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(trigger);

        JobBuilder<TJob> jobBuilder = JobBuilder.Create<TJob>();
        configure?.Invoke(jobBuilder);

        if (jobBuilder.Key is null)
        {
            // The job was given no identity of its own, so it takes one that makes the pair agree
            // rather than the Guid JobBuilder would otherwise generate — which would leave the
            // trigger pointing at a job nobody can name again.
            JobKey borrowed = trigger.JobKey ?? new JobKey(trigger.Key.Name, trigger.Key.Group);
            jobBuilder.WithIdentity(borrowed);
        }

        return scheduler.ScheduleJob(jobBuilder.Build(), trigger, cancellationToken);
    }
}
