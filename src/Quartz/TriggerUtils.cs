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

using Quartz.Spi;

namespace Quartz;

/// <summary>
/// Convenience and utility methods for simplifying the construction and
/// configuration of <see cref="ITrigger" />s and DateTimeOffsetOffsets.
/// </summary>
/// <seealso cref="ICronTrigger" />
/// <seealso cref="ISimpleTrigger" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public static class TriggerUtils
{
    /// <summary>
    /// Returns a list of Dates that are the next fire times of a
    /// <see cref="ITrigger" />.
    /// The input trigger will be cloned before any work is done, so you need
    /// not worry about its state being altered by this method.
    /// </summary>
    /// <param name="trigger">The trigger upon which to do the work</param>
    /// <param name="calendar">The calendar to apply to the trigger's schedule</param>
    /// <param name="numTimes">The number of next fire times to produce</param>
    public static IReadOnlyList<DateTimeOffset> ComputeFireTimes(IOperableTrigger trigger, ICalendar? calendar, int numTimes)
    {
        List<DateTimeOffset> lst = new List<DateTimeOffset>();

        IOperableTrigger t = (IOperableTrigger) trigger.Clone();

        if (t.GetNextFireTimeUtc() is null || !t.GetNextFireTimeUtc().HasValue)
        {
            t.ComputeFirstFireTimeUtc(calendar);
        }

        for (int i = 0; i < numTimes; i++)
        {
            DateTimeOffset? d = t.GetNextFireTimeUtc();
            if (d.HasValue)
            {
                lst.Add(d.Value);
                t.Triggered(calendar);
            }
            else
            {
                break;
            }
        }

        return lst.AsReadOnly();
    }

    /// <summary>
    /// Compute the <see cref="DateTimeOffset" /> that is 1 second after the Nth firing of
    /// the given <see cref="ITrigger" />, taking the trigger's associated
    /// <see cref="ICalendar" /> into consideration.
    /// </summary>
    /// <remarks>
    /// The input trigger will be cloned before any work is done, so you need
    /// not worry about its state being altered by this method.
    /// </remarks>
    /// <param name="trigger">The trigger upon which to do the work</param>
    /// <param name="calendar">The calendar to apply to the trigger's schedule</param>
    /// <param name="numberOfTimes">The number of next fire times to produce</param>
    /// <returns>the computed Date, or null if the trigger (as configured) will not fire that many times</returns>
    public static DateTimeOffset? ComputeEndTimeToAllowParticularNumberOfFirings(IOperableTrigger trigger, ICalendar? calendar, int numberOfTimes)
    {
        IOperableTrigger t = (IOperableTrigger) trigger.Clone();

        if (t.GetNextFireTimeUtc() is null)
        {
            t.ComputeFirstFireTimeUtc(calendar);
        }

        int c = 0;
        DateTimeOffset? endTime = null;

        for (int i = 0; i < numberOfTimes; i++)
        {
            DateTimeOffset? d = t.GetNextFireTimeUtc();
            if (d is not null)
            {
                c++;
                t.Triggered(calendar);
                if (c == numberOfTimes)
                {
                    endTime = d;
                }
            }
            else
            {
                break;
            }
        }

        if (endTime is null)
        {
            return null;
        }

        endTime = endTime.Value.AddSeconds(1);

        return endTime;
    }


    /// <summary>
    /// Returns a list of Dates that are the next fire times of a  <see cref="ITrigger" />
    /// that fall within the given date range. The input trigger will be cloned
    /// before any work is done, so you need not worry about its state being
    /// altered by this method.
    /// <para>
    /// NOTE: if this is a trigger that has previously fired within the given
    /// date range, then firings which have already occurred will not be listed
    /// in the output List.
    /// </para>
    /// </summary>
    /// <param name="trigger">The trigger upon which to do the work</param>
    /// <param name="calendar">The calendar to apply to the trigger's schedule</param>
    /// <param name="from">The starting date at which to find fire times</param>
    /// <param name="to">The ending date at which to stop finding fire times</param>
    public static IReadOnlyList<DateTimeOffset> ComputeFireTimesBetween(IOperableTrigger trigger, ICalendar? calendar, DateTimeOffset from, DateTimeOffset to)
    {
        List<DateTimeOffset> lst = new List<DateTimeOffset>();

        IOperableTrigger t = (IOperableTrigger) trigger.Clone();

        if (t.GetNextFireTimeUtc() is null || !t.GetNextFireTimeUtc().HasValue)
        {
            t.StartTimeUtc = from;
            t.EndTimeUtc = to;
            t.ComputeFirstFireTimeUtc(calendar);
        }

        // TODO: this method could be more efficient by using logic specific
        //        to the type of trigger ...
        while (true)
        {
            DateTimeOffset? d = t.GetNextFireTimeUtc();
            if (d.HasValue)
            {
                if (d.Value < from)
                {
                    t.Triggered(calendar);
                    continue;
                }
                if (d.Value > to)
                {
                    break;
                }
                lst.Add(d.Value);
                t.Triggered(calendar);
            }
            else
            {
                break;
            }
        }
        return lst.AsReadOnly();
    }
}