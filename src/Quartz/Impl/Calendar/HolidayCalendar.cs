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

using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace Quartz.Impl.Calendar;

/// <summary>
/// This implementation of the Calendar stores a list of holidays (full days
/// that are excluded from scheduling).
/// </summary>
/// <remarks>
/// The implementation DOES take the year into consideration, so if you want to
/// exclude July 4th for the next 10 years, you need to add 10 entries to the
/// exclude list.
/// </remarks>
/// <author>Sharada Jambula</author>
/// <author>Juergen Donnerstag</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class HolidayCalendar : BaseCalendar, IEquatable<HolidayCalendar>
{
    // A sorted set to store the holidays
    private SortedSet<DateOnly> dates = new SortedSet<DateOnly>();

    /// <summary>
    /// Initializes a new instance of the <see cref="HolidayCalendar"/> class.
    /// </summary>
    public HolidayCalendar()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HolidayCalendar"/> class.
    /// </summary>
    /// <param name="baseCalendar">The base calendar.</param>
    public HolidayCalendar(ICalendar baseCalendar)
    {
        CalendarBase = baseCalendar;
    }

    // Make sure that future calendar version changes are done in a DCS-friendly way (with [OnSerializing] and [OnDeserialized] methods).
    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private HolidayCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
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
            case 1:
                Throw.NotSupportedException("cannot deserialize old version, use latest Quartz 2.x version to re-serialize all HolidayCalendar instances in database");
                break;
            case 2:
                // The dates have always been stored as a DateTime array; keep reading that shape.
                var stored = (DateTime[]) info.GetValue("dates", typeof(DateTime[]))!;
                dates = new SortedSet<DateOnly>(stored.Select(DateOnly.FromDateTime));
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

        // Keep writing the version 2 layout - an array of DateTime - so a payload written here
        // stays readable by the versions that only know that shape.
        info.AddValue("version", 2);
        info.AddValue("dates", dates.Select(d => d.ToDateTime(TimeOnly.MinValue)).ToArray());
    }

    /// <summary>
    /// The days excluded by this calendar.
    /// </summary>
    public IReadOnlySet<DateOnly> DaysExcluded => dates;

    /// <summary>
    /// Excludes the given day.
    /// </summary>
    /// <returns><see langword="true" /> if the day was not already excluded.</returns>
    public bool AddExcludedDay(DateOnly day)
    {
        return dates.Add(day);
    }

    /// <summary>
    /// Stops excluding the given day.
    /// </summary>
    /// <returns><see langword="true" /> if the day was excluded.</returns>
    public bool RemoveExcludedDay(DateOnly day)
    {
        return dates.Remove(day);
    }

    /// <summary>
    /// Returns <see langword="true" /> if the given day is excluded by this calendar.
    /// </summary>
    public bool IsDayExcluded(DateOnly day)
    {
        return dates.Contains(day);
    }

    /// <summary>
    /// Determine whether the given time (in milliseconds) is 'included' by the
    /// Calendar.
    /// <para>
    /// Note that this Calendar is only has full-day precision.
    /// </para>
    /// </summary>
    public override bool IsTimeIncluded(DateTimeOffset timeStampUtc)
    {
        if (!base.IsTimeIncluded(timeStampUtc))
        {
            return false;
        }

        return IsTimeIncludedThisCalendar(timeStampUtc);
    }

    private bool IsTimeIncludedThisCalendar(DateTimeOffset timeStampUtc)
    {
        // apply the timezone
        timeStampUtc = TimeZones.ConvertTime(timeStampUtc, TimeZone);
        return !dates.Contains(DateOnly.FromDateTime(timeStampUtc.Date));
    }

    /// <summary>
    /// Determine the next time (in milliseconds) that is 'included' by the
    /// Calendar after the given time.
    /// <para>
    /// Note that this Calendar is only has full-day precision.
    /// </para>
    /// </summary>
    public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        // Call base calendar implementation first
        DateTimeOffset baseTime = base.GetNextIncludedTimeUtc(timeUtc);
        if (timeUtc != DateTimeOffset.MinValue && baseTime > timeUtc)
        {
            timeUtc = baseTime;
        }

        //apply the timezone
        timeUtc = TimeZones.ConvertTime(timeUtc, TimeZone);

        // The first instant of the local day the query lands in, resolved in the zone: a day does
        // not always begin at midnight, and the offset it begins at is not always the offset the
        // queried instant carries. Each further day is reached by naming the next local date and
        // resolving that, because adding a day to a DateTimeOffset keeps the old offset and so
        // drifts by the transition delta the moment the walk crosses one.
        DateOnly date = DateOnly.FromDateTime(timeUtc.Date);
        DateTimeOffset day = TimeZones.StartOfLocalDay(date, TimeZone);

        while (!IsTimeIncludedThisCalendar(day) || !base.IsTimeIncluded(timeUtc))
        {
            date = date.AddDays(1);
            day = TimeZones.StartOfLocalDay(date, TimeZone);
            timeUtc = day;
        }

        return timeUtc;
    }

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>A new object that is a copy of this instance.</returns>
    public override ICalendar Clone()
    {
        HolidayCalendar clone = new HolidayCalendar();
        CloneFields(clone);
        clone.dates = new SortedSet<DateOnly>(dates);
        return clone;
    }

    /// <inheritdoc />
    [SuppressMessage("Sonar", "S2328:GetHashCode should not reference mutable fields", Justification = "Content equality over mutable state is what a calendar is; see BaseCalendar.GetHashCode.")]
    public override int GetHashCode()
    {
        int baseHash = 0;
        if (CalendarBase is not null)
        {
            baseHash = CalendarBase.GetHashCode();
        }

        return dates.Count + 5 * baseHash;
    }

    /// <summary>
    /// Whether this calendar and <paramref name="other" /> exclude the same times.
    /// </summary>
    /// <param name="other">The calendar to compare with.</param>
    public bool Equals(HolidayCalendar? other)
    {
        if (other is null)
        {
            return false;
        }

        bool baseEqual = CalendarBase is null || CalendarBase.Equals(other.CalendarBase);

        return baseEqual && dates.SetEquals(other.dates);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not HolidayCalendar other)
        {
            return false;
        }

        return Equals(other);
    }
}
