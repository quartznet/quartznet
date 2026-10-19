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

using System.Text;
using System.Text.Json;

using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// How a simple trigger that measures its interval from the previous run's completion behaves as a
/// trigger (#4140): what the builders carry, the provisional fire time it takes as it fires, its misfire
/// instructions, its retries, and every shape it travels in. <c>FixedDelayStoreTest</c> holds the stores
/// to the hold and to the fire time a completion gives it.
/// </summary>
[TestFixture]
public class FixedDelayOnTriggersTest
{
    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan interval = TimeSpan.FromHours(1);

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The builders and the defaults
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void ASimpleTriggerRunsAtAFixedRateUnlessItIsToldOtherwise()
    {
        ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();

        trigger.MeasureIntervalFromCompletion.Should().BeFalse("a fixed rate is what every simple trigger did before 4.7");
        new SimpleTriggerAttribute("00:01:00").MeasureIntervalFromCompletion.Should().BeFalse();
    }

    [Test]
    public void TheSettingIsCarriedByTheBuilderAndKeptByARebuildAndAClone()
    {
        ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever().MeasureIntervalFromCompletion())
            .Build();

        trigger.MeasureIntervalFromCompletion.Should().BeTrue();
        ((ISimpleTrigger) trigger.GetTriggerBuilder().Build()).MeasureIntervalFromCompletion.Should().BeTrue(
            "rebuilding a trigger is how a reschedule keeps what the caller did not change");
        ((ISimpleTrigger) trigger.Clone()).MeasureIntervalFromCompletion.Should().BeTrue();

