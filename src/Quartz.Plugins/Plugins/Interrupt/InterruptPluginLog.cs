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

namespace Quartz.Plugins.Interrupt;

/// <summary>
/// Every event the job interrupt monitor plugin logs, as source-generated methods with a pinned
/// event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 6400-6499 belong to this area and are allocated in file order: the plugin itself
/// (6400-6409) and the monitor that does the interrupting (6410-6419). An id, once given out, is
/// what an operator filters and alerts on, so it is never reused for a different event and never
/// renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class InterruptPluginLog
{
    [LoggerMessage(EventId = 6400, Level = LogLevel.Information, Message = "Registering Job Interrupt Monitor Plugin")]
    public static partial void PluginRegistered(this ILogger logger);

    [LoggerMessage(EventId = 6401, Level = LogLevel.Warning, Message = "Job data map value for {Key} is not a number of milliseconds, using the plugin default of {Delay} instead")]
    public static partial void MaxRunTimeNotANumber(this ILogger logger, string key, TimeSpan delay);

    [LoggerMessage(EventId = 6402, Level = LogLevel.Debug, Message = "Job's Interrupt Monitor has been scheduled to interrupt with the delay: {Delay}")]
    public static partial void InterruptMonitorScheduled(this ILogger logger, TimeSpan delay);

    [LoggerMessage(EventId = 6403, Level = LogLevel.Error, Message = "Error scheduling interrupt monitor {ErrorMessage}")]
    public static partial void InterruptMonitorSchedulingFailed(this ILogger logger, string errorMessage, Exception exception);

    [LoggerMessage(EventId = 6410, Level = LogLevel.Information, Message = "Interrupted Job as it ran more than the configured max time. Job Details [{JobName}:{JobGroup}], fire instance id {FireInstanceId}")]
    public static partial void JobInterrupted(this ILogger logger, string jobName, string jobGroup, string fireInstanceId);

    [LoggerMessage(EventId = 6411, Level = LogLevel.Debug, Message = "Job execution was no longer running, nothing to interrupt. Job Details [{JobName}:{JobGroup}], fire instance id {FireInstanceId}")]
    public static partial void JobNoLongerRunning(this ILogger logger, string jobName, string jobGroup, string fireInstanceId);

    [LoggerMessage(EventId = 6412, Level = LogLevel.Error, Message = "Error interrupting Job: {ExceptionMessage}")]
    public static partial void JobInterruptFailed(this ILogger logger, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 6413, Level = LogLevel.Error, Message = "Error cancelling monitor: {ExceptionMessage}")]
    public static partial void MonitorCancellationFailed(this ILogger logger, string exceptionMessage, Exception exception);
}
