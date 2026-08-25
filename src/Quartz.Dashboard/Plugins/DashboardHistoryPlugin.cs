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
using Quartz.Extensibility;

namespace Quartz.Dashboard.Plugins;

public sealed class DashboardHistoryPlugin : ISchedulerPlugin, IJobListener
{
    private readonly IServiceProvider serviceProvider;

    /// <summary>
    /// Takes the container the history store is resolved from.
    /// </summary>
    /// <remarks>
    /// A plugin is constructed by the container — this one is registered for every scheduler by
    /// <c>AddQuartzDashboard</c> — so it asks for what it needs the way any other component does. It used
    /// to read the container back out of <c>scheduler.Context["Quartz.ServiceProvider"]</c>, which put
    /// Quartz's plumbing into the application's own map, and left the scheduler-context endpoint of the
    /// HTTP API answering <c>500</c> for every scheduler a container had built.
    /// </remarks>
    public DashboardHistoryPlugin(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public string Name { get; private set; } = "QuartzDashboardHistory";

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        scheduler.ListenerManager.AddJobListener(this, Matchers.AllJobs());
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
            // Resolved per execution and allowed to be absent, as when this went through the scheduler
            // context: a dashboard that was never registered is a reason to record nothing, not a reason
            // to fail the execution that just finished. Nor is a container the host has begun disposing,
            // which is what the catch below is for.
            IDashboardHistoryStore? store = serviceProvider.GetService<IDashboardHistoryStore>();
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
                Duration: context.JobRunTime,
                Succeeded: jobException is null,
                ExceptionMessage: jobException?.Message);

            return store.Add(entry, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return default;
        }
    }
}
