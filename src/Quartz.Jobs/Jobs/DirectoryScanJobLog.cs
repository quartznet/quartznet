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

namespace Quartz.Jobs;

/// <summary>
/// Every event the directory scan job logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 7000-7099 belong to this area and are allocated in file order: the job itself
/// (7000-7009) and the model that reads its configuration and finds its listener (7010-7019). An id,
/// once given out, is what an operator filters and alerts on, so it is never reused for a different
/// event and never renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class DirectoryScanJobLog
{
    [LoggerMessage(EventId = 7000, Level = LogLevel.Information, Message = "Directory {DirectoryName} contents updated, notifying listener.")]
    public static partial void DirectoryContentsUpdated(this ILogger logger, string? directoryName);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Debug, Message = "Directory '{Directory}' contents unchanged.")]
    public static partial void DirectoryContentsUnchanged(this ILogger logger, string directory);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Directory '{DirectoryName}' does not exist.")]
    public static partial void DirectoryDoesNotExist(this ILogger logger, string directoryName);

    [LoggerMessage(EventId = 7010, Level = LogLevel.Debug, Message = "Could not load some types from assembly {AssemblyName} while scanning for IDirectoryScanListener")]
    public static partial void SomeTypesNotLoaded(this ILogger logger, string? assemblyName, Exception exception);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Debug, Message = "Could not load assembly {AssemblyName} while scanning for IDirectoryScanListener")]
    public static partial void AssemblyNotLoaded(this ILogger logger, string? assemblyName, Exception exception);
}
