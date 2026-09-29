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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The 4.4 half of the history contract: results, the bounds sized by result and by job, the new filters,
/// and the per-job status.
/// </summary>
/// <remarks>
/// Written against the contract's seams only — <c>CreateStore(configure)</c>, <c>ApplyBounds</c>,
/// <c>KeepJob</c>, <c>Clock</c> and the row builders — so it runs against the in-memory store and the
/// database-backed one alike.
/// </remarks>
public abstract partial class ExecutionHistoryStoreContractTest
{
    private static readonly JobKey nightly = new("nightly-report", JobGroup);
    private static readonly JobKey hourly = new("hourly-report", JobGroup);

    // ---------------------------------------------------------------------------------------------
    // Results on the row
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task WhatTheJobReportedIsReadBack()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Execution(Start, nightly.Name) with
        {
            Result = JobRunResult.Skipped,
            Summary = "no stale reservations",
            MetricsJson = "{\"released\":0}",
            Manual = true,
            FireInstanceId = "node-a-17"
        });

        ExecutionHistoryEntry row = (await Executions(store)).Items.Should().ContainSingle().Subject;

        row.Result.Should().Be(JobRunResult.Skipped);
        row.EffectiveResult.Should().Be(JobRunResult.Skipped);
        row.Summary.Should().Be("no stale reservations");
        row.MetricsJson.Should().Be("{\"released\":0}");
        row.Manual.Should().BeTrue();
        row.FireInstanceId.Should().Be("node-a-17");
    }

    [Test]
    public async Task ARowWithoutAResultReadsAsItsSucceededFlagSays()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Execution(Start, "ok"));
        await store.AddExecution(Failed(Start.AddSeconds(-1), "broke", retryAttempt: 0, retryScheduled: false));

        List<ExecutionHistoryEntry> rows = (await Executions(store)).Items.ToList();

        rows.Should().OnlyContain(row => row.Result == null, "a row written before 4.4 carries no result");
        rows.Single(row => row.JobName == "ok").EffectiveResult.Should().Be(JobRunResult.Succeeded);
        rows.Single(row => row.JobName == "broke").EffectiveResult.Should().Be(JobRunResult.Failed);
    }

    // ---------------------------------------------------------------------------------------------
    // Bounds sized by result and by job
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task EachResultIsKeptForItsOwnAge()
    {
        IExecutionHistoryStore store = await CreateStore(options =>
        {
            options.Retention = TimeSpan.FromHours(24);
            options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(30);
            options.RetentionByResult[JobRunResult.Skipped] = TimeSpan.FromHours(1);
        });

        await store.AddExecution(Run(Start, "succeeded", JobRunResult.Succeeded));
        await store.AddExecution(Run(Start, "skipped", JobRunResult.Skipped));
        await store.AddExecution(Run(Start, "failed", JobRunResult.Failed));
        await store.AddExecution(Run(Start, "cancelled", JobRunResult.Cancelled));

        Clock.Advance(TimeSpan.FromHours(2));
        await ApplyBounds(store);
        (await JobNames(store)).Should().BeEquivalentTo(["succeeded", "failed", "cancelled"],
            "a skip is kept for its own hour, and the results no tier names for the default day");

        Clock.Advance(TimeSpan.FromDays(2));
        await ApplyBounds(store);
        (await JobNames(store)).Should().BeEquivalentTo(["failed"], "a failure is kept for its own month");

        Clock.Advance(TimeSpan.FromDays(30));
        await ApplyBounds(store);
        (await JobNames(store)).Should().BeEmpty();
    }

    [Test]
    public async Task ARowWithoutAResultIsKeptForTheAgeItsSucceededFlagImplies()
    {
        IExecutionHistoryStore store = await CreateStore(options =>
        {
            options.Retention = TimeSpan.FromHours(1);
            options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(30);
        });

        await store.AddExecution(Execution(Start, "old-success"));
        await store.AddExecution(Failed(Start, "old-failure", retryAttempt: 0, retryScheduled: false));

        Clock.Advance(TimeSpan.FromDays(1));
        await ApplyBounds(store);

        (await JobNames(store)).Should().BeEquivalentTo(["old-failure"],
            "a row from before 4.4 is kept for the tier of the result its Succeeded flag implies");
    }

    [Test]
    public async Task AnAgeOfForeverKeepsEverything()
    {
        IExecutionHistoryStore store = await CreateStore(options => options.RetentionByResult[JobRunResult.Failed] = TimeSpan.MaxValue);

        await store.AddExecution(Run(Start, "failed", JobRunResult.Failed));
        Clock.Advance(TimeSpan.FromDays(3650));
        await ApplyBounds(store);

        (await JobNames(store)).Should().BeEquivalentTo(["failed"],
            "TimeSpan.MaxValue is how a failure is kept for good, and it must not overflow the cutoff");
    }

    [Test]
    public async Task EachJobKeepsItsOwnNewestRowsAndEveryFailure()
    {
        IExecutionHistoryStore store = await CreateStore(options => options.MaxEntriesPerJob = 2);

        await store.AddExecution(Run(Start.AddMinutes(-9), nightly.Name, JobRunResult.Failed));
        await store.AddExecution(Run(Start.AddMinutes(-8), nightly.Name, JobRunResult.Failed));
        await store.AddExecution(Run(Start.AddMinutes(-7), nightly.Name, JobRunResult.Succeeded));
        await store.AddExecution(Run(Start.AddMinutes(-6), nightly.Name, JobRunResult.Skipped));
        await store.AddExecution(Run(Start.AddMinutes(-5), nightly.Name, JobRunResult.Cancelled));
        await store.AddExecution(Run(Start.AddMinutes(-4), nightly.Name, JobRunResult.Succeeded));
        await store.AddExecution(Run(Start.AddMinutes(-3), hourly.Name, JobRunResult.Succeeded));
        await ApplyBounds(store);

        List<ExecutionHistoryEntry> rows = (await Executions(store)).Items.ToList();

        rows.Where(row => row.JobName == nightly.Name).Select(row => row.FiredAtUtc).Should().BeEquivalentTo(
            [Start.AddMinutes(-4), Start.AddMinutes(-5), Start.AddMinutes(-8), Start.AddMinutes(-9)],
            "the job keeps its two newest runs that did not fail, and every failure beside them: a job that runs "
            + "often must not push its own failures out");
        rows.Where(row => row.JobName == hourly.Name).Should().ContainSingle("another job's rows are its own to count");
    }

    [Test]
    public async Task ARunThatCompletesLateIsCappedByItsFireTime()
    {
        IExecutionHistoryStore store = await CreateStore(options => options.MaxEntriesPerJob = 1);

        await store.AddExecution(Run(Start, nightly.Name, JobRunResult.Succeeded));
        await store.AddExecution(Run(Start.AddMinutes(-1), nightly.Name, JobRunResult.Succeeded));
        await ApplyBounds(store);

        (await Executions(store)).Items.Should().ContainSingle().Which.FiredAtUtc.Should().Be(Start,
            "the row that goes is the one that fired first, not the one that arrived first");
    }

    [Test]
    public async Task TheSchedulerWideCountIsTheBackstopEvenForFailures()
    {
        IExecutionHistoryStore store = await CreateStore(options =>
        {
            options.MaxEntriesPerJob = 10;
            options.MaxEntriesPerScheduler = 2;
        });

        await store.AddExecution(Run(Start.AddMinutes(-3), nightly.Name, JobRunResult.Failed));
        await store.AddExecution(Run(Start.AddMinutes(-2), nightly.Name, JobRunResult.Failed));
        await store.AddExecution(Run(Start.AddMinutes(-1), nightly.Name, JobRunResult.Failed));
        await ApplyBounds(store);

        (await Executions(store)).Items.Should().HaveCount(2, "the exemption is from the per-job cap, not from the feed's own");
    }

    [Test]
    public async Task MisfiresAreKeptForTheirOwnAge()
    {
        IExecutionHistoryStore store = await CreateStore(options =>
        {
            options.Retention = TimeSpan.FromHours(24);
            options.MisfireRetention = TimeSpan.FromHours(1);
        });

        await store.AddMisfire(Misfire(Start, "at-midnight"));
        await store.AddExecution(Execution(Start, nightly.Name));

        Clock.Advance(TimeSpan.FromHours(2));
        await ApplyBounds(store);

        (await Misfires(store)).Items.Should().BeEmpty("the misfire feed is kept for MisfireRetention");
        (await Executions(store)).Items.Should().ContainSingle("the executions keep their own age");
    }

    [Test]
    public async Task MisfiresAreKeptForTheDefaultAgeWhenTheyHaveNoneOfTheirOwn()
    {
        IExecutionHistoryStore store = await CreateStore(options => options.Retention = TimeSpan.FromHours(3));

        await store.AddMisfire(Misfire(Start, "at-midnight"));

        Clock.Advance(TimeSpan.FromHours(2));
        (await Misfires(store)).Items.Should().ContainSingle();

        Clock.Advance(TimeSpan.FromHours(2));
        await ApplyBounds(store);
        (await Misfires(store)).Items.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // Filters
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ExecutionsCanBeReadForOneJobExactly()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Execution(Start, "nightly"));
        await store.AddExecution(Execution(Start, "nightly-report"));
        await store.AddExecution(Execution(Start, "Nightly"));
        await store.AddExecution(Execution(Start, "nightly") with { JobGroup = "other" });

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            Job = new JobKey("nightly", JobGroup)
        });

        page.Items.Should().ContainSingle("a key is matched whole and as written, where JobContains matches a fragment")
            .Which.Should().Match<ExecutionHistoryEntry>(row => row.JobName == "nightly" && row.JobGroup == JobGroup);
    }

    [Test]
    public async Task ExecutionsCanBeReadForAWindowOfFireTimes()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        foreach (int minutes in new[] { -3, -2, -1, 0 })
        {
            await store.AddExecution(Execution(Start.AddMinutes(minutes), "at" + minutes));
        }

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            FiredFrom = Start.AddMinutes(-2),
            FiredBefore = Start
        });

        page.Items.Select(row => row.JobName).Should().Equal(["at-1", "at-2"],
            "the start of the window is inclusive and its end exclusive, so two windows that meet list a row once");
    }

    [Test]
    public async Task ExecutionsCanBeReadByResult()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Run(Start, "succeeded", JobRunResult.Succeeded));
        await store.AddExecution(Run(Start, "skipped", JobRunResult.Skipped));
        await store.AddExecution(Run(Start, "failed", JobRunResult.Failed));
        await store.AddExecution(Run(Start, "cancelled", JobRunResult.Cancelled));
        await store.AddExecution(Execution(Start, "legacy-success"));
        await store.AddExecution(Failed(Start, "legacy-failure", retryAttempt: 0, retryScheduled: false));

        (await JobNames(store, [JobRunResult.Failed, JobRunResult.Cancelled])).Should().BeEquivalentTo(
            ["failed", "cancelled", "legacy-failure"],
            "a row from before 4.4 is matched by the result its Succeeded flag implies");
        (await JobNames(store, [JobRunResult.Succeeded])).Should().BeEquivalentTo(["succeeded", "legacy-success"]);
        (await JobNames(store, [])).Should().BeEmpty("an empty set of results is a question with no answer, not no question");
    }

    [Test]
    public async Task CancelledRunsAreFinalFailures()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Run(Start, "cancelled", JobRunResult.Cancelled));
        await store.AddExecution(Run(Start, "skipped", JobRunResult.Skipped));

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            FailedFinally = true
        });

        page.Items.Should().ContainSingle().Which.JobName.Should().Be("cancelled",
            "a cancelled run did not succeed and nothing will retry it; a skip counts as a success");
    }

    [Test]
    public async Task MisfiresCanBeReadForOneJob()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddMisfire(Misfire(Start, "at-midnight"));
        await store.AddMisfire(Misfire(Start, "on-the-hour") with { JobKey = hourly });
        await store.AddMisfire(Misfire(Start, "orphan") with { JobKey = null });
        await store.AddMisfire(Misfire(Start, "vetoed") with { Reason = MisfireReason.Vetoed });

        PagedResult<MisfireHistoryEntry> page = await store.QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = SchedulerName,
            Job = nightly
        });

        page.Items.Select(row => row.TriggerName).Should().BeEquivalentTo(["at-midnight", "vetoed"]);
        (await store.CountMisfires(SchedulerName, Start.AddHours(-1))).Should().Be(3,
            "a veto is recorded beside the misfires and is not counted as one");
    }

    // ---------------------------------------------------------------------------------------------
    // Per-job status
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task EachRecordedRunMovesItsJobsStatus()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Run(Start.AddMinutes(-3), nightly.Name, JobRunResult.Succeeded));
        await store.AddExecution(Run(Start.AddMinutes(-2), nightly.Name, JobRunResult.Failed) with { ExceptionMessage = "first" });
        await store.AddExecution(Run(Start.AddMinutes(-1), nightly.Name, JobRunResult.Failed) with
        {
            ExceptionMessage = "second",
            EntryId = "latest",
            Summary = "gave up"
        });

        JobRunStatus? status = await store.GetJobRunStatus(SchedulerName, nightly);

        status.Should().NotBeNull();
        status!.RunCount.Should().Be(3);
        status.FailureCount.Should().Be(2);
        status.ConsecutiveFailures.Should().Be(2);
        status.LastResult.Should().Be(JobRunResult.Failed);
        status.LastFiredAtUtc.Should().Be(Start.AddMinutes(-1));
        status.LastEntryId.Should().Be("latest");
        status.LastSummary.Should().Be("gave up");
        status.LastSucceededAtUtc.Should().Be(Start.AddMinutes(-3));
        status.LastFailureMessage.Should().Be("second");
        status.FirstFiredAtUtc.Should().Be(Start.AddMinutes(-3));
    }

    /// <summary>
    /// A run whose job threw is recorded, row and status, with the job's own message.
    /// </summary>
    /// <remarks>
    /// Recorded through <see cref="ExecutionHistoryPlugin" />, handed the exception in the shape the run
    /// shell reports it: <c>JobExecutionException</c> → <c>JobExecutionProcessException</c> → what the job
    /// threw. Both wrappers say "Job threw an unhandled exception", which 4.3 recorded for every such job.
    /// </remarks>
    [Test]
    public async Task AFailedRunIsRecordedWithTheMessageTheJobThrew()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        ServiceCollection services = new();
        services.AddSingleton(store);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ExecutionHistoryPlugin recorder = new(provider, Clock);

        using (JobExecutionContextImpl firing = FailedFiring(Start.AddMinutes(-1)))
        {
            JobExecutionException wrapped = new(new JobExecutionProcessException(firing, new InvalidOperationException("the ledger is locked")));
            await recorder.JobWasExecuted(firing, wrapped);
        }

        (await Executions(store)).Items.Should().ContainSingle().Which.ExceptionMessage.Should().Be("the ledger is locked",
            "the row names what the job threw, not the run shell's wrapper around it");
        (await store.GetJobRunStatus(SchedulerName, nightly))!.LastFailureMessage.Should().Be("the ledger is locked",
            "the status is folded from the row, so it carries the same message");

        using (JobExecutionContextImpl firing = FailedFiring(Start))
        {
            await recorder.JobWasExecuted(firing, new JobExecutionException("quota exceeded", new InvalidOperationException("HTTP 429")));
        }

        (await store.GetJobRunStatus(SchedulerName, nightly))!.LastFailureMessage.Should().Be("quota exceeded",
            "a JobExecutionException the job threw itself is what it chose to say, and is not looked through to its cause");
    }

    [Test]
    public async Task AStatusOutlivesItsRows()
    {
        IExecutionHistoryStore store = await CreateStore(options => options.Retention = TimeSpan.FromHours(1));
        await KeepJob(nightly);

        await store.AddExecution(Run(Start, nightly.Name, JobRunResult.Failed));
        Clock.Advance(TimeSpan.FromDays(1));
        await ApplyBounds(store);

        (await Executions(store)).Items.Should().BeEmpty();
        JobRunStatus? status = await store.GetJobRunStatus(SchedulerName, nightly);
        status.Should().NotBeNull("the status is what says a job has been failing since before the history reaches");
        status!.ConsecutiveFailures.Should().Be(1);
    }

    [Test]
    public async Task AJobWithNoRecordedRunHasNoStatus()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        (await store.GetJobRunStatus(SchedulerName, nightly)).Should().BeNull();

        await store.AddExecution(Execution(Start, hourly.Name));

        (await store.GetJobRunStatus(SchedulerName, nightly)).Should().BeNull();
        (await store.GetJobRunStatus("another scheduler", hourly)).Should().BeNull("a status belongs to one scheduler");
    }

    [Test]
    public async Task StatusesAreListedByGroupAndThenName()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Execution(Start, "b") with { JobGroup = "g2" });
        await store.AddExecution(Execution(Start, "b") with { JobGroup = "g1" });
        await store.AddExecution(Execution(Start, "a") with { JobGroup = "g2" });
        await store.AddExecution(Execution(Start, "C") with { JobGroup = "g1" });

        PagedResult<JobRunStatus> page = await store.QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = SchedulerName,
            IncludeTotalCount = true
        });

        page.Items.Select(status => status.Job.ToString()).Should().Equal(["g1.C", "g1.b", "g2.a", "g2.b"],
            "a status page is ordered as every paged query is: group, then name, ordinal");
        page.TotalCount.Should().Be(4);

        PagedResult<JobRunStatus> second = await store.QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Skip = 1,
            Take = 2
        });

        second.Items.Select(status => status.Job.ToString()).Should().Equal(["g1.b", "g2.a"]);
        second.HasMore.Should().BeTrue();
    }

    [Test]
    public async Task StatusesOfNamedJobsArePagedInOrderAndCounted()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Execution(Start, "b") with { JobGroup = "g2" });
        await store.AddExecution(Execution(Start, "a") with { JobGroup = "g2" });
        await store.AddExecution(Execution(Start, "z") with { JobGroup = "g1" });

        PagedResult<JobRunStatus> named = await store.QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Jobs = [new JobKey("b", "g2"), new JobKey("z", "g1"), new JobKey("a", "g2")],
            Take = 2,
            IncludeTotalCount = true
        });

        named.Items.Select(status => status.Job.ToString()).Should().Equal(["g1.z", "g2.a"],
            "named jobs are listed by group and then name, whatever order they were named in");
        named.HasMore.Should().BeTrue();
        named.TotalCount.Should().Be(3);

        PagedResult<JobRunStatus> count = await store.QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Take = 0,
            IncludeTotalCount = true
        });

        count.Items.Should().BeEmpty("the count idiom reads no page");
        count.TotalCount.Should().Be(3);
        count.HasMore.Should().BeTrue();
    }

    [Test]
    public async Task StatusesCanBeReadForSomeJobsOrForTheFailingOnes()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Run(Start, "healthy", JobRunResult.Succeeded));
        await store.AddExecution(Run(Start, "failing", JobRunResult.Failed));
        await store.AddExecution(Run(Start.AddMinutes(-1), "recovered", JobRunResult.Failed));
        await store.AddExecution(Run(Start, "recovered", JobRunResult.Succeeded));
        await store.AddExecution(Run(Start, "retrying", JobRunResult.Failed) with { RetryScheduled = true });

        (await StatusNames(store, failing: true)).Should().Equal(["failing"],
            "failing means the last occurrence failed for good; a retry has an attempt left and a recovery succeeded since");
        (await StatusNames(store, failing: false)).Should().Equal(["healthy", "recovered", "retrying"]);

        (await StatusNames(store, jobs: [new JobKey("failing", JobGroup), new JobKey("absent", JobGroup), new JobKey("failing", JobGroup)]))
            .Should().Equal(["failing"], "a job named twice is listed once, and a job with no runs is not listed");
        (await StatusNames(store, jobs: [])).Should().BeEmpty();
    }

    /// <summary>
    /// The misfire feed narrowed to some reasons: the page, and a total that agrees with it.
    /// </summary>
    /// <remarks>
    /// The HTTP API lists <see cref="MisfireReason.Missed" /> and <see cref="MisfireReason.Overlap" /> unless
    /// asked for more, so that a 4.3 client never meets <see cref="MisfireReason.Vetoed" />. A store that
    /// ignored the filter would page vetoes in and count them.
    /// </remarks>
    [Test]
    public async Task MisfiresCanBeReadByReason()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddMisfire(Misfire(Start.AddMinutes(-3), "missed"));
        await store.AddMisfire(Misfire(Start.AddMinutes(-2), "skipped") with { Reason = MisfireReason.Overlap });
        await store.AddMisfire(Misfire(Start.AddMinutes(-1), "vetoed") with { Reason = MisfireReason.Vetoed });

        PagedResult<MisfireHistoryEntry> readable = await store.QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = SchedulerName,
            Reasons = [MisfireReason.Missed, MisfireReason.Overlap],
            IncludeTotalCount = true
        });

        readable.Items.Select(row => row.TriggerName).Should().Equal(["skipped", "missed"]);
        readable.TotalCount.Should().Be(2, "the total counts what the page lists, not the vetoes it leaves out");

        (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = SchedulerName, Reasons = [MisfireReason.Vetoed] }))
            .Items.Should().ContainSingle().Which.TriggerName.Should().Be("vetoed");

        PagedResult<MisfireHistoryEntry> none = await store.QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = SchedulerName,
            Reasons = [],
            IncludeTotalCount = true
        });

        none.Items.Should().BeEmpty("an empty set of reasons is a question with no answer, not no question");
        none.TotalCount.Should().Be(0);
    }

    [Test]
    public async Task AVetoedFiringIsReadBackFromTheMisfireFeed()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddMisfire(Misfire(Start, "vetoed") with { Reason = MisfireReason.Vetoed });

        (await Misfires(store)).Items.Should().ContainSingle().Which.Reason.Should().Be(MisfireReason.Vetoed,
            "a veto is recorded in the misfire feed, and a reader asking why a firing did not run has to see that it was vetoed");
        (await store.CountMisfires(SchedulerName, Start.AddHours(-1))).Should().Be(0, "a veto is not a misfire");
    }

    [Test]
    public async Task OutOfOrderCompletionsAreCountedAndLeaveTheLatestRunAlone()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Run(Start, nightly.Name, JobRunResult.Succeeded) with { EntryId = "latest", Summary = "on time" });
        await store.AddExecution(Run(Start.AddMinutes(-5), nightly.Name, JobRunResult.Failed) with
        {
            EntryId = "late",
            ExceptionMessage = "finished after the later run"
        });

        JobRunStatus status = (await store.GetJobRunStatus(SchedulerName, nightly))!;

        status.RunCount.Should().Be(2, "a run that completes late is still a run");
        status.FailureCount.Should().Be(1);
        status.LastEntryId.Should().Be("latest", "the latest-fired run is still the last run");
        status.LastResult.Should().Be(JobRunResult.Succeeded);
        status.LastSummary.Should().Be("on time");
        status.ConsecutiveFailures.Should().Be(0, "a failure older than the last success does not make the job failing");
        status.LastFailedAtUtc.Should().Be(Start.AddMinutes(-5));
        status.LastFailureMessage.Should().Be("finished after the later run");
        status.FirstFiredAtUtc.Should().Be(Start.AddMinutes(-5), "the earliest fire time is a minimum, whichever order the runs completed in");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes <paramref name="job" /> one the scheduler still has, for a store that forgets the status of a
    /// job that is gone.
    /// </summary>
    protected virtual ValueTask KeepJob(JobKey job) => default;

    protected static ExecutionHistoryEntry Run(DateTimeOffset firedAt, string jobName, JobRunResult result) =>
        Execution(firedAt, jobName) with
        {
            Result = result,
            Succeeded = result is JobRunResult.Succeeded or JobRunResult.Skipped
        };

    /// <summary>
    /// A firing of <c>nightly</c> that failed for good, as the scheduler hands it to its job listeners.
    /// </summary>
    private static JobExecutionContextImpl FailedFiring(DateTimeOffset firedAt)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(SchedulerName);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns("node-a");

        JobExecutionContextImpl firing = JobExecutionContextBuilder.For(new FailingJob())
            .WithJob(JobBuilder.Create<FailingJob>().WithIdentity(nightly).Build())
            .WithScheduler(scheduler)
            .FiredAt(firedAt)
            .Build();

        firing.Settle(ExecutionOutcome.Failed, retryScheduled: false);
        return firing;
    }

    private static async Task<List<string>> JobNames(IExecutionHistoryStore store, IReadOnlyCollection<JobRunResult>? results = null)
    {
        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            Results = results
        });

        return page.Items.Select(row => row.JobName).ToList();
    }

    protected static async Task<List<string>> StatusNames(
        IExecutionHistoryStore store,
        bool? failing = null,
        IReadOnlyCollection<JobKey>? jobs = null)
    {
        PagedResult<JobRunStatus> page = await store.QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Failing = failing,
            Jobs = jobs
        });

        return page.Items.Select(status => status.Job.Name).ToList();
    }

    /// <summary>The job a failed firing is for; it is never run.</summary>
    private sealed class FailingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("never run: the firing is built, not fired");
        }
    }
}
