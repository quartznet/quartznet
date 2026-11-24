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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The 4.9 per-trigger run status, which both shipped stores keep beside the per-job one (#4243).
/// </summary>
/// <remarks>
/// A partial of the contract, so it runs against the in-memory store and the database-backed one alike. The
/// trigger's status counts the firings of its own key, by the rule the job's is folded by, and leaves out a manual
/// run and a firing after which the trigger is finalized.
/// </remarks>
public abstract partial class ExecutionHistoryStoreContractTest
{
    private static readonly JobKey report = new("report", JobGroup);

    [Test]
    public async Task EachTriggerOfAJobCountsItsOwnFiringsAndTheJobCountsThemAll()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired("daily", Start.AddMinutes(-3), JobRunResult.Succeeded));
        await store.AddExecution(Fired("hourly", Start.AddMinutes(-2), JobRunResult.Failed) with { ExceptionMessage = "the feed is down" });
        await store.AddExecution(Fired("hourly", Start.AddMinutes(-1), JobRunResult.Failed) with { ExceptionMessage = "still down" });
        await store.AddExecution(Fired("daily", Start, JobRunResult.Succeeded));

        TriggerRunStatus daily = (await TriggerStatus(store, "daily"))!;
        TriggerRunStatus hourly = (await TriggerStatus(store, "hourly"))!;

        daily.RunCount.Should().Be(2, "a trigger's status counts its own firings");
        daily.ConsecutiveFailures.Should().Be(0, "its schedule is working");
        daily.LastResult.Should().Be(JobRunResult.Succeeded);
        daily.Trigger.Should().Be(new TriggerKey("daily", TriggerGroup));

        hourly.RunCount.Should().Be(2);
        hourly.ConsecutiveFailures.Should().Be(2, "the other schedule of the same job has failed twice in a row");
        hourly.LastFailureMessage.Should().Be("still down");

        JobRunStatus job = (await store.GetJobRunStatus(SchedulerName, report))!;
        job.RunCount.Should().Be(4, "the job's status counts every firing of every trigger");
        job.ConsecutiveFailures.Should().Be(0, "the job's last run succeeded, whichever trigger fired it");
    }

    [Test]
    public async Task EachRecordedRunMovesItsTriggersStatusAsTheFoldDoes()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        ExecutionHistoryEntry[] runs =
        [
            Fired("nightly", Start.AddMinutes(-10), JobRunResult.Succeeded) with { EntryId = "s1", Summary = "fine" },
            Fired("nightly", Start.AddMinutes(-9), JobRunResult.Failed) with { EntryId = "r1", RetryScheduled = true, ExceptionMessage = "attempt 1" },
            Fired("nightly", Start.AddMinutes(-8), JobRunResult.Failed) with { EntryId = "r2", RetryAttempt = 1, ExceptionMessage = "gave up" },
            Fired("nightly", Start.AddMinutes(-7), JobRunResult.Cancelled) with { EntryId = "c1" },
            Fired("nightly", Start.AddMinutes(-6), JobRunResult.Failed) with { EntryId = "f1", ExceptionMessage = "gave up again" },
            Fired("nightly", Start.AddMinutes(-20), JobRunResult.Failed) with { EntryId = "late", ExceptionMessage = "completed late" },
            Fired("nightly", Start.AddMinutes(-5), JobRunResult.Skipped) with { EntryId = "k1", Summary = "nothing to do" }
        ];

        TriggerRunStatus? folded = null;
        foreach (ExecutionHistoryEntry run in runs)
        {
            folded = TriggerRunStatusFold.Apply(folded, run);
            await store.AddExecution(run);

            (await TriggerStatus(store, "nightly")).Should().BeEquivalentTo(folded,
                "the store keeps the fold of the trigger's runs, after {0}", run.EntryId);
        }

        folded!.ConsecutiveFailures.Should().Be(0, "the premise: the skip at the end resets the run of failures");
        folded.FailureCount.Should().Be(3, "two final failures in order and one that completed late; the retried attempt is not one");
    }

    [Test]
    public async Task TriggerStatusesCanBeReadForSomeTriggersOrForTheFailingOnes()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired("healthy", Start, JobRunResult.Succeeded));
        await store.AddExecution(Fired("failing", Start, JobRunResult.Failed));
        await store.AddExecution(Fired("recovered", Start.AddMinutes(-1), JobRunResult.Failed));
        await store.AddExecution(Fired("recovered", Start, JobRunResult.Succeeded));
        await store.AddExecution(Fired("retrying", Start, JobRunResult.Failed) with { RetryScheduled = true });

        (await TriggerNames(store, failing: true)).Should().Equal(["failing"],
            "failing means the last occurrence failed for good; a retry has an attempt left and a recovery succeeded since");
        (await TriggerNames(store, failing: false)).Should().Equal(["healthy", "recovered", "retrying"]);
        (await TriggerNames(store)).Should().Equal(["failing", "healthy", "recovered", "retrying"]);

        (await TriggerNames(store, triggers: [Trigger("failing"), Trigger("absent"), Trigger("failing"), Trigger("healthy")]))
            .Should().Equal(["failing", "healthy"], "a trigger named twice is listed once, and one with no runs is not listed");
        (await TriggerNames(store, triggers: [Trigger("failing"), Trigger("healthy")], failing: true))
            .Should().Equal(["failing"], "the named triggers are narrowed by the failing filter too");
        (await TriggerNames(store, triggers: [])).Should().BeEmpty("an empty set lists nothing");
        (await TriggerNames(store, triggers: [Trigger("absent")])).Should().BeEmpty();
    }

    [Test]
    public async Task TriggerStatusesAreListedByGroupAndThenNameAndPaged()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired("b", Start, JobRunResult.Succeeded) with { TriggerGroup = "g2" });
        await store.AddExecution(Fired("b", Start, JobRunResult.Succeeded) with { TriggerGroup = "g1" });
        await store.AddExecution(Fired("a", Start, JobRunResult.Succeeded) with { TriggerGroup = "g2" });
        await store.AddExecution(Fired("C", Start, JobRunResult.Succeeded) with { TriggerGroup = "g1" });

        PagedResult<TriggerRunStatus> page = await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery
        {
            SchedulerName = SchedulerName,
            IncludeTotalCount = true
        });

        page.Items.Select(status => status.Trigger.ToString()).Should().Equal(["g1.C", "g1.b", "g2.a", "g2.b"],
            "a status page is ordered as every paged query is: group, then name, ordinal");
        page.TotalCount.Should().Be(4);
        page.HasMore.Should().BeFalse();

        PagedResult<TriggerRunStatus> second = await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Skip = 1,
            Take = 2
        });

        second.Items.Select(status => status.Trigger.ToString()).Should().Equal(["g1.b", "g2.a"]);
        second.HasMore.Should().BeTrue();

        PagedResult<TriggerRunStatus> named = await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Triggers = [new TriggerKey("b", "g2"), new TriggerKey("C", "g1"), new TriggerKey("a", "g2")],
            Take = 2,
            IncludeTotalCount = true
        });

        named.Items.Select(status => status.Trigger.ToString()).Should().Equal(["g1.C", "g2.a"],
            "named triggers are listed by group and then name, whatever order they were named in");
        named.HasMore.Should().BeTrue();
        named.TotalCount.Should().Be(3);

        PagedResult<TriggerRunStatus> count = await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Take = 0,
            IncludeTotalCount = true
        });

        count.Items.Should().BeEmpty("the count idiom reads no page");
        count.TotalCount.Should().Be(4);

        (await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery { SchedulerName = "another scheduler" }))
            .Items.Should().BeEmpty("a status belongs to one scheduler");
    }

    /// <summary>
    /// A firing after which its trigger is finalized folds into its job's status and leaves its trigger none.
    /// </summary>
    [Test]
    public async Task AFiringThatFinalizedItsTriggerLeavesNoTriggerStatus()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        ITriggerStatusHistoryStore recorder = (ITriggerStatusHistoryStore) store;

        await recorder.AddExecution(Fired("one-off", Start, JobRunResult.Failed), triggerFinalized: true);
        await recorder.AddExecution(Fired("recurring", Start, JobRunResult.Succeeded), triggerFinalized: false);

        (await TriggerNames(store)).Should().Equal(["recurring"],
            "a one-off's trigger is deleted as its firing completes, so no listing would show its status");
        (await store.GetJobRunStatus(SchedulerName, report))!.RunCount.Should().Be(2, "the job counts both firings");
        (await Executions(store)).Items.Should().HaveCount(2, "both rows are recorded");
    }

    /// <summary>
    /// A manual run fires under a trigger of its own, so it never folds into a trigger's status, however the
    /// recorder describes it.
    /// </summary>
    [Test]
    public async Task AManualRunNeverFoldsIntoATriggersStatus()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired("MT_manual", Start, JobRunResult.Succeeded) with { Manual = true });
        await ((ITriggerStatusHistoryStore) store).AddExecution(
            Fired("MT_other", Start, JobRunResult.Succeeded) with { Manual = true }, triggerFinalized: false);

        (await TriggerNames(store)).Should().BeEmpty("a manual run is no firing of a schedule");
        (await store.GetJobRunStatus(SchedulerName, report))!.RunCount.Should().Be(2, "it is still a run of the job");
    }

    /// <summary>
    /// A one-off whose first attempt failed and was retried has a next fire time when it completes, so that attempt
    /// folds; the last attempt finalizes the trigger and does not.
    /// </summary>
    [Test]
    public async Task AOneOffsRetriedFirstAttemptFoldsAndItsLastAttemptDoesNot()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        ITriggerStatusHistoryStore recorder = (ITriggerStatusHistoryStore) store;

        await recorder.AddExecution(
            Fired("retried-once", Start, JobRunResult.Failed) with { RetryScheduled = true, ExceptionMessage = "attempt 0" },
            triggerFinalized: false);
        await recorder.AddExecution(
            Fired("retried-once", Start.AddMinutes(1), JobRunResult.Failed) with { RetryAttempt = 1, ExceptionMessage = "attempt 1" },
            triggerFinalized: true);

        TriggerRunStatus status = (await TriggerStatus(store, "retried-once"))!;
        status.RunCount.Should().Be(1, "the first attempt folded and the finalizing one did not");
        status.ConsecutiveFailures.Should().Be(0, "a retried failure is not a final one");
        status.LastFailureMessage.Should().Be("attempt 0");
    }

    [Test]
    public async Task ATriggerStatusOutlivesItsRows()
    {
        IExecutionHistoryStore store = await CreateStore(options => options.Retention = TimeSpan.FromHours(1));
        await KeepTrigger(Trigger("nightly"));

        await store.AddExecution(Fired("nightly", Start, JobRunResult.Failed));
        Clock.Advance(TimeSpan.FromDays(1));
        await ApplyBounds(store);

        (await Executions(store)).Items.Should().BeEmpty();
        TriggerRunStatus? status = await TriggerStatus(store, "nightly");
        status.Should().NotBeNull("the status is what says a schedule has been failing since before the history reaches");
        status!.ConsecutiveFailures.Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes <paramref name="trigger" /> one the scheduler still has, for a store that forgets the status of a
    /// trigger that is gone.
    /// </summary>
    protected virtual ValueTask KeepTrigger(TriggerKey trigger) => default;

    /// <summary>One firing of <see cref="report" /> by the trigger <paramref name="triggerName" />.</summary>
    protected static ExecutionHistoryEntry Fired(string triggerName, DateTimeOffset firedAt, JobRunResult result) =>
        Run(firedAt, report.Name, result) with { TriggerName = triggerName };

    protected static TriggerKey Trigger(string name) => new(name, TriggerGroup);

    protected static async Task<TriggerRunStatus?> TriggerStatus(IExecutionHistoryStore store, string triggerName)
    {
        PagedResult<TriggerRunStatus> page = await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Triggers = [Trigger(triggerName)]
        });

        return page.Items.Count > 0 ? page.Items[0] : null;
    }

    protected static async Task<List<string>> TriggerNames(
        IExecutionHistoryStore store,
        bool? failing = null,
        IReadOnlyCollection<TriggerKey>? triggers = null)
    {
        PagedResult<TriggerRunStatus> page = await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery
        {
            SchedulerName = SchedulerName,
            Failing = failing,
            Triggers = triggers
        });

        return page.Items.Select(status => status.Trigger.Name).ToList();
    }
}
