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

using Quartz.AspNetCore;
using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Puts what an operator did through another surface of this process — the HTTP API, an MCP tool — in the
/// dashboard's Action Log, beside what the pages did.
/// </summary>
/// <remarks>
/// <para>
/// The host's operator has to see what scripts did through the host. Until 4.9 the Action Log held what
/// its own buttons did and nothing else, so a pause made with <c>curl</c> against the same process left the
/// page that shows the trigger paused with no entry saying who did it. The application's log always had
/// the line (<c>9007</c>, <c>9009</c>); this is the page.
/// </para>
/// <para>
/// A singleton and circuit-free: an API request has no circuit, so where the scheduler is comes from the
/// container's registries rather than from a picker's last listing — the target's kind for a key that names
/// one, and a window or a scheduler of this process for a bare one.
/// </para>
/// </remarks>
internal sealed class HttpApiActionRecorder : IOperatorActionRecorder
{
    private readonly DashboardActionLogService store;
    private readonly IServiceProvider services;
    private readonly TimeProvider timeProvider;

    public HttpApiActionRecorder(DashboardActionLogService store, IServiceProvider services)
    {
        this.store = store;
        this.services = services;
        timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    public void Record(OperatorAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        (SchedulerOrigin? origin, string? node) = Where(action.SchedulerKey);

        store.Record(new DashboardActionLogEntry(
            Timestamp: timeProvider.GetUtcNow(),
            SchedulerName: action.SchedulerKey,
            Action: action.Operation,
            Target: action.Subject,
            Succeeded: action.Succeeded,
            Message: action.Message)
        {
            Origin = origin,
            SchedulerInstanceId = node,
            NodeLocal = node is not null && IsNodeLocal(action.Operation),
            User = action.User,
            Refused = action.Refused,
            Via = action.Via,
            Client = action.Client
        });
    }

    /// <summary>
    /// Where the scheduler a key names is, and which node it is when this process runs it.
    /// </summary>
    /// <remarks>
    /// Only a scheduler of this process says which node it is without a request; a target's node would cost
    /// one, on the path of the request being recorded, so it is left unsaid as the pages leave a node they do
    /// not know.
    /// </remarks>
    private (SchedulerOrigin? Origin, string? Node) Where(string schedulerKey)
    {
        ISchedulerRepository? repository = services.GetService<ISchedulerRepository>();
        SchedulerRef key = SchedulerLookup.Read(repository, schedulerKey);

        if (key.Target is not null)
        {
            return (services.GetService<SchedulerTargets>()?.Find(key.Target)?.Origin, null);
        }

        if (services.GetService<SchedulerWindowRegistry>()?.TargetOf(key.SchedulerName) is not null)
        {
            return (SchedulerOrigin.Window, null);
        }

        IScheduler? scheduler = repository?.LookupByName(key.SchedulerName).FirstOrDefault();
        return scheduler switch
        {
            null => (null, null),
            IProxyScheduler proxy => (proxy.Origin, null),
            _ => (SchedulerOrigin.Container, scheduler.SchedulerInstanceId),
        };
    }

    /// <summary>
    /// Whether the operation lands on the node that served it rather than on the data every node shares —
    /// the rule the pages say per button, by the route's name here.
    /// </summary>
    private static bool IsNodeLocal(string operation)
    {
        return operation is nameof(IScheduler.Start) or nameof(IScheduler.Standby) or nameof(IScheduler.Shutdown) or "InterruptJobInstance";
    }
}
