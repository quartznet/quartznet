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

using System;
using System.Collections.Generic;
using System.Reflection;

using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// When a declared trigger re-applied at a start describes the stored one exactly, so that the store leaves
/// the stored row as it is (#4324): a comparison that misses a field would leave a changed declaration
/// unapplied, so every field of a trigger is either compared or listed here as one that is not.
/// </summary>
public sealed class TriggerDefinitionComparisonTest
{
    private static readonly DateTimeOffset epoch = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo plusTwo =
        TimeZoneInfo.CreateCustomTimeZone("q4324-plus-two", TimeSpan.FromHours(2), "plus two", "plus two");

    /// <summary>
    /// The fields of a trigger the comparison leaves out, with why. Every other field is compared, by itself
    /// or through the property over it.
    /// </summary>
    private static readonly Dictionary<string, string> notCompared = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AbstractTrigger.key"] = "a cache of the name and the group, which are compared",
        ["AbstractTrigger.preferredNodeDirty"] = "not stored: whether this instance changed the pin",
        ["AbstractTrigger.keepsPauseOnReplace"] = "an instruction to one replace, not part of the trigger",
        ["AbstractTrigger.replacesRunStateAsRead"] = "an instruction to one replace, not part of the trigger",
        ["AbstractTrigger.MisfiredFromFireTimeUtc"] = "a misfire's original fire time, run state a kept row keeps and no declaration says",
        ["AbstractTrigger.<FireInstanceId>k__BackingField"] = "the acquisition's, not the trigger's",
        ["DailyTimeIntervalTriggerImpl.complete"] = "set once the next fire time runs out, and the next fire time is compared",
        ["RecurrenceTriggerImpl.parsedRule"] = "parsed from the rule, which is compared",
    };

    private static IEnumerable<TestCaseData> Kinds()
    {
        yield return new TestCaseData(Build(b => b.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3)))).SetArgDisplayNames("simple");
        yield return new TestCaseData(Build(b => b.WithSchedule(CronScheduleBuilder.CronSchedule("0 0 2 * * ?").InTimeZone(plusTwo)))).SetArgDisplayNames("cron");
        yield return new TestCaseData(Build(b => b.WithSchedule(DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(10, IntervalUnit.Minute).OnMondayThroughFriday().InTimeZone(plusTwo)))).SetArgDisplayNames("daily time interval");
        yield return new TestCaseData(Build(b => b.WithSchedule(CalendarIntervalScheduleBuilder.Create()
            .WithInterval(2, IntervalUnit.Day).InTimeZone(plusTwo)))).SetArgDisplayNames("calendar interval");
        yield return new TestCaseData(Build(b => b.WithSchedule(RecurrenceScheduleBuilder.Create("FREQ=DAILY;COUNT=5").InTimeZone(plusTwo)))).SetArgDisplayNames("recurrence");
    }

    private static IEnumerable<TestCaseData> Changes()
    {
        yield return Change("job key", t => t.JobKey = new JobKey("other", "job"));
        yield return Change("description", t => t.Description = "changed");
        yield return Change("calendar", t => t.CalendarName = "holidays");
        yield return Change("priority", t => t.Priority = 9);
        yield return Change("misfire instruction", t => t.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy);
        yield return Change("start time", t => t.StartTimeUtc = epoch.AddMinutes(1));
        yield return Change("end time", t => t.EndTimeUtc = epoch.AddDays(30));
        yield return Change("next fire time", t => t.SetNextFireTimeUtc(epoch.AddHours(5)));
        yield return Change("previous fire time", t => t.SetPreviousFireTimeUtc(epoch.AddHours(-1)));
        yield return Change("execution group", t => t.ExecutionGroup = "reports");
        yield return Change("preferred node", t => t.PreferredNode = "node-b");
        yield return Change("automatic pin", t => ((INextVersionTrigger) t).SetPreferredNodeRaw("node-b", auto: true, markDirty: true),
            before: t => t.PreferredNode = "node-b");
        yield return Change("job data", t => t.JobDataMap["key"] = "value");
        yield return Change("job data value", t => t.JobDataMap["count"] = 2, before: t => t.JobDataMap["count"] = 1);
        yield return Change("schedule", t => ((SimpleTriggerImpl) t).RepeatInterval = TimeSpan.FromHours(2));
        yield return Change("fire count", t => ((SimpleTriggerImpl) t).TimesTriggered = 4);

        static TestCaseData Change(string name, Action<AbstractTrigger> change, Action<AbstractTrigger> before = null)
        {
            return new TestCaseData(change, before).SetArgDisplayNames(name);
        }
    }

    [TestCaseSource(nameof(Kinds))]
    public void ACopyDescribesTheTriggerItWasTakenFrom(Func<AbstractTrigger> build)
    {
        AbstractTrigger stored = build();
        stored.SetNextFireTimeUtc(epoch.AddHours(1));
        stored.SetPreviousFireTimeUtc(epoch);

        AbstractTrigger declared = (AbstractTrigger) stored.Clone();

        declared.DescribesSameTriggerAs(stored).Should().BeTrue();
    }

    [TestCaseSource(nameof(Changes))]
    public void AChangedFieldDescribesAnotherTrigger(Action<AbstractTrigger> change, Action<AbstractTrigger> before)
    {
        AbstractTrigger stored = Build(b => b.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()))();
        stored.SetNextFireTimeUtc(epoch.AddHours(1));
        stored.SetPreviousFireTimeUtc(epoch);
        before?.Invoke(stored);

        AbstractTrigger declared = (AbstractTrigger) stored.Clone();
        declared.DescribesSameTriggerAs(stored).Should().BeTrue("the premise: the copy is the same trigger");

        change(declared);

        declared.DescribesSameTriggerAs(stored).Should().BeFalse("a declaration that changed this is to be applied");
    }

    [Test]
    public void AnotherKindOfTriggerDescribesAnotherTrigger()
    {
        AbstractTrigger simple = Build(b => b.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()))();
        AbstractTrigger calendar = Build(b => b.WithSchedule(CalendarIntervalScheduleBuilder.Create().WithInterval(1, IntervalUnit.Hour)))();

        calendar.DescribesSameTriggerAs(simple).Should().BeFalse();
    }

    /// <summary>
    /// The guard: a field a trigger gains is either compared or added to <see cref="notCompared" /> with why.
    /// </summary>
    [Test]
    public void EveryFieldOfATriggerIsComparedOrSaidNotToBe()
    {
        Type[] kinds =
        [
            typeof(AbstractTrigger), typeof(SimpleTriggerImpl), typeof(CronTriggerImpl), typeof(DailyTimeIntervalTriggerImpl),
            typeof(CalendarIntervalTriggerImpl), typeof(RecurrenceTriggerImpl),
        ];

        // The fields the comparison reads, directly or through the property or the schedule comparison over them.
        HashSet<string> compared = new HashSet<string>(StringComparer.Ordinal)
        {
            "AbstractTrigger.name", "AbstractTrigger.group", "AbstractTrigger.jobName", "AbstractTrigger.jobGroup",
            "AbstractTrigger.jobDataMap", "AbstractTrigger.misfireInstruction", "AbstractTrigger.endTimeUtc",
            "AbstractTrigger.startTimeUtc", "AbstractTrigger.executionGroup", "AbstractTrigger.preferredNode",
            "AbstractTrigger.preferredNodeAuto", "AbstractTrigger.<Description>k__BackingField",
            "AbstractTrigger.<CalendarName>k__BackingField", "AbstractTrigger.<Priority>k__BackingField",
        };

        List<string> unaccounted = [];
        foreach (Type kind in kinds)
        {
            foreach (FieldInfo field in kind.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                string name = $"{kind.Name}.{field.Name}";
                if (!compared.Contains(name) && !notCompared.ContainsKey(name) && !ComparedByTheKind(kind, field.Name))
                {
                    unaccounted.Add(name);
                }
            }
        }

        unaccounted.Should().BeEmpty(
            "a field DescribesSameTriggerAs does not compare would leave a declaration that changed it unapplied at a start: "
            + "compare it there, or add it to the list of fields that are not compared, with why");
    }

    /// <summary>
    /// The fields of a kind of trigger its schedule comparison, its fire count comparison and the time
    /// properties cover: its schedule builder's settings, its count, and the fire, start and end times a kind
    /// keeps behind properties of its own.
    /// </summary>
    private static bool ComparedByTheKind(Type kind, string field)
    {
        if (kind == typeof(AbstractTrigger))
        {
            return false;
        }

        string[] covered =
        [
            "nextFireTimeUtc", "previousFireTimeUtc", "startTimeUtc", "endTimeUtc", "startTime", "endTime",
            "recurrenceRuleString", "timeZone", "timesTriggered", "TimesTriggered", "repeatCount", "repeatInterval",
            "repeatIntervalUnit", "RepeatIntervalUnit", "cronEx", "PreserveHourOfDayAcrossDaylightSavings",
            "SkipDayIfHourDoesNotExist", "daysOfWeek", "startTimeOfDay", "endTimeOfDay",
        ];

        foreach (string name in covered)
        {
            if (field == name || field == $"<{name}>k__BackingField")
            {
                return true;
            }
        }

        return false;
    }

    private static Func<AbstractTrigger> Build(Action<TriggerBuilder> schedule)
    {
        return () =>
        {
            TriggerBuilder builder = TriggerBuilder.Create().WithIdentity("t", "g").ForJob("job", "jobs").StartAt(epoch);
            schedule(builder);
            return (AbstractTrigger) builder.Build();
        };
    }
}
