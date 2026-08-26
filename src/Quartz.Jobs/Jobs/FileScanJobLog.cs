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
/// Every event the file scan job logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 7100-7199 belong to this area. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered;
/// <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class FileScanJobLog
{
    [LoggerMessage(EventId = 7100, Level = LogLevel.Warning, Message = "File '{FileName}' does not exist.")]
    public static partial void FileDoesNotExist(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Information, Message = "File '{FileName}' updated, notifying listener.")]
    public static partial void FileUpdated(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Debug, Message = "File '{FileName}' unchanged.")]
    public static partial void FileUnchanged(this ILogger logger, string fileName);
}
