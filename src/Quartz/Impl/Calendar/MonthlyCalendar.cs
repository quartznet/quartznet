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
/// This implementation of the Calendar excludes a set of days of the month. You
/// may use it to exclude every 1. of each month for example. But you may define
/// any day of a month.
/// </summary>
/// <seealso cref="ICalendar" />
/// <seealso cref="BaseCalendar" />
/// <author>Juergen Donnerstag</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class MonthlyCalendar : BaseCalendar, IEquatable<MonthlyCalendar>
{
    private const int MaxDaysInMonth = 31;

    // The days of month which are to be excluded, 1 through 31.
    private HashSet<int> excludeDays = new HashSet<int>();

    /// <summary>
    /// Initializes a new instance of the <see cref="MonthlyCalendar"/> class.
    /// </summary>
    public MonthlyCalendar()
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="baseCalendar">The base calendar.</param>
    public MonthlyCalendar(ICalendar baseCalendar) : base(baseCalendar)
    {
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private MonthlyCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
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
                // The days have always been stored as a bool array indexed by day-of-month minus
                // one; keep reading that shape and fold it into the set.
                var stored = (bool[]) info.GetValue("excludeDays", typeof(bool[]))!;
                for (int i = 0; i < stored.Length && i < MaxDaysInMonth; i++)
                {
                    if (stored[i])
                    {
                        excludeDays.Add(i + 1);
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
        bool[] stored = new bool[MaxDaysInMonth];
        foreach (int day in excludeDays)
        {
            stored[day - 1] = true;
        }

        info.AddValue("version", 1);
        info.AddValue("excludeDays", stored);
        info.AddValue("excludeAll", AreAllDaysExcluded());
    }

    /// <summary>
    /// The days of the month excluded by this calendar, 1 through 31.
    /// </summary>
    public IReadOnlySet<int> DaysExcluded => excludeDays;

    /// <summary>
    /// Excludes the given day of every month.
    /// </summary>
    /// <param name="day">The day of the month, 1 through 31.</param>
    /// <returns><see langword="true" /> if the day was not already excluded.</returns>
    public bool AddExcludedDay(int day)
    {
        ValidateDay(day);
        return excludeDays.Add(day);
    }

    /// <summary>
    /// Stops excluding the given day of the month.
    /// </summary>
    /// <param name="day">The day of the month, 1 through 31.</param>
    /// <returns><see langword="true" /> if the day was excluded.</returns>
    public bool RemoveExcludedDay(int day)
    {
        ValidateDay(day);
        return excludeDays.Remove(day);
    }

    /// <summary>
    /// Return true, if day is defined to be excluded.
    /// </summary>
    /// <param name="day">The day of the month, 1 through 31.</param>
    public bool IsDayExcluded(int day)
    {
        ValidateDay(day);
        return excludeDays.Contains(day);
    }

    /// <summary>
    /// Check if all days are excluded. That is no day is included.
    /// </summary>
    public bool AreAllDaysExcluded()
    {
        return excludeDays.Count == MaxDaysInMonth;
    }

    private static void ValidateDay(int day)
    {
        if (day < 1 || day > MaxDaysInMonth)
        {
            Throw.ArgumentException($"The day parameter must be in the range of 1 to {MaxDaysInMonth}");
        }
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
        if (AreAllDaysExcluded())
        {
            return false;
        }

        // Test the base calendar first. Only if the base calendar not already
        // excludes the time/date, continue evaluating this calendar instance.
        if (!base.IsTimeIncluded(timeStampUtc))
        {
            return false;
        }

        timeStampUtc = TimeZones.ConvertTime(timeStampUtc, TimeZone); //apply the timezone

        return !excludeDays.Contains(timeStampUtc.Day);
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
        DateTimeOffset newTimeStamp = TimeZones.StartOfLocalDay(date, TimeZone);

        while (excludeDays.Contains(newTimeStamp.Day))
        {
            date = date.AddDays(1);
            newTimeStamp = TimeZones.StartOfLocalDay(date, TimeZone);
        }

        return newTimeStamp;
    }

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>A new object that is a copy of this instance.</returns>
    public override ICalendar Clone()
    {
        MonthlyCalendar clone = new MonthlyCalendar();
        CloneFields(clone);
        clone.excludeDays = new HashSet<int>(excludeDays);
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

        return excludeDays.Count + 5 * baseHash;
    }

    /// <summary>
    /// Whether this calendar and <paramref name="other" /> exclude the same times.
    /// </summary>
    /// <param name="other">The calendar to compare with.</param>
    public bool Equals(MonthlyCalendar? other)
    {
        //a little trick here : Monthly calendar knows nothing
        //about the precise month it is dealing with, so
        //FebruaryCalendars will be only equal if their
        //31st days are equally included
        //but that's not going to be a problem since
        //there's no need to redefine default value of false
        //for such days
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
        if (obj is not MonthlyCalendar other)
        {
            return false;
        }

        return Equals(other);
    }
}
