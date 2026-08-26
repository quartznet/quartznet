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

namespace Quartz.Impl;

/// <summary>
/// Every event the in-memory job store logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// Event ids 2000-2999 belong to this area and are allocated in file order. An id, once given out, is
/// what an operator filters and alerts on, so it is never reused for a different event and never
/// renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </remarks>
internal static partial class RAMJobStoreLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "RAMJobStore initialized.")]
    public static partial void StoreInitialized(this ILogger logger);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Skipping trigger {TriggerKey}: its job {JobKey} no longer exists")]
    public static partial void TriggerSkippedJobMissing(this ILogger logger, TriggerKey triggerKey, JobKey jobKey);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Trigger {TriggerKey} references calendar '{CalendarName}', which does not exist - the fire was skipped and the trigger will not run until the calendar is added or the reference is cleared.")]
    public static partial void TriggerReferencesMissingCalendar(this ILogger logger, TriggerKey triggerKey, string calendarName);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "Deleting trigger")]
    public static partial void TriggerDeleting(this ILogger logger);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "Deleting cancelled - trigger still active")]
    public static partial void TriggerDeletionCancelled(this ILogger logger);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information, Message = "Trigger {TriggerKey} set to ERROR state.")]
    public static partial void TriggerSetToError(this ILogger logger, TriggerKey triggerKey);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information, Message = "All triggers of Job {JobKey} set to ERROR state.")]
    public static partial void JobTriggersSetToError(this ILogger logger, JobKey jobKey);
}
