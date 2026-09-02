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

using System.Globalization;

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// The trigger shapes the matrix runs. Five families, six shapes: a repeating simple trigger and a
/// one-shot one take different branches of the same instruction.
/// </summary>
public enum MisfireTriggerShape
{
    SimpleRepeating,
    SimpleOneShot,
    Cron,
    CalendarInterval,
    DailyTimeInterval,
    Recurrence
}

/// <summary>
/// One cell of the misfire matrix: a trigger shape and one of the misfire instructions its family
/// offers.
/// </summary>
public sealed class MisfireMatrixCase
{
    /// <summary>The trigger shape this cell is one instruction of.</summary>
    public MisfireTriggerShape Shape { get; init; }

    /// <summary>The trigger shape, as a failure message names it.</summary>
    public string ShapeName { get; init; }

    /// <summary>The enum the instruction is spelled in, which is what the coverage guard walks.</summary>
    public Type InstructionEnum { get; init; }

    /// <summary>The instruction, as its own family spells it.</summary>
    public string Instruction { get; init; }

    /// <summary>
    /// Builds the trigger this cell is about, anchored on the instant the test froze and holding the
    /// clock the store under test reads. Called more than once per test: once for the copy that is
    /// stored, and once for the detached copy that computes what the store must arrive at.
    /// </summary>
    /// <remarks>
    /// The clock is the trigger's, not only the builder's: a trigger keeps the clock it was built with,
    /// so a policy that reschedules to "now" resolves against the test's clock rather than the
    /// machine's, on either store.
    /// </remarks>
    public Func<DateTimeOffset, TimeProvider, TriggerBuilder<IJob>> Trigger { get; init; }

    /// <summary>
    /// Whether this cell's instruction is the one that says a missed firing is not a misfire at all.
    /// Both stores skip such a trigger outright rather than running its policy, and the matrix asserts
    /// that they do — but a test that wants a trigger to actually misfire has to leave it out.
    /// </summary>
    public bool IgnoresMisfires { get; init; }

    /// <summary>
    /// Whether this cell's instruction consults the trigger's calendar while it recomputes. Only the
    /// "reschedule to the next slot" branches do; the "fire now" ones set the clock's reading and never
    /// look at a calendar.
    /// </summary>
    public bool ConsultsCalendar { get; init; }

    public override string ToString() => $"{ShapeName} / {Instruction}";
}

/// <summary>
/// The trigger-shape by misfire-instruction matrix that every job store has to answer the same way.
/// </summary>
/// <remarks>
/// <para>
/// Six shapes cover the five trigger families; <see cref="ISimpleTrigger" /> gets two, because a
/// repeating simple trigger and a one-shot one take different branches of
/// <c>SimpleTriggerImpl.UpdateAfterMisfire</c> for the same instruction — <c>FireNow</c> on a repeating
/// trigger is rewritten to <c>RescheduleNowWithRemainingRepeatCount</c>, and the "reschedule to the
/// next slot" instructions run a one-shot trigger out of fire times altogether.
/// </para>
/// <para>
/// Every schedule has a period of one day and is anchored so that its missed firing sits half a day
/// before the test's <c>anchor</c> and its next slot half a day after. Nothing here is within hours of
/// a schedule boundary, so a cell reads as a schedule rather than as arithmetic on a boundary.
/// </para>
/// </remarks>
public static class MisfireMatrixCases
{
    private static readonly TimeSpan HalfPeriod = MisfireThroughAStoreTestBase.HalfPeriod;
    private static readonly TimeSpan Period = TimeSpan.FromDays(1);

