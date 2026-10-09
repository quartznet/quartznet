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

using Microsoft.AspNetCore.SignalR.Client;

namespace Quartz.Impl;

/// <summary>
/// How the agent's connection reconnects after it drops: at once, then after two, ten and thirty seconds,
/// and every thirty seconds after that, for ever.
/// </summary>
/// <remarks>
/// The SignalR client's own policy gives up after four attempts, which for a browser tab is right and
/// for an agent is wrong: a worker that outlives a dashboard restart has to be on the dashboard when it
/// comes back, however long that takes. This never answers <see langword="null" />.
/// </remarks>
internal sealed class AgentRetryPolicy : IRetryPolicy
{
    public static readonly AgentRetryPolicy Instance = new();

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    private AgentRetryPolicy()
    {
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        ArgumentNullException.ThrowIfNull(retryContext);

        long attempt = retryContext.PreviousRetryCount;
        return attempt < Delays.Length ? Delays[attempt] : Delays[^1];
    }
}
