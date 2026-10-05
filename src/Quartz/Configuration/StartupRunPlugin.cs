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

using System.Globalization;

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// Schedules one run of a job each time its scheduler starts: what
/// <see cref="QuartzBuilderExtensions.RunAtStartup" /> registers.
/// </summary>
/// <remarks>
/// <para>
/// A plugin, because <see cref="ISchedulerPlugin.Start" /> is the one hook that runs on a scheduler's
/// first start and on no other: not when the scheduler is built, which is when registered jobs and
/// triggers are stored, and not when it leaves standby. It runs after the store has recovered what the
/// last run of this node left, and before the scheduler thread starts.
/// </para>
/// <para>
/// The run is an ordinary stored trigger rather than a call made in this process, so everything that
/// applies to a firing applies to it — concurrency, listeners, history, and recovery of a firing a crash
/// interrupted. Its name is new on every start, so one start's run never replaces another's.
/// </para>
/// </remarks>
internal sealed class StartupRunPlugin : ISchedulerPlugin
{
    private readonly JobKey jobKey;
    private IScheduler? scheduler;

    public StartupRunPlugin(JobKey jobKey)
    {
        this.jobKey = jobKey;
    }

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        this.scheduler = scheduler;
        return default;
    }

    /// <exception cref="SchedulerException">The job is not stored.</exception>
    public async ValueTask Start(CancellationToken cancellationToken = default)
    {
        IScheduler target = scheduler ?? throw new InvalidOperationException("The plugin was started before it was initialized.");

        if (!await target.Exists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            Throw.SchedulerException(
                $"RunAtStartup names job '{jobKey}', which is not stored. Store it durably, or give it a trigger of its own.");
        }

        SchedulerMetadata metadata = await target.GetMetadata(cancellationToken).ConfigureAwait(false);
        ITrigger trigger = CreateTrigger(jobKey, metadata.SchedulerInstanceId, metadata.JobStoreClustered, target.TimeProvider);

        await target.ScheduleJob(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one-shot trigger a start schedules: due at once, fired at once if it is late, and pinned to the
    /// node that started when the store is shared with others.
    /// </summary>
    internal static ITrigger CreateTrigger(JobKey jobKey, string instanceId, bool clustered, TimeProvider timeProvider)
    {
        return TriggerBuilder.Create(timeProvider)
            .WithIdentity(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture), SchedulerConstants.StartupGroup)
            .ForJob(jobKey)
            .WithDescription($"Runs {jobKey} once as {instanceId} starts")
            .WithPreferredNode(clustered ? PreferredNode.For(instanceId) : PreferredNode.None)
            .StartNow()
            .WithSimpleSchedule(x => x.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();
    }
}