    /// <summary>Every cell of the matrix.</summary>
    public static IEnumerable<MisfireMatrixCase> All()
    {
        foreach (SimpleTriggerMisfireInstruction instruction in Enum.GetValues<SimpleTriggerMisfireInstruction>())
        {
            yield return new MisfireMatrixCase
            {
                Shape = MisfireTriggerShape.SimpleRepeating,
                ShapeName = "SimpleTrigger repeating daily",
                InstructionEnum = typeof(SimpleTriggerMisfireInstruction),
                Instruction = instruction.ToString(),
                IgnoresMisfires = instruction == SimpleTriggerMisfireInstruction.IgnoreMisfires,
                ConsultsCalendar = instruction
                    is SimpleTriggerMisfireInstruction.NextWithExistingCount
                    or SimpleTriggerMisfireInstruction.NextWithRemainingCount
                    or SimpleTriggerMisfireInstruction.SmartPolicy,
                Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                    .StartAt(anchor - HalfPeriod)
                    .WithSimpleSchedule(x => x
                        .WithInterval(Period)
                        .RepeatForever()
                        .WithMisfireInstruction(instruction))
            };

            yield return new MisfireMatrixCase
            {
                Shape = MisfireTriggerShape.SimpleOneShot,
                ShapeName = "SimpleTrigger one-shot",
                InstructionEnum = typeof(SimpleTriggerMisfireInstruction),
                Instruction = instruction.ToString(),
                IgnoresMisfires = instruction == SimpleTriggerMisfireInstruction.IgnoreMisfires,
                // A one-shot trigger has no next slot to skip to, so the calendar loop is unreachable
                // whatever the instruction: it runs out of fire times first.
                ConsultsCalendar = false,
                Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                    .StartAt(anchor - HalfPeriod)
                    .WithSimpleSchedule(x => x
                        .WithInterval(Period)
                        .WithRepeatCount(0)
                        .WithMisfireInstruction(instruction))
            };
        }

        foreach (CronTriggerMisfireInstruction instruction in Enum.GetValues<CronTriggerMisfireInstruction>())
        {
            yield return new MisfireMatrixCase
            {
                Shape = MisfireTriggerShape.Cron,
                ShapeName = "CronTrigger firing once a day",
                InstructionEnum = typeof(CronTriggerMisfireInstruction),
                Instruction = instruction.ToString(),
                IgnoresMisfires = instruction == CronTriggerMisfireInstruction.IgnoreMisfires,
                ConsultsCalendar = instruction == CronTriggerMisfireInstruction.DoNothing,
                Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                    .StartAt(anchor - HalfPeriod - Period)
                    .WithCronSchedule(DailyCronAt(anchor + HalfPeriod), x => x
                        .InTimeZone(TimeZoneInfo.Utc)
                        .WithMisfireInstruction(instruction))
            };
        }

        foreach (CalendarIntervalTriggerMisfireInstruction instruction in Enum.GetValues<CalendarIntervalTriggerMisfireInstruction>())
        {
            yield return new MisfireMatrixCase
            {
                Shape = MisfireTriggerShape.CalendarInterval,
                ShapeName = "CalendarIntervalTrigger every day",
                InstructionEnum = typeof(CalendarIntervalTriggerMisfireInstruction),
                Instruction = instruction.ToString(),
                IgnoresMisfires = instruction == CalendarIntervalTriggerMisfireInstruction.IgnoreMisfires,
                ConsultsCalendar = instruction == CalendarIntervalTriggerMisfireInstruction.DoNothing,
                Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                    .StartAt(anchor - HalfPeriod)
                    .WithCalendarIntervalSchedule(x => x
                        .WithInterval(1, IntervalUnit.Day)
                        .InTimeZone(TimeZoneInfo.Utc)
                        .WithMisfireInstruction(instruction))
            };
        }

        foreach (DailyTimeIntervalTriggerMisfireInstruction instruction in Enum.GetValues<DailyTimeIntervalTriggerMisfireInstruction>())
        {
            yield return new MisfireMatrixCase
            {
                Shape = MisfireTriggerShape.DailyTimeInterval,
                ShapeName = "DailyTimeIntervalTrigger with a one-instant window",
                InstructionEnum = typeof(DailyTimeIntervalTriggerMisfireInstruction),
                Instruction = instruction.ToString(),
                IgnoresMisfires = instruction == DailyTimeIntervalTriggerMisfireInstruction.IgnoreMisfires,
                ConsultsCalendar = instruction == DailyTimeIntervalTriggerMisfireInstruction.DoNothing,
                Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                    .StartAt(anchor - HalfPeriod - Period)
                    .WithDailyTimeIntervalSchedule(x => x
                        .StartingDailyAt(TimeOfDay(anchor + HalfPeriod))
                        .EndingDailyAt(TimeOfDay(anchor + HalfPeriod))
                        .WithInterval(1, IntervalUnit.Hour)
                        .InTimeZone(TimeZoneInfo.Utc)
                        .WithMisfireInstruction(instruction))
            };
        }

        foreach (RecurrenceTriggerMisfireInstruction instruction in Enum.GetValues<RecurrenceTriggerMisfireInstruction>())
        {
            yield return new MisfireMatrixCase
            {
                Shape = MisfireTriggerShape.Recurrence,
                ShapeName = "RecurrenceTrigger on FREQ=DAILY",
                InstructionEnum = typeof(RecurrenceTriggerMisfireInstruction),
                Instruction = instruction.ToString(),
                IgnoresMisfires = instruction == RecurrenceTriggerMisfireInstruction.IgnoreMisfires,
                ConsultsCalendar = instruction == RecurrenceTriggerMisfireInstruction.DoNothing,
                Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                    .StartAt(anchor - HalfPeriod)
                    .WithRecurrenceSchedule("FREQ=DAILY", x => x
                        .InTimeZone(TimeZoneInfo.Utc)
                        .WithMisfireInstruction(instruction))
            };
        }
    }

