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

using Quartz.Diagnostics;

namespace Quartz.Impl;

/// <summary>
/// Every event the HTTP client logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 9200-9299 belong to this package. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered; the
/// <c>LogEventCatalogTest</c> in <c>Quartz.Tests.Unit</c> makes a change to one a reviewed diff.
/// </para>
/// <para>
/// All four are about a host newer than this client: something it sent that this client has no name for.
/// Each is logged once per name and client, because a dashboard asks the same page again every few
/// seconds and the answer does not change until somebody upgrades the client.
/// </para>
/// </remarks>
internal static partial class HttpClientLog
{
    /// <summary>
    /// The one category the client logs under, whichever of its readers wrote the line.
    /// </summary>
    internal const string Category = "Quartz.HttpClient";

    /// <summary>
    /// The client's logger when no container handed it one: whatever
    /// <see cref="LogProvider.SetLogProvider" /> was given, or nothing.
    /// </summary>
    internal static ILogger Fallback() => LogProvider.CreateLogger(Category);

    /// <remarks>
    /// Warning: the live view misses these events until the client is upgraded. The subscription stays
    /// open and the events after it are delivered.
    /// </remarks>
    [LoggerMessage(EventId = 9200, Level = LogLevel.Warning, Message = "Skipped live events of kind {Kind} from scheduler {SchedulerName}: this version of Quartz.HttpClient does not know the kind. Upgrade the client to receive them.")]
    public static partial void UnknownEventKindSkipped(this ILogger logger, string kind, string schedulerName);

    /// <remarks>
    /// Warning, with the exception: a frame whose kind is known but whose body could not be read is not a
    /// newer host's doing, and the exception says what was wrong with it.
    /// </remarks>
    [LoggerMessage(EventId = 9201, Level = LogLevel.Warning, Message = "Skipped a live event of kind {Kind} from scheduler {SchedulerName} that could not be read.")]
    public static partial void UnreadableEventSkipped(this ILogger logger, string kind, string schedulerName, Exception exception);

    /// <remarks>
    /// Warning: the item is missing from what the caller sees, and from every page of the listing until
    /// the client is upgraded. A total count, where one was asked for, still counts it.
    /// </remarks>
    [LoggerMessage(EventId = 9202, Level = LogLevel.Warning, Message = "Left a {Item} out of a listing from scheduler {SchedulerName}: its {EnumType} is {Name}, which this version of Quartz.HttpClient does not know. Upgrade the client to list it.")]
    public static partial void UnknownItemLeftOut(this ILogger logger, string item, string schedulerName, string enumType, string name);

    /// <remarks>
    /// Information: nothing was dropped. The value was read as the member the contract keeps for a value
    /// it cannot name, or as absent.
    /// </remarks>
    [LoggerMessage(EventId = 9203, Level = LogLevel.Information, Message = "Read the {EnumType} {Name} from scheduler {SchedulerName} as {ReadAs}: this version of Quartz.HttpClient does not know the name.")]
    public static partial void UnknownNameReadAs(this ILogger logger, string enumType, string name, string schedulerName, string readAs);
}
