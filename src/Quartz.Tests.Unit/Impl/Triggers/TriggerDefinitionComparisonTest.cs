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

#nullable enable

using System.Reflection;

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// When a declared trigger re-applied at a start describes the stored one exactly, so that the store leaves
/// the stored row as it is (#4324): a comparison that misses a field would leave a changed declaration
/// unapplied, so every field of a trigger is either compared or listed here as one that is not.
/// </summary>
public sealed class TriggerDefinitionComparisonTest
{
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo plusTwo =
        TimeZoneInfo.CreateCustomTimeZone("q4324-plus-two", TimeSpan.FromHours(2), "plus two", "plus two");

    /// <summary>
    /// The fields of a trigger the comparison leaves out, with why. Every other field is compared, by itself
    /// or through the property over it.
    /// </summary>
    private static readonly Dictionary<string, string> notCompared = new(StringComparer.Ordinal)
    {
        ["TriggerBase.name"] = "the key, compared",
        ["TriggerBase.group"] = "the key, compared",
        ["TriggerBase.jobName"] = "the job key, compared",
        ["TriggerBase.jobGroup"] = "the job key, compared",
        ["TriggerBase.misfireInstructionStated"] = "not stored: the code is compared",
        ["TriggerBase.timeZoneStated"] = "not stored: the zone is compared by the schedule",
        ["TriggerBase.preferredNodeDirty"] = "not stored: whether this instance changed the pin",
        ["TriggerBase.overlapPolicyUnsettled"] = "the store's bookkeeping, which a kept row keeps",
        ["TriggerBase.retryAttemptCleared"] = "not stored: says something about one completion",
        ["TriggerBase.retryPolicyDecided"] = "not stored: a firing's cache",
        ["TriggerBase.appliedRetryPolicy"] = "not stored: a firing's cache",
        ["TriggerBase.appliedRetryPolicyInherited"] = "not stored: a firing's cache",
        ["TriggerBase.retryDeclinedForRoom"] = "not stored: a firing's cache",
        ["TriggerBase.keepsPauseOnReplace"] = "an instruction to one replace, not part of the trigger",
        ["TriggerBase.replacesRunStateAsRead"] = "an instruction to one replace, not part of the trigger",
        ["TriggerBase.retryPolicyValue"] = "the parsed form of the retry policy string, which is compared",
        ["TriggerBase.timeProvider"] = "the clock the trigger computes with, not stored",
        ["TriggerBase.MisfiredFromFireTimeUtc"] = "a misfire's original fire time, run state a kept row keeps and no declaration says",
        ["TriggerBase.<FireInstanceId>k__BackingField"] = "the acquisition's, not the trigger's",
        ["CronTriggerImpl.cronEx"] = "parsed from the expression, which is compared",
        ["DailyTimeIntervalTriggerImpl.complete"] = "set once the next fire time runs out, and the next fire time is compared",
        ["RecurrenceTriggerImpl.parsedRule"] = "parsed from the rule, which is compared",
    };