    /// <summary>
    /// One cell per trigger shape, for the tests whose subject is the recomputation rather than the
    /// instruction — the shape's calendar-consulting instruction, which is the only branch that has a
    /// calendar to consult.
    /// </summary>
    public static IEnumerable<MisfireMatrixCase> OnePerShapeThatConsultsACalendar()
    {
        // SmartPolicy resolves to the same branch, so it would do — but a failure message that names
        // the branch outright is worth more than one that makes the reader resolve it.
        return All()
            .Where(x => x.ConsultsCalendar && !string.Equals(x.Instruction, nameof(CronTriggerMisfireInstruction.SmartPolicy), StringComparison.Ordinal))
            .GroupBy(x => x.Shape)
            .Select(x => x.First());
    }

    /// <summary>
    /// One named cell, for a test whose subject is something other than the matrix but which wants a
    /// trigger the matrix has already pinned.
    /// </summary>
    public static MisfireMatrixCase Cell(MisfireTriggerShape shape, string instruction)
    {
        return All().Single(x => x.Shape == shape && string.Equals(x.Instruction, instruction, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every misfire-instruction enum the library exports, found rather than listed so that a new
    /// trigger family cannot arrive without the matrix noticing.
    /// </summary>
    public static IEnumerable<Type> InstructionEnums()
    {
        return typeof(ITrigger).Assembly.GetExportedTypes()
            .Where(x => x.IsEnum && x.Name.EndsWith("TriggerMisfireInstruction", StringComparison.Ordinal))
            .OrderBy(x => x.Name, StringComparer.Ordinal);
    }

    /// <summary>A cron expression that fires once a day, at the UTC time of <paramref name="when" />.</summary>
    private static string DailyCronAt(DateTimeOffset when)
    {
        DateTime utc = when.UtcDateTime;
        return string.Create(CultureInfo.InvariantCulture, $"{utc.Second} {utc.Minute} {utc.Hour} * * ?");
    }

    /// <summary>The UTC time of day of <paramref name="when" />, to the second.</summary>
    private static TimeOnly TimeOfDay(DateTimeOffset when)
    {
        DateTime utc = when.UtcDateTime;
        return new TimeOnly(utc.Hour, utc.Minute, utc.Second);
    }
}
