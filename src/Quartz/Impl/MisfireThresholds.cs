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
/// The one rule both job stores decide a misfire by.
/// </summary>
/// <remarks>
/// A trigger is misfired once its next fire time is at or before
/// <see cref="MisfireTime(DateTimeOffset, ITrigger, TimeSpan)" />: now less
/// the trigger's own <see cref="ITrigger.MisfireThreshold" />, or the store's where it has none (#4070).
/// </remarks>
internal static class MisfireThresholds
{
    /// <summary>
    /// The latest fire time that counts as misfired at <paramref name="nowUtc" /> under
    /// <paramref name="threshold" />.
    /// </summary>
    /// <remarks>
    /// A threshold that is not positive counts from now, as both stores always have. One longer than the
    /// time since <see cref="DateTimeOffset.MinValue" /> answers that, rather than throwing: a threshold
    /// that long misfires nothing.
    /// </remarks>
    internal static DateTimeOffset MisfireTime(DateTimeOffset nowUtc, TimeSpan threshold)
    {
        if (threshold <= TimeSpan.Zero)
        {
            return nowUtc;
        }

        return nowUtc.UtcTicks - DateTimeOffset.MinValue.UtcTicks <= threshold.Ticks
            ? DateTimeOffset.MinValue
            : nowUtc - threshold;
    }

    /// <summary>
    /// <see cref="MisfireTime(DateTimeOffset, TimeSpan)" /> for <paramref name="trigger" />: its own
    /// threshold where it has one, <paramref name="storeThreshold" /> where it has none.
    /// </summary>
    internal static DateTimeOffset MisfireTime(DateTimeOffset nowUtc, ITrigger trigger, TimeSpan storeThreshold)
    {
        return MisfireTime(nowUtc, trigger.MisfireThreshold ?? storeThreshold);
    }
}
