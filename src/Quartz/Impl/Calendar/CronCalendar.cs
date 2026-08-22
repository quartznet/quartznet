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
using System.Text;

namespace Quartz.Impl.Calendar;

/// <summary>
/// This implementation of the Calendar excludes the set of times expressed by a
/// given CronExpression.
/// </summary>
/// <remarks>
/// For example, you could use this calendar to exclude all but business hours (8AM - 5PM) every
/// day using the expression &quot;* * 0-7,18-23 ? * *&quot;.
/// <para>
/// It is important to remember that the cron expression here describes a set of
/// times to be <i>excluded</i> from firing. Whereas the cron expression in
/// CronTrigger describes a set of times that can
/// be <i>included</i> for firing. Thus, if a <see cref="ICronTrigger" /> has a
/// given cron expression and is associated with a <see cref="CronCalendar" /> with
/// the <i>same</i> expression, the calendar will exclude all the times the
/// trigger includes, and they will cancel each other out.
/// </para>
/// </remarks>
/// <author>Aaron Craven</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class CronCalendar : BaseCalendar, IEquatable<CronCalendar>
{
    private CronExpression cronExpression = null!;

    // ReSharper disable once UnusedMember.Local
    private CronCalendar()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronCalendar"/> class.
    /// </summary>
    /// <param name="expression">a string representation of the desired cron expression</param>
    public CronCalendar(string expression) : this(null, expression)
    {
    }

    /// <summary>
    /// Create a <see cref="CronCalendar" /> with the given cron expression and
    /// <see cref="BaseCalendar" />.
    /// </summary>
    /// <param name="baseCalendar">
    /// the base calendar for this calendar instance
    /// see BaseCalendar for more information on base
    /// calendar functionality
    /// </param>
    /// <param name="expression">a string representation of the desired cron expression</param>
    public CronCalendar(ICalendar? baseCalendar, string expression) : this(baseCalendar, expression, null)
    {
    }

    /// <summary>
    /// Create a <see cref="CronCalendar" /> with the given cron expression and
    /// <see cref="BaseCalendar" />.
    /// </summary>
    /// <param name="baseCalendar">
    /// the base calendar for this calendar instance
    /// see BaseCalendar for more information on base
    /// calendar functionality
    /// </param>
    /// <param name="expression">a string representation of the desired cron expression</param>
    /// <param name="timeZone">the time zone the expression's times are resolved in;
    /// <see langword="null" /> means the system's local time zone</param>
    public CronCalendar(ICalendar? baseCalendar, string expression, TimeZoneInfo? timeZone) : base(baseCalendar, timeZone)
    {
        // The zone has to reach the expression: TimeZone reads off it, so building the expression
        // without the zone would drop the argument on the floor.
        cronExpression = new CronExpression(expression, timeZone);
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private CronCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
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
                cronExpression = (CronExpression) info.GetValue("cronExpression", typeof(CronExpression))!;
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

        info.AddValue("version", 1);
        info.AddValue("cronExpression", cronExpression);
    }

    public override TimeZoneInfo TimeZone
    {
        get => cronExpression.TimeZone;
        // CronExpression is immutable, so changing the calendar's zone rebuilds its expression
        set => cronExpression = cronExpression.WithTimeZone(value);
    }

    /// <summary>
    /// Determine whether the given time  is 'included' by the
    /// Calendar.
    /// </summary>
    /// <param name="timeUtc">the time to test</param>
    /// <returns>a boolean indicating whether the specified time is 'included' by the CronCalendar</returns>
    public override bool IsTimeIncluded(DateTimeOffset timeUtc)
    {
        if (CalendarBase is not null &&
            CalendarBase.IsTimeIncluded(timeUtc) == false)
        {
            return false;
        }

        return !cronExpression.IsSatisfiedBy(timeUtc);
    }

    /// <summary>
    /// Determine the next time that is 'included' by the
    /// Calendar after the given time. Return the original value if timeStamp is
    /// included. Return 0 if all days are excluded.
    /// </summary>
    /// <param name="timeUtc"></param>
    /// <returns></returns>
    public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        DateTimeOffset nextIncludedTime = timeUtc.AddMilliseconds(1); //plus on millisecond

        while (!IsTimeIncluded(nextIncludedTime))
        {
            //If the time is in a range excluded by this calendar, we can
            // move to the end of the excluded time range and continue testing
            // from there. Otherwise, if nextIncludedTime is excluded by the
            // baseCalendar, ask it the next time it includes and begin testing
            // from there. Failing this, add one millisecond and continue
            // testing.
            if (cronExpression.IsSatisfiedBy(nextIncludedTime))
            {
                // The end of the excluded range is the next time the expression does NOT satisfy.
                // GetNextValidTimeAfter would land on another satisfied - excluded - time, and the
                // loop would walk the excluded run millisecond by millisecond forever.
                nextIncludedTime = cronExpression.GetNextInvalidTimeAfter(nextIncludedTime)!.Value;
            }
            else if (CalendarBase is not null &&
                     !CalendarBase.IsTimeIncluded(nextIncludedTime))
            {
                nextIncludedTime =
                    CalendarBase.GetNextIncludedTimeUtc(nextIncludedTime);
            }
            else
            {
                nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
            }
        }

        return nextIncludedTime;
    }

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>A new object that is a copy of this instance.</returns>
    public override ICalendar Clone()
    {
        var clone = new CronCalendar();
        // CronExpression is immutable, so the clone shares this instance.
        clone.cronExpression = cronExpression;
        CloneFields(clone);
        return clone;
    }

    /// <summary>
    /// Returns a <see cref="System.String"/> that represents the current <see cref="System.Object"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="System.String"/> that represents the current <see cref="System.Object"/>.
    /// </returns>
    public override string ToString()
    {
        StringBuilder buffer = new StringBuilder();
        buffer.Append("base calendar: [");
        if (CalendarBase is not null)
        {
            buffer.Append(CalendarBase);
        }
        else
        {
            buffer.Append("null");
        }
        buffer.Append("], excluded cron expression: '");
        buffer.Append(cronExpression);
        buffer.Append('\'');
        return buffer.ToString();
    }

    /// <summary>
    ///  Returns the object representation of the cron expression that defines the
    /// dates and times this calendar excludes.
    /// </summary>
    public CronExpression CronExpression
    {
        get => cronExpression;
        set
        {
            if (value is null)
            {
                Throw.ArgumentException("expression cannot be null");
            }
            cronExpression = value;
        }
    }

    public override int GetHashCode()
    {
        int baseHash = 0;
        if (CalendarBase is not null)
        {
            baseHash = CalendarBase.GetHashCode();
        }

        return CronExpression.GetHashCode() + 5 * baseHash;
    }

    public bool Equals(CronCalendar? other)
    {
        if (other is null)
        {
            return false;
        }
        bool baseEqual = CalendarBase is null || CalendarBase.Equals(other.CalendarBase);

        return baseEqual && CronExpression.Equals(other.CronExpression);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not CronCalendar other)
        {
            return false;
        }
        return Equals(other);
    }
}