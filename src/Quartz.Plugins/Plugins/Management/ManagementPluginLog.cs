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

namespace Quartz.Plugins.Management;

/// <summary>
/// Every event the management plugins log, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 6500-6599 belong to this area. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered;
/// <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class ManagementPluginLog
{
    [LoggerMessage(EventId = 6500, Level = LogLevel.Information, Message = "Registering Quartz Shutdown hook '{PluginName}'")]
    public static partial void ShutdownHookRegistered(this ILogger logger, string pluginName);

    [LoggerMessage(EventId = 6501, Level = LogLevel.Information, Message = "Shutting down Quartz...")]
    public static partial void ShuttingDown(this ILogger logger);

    [LoggerMessage(EventId = 6502, Level = LogLevel.Error, Message = "Error shutting down Quartz: {ErrorMessage}")]
    public static partial void ShutdownFailed(this ILogger logger, string errorMessage, Exception exception);
}
