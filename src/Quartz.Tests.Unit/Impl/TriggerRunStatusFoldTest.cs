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

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// How one recorded execution moves its trigger's <see cref="TriggerRunStatus" />: the job's rule, keyed by the
/// firing trigger.
/// </summary>
public sealed class TriggerRunStatusFoldTest
{
    private const string SchedulerName = "Fold";
    private static readonly JobKey job = new("reconcile", "billing");
    private static readonly TriggerKey trigger = new("at-midnight", "nightly");
    private static readonly DateTimeOffset t0 = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The trigger's twin of <c>JobRunStatusFoldTest</c>'s established job: five runs, two final failures in a
    /// row, a success at <c>t0 - 10 min</c>, a failure at <c>t0 - 5 min</c>, latest run at <c>t0</c>.
    /// </summary>
    private static readonly TriggerRunStatus established = new(SchedulerName, trigger, t0, JobRunResult.Failed)
    {
        RunCount = 5,
        FailureCount = 2,
        ConsecutiveFailures = 2,
        LastSucceededAtUtc = t0.AddMinutes(-10),
        LastFailedAtUtc = t0.AddMinutes(-5),
        LastFailureMessage = "the earlier failure",
        FirstFiredAtUtc = t0.AddDays(-1)
    };

    [TestCaseSource(typeof(JobRunStatusFoldTest), nameof(JobRunStatusFoldTest.OneNewerRun))]
    public void ANewerRunMovesTheStatusByItsResult(
        JobRunResult result,
        bool retryScheduled,
        long runs,
        long failures,
        int consecutive,
        bool successMoves,
        bool failureMoves)
    {
        DateTimeOffset firedAt = t0.AddMinutes(1);
        ExecutionHistoryEntry entry = Run(firedAt, result) with
        {
            RetryScheduled = retryScheduled,
            ExceptionMessage = result == JobRunResult.Failed ? "the upstream refused" : null,
            Duration = TimeSpan.FromSeconds(3),
            SchedulerInstanceId = "node-b",
            EntryId = "entry-6",
            Summary = "run six"
        };

        TriggerRunStatus status = TriggerRunStatusFold.Apply(established, entry);

        status.RunCount.Should().Be(runs, "every recorded run counts, whatever it achieved");
        status.FailureCount.Should().Be(failures, "only an occurrence that failed for the last time is a failure");
        status.ConsecutiveFailures.Should().Be(consecutive);

        status.LastFiredAtUtc.Should().Be(firedAt, "the run fired later than any before it");
        status.LastResult.Should().Be(result);
        status.LastDuration.Should().Be(TimeSpan.FromSeconds(3));
        status.LastSchedulerInstanceId.Should().Be("node-b");
        status.LastEntryId.Should().Be("entry-6");
        status.LastSummary.Should().Be("run six");

        status.LastSucceededAtUtc.Should().Be(successMoves ? firedAt : established.LastSucceededAtUtc);
        status.LastFailedAtUtc.Should().Be(failureMoves ? firedAt : established.LastFailedAtUtc);
        status.LastFailureMessage.Should().Be(failureMoves ? "the upstream refused" : "the earlier failure");

        status.FirstFiredAtUtc.Should().Be(established.FirstFiredAtUtc);
        status.Trigger.Should().Be(trigger);
        status.SchedulerName.Should().Be(SchedulerName);
    }

    [Test]
    public void TheFirstRunStartsTheStatusUnderTheFiringTriggersKey()
    {
        TriggerRunStatus status = TriggerRunStatusFold.Apply(null, Run(t0, JobRunResult.Failed) with { ExceptionMessage = "boom" });

        status.Trigger.Should().Be(trigger, "the status belongs to the trigger that fired, not to its job");
        status.SchedulerName.Should().Be(SchedulerName);
        status.RunCount.Should().Be(1);
        status.FailureCount.Should().Be(1);
        status.ConsecutiveFailures.Should().Be(1);
        status.FirstFiredAtUtc.Should().Be(t0);
        status.LastFiredAtUtc.Should().Be(t0);
        status.LastSucceededAtUtc.Should().BeNull("nothing has succeeded yet");
        status.LastFailedAtUtc.Should().Be(t0);
        status.LastFailureMessage.Should().Be("boom");
    }

    [Test]
    public void ARunThatCompletesOutOfOrderIsCountedButDoesNotBecomeTheLastRun()
    {
        TriggerRunStatus status = TriggerRunStatusFold.Apply(
            established,
            Run(t0.AddMinutes(-1), JobRunResult.Failed) with { ExceptionMessage = "a slow firing that failed", EntryId = "late" });

        status.RunCount.Should().Be(6);
        status.FailureCount.Should().Be(3);
        status.ConsecutiveFailures.Should().Be(2, "the run of failures counts back from the latest-fired run");
        status.LastFiredAtUtc.Should().Be(t0);
        status.LastEntryId.Should().BeNull();
        status.LastFailureMessage.Should().Be("a slow firing that failed");
    }

    [Test]
    public void TheTriggerAndTheJobFoldTheSameRunsToTheSameCounts()
    {
        ExecutionHistoryEntry[] runs =
        [
            Run(t0, JobRunResult.Succeeded),
            Run(t0.AddMinutes(1), JobRunResult.Failed) with { RetryScheduled = true, ExceptionMessage = "attempt 0" },
            Run(t0.AddMinutes(2), JobRunResult.Failed) with { RetryAttempt = 1, ExceptionMessage = "attempt 1" },
            Run(t0.AddMinutes(4), JobRunResult.Cancelled),
            Run(t0.AddMinutes(3), JobRunResult.Skipped),
            Run(t0.AddMinutes(5), JobRunResult.Failed) with { Summary = "reported" },
            Run(t0.AddMinutes(-5), JobRunResult.Failed) with { ExceptionMessage = "late" }
        ];

        JobRunStatus? jobStatus = null;
        TriggerRunStatus? triggerStatus = null;
        foreach (ExecutionHistoryEntry run in runs)
        {
            jobStatus = JobRunStatusFold.Apply(jobStatus, run);
            triggerStatus = TriggerRunStatusFold.Apply(triggerStatus, run);
        }

        triggerStatus.Should().BeEquivalentTo(jobStatus!, options => options.Excluding(status => status.Job),
            "one rule folds both statuses, so they cannot drift apart");
        triggerStatus!.Trigger.Should().Be(trigger);
    }

    private static ExecutionHistoryEntry Run(DateTimeOffset firedAt, JobRunResult result) => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: "node-a",
        JobGroup: job.Group,
        JobName: job.Name,
        TriggerGroup: trigger.Group,
        TriggerName: trigger.Name,
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: result is JobRunResult.Succeeded or JobRunResult.Skipped,
        ExceptionMessage: null)
    {
        Result = result
    };
}
