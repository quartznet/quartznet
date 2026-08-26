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

namespace Quartz.Plugins.History;

/// <summary>
/// Every event the history plugins log, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 6000-6199 belong to this area and are allocated in file order:
/// <see cref="LoggingJobHistoryPlugin" /> (6000-6009) and
/// <see cref="LoggingTriggerHistoryPlugin" /> (6010-6019). An id, once given out, is what an operator
/// filters and alerts on, so it is never reused for a different event and never renumbered;
/// <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// <para>
/// Every template here is <c>"{Message}"</c>, which is degenerate on purpose. What these two plugins
/// log is a <see cref="string.Format(IFormatProvider, string, object?[])" /> template the user
/// configures - <c>JobSuccessMessage</c> and its siblings, with <c>{0}</c>-style placeholders - so the
/// text is only known at run time and cannot be a compile-time template. Formatting it and passing the
/// result through one event is what the rendered message always was; the id is what is new. The
/// <c>StructuredLogging*HistoryPlugin</c> pair, whose configured templates carry named placeholders a
/// structured sink resolves for itself, keeps its direct calls for that reason and is recorded in
/// <c>LogCallSiteTest</c>'s allow-list.
/// </para>
/// </remarks>
internal static partial class HistoryPluginLog
{
    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "{Message}")]
    public static partial void JobToBeFired(this ILogger logger, string message);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "{Message}")]
    public static partial void JobSucceeded(this ILogger logger, string message);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "{Message}")]
    public static partial void JobFailed(this ILogger logger, string message, Exception exception);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Information, Message = "{Message}")]
    public static partial void JobVetoed(this ILogger logger, string message);

    [LoggerMessage(EventId = 6010, Level = LogLevel.Information, Message = "{Message}")]
    public static partial void TriggerFired(this ILogger logger, string message);

    [LoggerMessage(EventId = 6011, Level = LogLevel.Information, Message = "{Message}")]
    public static partial void TriggerMisfired(this ILogger logger, string message);

    [LoggerMessage(EventId = 6012, Level = LogLevel.Information, Message = "{Message}")]
    public static partial void TriggerCompleted(this ILogger logger, string message);
}
