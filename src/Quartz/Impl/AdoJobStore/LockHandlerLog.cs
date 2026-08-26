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
/// Every event the lock handlers log, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 3700-3799 belong to this area. The handlers share most of their vocabulary — a lock is
/// desired, obtained, given, returned — so a message that several of them spell identically is one
/// event here rather than one per handler: the event is what happened to the lock, and which handler
/// implements it is the logger's category. Where two handlers word the same moment differently, the
/// wording is preserved and they are two events, because the message text is what a structured-logging
/// consumer already filters on.
/// </para>
/// <para>
/// An id, once given out, is what an operator filters and alerts on, so it is never reused for a
/// different event and never renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed
/// diff.
/// </para>
/// </remarks>
internal static partial class LockHandlerLog
{
    [LoggerMessage(EventId = 3700, Level = LogLevel.Debug, Message = "Lock '{LockName}' is desired by: {RequestorId}")]
    public static partial void LockDesired(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3701, Level = LogLevel.Debug, Message = "Lock '{LockName}' given to: {RequestorId}")]
    public static partial void LockGiven(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3702, Level = LogLevel.Debug, Message = "Lock '{LockName}' Is already owned by: {RequestorId}")]
    public static partial void LockAlreadyHeld(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3703, Level = LogLevel.Debug, Message = "Lock '{LockName}' returned by: {RequestorId}")]
    public static partial void LockReturned(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3704, Level = LogLevel.Warning, Message = "Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!")]
    public static partial void LockReturnedByNonOwner(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3705, Level = LogLevel.Warning, Message = "stack-trace of wrongful returner: {Stacktrace}")]
    public static partial void WrongfulReturnerStack(this ILogger logger, string stacktrace);

    [LoggerMessage(EventId = 3706, Level = LogLevel.Debug, Message = "Lock '{LockName}' is being obtained: {RequestorId}")]
    public static partial void LockBeingObtained(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3707, Level = LogLevel.Debug, Message = "Inserting new lock row for lock: '{LockName}' being obtained by thread: {RequestorId}")]
    public static partial void LockRowInsertingForThread(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3708, Level = LogLevel.Debug, Message = "Lock '{LockName}' was not obtained by: {RequestorId}{RetryMessage}")]
    public static partial void LockNotObtainedWithRetryNote(this ILogger logger, string lockName, Guid requestorId, string retryMessage);

    [LoggerMessage(EventId = 3709, Level = LogLevel.Debug, Message = "Lock '{LockName}' already owned by: {RequestorId} -- but not owner!")]
    public static partial void LockAlreadyOwnedByOther(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3710, Level = LogLevel.Debug, Message = "stack-trace of wrongful returner: {StackTrace}")]
    public static partial void WrongfulReturnerStackDebug(this ILogger logger, string stackTrace);

    [LoggerMessage(EventId = 3711, Level = LogLevel.Debug, Message = "Lock '{LockName}' was not obtained by: {RequestorId}")]
    public static partial void LockNotObtained(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3712, Level = LogLevel.Debug, Message = "Lock '{LockName}' reentrant acquisition by: {RequestorId} (count: {LockCount})")]
    public static partial void LockReentrantAcquisition(this ILogger logger, string lockName, Guid requestorId, int lockCount);

    [LoggerMessage(EventId = 3713, Level = LogLevel.Debug, Message = "Lock '{LockName}' reentrant release by: {RequestorId} (remaining: {LockCount})")]
    public static partial void LockReentrantRelease(this ILogger logger, string lockName, Guid requestorId, int lockCount);

    [LoggerMessage(EventId = 3714, Level = LogLevel.Debug, Message = "Lock '{LockName}' was not obtained by: {RequestorId} - will try again.")]
    public static partial void LockNotObtainedWillRetry(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 3715, Level = LogLevel.Debug, Message = "Inserting new lock row for lock: '{LockName}' being obtained: {RequestorId}")]
    public static partial void LockRowInserting(this ILogger logger, string lockName, Guid requestorId);
}