    private static IEnumerable<TestCaseData> Kinds()
    {
        yield return new TestCaseData(Build(b => b.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3)))).SetArgDisplayNames("simple");
        yield return new TestCaseData(Build(b => b.WithSchedule(CronScheduleBuilder.Create("0 0 2 * * ?").InTimeZone(plusTwo)))).SetArgDisplayNames("cron");
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
        yield return Change("misfire instruction", t => t.MisfireInstructionCode = MisfireInstruction.IgnoreMisfirePolicy);
        yield return Change("misfire threshold", t => t.MisfireThreshold = TimeSpan.FromMinutes(3));
        yield return Change("start time", t => t.StartTimeUtc = epoch.AddMinutes(1));
        yield return Change("end time", t => t.EndTimeUtc = epoch.AddDays(30));
        yield return Change("next fire time", t => t.NextFireTimeUtc = epoch.AddHours(5));
        yield return Change("previous fire time", t => t.PreviousFireTimeUtc = epoch.AddHours(-1));
        yield return Change("execution group", t => t.ExecutionGroup = "reports");
        yield return Change("preferred node", t => t.PreferredNode = PreferredNode.For("node-b"));
        yield return Change("automatic pin", t => t.PreferredNode = PreferredNode.Auto);
        yield return Change("retry policy", t => t.RetryPolicy = RetryPolicy.Fixed(3, TimeSpan.FromMinutes(1)));
        yield return Change("retry attempt", t => t.RetryAttempt = 1);
        yield return Change("continuation", t => t.Continuation = new Continuation(new TriggerKey("parent"), ContinuationCondition.OnSuccess));
        yield return Change("overlap policy", t => t.OverlapPolicy = OverlapPolicy.Skip);
        yield return Change("job data", t => t.JobDataMap["key"] = "value");
        yield return Change("job data value", t => t.JobDataMap["count"] = 2, before: t => t.JobDataMap["count"] = 1);
        yield return Change("schedule", t => ((SimpleTriggerImpl) t).RepeatInterval = TimeSpan.FromHours(2));
        yield return Change("fire count", t => ((SimpleTriggerImpl) t).TimesTriggered = 4);

        static TestCaseData Change(string name, Action<TriggerBase> change, Action<TriggerBase>? before = null)
        {
            return new TestCaseData(change, before).SetArgDisplayNames(name);
        }
    }

    [TestCaseSource(nameof(Kinds))]
    public void ACopyDescribesTheTriggerItWasTakenFrom(Func<TriggerBase> build)
    {
        TriggerBase stored = build();
        stored.NextFireTimeUtc = epoch.AddHours(1);
        stored.PreviousFireTimeUtc = epoch;

        TriggerBase declared = (TriggerBase) stored.Clone();

        declared.DescribesSameTriggerAs(stored).Should().BeTrue();
    }

    [TestCaseSource(nameof(Changes))]
    public void AChangedFieldDescribesAnotherTrigger(Action<TriggerBase> change, Action<TriggerBase>? before)
    {
        TriggerBase stored = Build(b => b.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()))();
        stored.NextFireTimeUtc = epoch.AddHours(1);
        stored.PreviousFireTimeUtc = epoch;
        before?.Invoke(stored);

        TriggerBase declared = (TriggerBase) stored.Clone();
        declared.DescribesSameTriggerAs(stored).Should().BeTrue("the premise: the copy is the same trigger");

        change(declared);

        declared.DescribesSameTriggerAs(stored).Should().BeFalse("a declaration that changed this is to be applied");
    }

    [Test]
    public void AnotherKindOfTriggerDescribesAnotherTrigger()
    {
        TriggerBase simple = Build(b => b.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()))();
        TriggerBase calendar = Build(b => b.WithSchedule(CalendarIntervalScheduleBuilder.Create().WithInterval(1, IntervalUnit.Hour)))();

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
            typeof(TriggerBase), typeof(SimpleTriggerImpl), typeof(CronTriggerImpl), typeof(DailyTimeIntervalTriggerImpl),
            typeof(CalendarIntervalTriggerImpl), typeof(RecurrenceTriggerImpl),
        ];

        // The fields the comparison reads, directly or through the property or the schedule comparison over them.
        HashSet<string> compared = new(StringComparer.Ordinal)
        {
            "TriggerBase.key", "TriggerBase.jobKey", "TriggerBase.jobDataMap", "TriggerBase.misfireInstruction",
            "TriggerBase.misfireThreshold", "TriggerBase.endTimeUtc", "TriggerBase.startTimeUtc", "TriggerBase.executionGroup",
            "TriggerBase.preferredNode", "TriggerBase.preferredNodeAuto", "TriggerBase.retryPolicy", "TriggerBase.retryAttempt",
            "TriggerBase.continuesAfterTriggerName", "TriggerBase.continuesAfterTriggerGroup", "TriggerBase.continuationCondition",
            "TriggerBase.overlapPolicy", "TriggerBase.<Description>k__BackingField", "TriggerBase.<CalendarName>k__BackingField",
            "TriggerBase.<Priority>k__BackingField",
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
        if (kind == typeof(TriggerBase))
        {
            return false;
        }

        string[] covered =
        [
            "nextFireTimeUtc", "previousFireTimeUtc", "NextFireTimeUtc", "PreviousFireTimeUtc", "startTimeUtc", "endTimeUtc",
            "StartTimeUtc", "EndTimeUtc", "startTime", "endTime", "recurrenceRuleString", "triggerTimeZone", "timesTriggered", "TimesTriggered", "repeatCount", "repeatInterval",
            "RepeatInterval", "RepeatCount", "repeatIntervalUnit", "RepeatIntervalUnit", "MeasureIntervalFromCompletion",
            "cronExpressionString", "CronExpressionString", "timeZone", "TimeZone", "preserveHourOfDayAcrossDaylightSavings",
            "PreserveHourOfDayAcrossDaylightSavings", "skipDayIfHourDoesNotExist", "SkipDayIfHourDoesNotExist", "daysOfWeek",
            "DaySet", "startTimeOfDay", "StartTimeOfDay", "endTimeOfDay", "EndTimeOfDay", "recurrenceRule", "RecurrenceRule",
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

    private static Func<TriggerBase> Build(Action<TriggerBuilder<IJob>> schedule)
    {
        return () =>
        {
            TriggerBuilder<IJob> builder = TriggerBuilder.Create().WithIdentity("t", "g").ForJob("job", "jobs").StartAt(epoch);
            schedule(builder);
            return (TriggerBase) builder.Build();
        };
    }
}