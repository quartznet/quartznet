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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Every event misfire handling logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// Event ids 3600-3699 belong to this area and are allocated in file order: the misfire region of
/// <see cref="AdoJobStoreBase" /> first, then <see cref="MisfireHandler" />. An id, once given out, is
/// what an operator filters and alerts on, so it is never reused for a different event and never
/// renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </remarks>
internal static partial class MisfireLog
{
    [LoggerMessage(EventId = 3600, Level = LogLevel.Information, Message = "Handling the first {Count} triggers that missed their scheduled fire-time. More misfired triggers remain to be processed.")]
    public static partial void HandlingFirstMisfiredTriggers(this ILogger logger, int count);

    [LoggerMessage(EventId = 3601, Level = LogLevel.Information, Message = "Handling {Count} trigger(s) that missed their scheduled fire-time.")]
    public static partial void HandlingMisfiredTriggers(this ILogger logger, int count);

    [LoggerMessage(EventId = 3602, Level = LogLevel.Debug, Message = "Found 0 triggers that missed their scheduled fire-time.")]
    public static partial void NoMisfiredTriggers(this ILogger logger);

    [LoggerMessage(EventId = 3603, Level = LogLevel.Error, Message = "Error preparing misfire update for trigger: '{TriggerKey}'")]
    public static partial void MisfireUpdatePreparationFailed(this ILogger logger, TriggerKey triggerKey, Exception exception);

    [LoggerMessage(EventId = 3604, Level = LogLevel.Error, Message = "Error updating {Count} misfired trigger(s)")]
    public static partial void MisfiredTriggerUpdateFailed(this ILogger logger, int count, Exception exception);

    [LoggerMessage(EventId = 3605, Level = LogLevel.Debug, Message = "Found {MisfireCount} triggers that missed their scheduled fire-time.")]
    public static partial void MisfiredTriggersCounted(this ILogger logger, int misfireCount);

    [LoggerMessage(EventId = 3606, Level = LogLevel.Debug, Message = "Scanning for misfires...")]
    public static partial void ScanningForMisfires(this ILogger logger);

    [LoggerMessage(EventId = 3607, Level = LogLevel.Error, Message = "Error handling misfires: {ExceptionMessage}")]
    public static partial void MisfireHandlingFailed(this ILogger logger, string exceptionMessage, Exception exception);
}
