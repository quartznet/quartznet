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

namespace Quartz.Extensibility;

/// <summary>
/// Answers what a trigger would fire, without scheduling it.
/// </summary>
/// <remarks>
/// <para>
/// The primary overloads take an <see cref="IOperableTrigger" /> rather than an <see cref="ITrigger" />
/// because answering the question means advancing a copy of the trigger through its schedule, applying
/// the calendar at each step — which is precisely what <see cref="IOperableTrigger" /> adds over
/// <see cref="ITrigger" />. That is why this lives here rather than in the <c>Quartz</c> namespace: it
/// is a helper over the operable-trigger contract, not part of the scheduling API.
/// </para>
/// <para>
/// Each has an <see cref="ITrigger" /> twin that casts, because testing a schedule is the most
/// mainstream thing anybody does with these and requiring the cast at every call site taught a
/// <c>Quartz.Extensibility</c> type to people who wanted neither. The cast is the same one you would
/// write: every trigger this library builds is an <see cref="IOperableTrigger" />, and one that is not
/// gets an <see cref="ArgumentException" /> naming the type rather than an
/// <see cref="InvalidCastException" /> from inside.
/// </para>
/// </remarks>
/// <seealso cref="ICronTrigger" />
/// <seealso cref="ISimpleTrigger" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public static class TriggerFireTimes
{
    /// <summary>
    /// Returns a list of Dates that are the next fire times of a
    /// <see cref="ITrigger" />.
    /// The input trigger will be cloned before any work is done, so you need
    /// not worry about its state being altered by this method.
    /// </summary>
    /// <param name="trigger">The trigger upon which to do the work</param>
    /// <param name="calendar">The calendar to apply to the trigger's schedule</param>
    /// <param name="numberOfTimes">The number of next fire times to produce</param>
    public static List<DateTimeOffset> Compute(IOperableTrigger trigger, ICalendar? calendar, int numberOfTimes)
    {
        List<DateTimeOffset> lst = new List<DateTimeOffset>();

        IOperableTrigger t = (IOperableTrigger) trigger.Clone();

        if (t.NextFireTimeUtc is null || !t.NextFireTimeUtc.HasValue)
        {
            t.ComputeFirstFireTimeUtc(calendar);
        }

        for (int i = 0; i < numberOfTimes; i++)
        {
            DateTimeOffset? d = t.NextFireTimeUtc;
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

        return lst;
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
    public static DateTimeOffset? ComputeEndTimeForCount(IOperableTrigger trigger, ICalendar? calendar, int numberOfTimes)
    {
        IOperableTrigger t = (IOperableTrigger) trigger.Clone();

        if (t.NextFireTimeUtc is null)
        {
            t.ComputeFirstFireTimeUtc(calendar);
        }

        int c = 0;
        DateTimeOffset? endTime = null;

        for (int i = 0; i < numberOfTimes; i++)
        {
            DateTimeOffset? d = t.NextFireTimeUtc;
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
    /// <param name="from">The starting date at which to find fire times, inclusive</param>
    /// <param name="to">
    /// The ending date at which to stop finding fire times, inclusive: a fire time that lands
    /// exactly on it is listed. The window is handed to the trigger as its
    /// <see cref="ITrigger.EndTimeUtc" />, which is inclusive for the same reason.
    /// </param>
    public static List<DateTimeOffset> ComputeBetween(IOperableTrigger trigger, ICalendar? calendar, DateTimeOffset from, DateTimeOffset to)
    {
        List<DateTimeOffset> lst = new List<DateTimeOffset>();

        IOperableTrigger t = (IOperableTrigger) trigger.Clone();

        if (t.NextFireTimeUtc is null || !t.NextFireTimeUtc.HasValue)
        {
            // Only preserve the trigger's own StartTimeUtc if it falls within
            // the query range [from, to]. This ensures fire times stay aligned
            // with the trigger's configured schedule, while still supporting
            // arbitrary date range queries where from/to define the window.
            if (t.StartTimeUtc < from || t.StartTimeUtc > to)
            {
                t.StartTimeUtc = from;
            }
            if (!t.EndTimeUtc.HasValue || t.EndTimeUtc.Value > to)
            {
                t.EndTimeUtc = to;
            }
            t.ComputeFirstFireTimeUtc(calendar);
        }

        // TODO: this method could be more efficient by using logic specific
        //        to the type of trigger ...
        while (true)
        {
            DateTimeOffset? d = t.NextFireTimeUtc;
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
        return lst;
    }

    /// <summary>
    /// The fire times of <paramref name="trigger" /> in [<paramref name="from" />, <paramref name="to" />), earliest
    /// first and one at a time: <see cref="ComputeBetween(IOperableTrigger, ICalendar?, DateTimeOffset, DateTimeOffset)" />'s
    /// walk on a clone, done lazily, with the end excluded, and each step charged to <paramref name="budget" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A step is one <see cref="IOperableTrigger.Triggered" />, and one calendar check inside it: a trigger skips
    /// what its calendar excludes in a loop of its own, which a calendar that excludes the whole window would
    /// otherwise run until the year the triggers give up scheduling at. The walk stops early, with no more times,
    /// when the budget refuses a step: the caller tells that from the end of the schedule by
    /// <see cref="FireTimeStepBudget.IsSpent" />.
    /// </para>
    /// <para>
    /// The end is excluded where <see cref="ComputeBetween(IOperableTrigger, ICalendar?, DateTimeOffset, DateTimeOffset)" />
    /// includes it, so that two windows that meet never list the time they meet at twice. A trigger already
    /// scheduled walks from its next fire time, as there; one that has none is started at the window.
    /// </para>
    /// </remarks>
    internal static IEnumerable<DateTimeOffset> EnumerateBetween(
        IOperableTrigger trigger,
        ICalendar? calendar,
        DateTimeOffset from,
        DateTimeOffset to,
        FireTimeStepBudget budget)
    {
        IOperableTrigger t = (IOperableTrigger) trigger.Clone();
        ICalendar? charged = calendar is null ? null : new StepChargingCalendar(calendar, budget);

        if (t.NextFireTimeUtc is null)
        {
            if (t.StartTimeUtc < from || t.StartTimeUtc >= to)
            {
                t.StartTimeUtc = from;
            }
            if (!t.EndTimeUtc.HasValue || t.EndTimeUtc.Value > to)
            {
                t.EndTimeUtc = to;
            }
            t.ComputeFirstFireTimeUtc(charged);
        }

        while (!budget.IsSpent && t.NextFireTimeUtc is { } d && d < to)
        {
            if (d >= from)
            {
                yield return d;
            }

            if (!budget.TryTake())
            {
                yield break;
            }

            t.Triggered(charged);
        }
    }

    /// <inheritdoc cref="Compute(IOperableTrigger, ICalendar?, int)" />
    /// <exception cref="ArgumentException">
    /// <paramref name="trigger" /> is not an <see cref="IOperableTrigger" />, so its schedule cannot be
    /// advanced.
    /// </exception>
    public static List<DateTimeOffset> Compute(ITrigger trigger, ICalendar? calendar, int numberOfTimes)
    {
        return Compute(Operable(trigger), calendar, numberOfTimes);
    }

    /// <inheritdoc cref="ComputeEndTimeForCount(IOperableTrigger, ICalendar?, int)" />
    /// <exception cref="ArgumentException">
    /// <paramref name="trigger" /> is not an <see cref="IOperableTrigger" />, so its schedule cannot be
    /// advanced.
    /// </exception>
    public static DateTimeOffset? ComputeEndTimeForCount(ITrigger trigger, ICalendar? calendar, int numberOfTimes)
    {
        return ComputeEndTimeForCount(Operable(trigger), calendar, numberOfTimes);
    }

    /// <inheritdoc cref="ComputeBetween(IOperableTrigger, ICalendar?, DateTimeOffset, DateTimeOffset)" />
    /// <exception cref="ArgumentException">
    /// <paramref name="trigger" /> is not an <see cref="IOperableTrigger" />, so its schedule cannot be
    /// advanced.
    /// </exception>
    public static List<DateTimeOffset> ComputeBetween(ITrigger trigger, ICalendar? calendar, DateTimeOffset from, DateTimeOffset to)
    {
        return ComputeBetween(Operable(trigger), calendar, from, to);
    }

    /// <summary>
    /// The cast the <see cref="ITrigger" /> overloads exist to save a caller writing, with the failure
    /// it would otherwise produce turned into one that says what is wrong.
    /// </summary>
    private static IOperableTrigger Operable(ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        if (trigger is IOperableTrigger operable)
        {
            return operable;
        }

        return Throw.ArgumentException<IOperableTrigger>(
            $"{trigger.GetType()} does not implement IOperableTrigger, so its fire times cannot be computed: "
            + "answering means advancing a copy of the trigger through its schedule, which is what that "
            + "interface adds. Derive the trigger from Quartz.Impl.Triggers.TriggerBase, or compute a "
            + "single step with ITrigger.GetFireTimeAfter.",
            nameof(trigger));
    }
}

/// <summary>
/// How many schedule steps one computation over many triggers may take in all, shared by every walk it makes.
/// </summary>
/// <remarks>
/// It bounds the work of a call that a caller with read access can ask for: a trigger overdue for weeks that fires
/// every second would otherwise be stepped through every second it missed.
/// </remarks>
internal sealed class FireTimeStepBudget
{
    private long remaining;

    public FireTimeStepBudget(long steps)
    {
        remaining = steps;
    }

    /// <summary>
    /// Whether a step was refused: a walk stopped before its schedule ended.
    /// </summary>
    public bool IsSpent { get; private set; }

    /// <summary>
    /// Takes one step, or refuses it, and from then on every other, when none is left.
    /// </summary>
    public bool TryTake()
    {
        if (remaining <= 0)
        {
            IsSpent = true;
            return false;
        }

        remaining--;
        return true;
    }
}

/// <summary>
/// A calendar that charges each check to a <see cref="FireTimeStepBudget" />, and once the budget is spent includes
/// every time, so that the loop a trigger skips excluded times in ends.
/// </summary>
/// <remarks>
/// The time it then lands on is not a fire time: the walk that handed it to the trigger sees the budget spent and
/// lists nothing more. It reads its calendar and never changes it.
/// </remarks>
internal sealed class StepChargingCalendar : ICalendar
{
    private readonly ICalendar calendar;
    private readonly FireTimeStepBudget budget;

    public StepChargingCalendar(ICalendar calendar, FireTimeStepBudget budget)
    {
        this.calendar = calendar;
        this.budget = budget;
    }

    public string? Description
    {
        get => calendar.Description;
        set => throw new NotSupportedException("A calendar read to compute fire times is not changed.");
    }

    public ICalendar? CalendarBase
    {
        get => calendar.CalendarBase;
        set => throw new NotSupportedException("A calendar read to compute fire times is not changed.");
    }

    public bool IsTimeIncluded(DateTimeOffset timeUtc)
    {
        return !budget.TryTake() || calendar.IsTimeIncluded(timeUtc);
    }

    public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        return calendar.GetNextIncludedTimeUtc(timeUtc);
    }

    public ICalendar Clone()
    {
        return new StepChargingCalendar(calendar.Clone(), budget);
    }
}