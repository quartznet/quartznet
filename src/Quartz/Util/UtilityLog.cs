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

namespace Quartz.Util;

/// <summary>
/// Every event the parts of Quartz that nothing injects a logger into log — type loading, triggers,
/// listeners a caller constructs, and the static helpers — as source-generated methods with a pinned
/// event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 5100-5199 belong to this sub-group of the 5000-5999 range, and are allocated in file
/// order. These are the sites <see cref="Quartz.Diagnostics.LogProvider" /> exists for: a listener a
/// caller built, a trigger read back out of a job store, a static helper. Being source-generated
/// changes nothing about where the logger comes from.
/// </para>
/// <para>
/// An id, once given out, is what an operator filters and alerts on, so it is never reused for a
/// different event and never renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed
/// diff.
/// </para>
/// </remarks>
internal static partial class UtilityLog
{
    [LoggerMessage(EventId = 5100, Level = LogLevel.Warning, Message = "Misfire instruction '{MisfireInstruction}' is not one of the {Family} trigger names. It resolves to code {Code}, which for this trigger means {Policy}; spell it '{Canonical}'")]
    public static partial void MisfireInstructionFromAnotherFamily(
        this ILogger logger,
        string misfireInstruction,
        string family,
        int code,
        string policy,
        string canonical);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "Type '{OldName}' was found as '{NewName}'; the type moved in Quartz 4.0. Update the configuration, as this fallback will not last forever.")]
    public static partial void TypeFoundUnderNewName(this ILogger logger, string oldName, string newName);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Warning, Message = "Unrecognized misfire policy {MisfireInstruction}. Derived builder will use the default cron trigger behavior (FireOnceNow)")]
    public static partial void UnrecognizedCronMisfirePolicy(this ILogger logger, int misfireInstruction);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Error, Message = "Listener {ListenerName} - method {MethodName} raised an exception: {ExceptionMessage}")]
    public static partial void ListenerRaisedException(this ILogger logger, string listenerName, string methodName, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 5105, Level = LogLevel.Error, Message = "Listener method {MethodName} raised an exception: {ExceptionMessage}")]
    public static partial void SchedulerListenerRaisedException(this ILogger logger, string methodName, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 5106, Level = LogLevel.Information, Message = "Job '{JobKey}' will now chain to Job '{Job}'")]
    public static partial void ChainingToJob(this ILogger logger, JobKey jobKey, JobKey job);

    [LoggerMessage(EventId = 5107, Level = LogLevel.Error, Message = "Error encountered during chaining to Job '{Job}'")]
    public static partial void ChainingToJobFailed(this ILogger logger, JobKey job, Exception exception);

    [LoggerMessage(EventId = 5108, Level = LogLevel.Warning, Message = "Unable to resolve file path '{FileName}' due to security exception, probably running under medium trust")]
    public static partial void FilePathResolutionDenied(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 5109, Level = LogLevel.Warning, Message = "Unable to read environment variable '{Key}' due to security exception, probably running under medium trust")]
    public static partial void EnvironmentVariableReadDenied(this ILogger logger, string key);

    [LoggerMessage(EventId = 5110, Level = LogLevel.Warning, Message = "Unable to read environment variables due to security exception, probably running under medium trust")]
    public static partial void EnvironmentVariablesReadDenied(this ILogger logger);
}
