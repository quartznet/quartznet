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
/// Every event the native job logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 7200-7299 belong to this area. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered;
/// <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// <para>
/// The spawned process's output is one event per stream rather than one event with the stream's name
/// as a placeholder. What follows the <c>&gt;</c> is a line of the child process's own text, which has
/// no structure this package can name, so it is the whole of what the template carries — the same
/// degenerate shape <c>ConfigurationLog.JobPropertyNotSet</c> and <c>HistoryPluginLog</c> take. Which
/// stream a line came out of is then an event id an operator filters on rather than a property value
/// they match, and the two streams already logged at different levels.
/// </para>
/// </remarks>
internal static partial class NativeJobLog
{
    [LoggerMessage(EventId = 7200, Level = LogLevel.Information, Message = "About to run {Command} {Temp}...")]
    public static partial void AboutToRun(this ILogger logger, string command, string temp);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Information, Message = "stdout>{Line}")]
    public static partial void StandardOutputLine(this ILogger logger, string line);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning, Message = "stderr>{Line}")]
    public static partial void StandardErrorLine(this ILogger logger, string line);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Error, Message = "Error consuming {Type} stream of spawned process.")]
    public static partial void StreamConsumptionFailed(this ILogger logger, string type, Exception exception);
}
