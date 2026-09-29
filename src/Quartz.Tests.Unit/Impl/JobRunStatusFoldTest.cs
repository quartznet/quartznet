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
/// How one recorded execution moves its job's <see cref="JobRunStatus" />: the rollup every store that
/// keeps a status keeps.
/// </summary>
public sealed class JobRunStatusFoldTest
{
    private const string SchedulerName = "Fold";
    private static readonly JobKey job = new("reconcile", "billing");
    private static readonly DateTimeOffset t0 = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A job with history: five runs, two final failures, the last two runs in a row among them, a
    /// success at <c>t0 - 10 min</c>, a failure at <c>t0 - 5 min</c>, latest run at <c>t0</c>.
    /// </summary>
    private static readonly JobRunStatus established = new(SchedulerName, job, t0, JobRunResult.Failed)
    {
        RunCount = 5,
        FailureCount = 2,
        ConsecutiveFailures = 2,
        LastSucceededAtUtc = t0.AddMinutes(-10),
        LastFailedAtUtc = t0.AddMinutes(-5),
        LastFailureMessage = "the earlier failure",
        FirstFiredAtUtc = t0.AddDays(-1)
    };

    public static IEnumerable<TestCaseData> OneNewerRun()
    {
        // result, retry scheduled, runs, failures, consecutive, last success moved, last failure moved
        yield return new TestCaseData(JobRunResult.Succeeded, false, 6L, 2L, 0, true, false).SetName("Succeeded resets the run of failures");
        yield return new TestCaseData(JobRunResult.Skipped, false, 6L, 2L, 0, true, false).SetName("Skipped is a success");
        yield return new TestCaseData(JobRunResult.Failed, false, 6L, 3L, 3, false, true).SetName("A final failure counts twice over");
        yield return new TestCaseData(JobRunResult.Failed, true, 6L, 2L, 2, false, true).SetName("A retried failure is a run and a last failure only");
        yield return new TestCaseData(JobRunResult.Cancelled, false, 6L, 2L, 2, false, false).SetName("Cancelled is a run only");
    }

    [TestCaseSource(nameof(OneNewerRun))]
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

        JobRunStatus status = JobRunStatusFold.Apply(established, entry);

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
        status.LastFailureMessage.Should().Be(failureMoves ? "the upstream refused" : "the earlier failure",
            "the message describes the run at LastFailedAtUtc");

