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

namespace Quartz.Extensions.Redis;

/// <summary>
/// Every event the Redis lock handler logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 8000-8099 belong to this area. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered;
/// <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// <para>
/// The class is named for the concept rather than for the type that raises the events: the handler is
/// still called <c>RedisSemaphore</c> until #3440 renames it, and an event catalogue that renames
/// itself with its caller is a catalogue that renumbers. The ids and the class name outlive whatever
/// the implementing type is spelled.
/// </para>
/// <para>
/// The vocabulary is the ADO.NET lock handlers' — a lock is desired, obtained, given, returned — and
/// several messages are word for word what <c>LockHandlerLog</c> (3700-3799) raises. They are separate
/// events all the same, because an id names one event raised in one place: an operator filtering 8005
/// is asking about the Redis handler, not about whichever handler happened to say the same sentence.
/// </para>
/// </remarks>
internal static partial class RedisLockHandlerLog
{
    [LoggerMessage(EventId = 8000, Level = LogLevel.Debug, Message = "Lock '{LockName}' is desired by: {RequestorId}")]
    public static partial void LockDesired(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 8001, Level = LogLevel.Debug, Message = "Lock '{LockName}' already owned by: {RequestorId}")]
    public static partial void LockAlreadyOwned(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Debug, Message = "Lock '{LockName}' is being obtained: {RequestorId}")]
    public static partial void LockBeingObtained(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Debug, Message = "Lock '{LockName}' was not obtained by: {RequestorId} - cancelled")]
    public static partial void LockNotObtainedCancelled(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Debug, Message = "Lock '{LockName}' given to: {RequestorId}")]
    public static partial void LockGiven(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 8005, Level = LogLevel.Warning, Message = "Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!")]
    public static partial void LockReturnedByNonOwner(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 8006, Level = LogLevel.Warning, Message = "stack-trace of wrongful returner: {StackTrace}")]
    public static partial void WrongfulReturnerStack(this ILogger logger, string stackTrace);

    [LoggerMessage(EventId = 8007, Level = LogLevel.Debug, Message = "Lock '{LockName}' returned by: {RequestorId}")]
    public static partial void LockReturned(this ILogger logger, string lockName, Guid requestorId);

    [LoggerMessage(EventId = 8008, Level = LogLevel.Warning, Message = "Failed to release Redis lock '{LockName}'")]
    public static partial void LockReleaseFailed(this ILogger logger, string lockName, Exception exception);

    [LoggerMessage(EventId = 8009, Level = LogLevel.Information, Message = "Connecting to Redis")]
    public static partial void ConnectingToRedis(this ILogger logger);
}
