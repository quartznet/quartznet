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

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Every event the HTTP API logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 9000-9099 belong to this area. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered; the
/// <c>LogEventCatalogTest</c> in <c>Quartz.Tests.AspNetCore</c> makes a change to one a reviewed diff.
/// </para>
/// <para>
/// All five are raised while turning an exception into the problem details a request is answered with,
/// and the level says who has to act: a request the caller got wrong is Debug, a scheduler that
/// refused is Warning, and anything else is a server fault at Error.
/// </para>
/// </remarks>
internal static partial class HttpApiLog
{
    [LoggerMessage(EventId = 9000, Level = LogLevel.Debug, Message = "BadHttpRequestException thrown")]
    public static partial void BadHttpRequest(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Debug, Message = "Failed to deserialize request")]
    public static partial void RequestDeserializationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Debug, Message = "NotFoundException thrown")]
    public static partial void NotFound(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "SchedulerException thrown when handling api request to url {Url}")]
    public static partial void SchedulerExceptionHandlingRequest(this ILogger logger, string url, Exception exception);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Error, Message = "Exception thrown when handling api request to url {Url}")]
    public static partial void ExceptionHandlingRequest(this ILogger logger, string url, Exception exception);
}
