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

namespace Quartz.Plugins.Json;

/// <summary>
/// Every event the JSON scheduling data plugin logs, as source-generated methods with a pinned
/// event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 6300-6399 belong to this area and are allocated in file order: the processor that reads
/// the file (6300-6319) and the plugin that feeds it files (6320-6339). They are separate from the
/// XML plugin's 6200-6299 even where the two spell a message identically, because an id names one
/// event in one place. An id, once given out, is what an operator filters and alerts on, so it is
/// never reused for a different event and never renumbered; <c>LogEventCatalogTest</c> makes a change
/// to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class JsonSchedulingPluginLog
{
    [LoggerMessage(EventId = 6300, Level = LogLevel.Information, Message = "Parsing JSON file: {FileName}")]
    public static partial void ParsingFile(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 6301, Level = LogLevel.Information, Message = "Deleting all jobs in ALL groups")]
    public static partial void DeletingAllJobsInAllGroups(this ILogger logger);

    [LoggerMessage(EventId = 6302, Level = LogLevel.Information, Message = "Deleting all jobs in group: {Group}")]
    public static partial void DeletingAllJobsInGroup(this ILogger logger, string group);

    [LoggerMessage(EventId = 6303, Level = LogLevel.Information, Message = "Deleting all triggers in ALL groups")]
    public static partial void DeletingAllTriggersInAllGroups(this ILogger logger);

    [LoggerMessage(EventId = 6304, Level = LogLevel.Information, Message = "Deleting all triggers in group: {Group}")]
    public static partial void DeletingAllTriggersInGroup(this ILogger logger, string group);

    [LoggerMessage(EventId = 6305, Level = LogLevel.Information, Message = "Deleting job: {JobKey}")]
    public static partial void DeletingJob(this ILogger logger, JobKey jobKey);

    [LoggerMessage(EventId = 6306, Level = LogLevel.Information, Message = "Deleting trigger: {TriggerKey}")]
    public static partial void DeletingTrigger(this ILogger logger, TriggerKey triggerKey);

    [LoggerMessage(EventId = 6320, Level = LogLevel.Information, Message = "Registering Quartz JSON Job Initialization Plug-in")]
    public static partial void PluginRegistered(this ILogger logger);

    [LoggerMessage(EventId = 6321, Level = LogLevel.Debug, Message = "Scheduled file scan job for data file: {FileName}, at interval: {ScanInterval}")]
    public static partial void FileScanJobScheduled(this ILogger logger, string fileName, TimeSpan scanInterval);

    [LoggerMessage(EventId = 6322, Level = LogLevel.Error, Message = "Error starting background-task for watching JSON jobs file")]
    public static partial void FileWatchStartFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6323, Level = LogLevel.Error, Message = "Could not schedule jobs and triggers from JSON file {FileName}")]
    public static partial void FileProcessingFailed(this ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 6324, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of error")]
    public static partial void ListenerNotificationOfErrorFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6325, Level = LogLevel.Warning, Message = "File named '{FileName}' does not exist")]
    public static partial void FileNotFound(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 6326, Level = LogLevel.Warning, Message = "Error closing jobs file {FileName}")]
    public static partial void FileCloseFailed(this ILogger logger, string fileName, Exception exception);
}
