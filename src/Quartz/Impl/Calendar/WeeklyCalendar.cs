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

namespace Quartz.Impl.Calendar;

/// <summary>
/// This implementation of the Calendar excludes a set of days of the week. You
/// may use it to exclude weekends for example. But you may define any day of
/// the week. By default it excludes Saturday and Sunday.
/// </summary>
/// <seealso cref="ICalendar" />
/// <seealso cref="BaseCalendar" />
/// <author>Juergen Donnerstag</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class WeeklyCalendar : BaseCalendar, IEquatable<WeeklyCalendar>
{
    private const int DaysInWeek = 7;

    // The week days which are to be excluded.
    private HashSet<DayOfWeek> excludeDays = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>
    /// Initializes a new instance of the <see cref="WeeklyCalendar"/> class, excluding
    /// <see cref="DayOfWeek.Saturday" /> and <see cref="DayOfWeek.Sunday" />.
    /// </summary>
    public WeeklyCalendar()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeeklyCalendar"/> class, excluding
    /// <see cref="DayOfWeek.Saturday" /> and <see cref="DayOfWeek.Sunday" />.
    /// </summary>
    /// <param name="baseCalendar">The base calendar.</param>
    public WeeklyCalendar(ICalendar baseCalendar) : base(baseCalendar)
    {
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private WeeklyCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
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
                // The days have always been stored as a bool array indexed by DayOfWeek; keep
                // reading that shape and fold it into the set.
                var stored = (bool[]) info.GetValue("excludeDays", typeof(bool[]))!;
                excludeDays = new HashSet<DayOfWeek>();
                for (int i = 0; i < stored.Length && i < DaysInWeek; i++)
                {
                    if (stored[i])
                    {
                        excludeDays.Add((DayOfWeek) i);
                    }
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

        // Keep writing the bool-array layout so a payload written here stays readable by the
        // versions that only know that shape.
        bool[] stored = new bool[DaysInWeek];
        foreach (DayOfWeek day in excludeDays)
        {
            stored[(int) day] = true;
        }

        info.AddValue("version", 1);
        info.AddValue("excludeDays", stored);
        info.AddValue("excludeAll", AreAllDaysExcluded());
    }

    /// <summary>
    /// The days of the week excluded by this calendar.
    /// </summary>
    public IReadOnlySet<DayOfWeek> DaysExcluded => excludeDays;

    /// <summary>
    /// Excludes the given day of every week.
    /// </summary>
    /// <returns><see langword="true" /> if the day was not already excluded.</returns>
    public bool AddExcludedDay(DayOfWeek day)
    {
        return excludeDays.Add(day);
    }

    /// <summary>
    /// Stops excluding the given day of the week.
    /// </summary>
    /// <returns><see langword="true" /> if the day was excluded.</returns>
    public bool RemoveExcludedDay(DayOfWeek day)
    {
        return excludeDays.Remove(day);
    }

    /// <summary>
    /// Return true, if the given day of the week is defined to be excluded.
    /// </summary>
    public bool IsDayExcluded(DayOfWeek day)
    {
        return excludeDays.Contains(day);
    }

    /// <summary>
    /// Check if all week days are excluded. That is no day is included.
    /// </summary>
    public bool AreAllDaysExcluded()
    {
        return excludeDays.Count == DaysInWeek;
    }

    /// <summary>
    /// Determine whether the given time (in milliseconds) is 'included' by the
    /// Calendar.
    /// <para>
    /// Note that this Calendar is only has full-day precision.
    /// </para>
    /// </summary>
    public override bool IsTimeIncluded(DateTimeOffset timeUtc)
    {
        if (AreAllDaysExcluded())
        {
            return false;
        }

        // Test the base calendar first. Only if the base calendar not already
        // excludes the time/date, continue evaluating this calendar instance.
        if (!base.IsTimeIncluded(timeUtc))
        {
            return false;
        }

        timeUtc = TimeZones.ConvertTime(timeUtc, TimeZone); //apply the timezone
        return !excludeDays.Contains(timeUtc.DayOfWeek);
    }

    /// <summary>
    /// Determine the next time (in milliseconds) that is 'included' by the
    /// Calendar after the given time. Return the original value if timeStamp is
    /// included. Return DateTime.MinValue if all days are excluded.
    /// <para>
    /// Note that this Calendar is only has full-day precision.
    /// </para>
    /// </summary>
    public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        if (AreAllDaysExcluded())
        {
            return DateTimeOffset.MinValue;
        }

        // Call base calendar implementation first
        DateTimeOffset baseTime = base.GetNextIncludedTimeUtc(timeUtc);
        if (baseTime != DateTimeOffset.MinValue && baseTime > timeUtc)
        {
            timeUtc = baseTime;
        }

        //apply the timezone
        timeUtc = TimeZones.ConvertTime(timeUtc, TimeZone);

        // The first instant of the local day, resolved in the zone: a day does not always begin at
        // midnight, and the offset it begins at is not always the offset the queried instant
        // carries. Each further day is reached by naming the next local date and resolving that,
        // because adding a day to a DateTimeOffset keeps the old offset and so drifts by the
        // transition delta the moment the walk crosses one.
        DateOnly date = DateOnly.FromDateTime(timeUtc.Date);
        DateTimeOffset d = TimeZones.StartOfLocalDay(date, TimeZone);

        while (excludeDays.Contains(d.DayOfWeek))
        {
            date = date.AddDays(1);
            d = TimeZones.StartOfLocalDay(date, TimeZone);
        }

        return d;
    }

    /// <inheritdoc />
    public override ICalendar Clone()
    {
        WeeklyCalendar clone = new WeeklyCalendar();
        CloneFields(clone);
        clone.excludeDays = new HashSet<DayOfWeek>(excludeDays);
        return clone;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        int baseHash = 0;
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
    public bool Equals(WeeklyCalendar? other)
    {
        if (other is null)
        {
            return false;
        }
        bool baseEqual = CalendarBase is null || CalendarBase.Equals(other.CalendarBase);

        return baseEqual && excludeDays.SetEquals(other.excludeDays);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not WeeklyCalendar other)
        {
            return false;
        }

        return Equals(other);
    }
}
