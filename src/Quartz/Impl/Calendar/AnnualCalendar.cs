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

using System.Collections;
using System.Runtime.Serialization;

using Quartz.Util;

namespace Quartz.Impl.Calendar;

/// <summary>
/// This implementation of the Calendar excludes a set of days of the year. You
/// may use it to exclude bank holidays which are on the same date every year.
/// </summary>
/// <seealso cref="ICalendar" />
/// <seealso cref="BaseCalendar" />
/// <author>Juergen Donnerstag</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class AnnualCalendar : BaseCalendar
{
    private SortedSet<DateOnly> excludeDays = new SortedSet<DateOnly>();

    // year to use as fixed year
    private const int FixedYear = 2000;

    /// <summary>
    /// Constructor
    /// </summary>
    public AnnualCalendar()
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="baseCalendar">The base calendar.</param>
    public AnnualCalendar(ICalendar baseCalendar) : base(baseCalendar)
    {
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private AnnualCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        int version;
        try
        {
            version = info.GetInt32("version");
        }
        catch
        {
            version = 0;
        }

        switch (version)
        {
            case 0:
                // 1.x
                object o = info.GetValue("excludeDays", typeof(object))!;
                if (o is ArrayList oldFormat)
                {
#pragma warning disable 8605
                    foreach (DateTime dateTime in oldFormat)
#pragma warning restore 8605
                    {
                        excludeDays.Add(Normalize(DateOnly.FromDateTime(dateTime)));
                    }
                }
                else
                {
                    // must be new..
                    foreach (var offset in (List<DateTimeOffset>) o)
                    {
                        excludeDays.Add(Normalize(DateOnly.FromDateTime(offset.Date)));
                    }
                }
                break;
            case 1:
                var dateTimeOffsets = (List<DateTimeOffset>) info.GetValue("excludeDays", typeof(List<DateTimeOffset>))!;
                foreach (var offset in dateTimeOffsets)
                {
                    excludeDays.Add(Normalize(DateOnly.FromDateTime(offset.Date)));
                }
                break;
            case 2:
                var dateTimes = (SortedSet<DateTime>) info.GetValue("excludeDays", typeof(SortedSet<DateTime>))!;
                foreach (var dateTime in dateTimes)
                {
                    excludeDays.Add(Normalize(DateOnly.FromDateTime(dateTime)));
                }
                break;
            default:
                Throw.NotSupportedException("Unknown serialization version");
                break;
        }
    }

    [System.Security.SecurityCritical]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);

        // Keep writing the version 2 layout - a sorted set of DateTime - so a payload written here
        // stays readable by the versions that only know that shape.
        info.AddValue("version", 2);
        info.AddValue("excludeDays", new SortedSet<DateTime>(excludeDays.Select(d => d.ToDateTime(TimeOnly.MinValue))));
    }

    /// <summary>
    /// The days excluded by this calendar.
    /// </summary>
    /// <remarks>
    /// Only the month and the day of a value are significant - the calendar excludes the same
    /// date every year - so the days come back normalized onto a single fixed year.
    /// </remarks>
    public IReadOnlySet<DateOnly> DaysExcluded => excludeDays;

    /// <summary>
    /// Excludes the given day of every year. Only the month and the day are significant.
    /// </summary>
    /// <returns><see langword="true" /> if the day was not already excluded.</returns>
    public bool AddExcludedDay(DateOnly day)
    {
        return excludeDays.Add(Normalize(day));
    }

    /// <summary>
    /// Stops excluding the given day. Only the month and the day are significant.
    /// </summary>
    /// <returns><see langword="true" /> if the day was excluded.</returns>
    public bool RemoveExcludedDay(DateOnly day)
    {
        return excludeDays.Remove(Normalize(day));
    }

    /// <summary>
    /// Returns <see langword="true" /> if the given day is excluded by this calendar. Only the
    /// month and the day are significant.
    /// </summary>
    public bool IsDayExcluded(DateOnly day)
    {
        return excludeDays.Contains(Normalize(day));
    }

    private static DateOnly Normalize(DateOnly day) => new DateOnly(FixedYear, day.Month, day.Day);

    private bool IsDateTimeExcluded(DateTimeOffset day, bool checkBaseCalendar)
    {
        // Check baseCalendar first
        if (checkBaseCalendar && !base.IsTimeIncluded(day))
        {
            return true;
        }

        return excludeDays.Contains(new DateOnly(FixedYear, day.Month, day.Day));
    }

    /// <summary>
    /// Determine whether the given UTC time (in milliseconds) is 'included' by the
    /// Calendar.
    /// <para>
    /// Note that this Calendar is only has full-day precision.
    /// </para>
    /// </summary>
    public override bool IsTimeIncluded(DateTimeOffset dateUtc)
    {
        // Test the base calendar first. Only if the base calendar not already
        // excludes the time/date, continue evaluating this calendar instance.
        if (!base.IsTimeIncluded(dateUtc))
        {
            return false;
        }

        //apply the timezone
        dateUtc = TimeZoneUtil.ConvertTime(dateUtc, TimeZone);

        return !IsDateTimeExcluded(dateUtc, checkBaseCalendar: true);
    }

    /// <summary>
    /// Determine the next UTC time (in milliseconds) that is 'included' by the
    /// Calendar after the given time. Return the original value if timeStampUtc is
    /// included. Return 0 if all days are excluded.
    /// <para>
    /// Note that this Calendar is only has full-day precision.
    /// </para>
    /// </summary>
    public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeStampUtc)
    {
        // Call base calendar implementation first
        DateTimeOffset baseTime = base.GetNextIncludedTimeUtc(timeStampUtc);
        if (baseTime != DateTimeOffset.MinValue && baseTime > timeStampUtc)
        {
            timeStampUtc = baseTime;
        }

        //apply the timezone
        timeStampUtc = TimeZoneUtil.ConvertTime(timeStampUtc, TimeZone);

        // Get timestamp for 00:00:00, in the correct timezone offset
        DateTimeOffset day = new DateTimeOffset(timeStampUtc.Date, timeStampUtc.Offset);

        if (!IsDateTimeExcluded(day, checkBaseCalendar: true))
        {
            // return the original value
            return timeStampUtc;
        }

        while (IsDateTimeExcluded(day, checkBaseCalendar: true))
        {
            day = day.AddDays(1);
        }

        return day;
    }

    public override int GetHashCode()
    {
        int baseHash = 13;
        if (CalendarBase is not null)
        {
            baseHash = CalendarBase.GetHashCode();
        }

        return excludeDays.Count + 5 * baseHash;
    }

    public bool Equals(AnnualCalendar obj)
    {
        if (obj is null)
        {
            return false;
        }

        bool toReturn = CalendarBase is null || CalendarBase.Equals(obj.CalendarBase);

        return toReturn && excludeDays.SetEquals(obj.excludeDays);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not AnnualCalendar other)
        {
            return false;
        }

        return Equals(other);
    }

    public override ICalendar Clone()
    {
        var clone = new AnnualCalendar();
        CloneFields(clone);
        clone.excludeDays = new SortedSet<DateOnly>(excludeDays);
        return clone;
    }
}