        status.FirstFiredAtUtc.Should().Be(established.FirstFiredAtUtc);
        status.Job.Should().Be(job);
        status.SchedulerName.Should().Be(SchedulerName);
    }

    [Test]
    public void TheFirstRunStartsTheStatus()
    {
        JobRunStatus status = JobRunStatusFold.Apply(null, Run(t0, JobRunResult.Failed) with { ExceptionMessage = "boom" });

        status.SchedulerName.Should().Be(SchedulerName);
        status.Job.Should().Be(job);
        status.RunCount.Should().Be(1);
        status.FailureCount.Should().Be(1);
        status.ConsecutiveFailures.Should().Be(1);
        status.FirstFiredAtUtc.Should().Be(t0);
        status.LastFiredAtUtc.Should().Be(t0);
        status.LastResult.Should().Be(JobRunResult.Failed);
        status.LastSucceededAtUtc.Should().BeNull("nothing has succeeded yet");
        status.LastFailedAtUtc.Should().Be(t0);
        status.LastFailureMessage.Should().Be("boom");
    }

    [Test]
    public void ARunThatCompletesOutOfOrderIsCountedButDoesNotBecomeTheLastRun()
    {
        DateTimeOffset earlier = t0.AddMinutes(-1);
        ExecutionHistoryEntry late = Run(earlier, JobRunResult.Failed) with
        {
            ExceptionMessage = "a slow firing that failed",
            EntryId = "late",
            Summary = "late"
        };

        JobRunStatus status = JobRunStatusFold.Apply(established, late);

        status.RunCount.Should().Be(6, "it ran");
        status.FailureCount.Should().Be(3, "and it failed for the last time");
        status.ConsecutiveFailures.Should().Be(2,
            "the run of failures counts back from the latest-fired run, and this one fired before it");
        status.LastFiredAtUtc.Should().Be(t0);
        status.LastResult.Should().Be(JobRunResult.Failed);
        status.LastEntryId.Should().BeNull("the last-run fields still describe the run that fired latest");
        status.LastSummary.Should().BeNull();
        status.LastFailedAtUtc.Should().Be(earlier, "it failed later than the failure recorded before it");
        status.LastFailureMessage.Should().Be("a slow firing that failed");
    }

    [Test]
    public void AnOlderSuccessDoesNotMoveTheLastSuccessBack()
    {
        JobRunStatus status = JobRunStatusFold.Apply(established, Run(t0.AddMinutes(-30), JobRunResult.Succeeded));

        status.LastSucceededAtUtc.Should().Be(t0.AddMinutes(-10), "the last success is the later of the two");
        status.ConsecutiveFailures.Should().Be(2, "an out-of-order success does not end the run of failures after it");
        status.RunCount.Should().Be(6);
    }

    [Test]
    public void AnOlderFailureDoesNotMoveTheLastFailureBack()
    {
        JobRunStatus status = JobRunStatusFold.Apply(
            established,
            Run(t0.AddMinutes(-30), JobRunResult.Failed) with { ExceptionMessage = "older" });

        status.LastFailedAtUtc.Should().Be(t0.AddMinutes(-5));
        status.LastFailureMessage.Should().Be("the earlier failure");
    }

    [Test]
    public void AnEarlierFireTimeMovesTheFirstFireTimeBack()
    {
        JobRunStatus status = JobRunStatusFold.Apply(established, Run(t0.AddDays(-2), JobRunResult.Succeeded));

        status.FirstFiredAtUtc.Should().Be(t0.AddDays(-2));
    }

    [Test]
    public void AnOccurrenceRetriedTwiceAndThenGivenUpIsOneFailure()
    {
        JobRunStatus? status = null;
        status = JobRunStatusFold.Apply(status, Run(t0, JobRunResult.Failed) with { RetryScheduled = true, ExceptionMessage = "attempt 0" });
        status = JobRunStatusFold.Apply(status, Run(t0.AddMinutes(1), JobRunResult.Failed) with { RetryScheduled = true, RetryAttempt = 1, ExceptionMessage = "attempt 1" });

        status.ConsecutiveFailures.Should().Be(0, "the occurrence has an attempt left, so it has not failed yet");
        status.FailureCount.Should().Be(0);
        status.LastFailedAtUtc.Should().Be(t0.AddMinutes(1));
        status.LastFailureMessage.Should().Be("attempt 1");

        status = JobRunStatusFold.Apply(status, Run(t0.AddMinutes(2), JobRunResult.Failed) with { RetryAttempt = 2, ExceptionMessage = "attempt 2" });

        status.RunCount.Should().Be(3, "three attempts ran");
        status.FailureCount.Should().Be(1, "one occurrence failed");
        status.ConsecutiveFailures.Should().Be(1);
        status.LastFailureMessage.Should().Be("attempt 2");
    }

    [Test]
    public void AReportedFailureWithoutAnExceptionIsDescribedByItsSummary()
    {
        JobRunStatus status = JobRunStatusFold.Apply(null, Run(t0, JobRunResult.Failed) with { Summary = "3 invoices did not reconcile" });

        status.LastFailureMessage.Should().Be("3 invoices did not reconcile",
            "a job that reports a failure without throwing says what went wrong in its summary");
    }

    [Test]
    public void ARowFromBefore44IsFoldedByWhatItsSucceededFlagImplies()
    {
        ExecutionHistoryEntry legacy = Run(t0, JobRunResult.Succeeded) with { Result = null, Succeeded = false, ExceptionMessage = "old" };

        JobRunStatus status = JobRunStatusFold.Apply(null, legacy);

        status.LastResult.Should().Be(JobRunResult.Failed, "a row with no result reads as its Succeeded flag says");
        status.FailureCount.Should().Be(1);
    }

    private static ExecutionHistoryEntry Run(DateTimeOffset firedAt, JobRunResult result) => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: "node-a",
        JobGroup: job.Group,
        JobName: job.Name,
        TriggerGroup: "nightly",
        TriggerName: "at-midnight",
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: result is JobRunResult.Succeeded or JobRunResult.Skipped,
        ExceptionMessage: null)
    {
        Result = result
    };
}
