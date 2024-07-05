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

using System.Runtime.Serialization;

using Quartz.Util;

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
public sealed class HolidayCalendar : BaseCalendar
{
    /// <summary>
    /// Returns a collection of dates representing the excluded
    /// days. Only the month, day and year of the returned dates are
    /// significant.
    /// </summary>
    public IReadOnlyCollection<DateTime> ExcludedDates
    {
        get => new HashSet<DateTime>(dates);
        internal set => dates = new SortedSet<DateTime>(value);
    }

    // A sorted set to store the holidays
    private SortedSet<DateTime> dates = new SortedSet<DateTime>();

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
                ThrowHelper.ThrowNotSupportedException("cannot deserialize old version, use latest Quartz 2.x version to re-serialize all HolidayCalendar instances in database");
                break;
            case 2:
                dates = new SortedSet<DateTime>((DateTime[]) info.GetValue("dates", typeof(DateTime[]))!);
                break;
            default:
                ThrowHelper.ThrowNotSupportedException("Unknown serialization version");
                break;
        }
    }

    [System.Security.SecurityCritical]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);

        info.AddValue("version", 2);
        info.AddValue("dates", dates.ToArray());
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
        timeStampUtc = TimeZoneUtil.ConvertTime(timeStampUtc, TimeZone);
        var lookFor = timeStampUtc.Date;
        return !dates.Contains(lookFor);
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
        timeUtc = TimeZoneUtil.ConvertTime(timeUtc, TimeZone);

        // Get timestamp for 00:00:00, with the correct timezone offset
        DateTimeOffset day = new DateTimeOffset(timeUtc.Date, timeUtc.Offset);
        while (!IsTimeIncludedThisCalendar(day) || !base.IsTimeIncluded(timeUtc))
        {
            day = day.AddDays(1);
            timeUtc = timeUtc.AddDays(1);
            //ensure earliest value is assigned to return value
            if (day < timeUtc)
            {
                timeUtc = day;
            }
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
        clone.dates = new SortedSet<DateTime>(dates);
        return clone;
    }

    /// <summary>
    /// Add the given Date to the list of excluded days. Only the month, day and
    /// year of the returned dates are significant.
    /// </summary>
    public void AddExcludedDate(DateTime excludedDateUtc)
    {
        DateTime date = excludedDateUtc.Date;
        dates.Add(date);
    }

    /// <summary>
    /// Removes the excluded date.
    /// </summary>
    /// <param name="dateToRemoveUtc">The date to remove.</param>
    public void RemoveExcludedDate(DateTime dateToRemoveUtc)
    {
        DateTime date = dateToRemoveUtc.Date;
        dates.Remove(date);
    }

    public override int GetHashCode()
    {
        int baseHash = 0;
        if (CalendarBase is not null)
        {
            baseHash = CalendarBase.GetHashCode();
        }

        return ExcludedDates.GetHashCode() + 5 * baseHash;
    }

    public bool Equals(HolidayCalendar obj)
    {
        if (obj is null)
        {
            return false;
        }

        bool baseEqual = CalendarBase is null || CalendarBase.Equals(obj.CalendarBase);

        return baseEqual && ExcludedDates.SequenceEqual(obj.ExcludedDates);
    }

    public override bool Equals(object? obj)
    {
        if (!(obj is HolidayCalendar))
        {
            return false;
        }

        return Equals((HolidayCalendar) obj);
    }
}