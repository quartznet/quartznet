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

namespace Quartz.Plugins.Xml;

/// <summary>
/// Every event the XML scheduling data plugin logs, as source-generated methods with a pinned
/// event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 6200-6299 belong to this area. The processor these events surround lives in the core
/// package and draws from 5000-5099 (<c>Quartz.Xml.XmlSchedulingLog</c>); this class covers the
/// plugin that feeds it files. An id, once given out, is what an operator filters and alerts on, so
/// it is never reused for a different event and never renumbered; <c>LogEventCatalogTest</c> makes a
/// change to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class XmlSchedulingPluginLog
{
    [LoggerMessage(EventId = 6200, Level = LogLevel.Information, Message = "Registering Quartz Job Initialization Plug-in.")]
    public static partial void PluginRegistered(this ILogger logger);

    [LoggerMessage(EventId = 6201, Level = LogLevel.Debug, Message = "Scheduled file scan job for data file: {FileName}, at interval: {ScanInterval}")]
    public static partial void FileScanJobScheduled(this ILogger logger, string fileName, TimeSpan scanInterval);

    [LoggerMessage(EventId = 6202, Level = LogLevel.Error, Message = "Error starting background-task for watching jobs file.")]
    public static partial void FileWatchStartFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6203, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of error")]
    public static partial void ListenerNotificationOfErrorFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6204, Level = LogLevel.Error, Message = "Original error while notifying scheduler listeners: {Message}")]
    public static partial void OriginalErrorForNotification(this ILogger logger, string message, Exception exception);

    [LoggerMessage(EventId = 6205, Level = LogLevel.Error, Message = "Could not schedule jobs and triggers from file {FileName}: {Message}")]
    public static partial void FileProcessingFailed(this ILogger logger, string fileName, string message, Exception exception);

    [LoggerMessage(EventId = 6206, Level = LogLevel.Warning, Message = "File named '{FileName}' does not exist.")]
    public static partial void FileNotFound(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 6207, Level = LogLevel.Warning, Message = "Error closing jobs file {FileName}")]
    public static partial void FileCloseFailed(this ILogger logger, string fileName, Exception exception);
}
