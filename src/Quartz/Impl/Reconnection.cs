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

namespace Quartz.Impl;

/// <summary>
/// How long a reader of another process waits before it tries that process again.
/// </summary>
/// <remarks>
/// A second, doubling to thirty. Short enough that a restarted worker is picked up while an operator is
/// still looking at the page, and bounded so that an unreachable target is asked twice a minute rather
/// than continuously. The HTTP event reader reopens its stream on it, and the dashboard agent dials the
/// dashboard again on it.
/// </remarks>
internal static class Reconnection
{
    /// <summary>
    /// How long to wait before the first retry.
    /// </summary>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The most the wait grows to.
    /// </summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The wait after <paramref name="current" />: doubled, and no longer than <see cref="MaxRetryDelay" />.
    /// </summary>
    internal static TimeSpan Next(TimeSpan current)
    {
        if (current >= MaxRetryDelay)
        {
            return MaxRetryDelay;
        }

        TimeSpan doubled = current + current;
        return doubled < MaxRetryDelay ? doubled : MaxRetryDelay;
    }
}
