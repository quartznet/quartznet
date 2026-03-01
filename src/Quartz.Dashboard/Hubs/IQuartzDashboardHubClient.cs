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

using Quartz.Dashboard.Services;

namespace Quartz.Dashboard.Hubs;

public interface IQuartzDashboardHubClient
{
    Task JobExecuting(JobEventDto jobEvent);

    Task JobExecuted(JobExecutionResultDto result);

    Task TriggerFired(TriggerEventDto triggerEvent);

    Task TriggerCompleted(TriggerEventDto triggerEvent);

    Task TriggerMisfired(TriggerEventDto triggerEvent);

    Task TriggerPaused(TriggerKeyDto triggerKey);

    Task TriggerResumed(TriggerKeyDto triggerKey);

    Task JobPaused(JobKeyDto jobKey);

    Task JobResumed(JobKeyDto jobKey);

    Task SchedulerStateChanged(SchedulerStateDto state);

    Task SchedulerError(SchedulerErrorDto schedulerError);
}

public sealed record JobEventDto(
    JobKeyDto JobKey,
    TriggerKeyDto TriggerKey,
    DateTimeOffset FireTimeUtc,
    string? FireInstanceId);

public sealed record JobExecutionResultDto(
    JobKeyDto JobKey,
    TriggerKeyDto TriggerKey,
    DateTimeOffset FireTimeUtc,
    long RunTimeMs,
    bool Vetoed,
    string? ExceptionMessage);

public sealed record TriggerEventDto(
    TriggerKeyDto TriggerKey,
    JobKeyDto? JobKey,
    DateTimeOffset? FireTimeUtc);

public sealed record SchedulerStateDto(string SchedulerName, string State);

public sealed record SchedulerErrorDto(string SchedulerName, string Message, string? Cause);
