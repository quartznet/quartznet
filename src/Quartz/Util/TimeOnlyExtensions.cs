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

namespace Quartz.Util;

/// <summary>
/// Helpers for the <see cref="TimeOnly" /> values that describe a trigger's or a calendar's
/// daily time window.
/// </summary>
internal static class TimeOnlyExtensions
{
    /// <summary>
    /// Returns the date of <paramref name="dateTime" /> with its time of day replaced by
    /// <paramref name="timeOfDay" />.
    /// </summary>
    /// <remarks>
    /// The returned value inherits the offset carried by <paramref name="dateTime" /> without
    /// consulting any time zone. Around a daylight saving transition that inherited offset can be
    /// wrong for the produced wall-clock time (see #3190), so callers that cross transitions must
    /// re-resolve the result, for example with <see cref="TimeZones.ResolveLocal" />.
    /// </remarks>
    internal static DateTimeOffset OnDate(this TimeOnly timeOfDay, DateTimeOffset dateTime)
    {
        return new DateTimeOffset(dateTime.Date, dateTime.Offset).Add(timeOfDay.ToTimeSpan());
    }

    /// <summary>
    /// Returns the date of <paramref name="dateTime" /> with its time of day replaced by
    /// <paramref name="timeOfDay" />, or <see langword="null" /> when no date was given.
    /// </summary>
    internal static DateTimeOffset? OnDate(this TimeOnly timeOfDay, DateTimeOffset? dateTime)
    {
        return dateTime is null ? null : timeOfDay.OnDate(dateTime.Value);
    }

    /// <summary>
    /// Throws when <paramref name="value" /> carries precision finer than a whole second.
    /// </summary>
    /// <remarks>
    /// A daily time interval trigger stores its window as hour, minute and second columns, so any
    /// finer component would be silently lost the moment the trigger is persisted. Rejecting it is
    /// how the caller finds out.
    /// </remarks>
    internal static void ValidateWholeSeconds(TimeOnly value, string paramName)
    {
        if (value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            Throw.ArgumentException($"Time of day {value:HH:mm:ss.fffffff} must be a whole number of seconds; a daily time interval trigger is stored with one-second resolution.", paramName);
        }
    }

    /// <summary>
    /// Throws when <paramref name="value" /> carries precision finer than a whole millisecond.
    /// </summary>
    /// <remarks>
    /// <see cref="Quartz.Impl.Calendar.DailyCalendar" /> keeps its range with millisecond
    /// resolution, which is also what its serialized form carries.
    /// </remarks>
    internal static void ValidateWholeMilliseconds(TimeOnly value, string paramName)
    {
        if (value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            Throw.ArgumentException($"Time of day {value:HH:mm:ss.fffffff} must be a whole number of milliseconds; a daily calendar range is kept with one-millisecond resolution.", paramName);
        }
    }
}
