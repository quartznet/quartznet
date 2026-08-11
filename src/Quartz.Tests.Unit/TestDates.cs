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

namespace Quartz.Tests.Unit;

/// <summary>
/// Date shapes the trigger tests need, in local time.
/// </summary>
/// <remarks>
/// These used to be static conveniences on <see cref="DateBuilder" />. They are test scaffolding
/// rather than API: the tests below are about triggers, and only need a compact way to name a
/// wall-clock instant. Production code should say what it means with <see cref="DateTimeOffset" />
/// arithmetic, or build a specific date with <see cref="DateBuilder.Create" />.
/// </remarks>
public static class TestDates
{
    /// <summary>
    /// The given time, on today's date.
    /// </summary>
    public static DateTimeOffset DateOf(int hour, int minute, int second)
    {
        DateTimeOffset now = TimeProvider.System.GetLocalNow();
        return DateOf(hour, minute, second, now.Day, now.Month, now.Year);
    }

    /// <summary>
    /// The given time, on the given day of the current year.
    /// </summary>
    public static DateTimeOffset DateOf(int hour, int minute, int second, int dayOfMonth, int month)
    {
        return DateOf(hour, minute, second, dayOfMonth, month, TimeProvider.System.GetLocalNow().Year);
    }

    /// <summary>
    /// The given time, on the given date.
    /// </summary>
    public static DateTimeOffset DateOf(int hour, int minute, int second, int dayOfMonth, int month, int year)
    {
        DateTime dt = new DateTime(year, month, dayOfMonth, hour, minute, second);
        return new DateTimeOffset(dt, TimeZoneUtil.GetUtcOffset(dt, TimeZoneInfo.Local));
    }

    /// <summary>
    /// The given time, on today's date.
    /// </summary>
    public static DateTimeOffset TodayAt(int hour, int minute, int second) => DateOf(hour, minute, second);

    /// <summary>
    /// Now, rounded up to the next whole minute.
    /// </summary>
    public static DateTimeOffset EvenMinuteDateAfterNow() => EvenMinuteDate(TimeProvider.System.GetLocalNow());

    /// <summary>
    /// The given date, rounded up to the next whole minute.
    /// </summary>
    public static DateTimeOffset EvenMinuteDate(DateTimeOffset date)
    {
        DateTimeOffset d = date.AddMinutes(1);
        return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
    }

    /// <summary>
    /// The given date, rounded down to the whole minute it sits in.
    /// </summary>
    public static DateTimeOffset EvenMinuteDateBefore(DateTimeOffset date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, date.Offset);
    }

    /// <summary>
    /// Now, rounded up to the next whole second.
    /// </summary>
    public static DateTimeOffset EvenSecondDateAfterNow() => EvenSecondDate(TimeProvider.System.GetLocalNow());

    /// <summary>
    /// The given date, rounded up to the next whole second.
    /// </summary>
    public static DateTimeOffset EvenSecondDate(DateTimeOffset date)
    {
        date = date.AddSeconds(1);
        return new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0, date.Offset);
    }

    /// <summary>
    /// Now, plus the given number of the given unit.
    /// </summary>
    public static DateTimeOffset FutureDate(int interval, IntervalUnit unit)
    {
        DateTimeOffset date = TimeProvider.System.GetLocalNow();
        return unit switch
        {
            IntervalUnit.Millisecond => date.AddMilliseconds(interval),
            IntervalUnit.Second => date.AddSeconds(interval),
            IntervalUnit.Minute => date.AddMinutes(interval),
            IntervalUnit.Hour => date.AddHours(interval),
            IntervalUnit.Day => date.AddDays(interval),
            IntervalUnit.Week => date.AddDays(interval * 7),
            IntervalUnit.Month => date.AddMonths(interval),
            IntervalUnit.Year => date.AddYears(interval),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown IntervalUnit")
        };
    }
}
