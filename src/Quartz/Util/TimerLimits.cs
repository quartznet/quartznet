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

using System.Globalization;

namespace Quartz.Util;

/// <summary>
/// The longest wait a timer will take, which is shorter than the longest wait a <see cref="TimeSpan" />
/// can express.
/// </summary>
/// <remarks>
/// <para>
/// The limit is not Quartz's choice, and a wait past it is not something Quartz can honour: the BCL
/// refuses it with an <see cref="ArgumentOutOfRangeException" /> naming a parameter of whichever method
/// happened to be running, which is never the option the duration was configured on and is often not
/// even the operation the application was performing — the misfire handler's arrived out of
/// <c>Shutdown</c> (#3577). Every configurable duration that ends up in a timer is therefore checked
/// against this while the options are validated, so that the report names the setting and the ceiling.
/// </para>
/// <para>
/// A duration that ends up somewhere else is a separate question and gets a separate answer:
/// <c>IdleWaitTime</c> is spent on a <see cref="SemaphoreSlim" />, which takes a timeout of any length,
/// so it is bounded below and not above. <c>TimerLimitsTest</c> holds both of those facts to what the
/// primitives actually do, because neither is a number the BCL exposes.
/// </para>
/// </remarks>
internal static class TimerLimits
{
    /// <summary>
    /// The longest delay <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)" /> accepts:
    /// <c>uint.MaxValue - 1</c> milliseconds, a little under 50 days.
    /// </summary>
    /// <remarks>
    /// This is <c>System.Threading.Timer.MaxSupportedTimeout</c>, which every timer in the BCL
    /// validates against and none of them exposes.
    /// </remarks>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// The failure reported for a configured duration longer than the primitive it ends up in accepts.
    /// </summary>
    /// <param name="option">The option as it is spelled, which is what a reader goes looking for.</param>
    /// <param name="value">What it was configured to.</param>
    /// <param name="limit">The ceiling, which is <see cref="MaxDelay" /> for everything so far.</param>
    /// <param name="because">
    /// One sentence saying which wait the duration becomes. The ceiling is arbitrary without it: the
    /// number is the BCL's, and nothing about the option's own meaning suggests it.
    /// </param>
    /// <remarks>
    /// Milliseconds and days both, in the shape the surrounding validators already use for their lower
    /// bounds: the millisecond count is the number that has to be compared with a configuration file,
    /// and the day count is the one that says why it is a mistake.
    /// </remarks>
    internal static string TooLong(string option, TimeSpan value, TimeSpan limit, string because)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{option} must be at most {limit.TotalMilliseconds}ms ({limit.TotalDays:0.#} days), was {value.TotalMilliseconds}ms ({value.TotalDays:0.#} days). {because}");
    }

    /// <summary>
    /// Refuses a duration no timer will wait out, where the value arrives somewhere an options validator
    /// cannot see it.
    /// </summary>
    /// <param name="value">The duration, which is about to be waited out.</param>
    /// <param name="option">
    /// The name the duration is configured under, which is what the report has to carry — the parameter
    /// the framework would have named is called <c>delay</c>.
    /// </param>
    /// <remarks>
    /// For the lock handlers, whose retry period has no options type and so no startup validator: the
    /// flat <c>quartz.jobStore.lockHandler.retryPeriod</c> key writes the property by reflection, and the
    /// property binder turns whatever it throws into a <c>SchedulerConfigException</c> naming the key.
    /// </remarks>
    internal static void EnsureWaitable(TimeSpan value, string option)
    {
        if (value < TimeSpan.Zero || value > MaxDelay)
        {
            Throw.ArgumentOutOfRangeException(
                option,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{option} is waited out by a timer, so it is between zero and {MaxDelay.TotalMilliseconds}ms ({MaxDelay.TotalDays:0.#} days); {value.TotalMilliseconds}ms is not."));
        }
    }
}
