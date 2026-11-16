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

namespace Quartz.Trimming.Canary;

/// <summary>
/// How a check waits for what a job signals: for a minute at most, and with no timer left behind.
/// </summary>
internal static class Signal
{
    /// <summary>
    /// Whether <paramref name="signal" /> completes, in whatever state, within a minute of
    /// <paramref name="timeProvider" />'s time, the system's when none is given.
    /// </summary>
    /// <remarks>
    /// <see cref="Task.WaitAsync(TimeSpan, TimeProvider)" /> rather than <c>Task.WhenAny</c> over a
    /// <c>Task.Delay</c>, whose timer runs out the whole minute after the signal arrives (CA2027). A signal
    /// that faulted or was cancelled has arrived all the same, as it had for <c>Task.WhenAny</c>: what it
    /// carries is for the caller, which awaits it.
    /// </remarks>
    public static async Task<bool> Within(Task signal, TimeProvider? timeProvider = null)
    {
        await signal.WaitAsync(TimeSpan.FromSeconds(60), timeProvider ?? TimeProvider.System)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        return signal.IsCompleted;
    }
}
