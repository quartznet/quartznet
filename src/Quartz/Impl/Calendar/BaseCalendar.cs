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
/// This implementation of the Calendar may be used (you don't have to) as a
/// base class for more sophisticated one's. It merely implements the base
/// functionality required by each Calendar.
/// </summary>
/// <remarks>
/// Regarded as base functionality is the treatment of base calendars. Base
/// calendar allow you to chain (stack) as much calendars as you may need. For
/// example to exclude weekends you may use WeeklyCalendar. In order to exclude
/// holidays as well you may define a WeeklyCalendar instance to be the base
/// calendar for HolidayCalendar instance.
/// </remarks>
/// <seealso cref="ICalendar" />
/// <author>Juergen Donnerstag</author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public class BaseCalendar : ICalendar, ISerializable, IEquatable<BaseCalendar>
{
    private TimeZoneInfo? timeZone;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
    /// </summary>
    public BaseCalendar()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
    /// </summary>
    /// <param name="baseCalendar">The base calendar.</param>
    public BaseCalendar(ICalendar? baseCalendar)
    {
        CalendarBase = baseCalendar;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
    /// </summary>
    /// <param name="timeZone">The time zone.</param>
    public BaseCalendar(TimeZoneInfo timeZone)
    {
        this.timeZone = timeZone;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
    /// </summary>
    /// <param name="baseCalendar">The base calendar.</param>
    /// <param name="timeZone">The time zone.</param>
    public BaseCalendar(ICalendar? baseCalendar, TimeZoneInfo? timeZone)
    {
        CalendarBase = baseCalendar;
        this.timeZone = timeZone;
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    protected BaseCalendar(SerializationInfo info, StreamingContext context)
    {
        int version;
        try
        {
            version = info.GetInt32("baseCalendarVersion");
        }
        catch
        {
            version = 0;
        }

        string prefix = "";
        try
        {
            info.GetValue("description", typeof(string));
        }
        catch
        {
            // base class for other
            prefix = "BaseCalendar+";
        }

        // Serializing TimeZones is tricky in .NET Core. This helper will ensure that we get the same timezone on a given platform,
        // but there's not yet a good method of serializing/deserializing timezones cross-platform since Windows timezone IDs don't
        // match IANA tz IDs (https://en.wikipedia.org/wiki/List_of_tz_database_time_zones). This feature is coming, but depending
        // on timelines, it may be worth doign the mapping here.
        // More info: https://github.com/dotnet/corefx/issues/7757

        switch (version)
        {
            case 0:
                timeZone = (TimeZoneInfo) info.GetValue(prefix + "timeZone", typeof(TimeZoneInfo))!;
                break;
            case 1:
                var timeZoneId = (string) info.GetValue(prefix + "timeZoneId", typeof(string))!;
                if (!string.IsNullOrEmpty(timeZoneId))
                {
                    timeZone = Util.TimeZoneUtil.FindTimeZoneById(timeZoneId);
                }
                break;
            default:
                ThrowHelper.ThrowNotSupportedException("Unknown serialization version");
                break;
        }

        CalendarBase = (ICalendar) info.GetValue(prefix + "baseCalendar", typeof(ICalendar))!;
        Description = (string) info.GetValue(prefix + "description", typeof(string))!;
    }

    [System.Security.SecurityCritical]
    public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("baseCalendarVersion", 1);
        info.AddValue("baseCalendar", CalendarBase);
        info.AddValue("description", Description);
        info.AddValue("timeZoneId", timeZone?.Id);
    }

    /// <summary>
    /// Gets or sets the time zone.
    /// </summary>
    /// <value>The time zone.</value>
    public virtual TimeZoneInfo TimeZone
    {
        get
        {
            if (timeZone is null)
            {
                timeZone = TimeZoneInfo.Local;
            }
            return timeZone;
        }
        set => timeZone = value;
    }

    /// <summary>
    /// Gets or sets the description given to the <see cref="ICalendar" /> instance by
    /// its creator (if any).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Set a new base calendar or remove the existing one
    /// </summary>
    /// <value></value>
    public ICalendar? CalendarBase { set; get; }

    /// <summary>
    /// Check if date/time represented by timeStamp is included. If included
    /// return true. The implementation of BaseCalendar simply calls the base
    /// calendars IsTimeIncluded() method if base calendar is set.
    /// </summary>
    /// <seealso cref="ICalendar.IsTimeIncluded" />
    public virtual bool IsTimeIncluded(DateTimeOffset timeStampUtc)
    {
        if (timeStampUtc == DateTimeOffset.MinValue)
        {
            ThrowHelper.ThrowArgumentException("timeStampUtc must be greater 0");
        }

        if (CalendarBase is not null)
        {
            if (!CalendarBase.IsTimeIncluded(timeStampUtc))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determine the next UTC time (in milliseconds) that is 'included' by the
    /// Calendar after the given time. Return the original value if timeStamp is
    /// included. Return 0 if all days are excluded.
    /// </summary>
    /// <seealso cref="ICalendar.GetNextIncludedTimeUtc" />
    public virtual DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        if (timeUtc == DateTimeOffset.MinValue)
        {
            ThrowHelper.ThrowArgumentException("timeStamp must be greater DateTimeOffset.MinValue");
        }

        if (CalendarBase is not null)
        {
            return CalendarBase.GetNextIncludedTimeUtc(timeUtc);
        }

        return timeUtc;
    }

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>A new object that is a copy of this instance.</returns>
    public virtual ICalendar Clone()
    {
        var clone = CloneFields(new BaseCalendar());
        return clone;
    }

    protected BaseCalendar CloneFields(BaseCalendar clone)
    {
        clone.Description = Description;
        clone.TimeZone = TimeZone;
        clone.CalendarBase = CalendarBase?.Clone();
        return clone;
    }

    public bool Equals(BaseCalendar? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Equals(CalendarBase, other.CalendarBase) && string.Equals(Description, other.Description, StringComparison.Ordinal) && Equals(TimeZone, other.TimeZone);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((BaseCalendar) obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = CalendarBase?.GetHashCode() ?? 0;
            hashCode = (hashCode * 397) ^ (Description?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ (timeZone?.GetHashCode() ?? 0);
            return hashCode;
        }
    }
}