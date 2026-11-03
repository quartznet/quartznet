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

namespace Quartz.Dashboard.Host;

/// <summary>
/// Every event the dashboard host logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 9400-9499 belong to the host. An id, once given out, is what an operator filters and alerts
/// on, so it is never reused for a different event and never renumbered; the <c>LogEventCatalogTest</c> in
/// <c>Quartz.Tests.AspNetCore</c> makes a change to one a reviewed diff.
/// </para>
/// <para>
/// No message carries a token, a client secret, a connection string or a URL's query: a sign-in names the
/// role it was given, and a target its name and its address without credentials.
/// </para>
/// </remarks>
internal static partial class DashboardHostLog
{
    /// <summary>
    /// The one category the host logs under.
    /// </summary>
    internal const string Category = "Quartz.Dashboard.Host";

    [LoggerMessage(EventId = 9400, Level = LogLevel.Information, Message = "Dashboard host started at {DashboardPath}: authentication {Mode}, {HttpTargets} HTTP target(s), {StoreTargets} attached store(s), accepting agents: {AcceptsAgents}")]
    public static partial void Started(this ILogger logger, string dashboardPath, string mode, int httpTargets, int storeTargets, bool acceptsAgents);

    /// <remarks>
    /// At every start, not once: the configuration that serves every scheduler to anyone who can reach the
    /// port should never be quiet about it.
    /// </remarks>
    [LoggerMessage(EventId = 9401, Level = LogLevel.Warning, Message = "Authentication is Unsecured: every visitor is signed in, without a credential, at {Access}. Use Token or OpenIdConnect anywhere a visitor is not already trusted")]
    public static partial void Unsecured(this ILogger logger, SchedulerAccess access);

    [LoggerMessage(EventId = 9402, Level = LogLevel.Critical, Message = "The dashboard host refuses to start: {Problem}")]
    public static partial void RefusedToStart(this ILogger logger, string problem);

    [LoggerMessage(EventId = 9403, Level = LogLevel.Information, Message = "A visitor signed in as {Role} through {Via}")]
    public static partial void SignedIn(this ILogger logger, string role, string via);

    [LoggerMessage(EventId = 9404, Level = LogLevel.Warning, Message = "A sign-in through {Via} was refused: the token matched no role")]
    public static partial void SignInRefused(this ILogger logger, string via);

    [LoggerMessage(EventId = 9405, Level = LogLevel.Warning, Message = "A token in the address was refused: Quartz:Dashboard:Authentication:AllowTokenInUrl is not set")]
    public static partial void TokenInUrlRefused(this ILogger logger);

    [LoggerMessage(EventId = 9406, Level = LogLevel.Information, Message = "A visitor signed out")]
    public static partial void SignedOut(this ILogger logger);

    [LoggerMessage(EventId = 9407, Level = LogLevel.Information, Message = "HTTP target {Target} reaches scheduler {SchedulerName} at {Address}")]
    public static partial void HttpTarget(this ILogger logger, string target, string schedulerName, string address);

    [LoggerMessage(EventId = 9408, Level = LogLevel.Information, Message = "Store-attached target {Target} reads a {Provider} database with the {Serializer} serializer")]
    public static partial void StoreTarget(this ILogger logger, string target, string provider, string serializer);

    [LoggerMessage(EventId = 9409, Level = LogLevel.Information, Message = "Accepting agents at {HubPath}. Run one instance of the host while it accepts agents")]
    public static partial void AcceptingAgents(this ILogger logger, string hubPath);
}
