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

using System;

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
/// against this where it is configured, so that the report names the setting and the ceiling.
/// </para>
/// <para>
/// A duration that ends up somewhere else is a separate question and gets a separate answer:
/// <c>IdleWaitTime</c> is spent on a <see cref="System.Threading.SemaphoreSlim" />, which takes a
/// timeout of any length, so it is bounded below and not above. <c>TimerLimitsTest</c> holds both of
/// those facts to what the primitives actually do, because neither is a number the BCL exposes.
/// </para>
/// </remarks>
internal static class TimerLimits
{
#if NETFRAMEWORK || NETSTANDARD2_0
    /// <summary>
    /// The longest delay <see cref="System.Threading.Tasks.Task.Delay(TimeSpan)" /> accepts here:
    /// <c>int.MaxValue</c> milliseconds, a little under 25 days.
    /// </summary>
    /// <remarks>
    /// .NET Framework's <c>Task.Delay</c> narrows the duration to an <see cref="int" /> millisecond
    /// count and refuses anything past <see cref="int.MaxValue" />, where .NET 5 and later go on to
    /// <c>System.Threading.Timer.MaxSupportedTimeout</c> — nearly twice as far. A
    /// <c>netstandard2.0</c> assembly can be loaded by either, so it takes the lower of the two:
    /// refusing a wait that would have worked is a configuration error the operator can see, and
    /// accepting one that will not is the failure #3577 reported.
    /// </remarks>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(int.MaxValue);
#else
    /// <summary>
    /// The longest delay <see cref="System.Threading.Tasks.Task.Delay(TimeSpan)" /> accepts:
    /// <c>uint.MaxValue - 1</c> milliseconds, a little under 50 days.
    /// </summary>
    /// <remarks>
    /// This is <c>System.Threading.Timer.MaxSupportedTimeout</c>, which every timer in the BCL
    /// validates against and none of them exposes.
    /// </remarks>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
#endif

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
    /// Milliseconds and days both: the millisecond count is the number that has to be compared with a
    /// configuration file, and the day count is the one that says why it is a mistake.
    /// </remarks>
    internal static string TooLong(string option, TimeSpan value, TimeSpan limit, string because)
    {
        return FormattableString.Invariant(
            $"{option} must be at most {limit.TotalMilliseconds}ms ({limit.TotalDays:0.#} days), was {value.TotalMilliseconds}ms ({value.TotalDays:0.#} days). {because}");
    }

    /// <summary>
    /// Refuses a duration no timer will wait out, where the value is configured.
    /// </summary>
    /// <param name="value">The duration, which will one day be waited out.</param>
    /// <param name="option">
    /// The name the duration is configured under, which is what the report has to carry — the parameter
    /// the framework would have named is called <c>delay</c>.
    /// </param>
    /// <param name="because">One sentence saying which wait the duration becomes.</param>
    /// <remarks>
    /// This branch has no options validators, so the check lives on the property setter the flat
    /// <c>quartz.*</c> key writes by reflection; <c>ObjectUtils.SetObjectProperties</c> turns whatever a
    /// setter throws into a <c>SchedulerConfigException</c> naming the key, so the report carries both
    /// the key and this message.
    /// </remarks>
    internal static void EnsureWaitable(TimeSpan value, string option, string because)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                option,
                FormattableString.Invariant($"{option} must not be negative, was {value.TotalMilliseconds}ms. {because}"));
        }

        if (value > MaxDelay)
        {
            throw new ArgumentOutOfRangeException(option, TooLong(option, value, MaxDelay, because));
        }
    }
}
