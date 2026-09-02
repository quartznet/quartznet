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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

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
public sealed class AnnualCalendar : BaseCalendar, IEquatable<AnnualCalendar>
{
    private SortedSet<MonthDay> excludeDays = new SortedSet<MonthDay>();

    // the year serialized payloads pin their date-shaped values to; a leap year so that
    // February 29th survives the round-trip
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
                        excludeDays.Add(new MonthDay(dateTime.Month, dateTime.Day));
                    }
                }
                else
                {
                    // must be new..
                    foreach (var offset in (List<DateTimeOffset>) o)
                    {
                        excludeDays.Add(new MonthDay(offset.Month, offset.Day));
                    }
                }
                break;
            case 1:
                var dateTimeOffsets = (List<DateTimeOffset>) info.GetValue("excludeDays", typeof(List<DateTimeOffset>))!;
                foreach (var offset in dateTimeOffsets)
                {
                    excludeDays.Add(new MonthDay(offset.Month, offset.Day));
                }
                break;
            case 2:
                var dateTimes = (SortedSet<DateTime>) info.GetValue("excludeDays", typeof(SortedSet<DateTime>))!;
                foreach (var dateTime in dateTimes)
                {
                    excludeDays.Add(new MonthDay(dateTime.Month, dateTime.Day));
                }
                break;
            default:
                Throw.NotSupportedException("Unknown serialization version");
                break;
        }
    }

    /// <summary>
    /// Writes this calendar's fields into a serialization payload.
    /// </summary>
    /// <param name="info">The payload being written.</param>
    /// <param name="context">The serialization context.</param>
    [System.Security.SecurityCritical]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);

        // Keep writing the version 2 layout - a sorted set of DateTime pinned to the fixed year -
        // so a payload written here stays readable by the versions that only know that shape.
        info.AddValue("version", 2);
        info.AddValue("excludeDays", new SortedSet<DateTime>(excludeDays.Select(d => new DateTime(FixedYear, d.Month, d.Day))));
    }

    /// <summary>
    /// The days excluded by this calendar, every year.
    /// </summary>
    public IReadOnlySet<MonthDay> DaysExcluded => excludeDays;

    /// <summary>
    /// Excludes the given day of every year.
    /// </summary>
    /// <returns><see langword="true" /> if the day was not already excluded.</returns>
    public bool AddExcludedDay(MonthDay day)
    {
        return excludeDays.Add(day);
    }

    /// <summary>
    /// Stops excluding the given day.
    /// </summary>
    /// <returns><see langword="true" /> if the day was excluded.</returns>
    public bool RemoveExcludedDay(MonthDay day)
    {
        return excludeDays.Remove(day);
    }

    /// <summary>
    /// Returns <see langword="true" /> if the given day is excluded by this calendar.
    /// </summary>
    public bool IsDayExcluded(MonthDay day)
    {
        return excludeDays.Contains(day);
    }

    private bool IsDateTimeExcluded(DateTimeOffset day, bool checkBaseCalendar)
    {
        // Check baseCalendar first
        if (checkBaseCalendar && !base.IsTimeIncluded(day))
        {
            return true;
        }

        return excludeDays.Contains(new MonthDay(day.Month, day.Day));
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
        dateUtc = TimeZones.ConvertTime(dateUtc, TimeZone);

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
        timeStampUtc = TimeZones.ConvertTime(timeStampUtc, TimeZone);

        // The first instant of the local day, resolved in the zone: a day does not always begin at
        // midnight, and the offset it begins at is not always the offset the queried instant
        // carries. Each further day is reached by naming the next local date and resolving that,
        // because adding a day to a DateTimeOffset keeps the old offset and so drifts by the
        // transition delta the moment the walk crosses one.
        DateOnly date = DateOnly.FromDateTime(timeStampUtc.Date);
        DateTimeOffset day = TimeZones.StartOfLocalDay(date, TimeZone);

        if (!IsDateTimeExcluded(day, checkBaseCalendar: true))
        {
            // return the original value
            return timeStampUtc;
        }

        while (IsDateTimeExcluded(day, checkBaseCalendar: true))
        {
            date = date.AddDays(1);
            day = TimeZones.StartOfLocalDay(date, TimeZone);
        }

        return day;
    }

    /// <inheritdoc />
    [SuppressMessage("Sonar", "S2328:GetHashCode should not reference mutable fields", Justification = "Content equality over mutable state is what a calendar is; see BaseCalendar.GetHashCode.")]
    public override int GetHashCode()
    {
        int baseHash = 13;
        if (CalendarBase is not null)
        {
            baseHash = CalendarBase.GetHashCode();
        }

        return excludeDays.Count + 5 * baseHash;
    }

    /// <summary>
    /// Whether this calendar and <paramref name="other" /> exclude the same times.
    /// </summary>
    /// <param name="other">The calendar to compare with.</param>
    public bool Equals(AnnualCalendar? other)
    {
        if (other is null)
        {
            return false;
        }

        bool toReturn = CalendarBase is null || CalendarBase.Equals(other.CalendarBase);

        return toReturn && excludeDays.SetEquals(other.excludeDays);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not AnnualCalendar other)
        {
            return false;
        }

        return Equals(other);
    }

    /// <inheritdoc />
    public override ICalendar Clone()
    {
        var clone = new AnnualCalendar();
        CloneFields(clone);
        clone.excludeDays = new SortedSet<MonthDay>(excludeDays);
        return clone;
    }
}
