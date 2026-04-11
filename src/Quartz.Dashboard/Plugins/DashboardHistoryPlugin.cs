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

using Quartz.Dashboard.Services;
using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.Dashboard.Plugins;

public sealed class DashboardHistoryPlugin : ISchedulerPlugin, IJobListener
{
    private IScheduler? scheduler;

    public string Name { get; private set; } = "QuartzDashboardHistory";

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        this.scheduler = scheduler;
        scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
        return default;
    }

    public ValueTask Start(CancellationToken cancellationToken = default) => default;

    public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;

    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        try
        {
            if (scheduler?.Context is not { } ctx
                || !ctx.TryGetValue(DashboardPluginKeys.ServiceProvider, out var value)
                || value is not IServiceProvider sp)
            {
                return default;
            }

            IDashboardHistoryStore? store = sp.GetService<IDashboardHistoryStore>();
            if (store is null)
            {
                return default;
            }

            DashboardHistoryEntry entry = new(
                SchedulerName: context.Scheduler.SchedulerName,
                JobGroup: context.JobDetail.Key.Group,
                JobName: context.JobDetail.Key.Name,
                TriggerGroup: context.Trigger.Key.Group,
                TriggerName: context.Trigger.Key.Name,
                FiredAtUtc: context.FireTimeUtc,
                DurationMs: (long) context.JobRunTime.TotalMilliseconds,
                Succeeded: jobException is null,
                ExceptionMessage: jobException?.Message);

            store.Add(entry);
            return default;
        }
        catch (ObjectDisposedException)
        {
            return default;
        }
    }
}