        SimpleScheduleBuilder.Create().MeasureIntervalFromCompletion().MeasureIntervalFromCompletion(false).Build()
            .Should().BeOfType<SimpleTriggerImpl>().Which.MeasureIntervalFromCompletion.Should().BeFalse("the last word wins, as with JobBuilder's flags");
    }

    [Test]
    public void TheInterfaceDefaultAnswersAFixedRate()
    {
        ISimpleTrigger trigger = A.Fake<ISimpleTrigger>();
        A.CallTo(() => trigger.MeasureIntervalFromCompletion).CallsBaseMethod();

        trigger.MeasureIntervalFromCompletion.Should().BeFalse("a simple trigger written against 4.6 runs at a fixed rate");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The provisional fire time a firing leaves
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void AFiringLeavesItsScheduledTimePlusTheIntervalCountingRunsRatherThanReadingTheGrid()
    {
        // The third of four runs, drifted 1h20m off the start time's grid by the runs before it.
        SimpleTriggerImpl trigger = FixedDelay(repeatCount: 3);
        trigger.TimesTriggered = 2;
        trigger.NextFireTimeUtc = epoch.AddMinutes(200);

        trigger.Triggered(null);

        trigger.TimesTriggered.Should().Be(3);
        trigger.PreviousFireTimeUtc.Should().Be(epoch.AddMinutes(200));
        trigger.NextFireTimeUtc.Should().Be(epoch.AddMinutes(260),
            "one run is left, which a count read off the grid — this is its fourth slot — would have spent");

        trigger.Triggered(null);
        trigger.NextFireTimeUtc.Should().BeNull("the fourth run was the last");
    }

    [Test]
    public void ARetryLeavesItsRetryTimePlusTheIntervalAndCountsNoRun()
    {
        SimpleTriggerImpl trigger = FixedDelay(repeatCount: 3);
        trigger.TimesTriggered = 1;
        trigger.NextFireTimeUtc = epoch.AddMinutes(30);

        trigger.RetryFired(null);

        trigger.NextFireTimeUtc.Should().Be(epoch.AddMinutes(90));
        trigger.TimesTriggered.Should().Be(1, "a retry is another attempt at a run already counted");
    }

    [Test]
    public void ARetryIsNotTakenOverByTheNextOccurrence()
    {
        // A failure half an hour in, retried half an hour later: exactly where the fire time the firing
        // left is.
        FakeTimeProvider clock = new(epoch.AddMinutes(30));
        SimpleTriggerImpl fixedDelay = FixedDelay(clock: clock);
        SimpleTriggerImpl fixedRate = FixedDelay(clock: clock);
        fixedRate.MeasureIntervalFromCompletion = false;

        foreach (SimpleTriggerImpl trigger in new[] { fixedDelay, fixedRate })
        {
            trigger.RetryPolicy = RetryPolicy.Fixed(3, TimeSpan.FromMinutes(30));
            trigger.NextFireTimeUtc = epoch.AddHours(1);
        }

        fixedDelay.ExecutionComplete(A.Fake<IJobExecutionContext>(), new JobExecutionException("failed")).Should().Be(SchedulerInstruction.RetryTrigger,
            "a trigger that measures from completion has no next occurrence until its last attempt completes");
        fixedDelay.NextFireTimeUtc.Should().Be(epoch.AddHours(1));

        fixedRate.ExecutionComplete(A.Fake<IJobExecutionContext>(), new JobExecutionException("failed")).Should().Be(SchedulerInstruction.NoInstruction,
            "the control: at a fixed rate the occurrence takes over a retry that lands on it");
    }

    [Test]
    public void ANewCalendarOnlyMovesTheFireTimeOnPastWhatItExcludes()
    {
        SimpleTriggerImpl trigger = FixedDelay();
        trigger.TimesTriggered = 1;
        trigger.PreviousFireTimeUtc = epoch;
        trigger.NextFireTimeUtc = epoch.AddMinutes(90);

        trigger.UpdateWithNewCalendar(new DailyCalendar(new TimeOnly(9, 0), new TimeOnly(10, 0)) { TimeZone = TimeZoneInfo.Utc }, TimeSpan.FromMinutes(1));
        trigger.NextFireTimeUtc.Should().Be(epoch.AddMinutes(90),
            "the fire time was measured from a completion, which recomputing from the previous fire time would lose");

        trigger.UpdateWithNewCalendar(new DailyCalendar(new TimeOnly(11, 0), new TimeOnly(12, 0)) { TimeZone = TimeZoneInfo.Utc }, TimeSpan.FromMinutes(1));
        trigger.NextFireTimeUtc.Should().Be(epoch.AddMinutes(150), "11:30 is excluded, and an interval later is not");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Misfire instructions
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A trigger that has run twice of six, whose next run was due at 12:30, one interval after a
    /// completion, handled at 16:00.
    /// </summary>
    [TestCase(SimpleTriggerMisfireInstruction.SmartPolicy, 360, 2)]
    [TestCase(SimpleTriggerMisfireInstruction.FireNow, 360, 2)]
    [TestCase(SimpleTriggerMisfireInstruction.NowWithExistingCount, 360, 2)]
    [TestCase(SimpleTriggerMisfireInstruction.NowWithRemainingCount, 360, 3)]
    [TestCase(SimpleTriggerMisfireInstruction.NextWithExistingCount, 420, 2)]
    [TestCase(SimpleTriggerMisfireInstruction.NextWithRemainingCount, 420, 3)]
    [TestCase(SimpleTriggerMisfireInstruction.IgnoreMisfires, 150, 2)]
    public void AMisfireIsSettledWithoutABacklog(SimpleTriggerMisfireInstruction instruction, int minutesAfterEpoch, int timesTriggered)
    {
        SimpleTriggerImpl trigger = Misfired(instruction, repeatCount: 5);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(epoch.AddMinutes(minutesAfterEpoch),
            "a run that never happened has no completion to measure the next from, so it is now or an interval from now");
        trigger.TimesTriggered.Should().Be(timesTriggered, "only the Remaining instructions count the missed run");
        trigger.RepeatCount.Should().Be(5, "the repeat count is the trigger's definition, which a misfire leaves alone");
    }

    [TestCase(SimpleTriggerMisfireInstruction.SmartPolicy)]
    [TestCase(SimpleTriggerMisfireInstruction.NextWithExistingCount)]
    public void AMisfireAfterTheEndTimeEndsTheTrigger(SimpleTriggerMisfireInstruction instruction)
    {
        SimpleTriggerImpl trigger = Misfired(instruction, repeatCount: SimpleTriggerImpl.RepeatIndefinitely);
        trigger.EndTimeUtc = epoch.AddHours(5);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().BeNull("the end time bounds a run measured from now as it bounds every other");
    }

    [Test]
    public void AMissedRunThatWasTheLastEndsTheTriggerUnderTheRemainingInstructions()
    {
        SimpleTriggerImpl trigger = Misfired(SimpleTriggerMisfireInstruction.NowWithRemainingCount, repeatCount: 2);
        trigger.TimesTriggered = 2;

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().BeNull("the missed run was the third of three, and it counts");
    }

    [Test]
    public void TheNextInstructionsKeepToTheCalendar()
    {
        SimpleTriggerImpl trigger = Misfired(SimpleTriggerMisfireInstruction.NextWithExistingCount, repeatCount: 5);

        trigger.UpdateAfterMisfire(new DailyCalendar(new TimeOnly(16, 30), new TimeOnly(17, 30)) { TimeZone = TimeZoneInfo.Utc });

        trigger.NextFireTimeUtc.Should().Be(epoch.AddHours(8), "17:00 is excluded, so the run moves on an interval to 18:00");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The arithmetic's edges
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void ThereIsNoFireTimeAfterTheEndOfTime()
    {
        SimpleTriggerImpl trigger = FixedDelay();

        FixedDelaySchedule.FireTimeAfter(trigger, DateTimeOffset.MaxValue.AddMinutes(-1), null).Should().BeNull();
        FixedDelaySchedule.FirstIncludedFrom(trigger, DateTimeOffset.MaxValue.AddMinutes(-1), new ExcludeEverything()).Should().BeNull(
            "stepping past what the calendar excludes cannot step past the end of representable time");
    }

    [Test]
    public void ACalendarThatExcludesEverythingGivesUpAsTheFixedRateDoes()
    {
        SimpleTriggerImpl trigger = FixedDelay();
        trigger.RepeatInterval = TimeSpan.FromDays(365);

        FixedDelaySchedule.FireTimeAfter(trigger, epoch, new ExcludeEverything()).Should().BeNull(
            "a calendar that never includes a time leaves the trigger with nothing to fire");
    }

    [Test]
    public void AZeroIntervalHasNoNextRun()
    {
        SimpleTriggerImpl trigger = FixedDelay();
        trigger.RepeatInterval = TimeSpan.Zero;

        FixedDelaySchedule.FireTimeAfter(trigger, epoch, null).Should().BeNull();
        FixedDelaySchedule.FirstIncludedFrom(trigger, epoch, new ExcludeEverything()).Should().BeNull();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The shapes a trigger travels in
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void TheSettingSurvivesTheSystemTextJsonRoundTripAndAFixedRatePayloadIsTheOneItAlwaysWas()
    {
        SystemTextJsonObjectSerializer serializer = new();
        IOperableTrigger fixedDelay = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g").ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever().MeasureIntervalFromCompletion())
            .Build();
        IOperableTrigger fixedRate = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g").ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();

        byte[] payload = serializer.Serialize(fixedDelay);
        Encoding.UTF8.GetString(payload).Should().Contain("\"MeasureIntervalFromCompletion\":true");
        ((ISimpleTrigger) serializer.Deserialize<IOperableTrigger>(payload)!).MeasureIntervalFromCompletion.Should().BeTrue();
        ((ISimpleTrigger) serializer.Deserialize<IOperableTrigger>(payload)!.GetTriggerBuilder().Build()).MeasureIntervalFromCompletion.Should().BeTrue(
            "the schedule builder the serializer rebuilds from carries it too");

        Encoding.UTF8.GetString(serializer.Serialize(fixedRate)).Should().NotContain("MeasureIntervalFromCompletion",
            "a fixed-rate trigger is written exactly as a 4.6 node wrote it");
    }

    [Test]
    public void AnUpdateCarriesTheSettingOverTheWire()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new UpdateTriggerDetailsRequest.Converter() } };
        UpdateTriggerDetailsRequest request = UpdateTriggerDetailsRequest.Create(new TriggerDetailsUpdate().WithMeasureIntervalFromCompletion(true));

        string body = JsonSerializer.Serialize(request, options);
        body.Should().Be("{\"measureIntervalFromCompletion\":true}", "an update sends only what it changes");

        UpdateTriggerDetailsRequest read = JsonSerializer.Deserialize<UpdateTriggerDetailsRequest>(body, options)!;
        read.Validate().Should().BeEmpty();
        TriggerDetailsUpdate update = read.AsUpdate();
        update.HasMeasureIntervalFromCompletion.Should().BeTrue();
        update.MeasureIntervalFromCompletion.Should().BeTrue();
        update.ChangesNothing.Should().BeFalse();

        TriggerDetailsUpdate cleared = JsonSerializer.Deserialize<UpdateTriggerDetailsRequest>("{\"measureIntervalFromCompletion\":null}", options)!.AsUpdate();
        cleared.HasMeasureIntervalFromCompletion.Should().BeTrue("a present null is a change");
        cleared.MeasureIntervalFromCompletion.Should().BeFalse("and it reads as a fixed rate");

        UpdateTriggerDetailsRequest.Create(new TriggerDetailsUpdate().WithPriority(3)).AsUpdate().HasMeasureIntervalFromCompletion.Should().BeFalse(
            "an update changes only what it names");
    }

    [Test]
    public void AnUpdateWithAValueThatIsNotABooleanIsRefused()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new UpdateTriggerDetailsRequest.Converter() } };

        Action read = () => JsonSerializer.Deserialize<UpdateTriggerDetailsRequest>("{\"measureIntervalFromCompletion\":\"yes\"}", options);

        read.Should().Throw<JsonException>("a malformed body is a 400, not a 500").WithMessage("*measureIntervalFromCompletion*true, false or null*");
    }

    [Test]
    public void OnlyASimpleTriggerCanBeToldToMeasureFromCompletion()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithMeasureIntervalFromCompletion(true);
        Action ensure = () => update.EnsureMeasureIntervalFromCompletionCanBeCarried(new CronTriggerImpl(), new TriggerKey("t", "g"));

        ensure.Should().Throw<JobPersistenceException>().WithMessage("*not a simple trigger*");
        new TriggerDetailsUpdate().EnsureMeasureIntervalFromCompletionCanBeCarried(new CronTriggerImpl(), new TriggerKey("t", "g"));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    private static SimpleTriggerImpl FixedDelay(int repeatCount = SimpleTriggerImpl.RepeatIndefinitely, TimeProvider? clock = null)
    {
        return new SimpleTriggerImpl(clock)
        {
            Key = new TriggerKey("t", "g"),
            JobKey = new JobKey("j", "jg"),
            StartTimeUtc = epoch,
            RepeatInterval = interval,
            RepeatCount = repeatCount,
            MeasureIntervalFromCompletion = true
        };
    }

    private static SimpleTriggerImpl Misfired(SimpleTriggerMisfireInstruction instruction, int repeatCount)
    {
        SimpleTriggerImpl trigger = FixedDelay(repeatCount, new FakeTimeProvider(epoch.AddHours(6)));
        trigger.MisfireInstructionCode = (int) instruction;
        trigger.TimesTriggered = 2;
        trigger.PreviousFireTimeUtc = epoch.AddMinutes(80);
        trigger.NextFireTimeUtc = epoch.AddMinutes(150);
        return trigger;
    }

    private sealed class ExcludeEverything : ICalendar
    {
        public string? Description { get; set; }

        public ICalendar? CalendarBase { get; set; }

        public bool IsTimeIncluded(DateTimeOffset timeUtc) => false;

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc) => DateTimeOffset.MaxValue;

        public ICalendar Clone() => this;
    }
}
