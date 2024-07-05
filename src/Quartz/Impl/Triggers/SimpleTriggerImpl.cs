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

namespace Quartz.Impl.Triggers;

/// <summary>
/// A concrete <see cref="ITrigger" /> that is used to fire a <see cref="IJobDetail" />
/// at a given moment in time, and optionally repeated at a specified interval.
/// </summary>
/// <seealso cref="ITrigger" />
/// <seealso cref="ICronTrigger" />
/// <author>James House</author>
/// <author>Contributions by Lieven Govaerts of Ebitec Nv, Belgium.</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public class SimpleTriggerImpl : AbstractTrigger, ISimpleTrigger
{
    /// <summary>
    /// Used to indicate the 'repeat count' of the trigger is indefinite. Or in
    /// other words, the trigger should repeat continually until the trigger's
    /// ending timestamp.
    /// </summary>
    public const int RepeatIndefinitely = -1;

    private DateTimeOffset? nextFireTimeUtc;
    private DateTimeOffset? previousFireTimeUtc;

    private int repeatCount;
    private TimeSpan repeatInterval = TimeSpan.Zero;
    private int timesTriggered;


    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> with no settings.
    /// </summary>
    public SimpleTriggerImpl() : this(null)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> with no settings.
    /// </summary>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    public SimpleTriggerImpl(TimeProvider? timeProvider = null) : base(timeProvider ?? TimeProvider.System)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
    /// not repeat.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public SimpleTriggerImpl(string name, TimeProvider? timeProvider = null) : this(name, SchedulerConstants.DefaultGroup, timeProvider)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
    /// not repeat.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
    public SimpleTriggerImpl(string name, string group, TimeProvider? timeProvider = null)
        : this(name, group, (timeProvider ?? TimeProvider.System).GetUtcNow(), endTimeUtc: null, repeatCount: 0, TimeSpan.Zero)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
    /// repeat at the given interval the given number of times.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public SimpleTriggerImpl(string name, int repeatCount, TimeSpan repeatInterval, TimeProvider? timeProvider = null)
        : this(name, SchedulerConstants.DefaultGroup, repeatCount, repeatInterval, timeProvider)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
    /// repeat at the given interval the given number of times.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
    public SimpleTriggerImpl(string name, string group, int repeatCount, TimeSpan repeatInterval, TimeProvider? timeProvider = null)
        : this(name, group, (timeProvider ?? TimeProvider.System).GetUtcNow(), endTimeUtc: null, repeatCount, repeatInterval)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
    /// and not repeat.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public SimpleTriggerImpl(string name, DateTimeOffset startTimeUtc, TimeProvider? timeProvider = null)
        : this(name, SchedulerConstants.DefaultGroup, startTimeUtc, timeProvider)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
    /// and not repeat.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
    public SimpleTriggerImpl(string name, string group, DateTimeOffset startTimeUtc, TimeProvider? timeProvider = null)
        : this(name, group, startTimeUtc, endTimeUtc: null, 0, TimeSpan.Zero, timeProvider)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
    /// and repeat at the given interval the given number of times, or until
    /// the given end time.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to fire.</param>
    /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
    /// to quit repeat firing.</param>
    /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
    /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
    /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public SimpleTriggerImpl(
        string name,
        DateTimeOffset startTimeUtc,
        DateTimeOffset? endTimeUtc,
        int repeatCount,
        TimeSpan repeatInterval,
        TimeProvider? timeProvider = null)
        : this(name, SchedulerConstants.DefaultGroup, startTimeUtc, endTimeUtc, repeatCount, repeatInterval, timeProvider)
    {
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
    /// and repeat at the given interval the given number of times, or until
    /// the given end time.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="group">The group.</param>
    /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to fire.</param>
    /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
    /// to quit repeat firing.</param>
    /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
    /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
    /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
    public SimpleTriggerImpl(
        string name,
        string group,
        DateTimeOffset startTimeUtc,
        DateTimeOffset? endTimeUtc,
        int repeatCount,
        TimeSpan repeatInterval,
        TimeProvider? timeProvider = null)
        : base(name, group, timeProvider ?? TimeProvider.System)
    {
        StartTimeUtc = startTimeUtc;
        EndTimeUtc = endTimeUtc;
        RepeatCount = repeatCount;
        RepeatInterval = repeatInterval;
    }

    /// <summary>
    /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
    /// fire the identified <see cref="IJob" /> and repeat at the given
    /// interval the given number of times, or until the given end time.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="group">The group.</param>
    /// <param name="jobName">Name of the job.</param>
    /// <param name="jobGroup">The job group.</param>
    /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
    /// to fire.</param>
    /// <param name="endTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
    /// to quit repeat firing.</param>
    /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
    /// firing, use RepeatIndefinitely for unlimited times.</param>
    /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="group"/>, <paramref name="jobName"/> or <paramref name="jobGroup"/> are <see langword="null"/>.</exception>
    public SimpleTriggerImpl(
        string name,
        string group,
        string jobName,
        string jobGroup,
        DateTimeOffset startTimeUtc,
        DateTimeOffset? endTimeUtc,
        int repeatCount,
        TimeSpan repeatInterval,
        TimeProvider? timeProvider = null)
        : base(name, group, jobName, jobGroup, timeProvider ?? TimeProvider.System)
    {
        StartTimeUtc = startTimeUtc;
        EndTimeUtc = endTimeUtc;
        RepeatCount = repeatCount;
        RepeatInterval = repeatInterval;
    }

    /// <summary>
    /// Get or set the number of times the <see cref="SimpleTriggerImpl" /> should
    /// repeat, after which it will be automatically deleted.
    /// </summary>
    /// <seealso cref="RepeatIndefinitely" />
    public int RepeatCount
    {
        get => repeatCount;

        set
        {
            if (value < 0 && value != RepeatIndefinitely)
            {
                ThrowHelper.ThrowArgumentException("Repeat count must be >= 0, use the constant RepeatIndefinitely for infinite.");
            }

            repeatCount = value;
        }
    }

    /// <summary>
    /// Get or set the time interval at which the <see cref="ISimpleTrigger" /> should repeat.
    /// </summary>
    public TimeSpan RepeatInterval
    {
        get => repeatInterval;

        set
        {
            if (value < TimeSpan.Zero)
            {
                ThrowHelper.ThrowArgumentException("Repeat interval must be >= 0");
            }

            repeatInterval = value;
        }
    }

    /// <summary>
    /// Get or set the number of times the <see cref="ISimpleTrigger" /> has already
    /// fired.
    /// </summary>
    public int TimesTriggered
    {
        get => timesTriggered;
        set => timesTriggered = value;
    }

    public override IScheduleBuilder GetScheduleBuilder()
    {
        SimpleScheduleBuilder sb = SimpleScheduleBuilder.Create()
            .WithInterval(RepeatInterval)
            .WithRepeatCount(RepeatCount);

        switch (MisfireInstruction)
        {
            case Quartz.MisfireInstruction.SimpleTrigger.FireNow:
                sb.WithMisfireHandlingInstructionFireNow();
                break;
            case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount:
                sb.WithMisfireHandlingInstructionNextWithExistingCount();
                break;
            case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount:
                sb.WithMisfireHandlingInstructionNextWithRemainingCount();
                break;
            case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount:
                sb.WithMisfireHandlingInstructionNowWithExistingCount();
                break;
            case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount:
                sb.WithMisfireHandlingInstructionNowWithRemainingCount();
                break;
            case Quartz.MisfireInstruction.IgnoreMisfirePolicy:
                sb.WithMisfireHandlingInstructionIgnoreMisfires();
                break;
        }

        return sb;
    }

    /// <summary>
    /// Returns the final UTC time at which the <see cref="ISimpleTrigger" /> will
    /// fire, if repeatCount is RepeatIndefinitely, null will be returned.
    /// <para>
    /// Note that the return time may be in the past.
    /// </para>
    /// </summary>
    public override DateTimeOffset? FinalFireTimeUtc
    {
        get
        {
            if (repeatCount == 0)
            {
                return StartTimeUtc;
            }

            var endTimeUtc = EndTimeUtc;

            if (repeatCount == RepeatIndefinitely && !endTimeUtc.HasValue)
            {
                return null;
            }
            if (repeatCount == RepeatIndefinitely)
            {
                return GetFireTimeBefore(endTimeUtc.GetValueOrDefault());
            }

            DateTimeOffset lastTrigger = StartTimeUtc.AddTicks(repeatCount * repeatInterval.Ticks);

            if (!endTimeUtc.HasValue || lastTrigger < endTimeUtc.GetValueOrDefault())
            {
                return lastTrigger;
            }
            return GetFireTimeBefore(endTimeUtc.GetValueOrDefault());
        }
    }

    /// <summary>
    /// Tells whether this Trigger instance can handle events
    /// in millisecond precision.
    /// </summary>
    /// <value></value>
    public override bool HasMillisecondPrecision => true;

    /// <summary>
    /// Validates the misfire instruction.
    /// </summary>
    /// <param name="misfireInstruction">The misfire instruction.</param>
    /// <returns></returns>
    protected override bool ValidateMisfireInstruction(int misfireInstruction)
    {
        if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
        {
            return false;
        }

        if (misfireInstruction > Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount)
        {
            return false;
        }

        return true;

    }

    /// <summary>
    /// Updates the <see cref="ISimpleTrigger" />'s state based on the
    /// MisfireInstruction value that was selected when the <see cref="ISimpleTrigger" />
    /// was created.
    /// </summary>
    /// <remarks>
    /// If MisfireSmartPolicyEnabled is set to true,
    /// then the following scheme will be used: <br />
    /// <ul>
    /// <li>If the Repeat Count is 0, then the instruction will
    /// be interpreted as <see cref="MisfireInstruction.SimpleTrigger.FireNow" />.</li>
    /// <li>If the Repeat Count is <see cref="RepeatIndefinitely" />, then
    /// the instruction will be interpreted as <see cref="MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount" />.
    /// <b>WARNING:</b> using MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount
    /// with a trigger that has a non-null end-time may cause the trigger to
    /// never fire again if the end-time arrived during the misfire time span.
    /// </li>
    /// <li>If the Repeat Count is > 0, then the instruction
    /// will be interpreted as <see cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount" />.
    /// </li>
    /// </ul>
    /// </remarks>
    public override void UpdateAfterMisfire(ICalendar? cal)
    {
        int instr = MisfireInstruction;
        if (instr == Quartz.MisfireInstruction.SmartPolicy)
        {
            if (RepeatCount == 0)
            {
                instr = Quartz.MisfireInstruction.SimpleTrigger.FireNow;
            }
            else if (RepeatCount == RepeatIndefinitely)
            {
                instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;
            }
            else
            {
                instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
            }
        }
        else if (instr == Quartz.MisfireInstruction.SimpleTrigger.FireNow && RepeatCount != 0)
        {
            instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
        }

        if (instr == Quartz.MisfireInstruction.SimpleTrigger.FireNow)
        {
            nextFireTimeUtc = TimeProvider.GetUtcNow();
        }
        else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount)
        {
            DateTimeOffset? newFireTime = GetFireTimeAfter(null);

            if (cal is not null && newFireTime.HasValue)
            {
                while (!cal.IsTimeIncluded(newFireTime.GetValueOrDefault()))
                {
                    newFireTime = GetFireTimeAfter(newFireTime);

                    if (!newFireTime.HasValue)
                    {
                        break;
                    }

                    //avoid infinite loop
                    if (newFireTime.GetValueOrDefault().Year > TriggerConstants.YearToGiveUpSchedulingAt)
                    {
                        newFireTime = null;
                        break;
                    }
                }
            }
            nextFireTimeUtc = newFireTime;
        }
        else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount)
        {
            DateTimeOffset? newFireTime = GetFireTimeAfter(null);

            if (cal is not null && newFireTime.HasValue)
            {
                while (!cal.IsTimeIncluded(newFireTime.GetValueOrDefault()))
                {
                    newFireTime = GetFireTimeAfter(newFireTime);

                    if (!newFireTime.HasValue)
                    {
                        break;
                    }

                    //avoid infinite loop
                    if (newFireTime.GetValueOrDefault().Year > TriggerConstants.YearToGiveUpSchedulingAt)
                    {
                        newFireTime = null;
                        break;
                    }
                }
            }

            if (newFireTime.HasValue)
            {
                int timesMissed = ComputeNumTimesFiredBetween(nextFireTimeUtc.GetValueOrDefault(), newFireTime.GetValueOrDefault());
                TimesTriggered = TimesTriggered + timesMissed;
            }

            nextFireTimeUtc = newFireTime;
        }
        else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount)
        {
            if (repeatCount != 0 && repeatCount != RepeatIndefinitely)
            {
                RepeatCount = RepeatCount - TimesTriggered;
                TimesTriggered = 0;
            }

            DateTimeOffset newFireTime = TimeProvider.GetUtcNow();

            if (EndTimeUtc.HasValue && EndTimeUtc.GetValueOrDefault() < newFireTime)
            {
                nextFireTimeUtc = null; // We are past the end time
            }
            else
            {
                StartTimeUtc = newFireTime;
                nextFireTimeUtc = newFireTime;
            }
        }
        else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount)
        {
            DateTimeOffset newFireTime = TimeProvider.GetUtcNow();

            if (repeatCount != 0 && repeatCount != RepeatIndefinitely)
            {
                int timesMissed = ComputeNumTimesFiredBetween(nextFireTimeUtc.GetValueOrDefault(), newFireTime);
                int remainingCount = RepeatCount - (TimesTriggered + timesMissed);
                if (remainingCount <= 0)
                {
                    remainingCount = 0;
                }
                RepeatCount = remainingCount;
                TimesTriggered = 0;
            }

            if (EndTimeUtc.HasValue && EndTimeUtc.GetValueOrDefault() < newFireTime)
            {
                nextFireTimeUtc = null; // We are past the end time
            }
            else
            {
                StartTimeUtc = newFireTime;
                nextFireTimeUtc = newFireTime;
            }
        }
    }

    /// <summary>
    /// Called when the <see cref="IScheduler" /> has decided to 'fire'
    /// the trigger (Execute the associated <see cref="IJob" />), in order to
    /// give the <see cref="ITrigger" /> a chance to update itself for its next
    /// triggering (if any).
    /// </summary>
    /// <seealso cref="JobExecutionException" />
    public override void Triggered(ICalendar? cal)
    {
        timesTriggered++;
        previousFireTimeUtc = nextFireTimeUtc;
        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

        if (cal is not null && nextFireTimeUtc.HasValue)
        {
            while (!cal.IsTimeIncluded(nextFireTimeUtc.GetValueOrDefault()))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (!nextFireTimeUtc.HasValue)
                {
                    break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.GetValueOrDefault().Year > TriggerConstants.YearToGiveUpSchedulingAt)
                {
                    nextFireTimeUtc = null;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Updates the instance with new calendar.
    /// </summary>
    /// <param name="calendar">The calendar.</param>
    /// <param name="misfireThreshold">The misfire threshold.</param>
    public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
    {
        nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

        if (nextFireTimeUtc is null || calendar is null)
        {
            return;
        }

        DateTimeOffset now = TimeProvider.GetUtcNow();
        while (nextFireTimeUtc.HasValue && !calendar.IsTimeIncluded(nextFireTimeUtc.GetValueOrDefault()))
        {
            nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

            if (!nextFireTimeUtc.HasValue)
            {
                break;
            }

            //avoid infinite loop
            if (nextFireTimeUtc.GetValueOrDefault().Year > TriggerConstants.YearToGiveUpSchedulingAt)
            {
                nextFireTimeUtc = null;
            }

            if (nextFireTimeUtc is not null && nextFireTimeUtc.GetValueOrDefault() < now)
            {
                TimeSpan diff = now - nextFireTimeUtc.GetValueOrDefault();
                if (diff >= misfireThreshold)
                {
                    nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
                }
            }
        }
    }

    /// <summary>
    /// Called by the scheduler at the time a <see cref="ITrigger" /> is first
    /// added to the scheduler, in order to have the <see cref="ITrigger" />
    /// compute its first fire time, based on any associated calendar.
    /// <para>
    /// After this method has been called, <see cref="GetNextFireTimeUtc" />
    /// should return a valid answer.
    /// </para>
    /// </summary>
    /// <returns>
    /// The first time at which the <see cref="ITrigger" /> will be fired
    /// by the scheduler, which is also the same value <see cref="GetNextFireTimeUtc" />
    /// will return (until after the first firing of the <see cref="ITrigger" />).
    /// </returns>
    public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? cal)
    {
        nextFireTimeUtc = StartTimeUtc;

        if (cal is not null)
        {
            while (!cal.IsTimeIncluded(nextFireTimeUtc.GetValueOrDefault()))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (!nextFireTimeUtc.HasValue)
                {
                    break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.GetValueOrDefault().Year > TriggerConstants.YearToGiveUpSchedulingAt)
                {
                    return null;
                }
            }
        }

        return nextFireTimeUtc;
    }


    /// <summary>
    /// Returns the next time at which the <see cref="ISimpleTrigger" /> will
    /// fire. If the trigger will not fire again, <see langword="null" /> will be
    /// returned. The value returned is not guaranteed to be valid until after
    /// the <see cref="ITrigger" /> has been added to the scheduler.
    /// </summary>
    public override DateTimeOffset? GetNextFireTimeUtc()
    {
        return nextFireTimeUtc;
    }

    public override void SetNextFireTimeUtc(DateTimeOffset? nextFireTime)
    {
        nextFireTimeUtc = nextFireTime;
    }

    public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime)
    {
        previousFireTimeUtc = previousFireTime;
    }

    /// <summary>
    /// Returns the previous time at which the <see cref="ISimpleTrigger" /> fired.
    /// If the trigger has not yet fired, <see langword="null" /> will be
    /// returned.
    /// </summary>
    public override DateTimeOffset? GetPreviousFireTimeUtc()
    {
        return previousFireTimeUtc;
    }

    /// <summary>
    /// Returns the next UTC time at which the <see cref="ISimpleTrigger" /> will
    /// fire, after the given UTC time. If the trigger will not fire after the given
    /// time, <see langword="null" /> will be returned.
    /// </summary>
    public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTimeUtc)
    {
        if (repeatCount != RepeatIndefinitely && timesTriggered > repeatCount)
        {
            return null;
        }

        if (!afterTimeUtc.HasValue)
        {
            afterTimeUtc = TimeProvider.GetUtcNow();
        }

        DateTimeOffset startMillis = StartTimeUtc;
        DateTimeOffset afterMillis = afterTimeUtc.GetValueOrDefault();

        if (repeatCount == 0 && afterMillis >= startMillis)
        {
            return null;
        }

        DateTimeOffset? endTimeUtc = EndTimeUtc;

        if (endTimeUtc.HasValue && endTimeUtc.GetValueOrDefault() <= afterMillis)
        {
            return null;
        }

        var timeBetweenAfterAndStart = afterMillis - startMillis;
        if (timeBetweenAfterAndStart < TimeSpan.Zero)
        {
            return startMillis;
        }

        long numberOfTimesExecuted = (timeBetweenAfterAndStart.Ticks / repeatInterval.Ticks) + 1;

        if (repeatCount != RepeatIndefinitely && numberOfTimesExecuted > repeatCount)
        {
            return null;
        }

        DateTimeOffset time = startMillis.AddTicks(numberOfTimesExecuted * repeatInterval.Ticks);

        if (endTimeUtc.HasValue && endTimeUtc.GetValueOrDefault() <= time)
        {
            return null;
        }

        return time;
    }

    /// <summary>
    /// Returns the last UTC time at which the <see cref="ISimpleTrigger" /> will
    /// fire, before the given time. If the trigger will not fire before the
    /// given time, <see langword="null" /> will be returned.
    /// </summary>
    public DateTimeOffset? GetFireTimeBefore(DateTimeOffset endUtc)
    {
        if (endUtc < StartTimeUtc)
        {
            return null;
        }

        int numFires = ComputeNumTimesFiredBetween(StartTimeUtc, endUtc);
        return StartTimeUtc.AddTicks(numFires * repeatInterval.Ticks);
    }

    /// <summary>
    /// Computes the number of times fired between the two UTC date times.
    /// </summary>
    /// <param name="startTimeUtc">The UTC start date and time.</param>
    /// <param name="endTimeUtc">The UTC end date and time.</param>
    /// <returns></returns>
    public int ComputeNumTimesFiredBetween(DateTimeOffset startTimeUtc, DateTimeOffset endTimeUtc)
    {
        long time = (endTimeUtc - startTimeUtc).Ticks;
        return (int) (time / repeatInterval.Ticks);
    }

    /// <summary>
    /// Determines whether or not the <see cref="ISimpleTrigger" /> will occur
    /// again.
    /// </summary>
    public override bool GetMayFireAgain()
    {
        return GetNextFireTimeUtc().HasValue;
    }

    /// <summary>
    /// Validates whether the properties of the <see cref="IJobDetail" /> are
    /// valid for submission into a <see cref="IScheduler" />.
    /// </summary>
    public override void Validate()
    {
        base.Validate();

        if (repeatCount != 0 && repeatInterval.Ticks < 1)
        {
            ThrowHelper.ThrowSchedulerException("Repeat Interval cannot be zero.");
        }
    }
}