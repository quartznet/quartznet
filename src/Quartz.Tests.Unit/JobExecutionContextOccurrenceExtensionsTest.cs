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

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// How <see cref="JobExecutionContextOccurrenceExtensions.GetOccurrence" /> maps a firing's context to the
/// occurrence it serves and its origin (#4246). The firings a real store hands back are in
/// <c>FiringOccurrenceThroughAStoreTest</c> and <c>FiringOccurrenceRecoverySqliteTest</c>.
/// </summary>
public sealed class JobExecutionContextOccurrenceExtensionsTest
{
    private static readonly DateTimeOffset due = new(2026, 9, 10, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset slot = new(2026, 9, 9, 2, 0, 0, TimeSpan.Zero);

    [Test]
    public void AScheduledFiringServesTheTimeItWasDueNotTheTimeItRan()
    {
        using JobExecutionContextImpl context = Build(Trigger(), late: TimeSpan.FromSeconds(7));

        context.GetOccurrence().Should().Be(new FiringOccurrence(due, FiringOrigin.Schedule),
            "the occurrence is what the schedule called for; a late FireTimeUtc already says how late it ran");
    }

    [Test]
    public void ARetryServesTheSameOccurrenceAndSaysWhichAttemptItIs()
    {
        using JobExecutionContextImpl first = Build(Trigger());
        using JobExecutionContextImpl third = Build(Trigger(), retryAttempt: 2, late: TimeSpan.FromMinutes(4));

        third.GetOccurrence().Should().Be(first.GetOccurrence() with { RetryAttempt = 2 },
            "a retry is another attempt at one occurrence, so only the attempt differs");
    }

    [Test]
    public void AManualRunServesTheTimeItWasAskedFor()
    {
        using JobExecutionContextImpl context = Build(Trigger(manual: "true"), late: TimeSpan.FromMilliseconds(30));

        context.GetOccurrence().Should().Be(new FiringOccurrence(due, FiringOrigin.Manual),
            "a TriggerJob trigger is due when it was asked for, which is its scheduled fire time");
    }

    [Test]
    public void ABackfillServesItsSlotNotTheTimeTheBackfillStartedIt()
    {
        using JobExecutionContextImpl context = Build(Trigger(backfillSlot: slot));

        FiringOccurrence occurrence = context.GetOccurrence();

        occurrence.Should().Be(new FiringOccurrence(slot, FiringOrigin.Backfill));
        context.ScheduledFireTimeUtc.Should().Be(due, "the premise: the backfill started it now, a day after the slot");
    }

    [Test]
    public void ABackfillOfAManualRunIsABackfill()
    {
        using JobExecutionContextImpl context = Build(Trigger(manual: "true", backfillSlot: slot));

        context.GetOccurrence().Should().Be(new FiringOccurrence(slot, FiringOrigin.Backfill),
            "a backfill copies the trigger's data, a manual trigger's mark with it, and it stands in for the slot");
    }

    [Test]
    public void ARecoveryServesTheOccurrenceOfTheFiringItRunsAgainToTheTick()
    {
        // What a recovery trigger carries: the original's data, and the original firing's times as markers,
        // which are strings to the second. The trigger itself is scheduled for the original's time to the tick.
        DateTimeOffset dueAt = due.AddMilliseconds(250);
        ITrigger recoveryTrigger = Trigger(name: "recover_node-a_1", group: SchedulerConstants.DefaultRecoveryGroup);
        recoveryTrigger.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerName] = "t-export";
        recoveryTrigger.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerGroup] = "reports";
        recoveryTrigger.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerFireTime] = Invariant(dueAt.AddSeconds(9));
        recoveryTrigger.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerScheduledFireTime] = Invariant(dueAt);

        using JobExecutionContextImpl context = Build(recoveryTrigger, recovering: true, late: TimeSpan.FromMinutes(3), scheduled: dueAt);

        context.GetOccurrence().Should().Be(new FiringOccurrence(dueAt, FiringOrigin.Schedule) { Recovering = true },
            "a recovery runs the original firing again, so it serves the occurrence that firing was due for, to the tick");
    }

    [Test]
    public void ARecoveryKeepsTheOriginOfTheFiringItRunsAgain()
    {
        using JobExecutionContextImpl manual = Build(Trigger(manual: "true"), recovering: true);
        using JobExecutionContextImpl backfill = Build(Trigger(backfillSlot: slot), recovering: true);

        manual.GetOccurrence().Should().Be(new FiringOccurrence(due, FiringOrigin.Manual) { Recovering = true });
        backfill.GetOccurrence().Should().Be(new FiringOccurrence(slot, FiringOrigin.Backfill) { Recovering = true });
    }

    [Test]
    public void TheMarkersAreTheTriggersNotTheJobs()
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("export", "reports")
            .UsingJobData(SchedulerConstants.ManualTrigger, "true")
            .UsingJobData(SchedulerConstants.BackfillOriginalFireTime, slot.ToString("O"))
            .Build();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new NoOpJob())
            .WithJob(job)
            .WithTrigger(Trigger())
            .FiredAt(due)
            .Build();

        context.MergedJobDataMap.ContainsKey(SchedulerConstants.ManualTrigger).Should().BeTrue("the premise: the merged map carries the job's keys");
        context.GetOccurrence().Should().Be(new FiringOccurrence(due, FiringOrigin.Schedule),
            "the job's own data is on every firing of it, so a mark there says nothing about this one");
    }

    [Test]
    public void AFiringWithNoScheduledFireTimeServesTheTimeItFired()
    {
        IJobExecutionContext context = Fake(Trigger(), scheduledFireTimeUtc: null);

        context.GetOccurrence().OccurrenceUtc.Should().Be(due.AddSeconds(1), "a firing nobody scheduled is taken to be due when it fired");
    }

    [Test]
    public void TheOccurrenceIsInUtcWhateverOffsetTheContextCarries()
    {
        DateTimeOffset helsinki = due.ToOffset(TimeSpan.FromHours(3));
        IJobExecutionContext context = Fake(Trigger(), scheduledFireTimeUtc: helsinki);

        DateTimeOffset occurrence = context.GetOccurrence().OccurrenceUtc;

        occurrence.Should().Be(due);
        occurrence.Offset.Should().Be(TimeSpan.Zero, "a key formatted from it is the same string whichever offset the firing reported");
    }

    [Test]
    public void ScheduleIsTheDefaultOrigin()
    {
        default(FiringOrigin).Should().Be(FiringOrigin.Schedule, "every firing has an origin, so there is no None to default to");
        default(FiringOccurrence).Origin.Should().Be(FiringOrigin.Schedule);
    }

    [Test]
    public void ANullContextIsRefused()
    {
        Action act = () => ((IJobExecutionContext) null!).GetOccurrence();

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    private static JobExecutionContextImpl Build(
        ITrigger trigger,
        int retryAttempt = 0,
        bool recovering = false,
        TimeSpan late = default,
        DateTimeOffset? scheduled = null)
    {
        DateTimeOffset dueAt = scheduled ?? due;
        JobExecutionContextBuilder builder = JobExecutionContextBuilder.For(new NoOpJob())
            .WithTrigger(trigger)
            .FiredAt(dueAt + late, dueAt)
            .WithRetryAttempt(retryAttempt);

        if (recovering)
        {
            builder.Recovering();
        }

        return builder.Build();
    }

    private static IJobExecutionContext Fake(ITrigger trigger, DateTimeOffset? scheduledFireTimeUtc, bool recovering = false)
    {
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.Trigger).Returns(trigger);
        A.CallTo(() => context.ScheduledFireTimeUtc).Returns(scheduledFireTimeUtc);
        A.CallTo(() => context.FireTimeUtc).Returns(due.AddSeconds(1));
        A.CallTo(() => context.Recovering).Returns(recovering);
        return context;
    }

    private static ITrigger Trigger(string? manual = null, DateTimeOffset? backfillSlot = null, string name = "t-export", string group = "reports")
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob("export", "reports")
            .StartAt(due)
            .Build();

        if (manual is not null)
        {
            trigger.JobDataMap[SchedulerConstants.ManualTrigger] = manual;
        }

        if (backfillSlot is { } value)
        {
            trigger.JobDataMap.PutAsString(SchedulerConstants.BackfillOriginalFireTime, value);
        }

        return trigger;
    }

    /// <summary>The form a job store writes a recovery trigger's time markers in.</summary>
    private static string Invariant(DateTimeOffset value) => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
