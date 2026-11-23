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

using FakeItEasy;

using Quartz.Dashboard.Services;
using Quartz.Extensibility;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The rules of Trigger Detail's "Why isn't it firing?" (#4411), judged without a page, and the reads they
/// are judged from.
/// </summary>
public class TriggerDiagnosisTest
{
    private const string SchedulerName = "acme";
    private const string NodeA = "node-a";

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void AHealthyTriggerHasNothingHoldingItBack()
    {
        List<DiagnosisCheck> checks = Evaluate(Facts(Trigger()));

        checks.Select(check => check.Name).Should().Equal(
            ["Scheduler", "Trigger", "Trigger group", "Job group", "Its job's firings", "Next fire", "Execution group", "Misfires", "Retry"],
            "every check is shown, in the order an operator works through them");
        checks.Should().OnlyContain(check => check.Verdict == DiagnosisVerdict.Ok);
        Check(checks, "Next fire").Text.Should().Be(Format(Now.AddHours(1)) + " (in 1h).");
    }

    [TestCase(SchedulerStatus.Standby, "In standby: nothing fires until it is started.")]
    [TestCase(SchedulerStatus.Created, "Not started: nothing fires until it is.")]
    [TestCase(SchedulerStatus.Shutdown, "Shut down: nothing fires here again.")]
    public void ASchedulerThatIsNotRunningHoldsEveryTriggerBack(SchedulerStatus status, string text)
    {
        DiagnosisCheck check = Check(Evaluate(Facts(Trigger()) with { Scheduler = Answered(Scheduler(status)) }), "Scheduler");

        check.Verdict.Should().Be(DiagnosisVerdict.Problem);
        check.Text.Should().Be(text);
    }

    /// <summary>
    /// A window has no status of its own: <see cref="IQuartzApiClient.GetScheduler" /> answers the cluster's,
    /// from its check-ins, and the check says so in those terms.
    /// </summary>
    [TestCase(SchedulerStatus.Running, "Ok", "Running: a node of the cluster checked in within its interval.")]
    [TestCase(SchedulerStatus.Shutdown, "Problem", "No node of the cluster is checking in: nothing fires until one starts.")]
    [TestCase(SchedulerStatus.Unknown, "Unknown", "The store holds no check-in row for this scheduler, so whether a node runs it cannot be told from here.")]
    public void AWindowsStatusIsTheClustersCheckIns(SchedulerStatus status, string verdictName, string text)
    {
        DiagnosisVerdict verdict = Enum.Parse<DiagnosisVerdict>(verdictName);
        TriggerDiagnosisFacts facts = Facts(Trigger(), window: true) with { Scheduler = Answered(Scheduler(status)) };

        DiagnosisCheck check = Check(Evaluate(facts), "Scheduler");

        check.Verdict.Should().Be(verdict);
        check.Text.Should().Be(text);
    }

    [Test]
    public void APausedTriggerSaysWhoPausedItWhenAndWhy()
    {
        PauseInfo pause = new("vendor outage", "ops@example.com", Now.AddHours(-2));

        DiagnosisCheck check = Check(Evaluate(Facts(Trigger(), TriggerState.Paused, pause)), "Trigger");

        check.Verdict.Should().Be(DiagnosisVerdict.Problem);
        check.Text.Should().Be("Paused by ops@example.com at " + Format(Now.AddHours(-2)) + " (vendor outage).");
    }

    [Test]
    public void APausedTriggerGroupIsWhatHoldsAPausedTriggerBack()
    {
        TriggerDiagnosisFacts facts = Facts(Trigger(), TriggerState.Paused) with { PausedTriggerGroups = Answered(Set("nightly")) };

        DiagnosisCheck check = Check(Evaluate(facts), "Trigger group");

        check.Verdict.Should().Be(DiagnosisVerdict.Problem);
        check.Text.Should().StartWith("'nightly' is paused");
        check.LinkPath.Should().Be("triggers?group=nightly");
    }

    [Test]
    public void APausedGroupBesideAResumedTriggerIsWorthKnowingOnly()
    {
        TriggerDiagnosisFacts facts = Facts(Trigger()) with { PausedTriggerGroups = Answered(Set("nightly")) };

        Check(Evaluate(facts), "Trigger group").Verdict.Should().Be(DiagnosisVerdict.Info,
            "the trigger was resumed on its own, so the group's pause holds only what is stored into it later");
    }

    [Test]
    public void APausedJobGroupSaysWhoPausedIt()
    {
        TriggerDiagnosisFacts facts = Facts(Trigger(), TriggerState.Paused) with
        {
            PausedJobGroups = Answered(Set("batch")),
            JobGroupPause = Answered<PauseInfo?>(new PauseInfo("quarter close", "finance", Now.AddDays(-1))),
        };

        DiagnosisCheck check = Check(Evaluate(facts), "Job group");

        check.Verdict.Should().Be(DiagnosisVerdict.Problem);
        check.Text.Should().StartWith("'batch' is paused by finance at " + Format(Now.AddDays(-1)) + " (quarter close):");
        check.LinkPath.Should().Be("jobs?group=batch");
    }

    /// <summary>
    /// The commonest "nothing runs" case (#2202): the trigger is blocked, and the check names the firing that
    /// holds its job, the node it runs on and since when, with <em>Interrupt</em>.
    /// </summary>
    [Test]
    public void ABlockedTriggerNamesTheFiringThatHoldsItsJob()
    {
        TriggerDiagnosisFacts facts = Facts(Trigger(), TriggerState.Blocked) with
        {
            JobFirings = Answered(Page(Firing("fire-1", NodeA, Now.AddMinutes(-40)), Firing("fire-2", "node-b", Now.AddMinutes(-5)))),
        };

        List<DiagnosisCheck> checks = Evaluate(facts);

        Check(checks, "Trigger").Verdict.Should().Be(DiagnosisVerdict.Problem);
        DiagnosisCheck firings = Check(checks, "Its job's firings");
        firings.Verdict.Should().Be(DiagnosisVerdict.Problem);
        firings.Text.Should().Be("2 running: on node node-a since " + Format(Now.AddMinutes(-40)) + ", the oldest.");
        firings.Action.Should().Be(DiagnosisAction.Interrupt);
        firings.FireInstanceId.Should().Be("fire-1", "the oldest firing is the one holding the job");
    }

    [Test]
    public void ABlockWithNothingRunningIsLeftOver()
    {
        DiagnosisCheck check = Check(Evaluate(Facts(Trigger(), TriggerState.Blocked)), "Its job's firings");

        check.Verdict.Should().Be(DiagnosisVerdict.Warning);
        check.Action.Should().Be(DiagnosisAction.None);
    }

    [Test]
    public void AFiringOfAnUnblockedTriggersJobIsWorthKnowingOnly()
    {
        FireInstanceDto running = Firing("fire-1", NodeA, Now.AddMinutes(-3)) with { InterruptRequestedUtc = Now.AddMinutes(-1) };
        TriggerDiagnosisFacts facts = Facts(Trigger()) with { JobFirings = Answered(Page(running)) };

        DiagnosisCheck check = Check(Evaluate(facts), "Its job's firings");

        check.Verdict.Should().Be(DiagnosisVerdict.Info);
        check.Action.Should().Be(DiagnosisAction.None, "nothing is held, so there is nothing to interrupt from here");
        check.Text.Should().EndWith("An interrupt was requested at " + Format(Now.AddMinutes(-1)) + ".");
    }

    [Test]
    public void ATriggerInErrorShowsItsLastFailureWithReset()
    {
        DashboardHistoryEntry run = new(SchedulerName, NodeA, "batch", "import", "nightly", "at-three", Now.AddHours(-1), TimeSpan.FromSeconds(2), Succeeded: false, "connection refused")
        {
            EntryId = "entry-9",
        };
        TriggerDiagnosisFacts facts = Facts(Trigger(), TriggerState.Error) with { LastRun = Answered(new PagedResult<DashboardHistoryEntry>([run], HasMore: false, TotalCount: 1)) };

        DiagnosisCheck check = Check(Evaluate(facts), "Trigger");

        check.Verdict.Should().Be(DiagnosisVerdict.Problem);
        check.Text.Should().EndWith("Its last run, at " + Format(Now.AddHours(-1)) + ", failed: connection refused");
        check.Action.Should().Be(DiagnosisAction.ResetError);
        check.LinkPath.Should().Be("history/entry-9");
    }

    [Test]
    public void AContinuationWhoseParentWasDeletedSaysSo()
    {
        ITrigger trigger = Trigger(builder => builder.StartAfter(new TriggerKey("extract", "nightly")));

        DiagnosisCheck check = Check(Evaluate(Facts(trigger, TriggerState.Error, continuationParentExists: false)), "Trigger");

        check.Text.Should().Be("In error: the trigger it continues after, nightly.extract, was deleted.");
        check.Action.Should().Be(DiagnosisAction.ResetError);
    }

    [Test]
    public void AnAwaitingTriggerLinksToItsParent()
    {
        ITrigger trigger = Trigger(builder => builder.StartAfter(new TriggerKey("extract", "nightly"), ContinuationCondition.OnSuccess));

        DiagnosisCheck check = Check(Evaluate(Facts(trigger, TriggerState.Awaiting)), "Trigger");

        check.Verdict.Should().Be(DiagnosisVerdict.Info);
        check.LinkPath.Should().Be("triggers/nightly/extract");
    }

    [Test]
    public void ATriggerWhoseEndTimeHasPassedHasNoNextFire()
    {
        ITrigger trigger = Trigger(builder => builder.StartAt(Now.AddDays(-2)).EndAt(Now.AddDays(-1)), noNextFire: true);

        DiagnosisCheck check = Check(Evaluate(Facts(trigger, TriggerState.Complete)), "Next fire");

        check.Verdict.Should().Be(DiagnosisVerdict.Problem);
        check.Text.Should().Be("None: its end time, " + Format(Now.AddDays(-1)) + ", has passed.");
    }

    /// <summary>
    /// "Due" is what the due-backlog gauges count: a waiting trigger more than five minutes past its fire time.
    /// </summary>
    [Test]
    public void AWaitingTriggerFiveMinutesPastItsFireTimeIsOverdue()
    {
        DiagnosisCheck late = Check(Evaluate(Facts(Trigger(next: Now.AddMinutes(-12)))), "Next fire");
        late.Verdict.Should().Be(DiagnosisVerdict.Warning);
        late.Text.Should().StartWith("Overdue: it was due at " + Format(Now.AddMinutes(-12)) + ", 12m ago");

        Check(Evaluate(Facts(Trigger(next: Now.AddMinutes(-4)))), "Next fire").Verdict.Should().Be(DiagnosisVerdict.Ok,
            "four minutes late is inside the cutoff");
    }

    [Test]
    public void ATriggerBeforeItsStartTimeSaysWhenItStarts()
    {
        ITrigger trigger = Trigger(builder => builder.StartAt(Now.AddDays(3)), next: Now.AddDays(3));

        Check(Evaluate(Facts(trigger)), "Next fire").Verdict.Should().Be(DiagnosisVerdict.Info);
    }

    /// <summary>
    /// Nodes publish nothing about what they serve, so the check speaks for the node that answered and never
    /// says that no node serves the group.
    /// </summary>
    [Test]
    public void AGroupThisNodeDoesNotServeIsSaidOfThisNodeOnly()
    {
        ITrigger trigger = Trigger(builder => builder.WithExecutionGroup("gpu"));
        TriggerDiagnosisFacts facts = Facts(trigger) with
        {
            Scheduler = Answered(Scheduler(SchedulerStatus.Running) with { ServedExecutionGroups = ["reports"] }),
        };

        DiagnosisCheck check = Check(Evaluate(facts), "Execution group");

        check.Verdict.Should().Be(DiagnosisVerdict.Warning);
        check.Text.Should().Be("'gpu': this node does not serve it, so the trigger fires on a node that does.");
        check.Text.Should().NotContain("no node");
    }

    [Test]
    public void AWindowSaysNothingAboutRouting()
    {
        ITrigger trigger = Trigger(builder => builder.WithExecutionGroup("gpu"));
        TriggerDiagnosisFacts facts = Facts(trigger, window: true) with
        {
            Scheduler = Answered(Scheduler(SchedulerStatus.Running) with { ServedExecutionGroups = ["reports"] }),
        };

        Check(Evaluate(facts), "Execution group").Text.Should().Be("'gpu': unlimited.", "a window runs nothing, so it serves nothing to speak of");
    }

    [Test]
    public void AGroupAtItsLimitHoldsTheTriggerBack()
    {
        ITrigger trigger = Trigger(builder => builder.WithExecutionGroup("gpu"));
        TriggerDiagnosisFacts facts = Facts(trigger) with
        {
            Limits = Answered(new ExecutionLimitsDto(new Dictionary<string, DashboardExecutionLimit> { ["gpu"] = new(2, ExecutionLimitScope.Cluster) })),
            GroupFirings = Answered(Page(
                Firing("f1", NodeA, Now) with { ExecutionGroup = "gpu" },
                Firing("f2", "node-b", Now) with { ExecutionGroup = "gpu" },
                Firing("f3", NodeA, Now) with { ExecutionGroup = "cpu" })),
        };

        DiagnosisCheck check = Check(Evaluate(facts), "Execution group");

        check.Verdict.Should().Be(DiagnosisVerdict.Warning);
        check.Text.Should().Be("'gpu': at its limit, 2 of 2 across the cluster in flight, so the trigger waits for a slot.");
    }

    [Test]
    public void ANodeScopedLimitCountsThisNodesFiringsOnly()
    {
        ITrigger trigger = Trigger(builder => builder.WithExecutionGroup("gpu"));
        TriggerDiagnosisFacts facts = Facts(trigger) with
        {
            Limits = Answered(new ExecutionLimitsDto(new Dictionary<string, DashboardExecutionLimit> { ["*"] = new(2, ExecutionLimitScope.Node) })),
            GroupFirings = Answered(Page(
                Firing("f1", NodeA, Now) with { ExecutionGroup = "gpu" },
                Firing("f2", "node-b", Now) with { ExecutionGroup = "gpu" })),
        };

        Check(Evaluate(facts), "Execution group").Text.Should().Be("'gpu': 1 of 2 per node in flight on this node.",
            "the catch-all governs the group, and a limit per node is counted on the node that answered");
    }

    [Test]
    public void AForbiddenGroupNeverRuns()
    {
        ITrigger trigger = Trigger(builder => builder.WithExecutionGroup("gpu"));
        TriggerDiagnosisFacts facts = Facts(trigger) with
        {
            Limits = Answered(new ExecutionLimitsDto(new Dictionary<string, DashboardExecutionLimit> { ["gpu"] = new(0, ExecutionLimitScope.Node) })),
        };

        Check(Evaluate(facts), "Execution group").Verdict.Should().Be(DiagnosisVerdict.Problem);
    }

    [Test]
    public void RecordedMisfiresAreCountedWithTheLatest()
    {
        DashboardMisfireEntry latest = new(SchedulerName, NodeA, "nightly", "at-three", new JobKeyDto("batch", "import"), Now.AddMinutes(-30), Now.AddMinutes(-31))
        {
            Reason = MisfireReason.Missed,
        };
        TriggerDiagnosisFacts facts = Facts(Trigger()) with
        {
            Misfires = Answered(new PagedResult<DashboardMisfireEntry>([latest], HasMore: true, TotalCount: 4)),
        };

        DiagnosisCheck check = Check(Evaluate(facts), "Misfires");

        check.Verdict.Should().Be(DiagnosisVerdict.Warning);
        check.Text.Should().Be("4 recorded; the latest at " + Format(Now.AddMinutes(-30)) + ", Missed.");
        check.LinkPath.Should().Be("history?triggerGroup=nightly&triggerName=at-three#misfires");
    }

    [Test]
    public void ASourceWithoutHistorySaysSoForMisfires()
    {
        TriggerDiagnosisFacts facts = Facts(Trigger()) with { Misfires = new Answer<PagedResult<DashboardMisfireEntry>>(null, "no history", Unsupported: true) };

        DiagnosisCheck check = Check(Evaluate(facts), "Misfires");

        check.Verdict.Should().Be(DiagnosisVerdict.Unknown);
        check.Text.Should().Be("This source keeps no misfire history.");
    }

    [Test]
    public void APendingRetryIsWorthKnowing()
    {
        ITrigger trigger = Trigger();
        ((IMutableTrigger) trigger).RetryAttempt = 2;

        DiagnosisCheck check = Check(Evaluate(Facts(trigger)), "Retry");

        check.Verdict.Should().Be(DiagnosisVerdict.Info);
        check.Text.Should().Be("Retry 2 of a failed occurrence is pending, due at " + Format(Now.AddHours(1)) + ".");
    }

    [Test]
    public void AReadThatFailedLeavesItsCheckUnknownAndTheOthersStanding()
    {
        TriggerDiagnosisFacts facts = Facts(Trigger()) with { PausedJobGroups = new Answer<HashSet<string>>(null, "timed out", Unsupported: false) };

        List<DiagnosisCheck> checks = Evaluate(facts);

        Check(checks, "Job group").Verdict.Should().Be(DiagnosisVerdict.Unknown);
        Check(checks, "Job group").Text.Should().Be("Could not be read: timed out");
        checks.Where(check => check.Name != "Job group").Should().OnlyContain(check => check.Verdict == DiagnosisVerdict.Ok);
    }

    /// <summary>
    /// The diagnosis is a fixed handful of reads, never one per row: six for a trigger with nothing wrong,
    /// the job filter among them.
    /// </summary>
    [Test]
    public async Task TheReadsAreAFixedHandfulAndUseTheJobFilter()
    {
        IQuartzApiClient api = FakeApi();

        TriggerDiagnosisFacts facts = await TriggerDiagnosis.Read(api, Subject(Trigger(), TriggerState.Normal));

        Fake.GetCalls(api).Should().HaveCount(6);
        A.CallTo(() => api.QueryFireInstances(SchedulerName, A<DashboardFireInstanceQuery>.That.Matches(query =>
                query.Job == new JobKeyDto("batch", "import") && query.State == null), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => api.GetJobGroupPause(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._)).MustNotHaveHappened();
        Evaluate(facts).Should().OnlyContain(check => check.Verdict == DiagnosisVerdict.Ok);
    }

    [Test]
    public async Task ATroubledTriggerReadsAtMostNine()
    {
        IQuartzApiClient api = FakeApi();
        A.CallTo(() => api.QueryJobGroups(SchedulerName, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobGroupDto>([new JobGroupDto("batch", Paused: true)], HasMore: false, TotalCount: 1));
        A.CallTo(() => api.GetExecutionLimits(SchedulerName, A<CancellationToken>._))
            .Returns(new ExecutionLimitsDto(new Dictionary<string, DashboardExecutionLimit> { ["*"] = new(4, ExecutionLimitScope.Cluster) }));

        await TriggerDiagnosis.Read(api, Subject(Trigger(builder => builder.WithExecutionGroup("gpu")), TriggerState.Error));

        Fake.GetCalls(api).Should().HaveCount(9, "the job group's pause, the group's firings and the last run are read only when they matter");
    }

    [Test]
    public async Task ASourceThatCannotAnswerLeavesThatCheckUnknown()
    {
        IQuartzApiClient api = FakeApi();
        A.CallTo(() => api.QueryMisfires(A<DashboardMisfireQuery>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("The target serves no history."));

        TriggerDiagnosisFacts facts = await TriggerDiagnosis.Read(api, Subject(Trigger(), TriggerState.Normal));

        Check(Evaluate(facts), "Misfires").Text.Should().Be("This source keeps no misfire history.");
    }

    [Test]
    public async Task LeavingThePageStopsTheReads()
    {
        IQuartzApiClient api = FakeApi();
        using CancellationTokenSource cancellation = new();
        A.CallTo(() => api.GetScheduler(SchedulerName, A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<SchedulerDetailDto>(cancellation.Token);
            });

        Func<Task> read = () => TriggerDiagnosis.Read(api, Subject(Trigger(), TriggerState.Normal), cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>("a read nobody waits for is not a check to show");
    }

    private static IQuartzApiClient FakeApi()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        A.CallTo(() => api.GetScheduler(SchedulerName, A<CancellationToken>._)).Returns(Scheduler(SchedulerStatus.Running));
        A.CallTo(() => api.QueryTriggerGroups(SchedulerName, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroupDto>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => api.QueryJobGroups(SchedulerName, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobGroupDto>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => api.GetJobGroupPause(SchedulerName, A<string>._, A<CancellationToken>._)).Returns(new ValueTask<PauseInfo?>((PauseInfo?) null));
        A.CallTo(() => api.QueryFireInstances(SchedulerName, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => api.GetExecutionLimits(SchedulerName, A<CancellationToken>._)).Returns(new ExecutionLimitsDto([]));
        A.CallTo(() => api.QueryMisfires(A<DashboardMisfireQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardMisfireEntry>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardHistoryEntry>([], HasMore: false, TotalCount: 0));
        return api;
    }

    private static List<DiagnosisCheck> Evaluate(TriggerDiagnosisFacts facts) => TriggerDiagnosis.Evaluate(facts, Format);

    private static string Format(DateTimeOffset? value) => value?.ToString("u", CultureInfo.InvariantCulture) ?? "-";

    private static DiagnosisCheck Check(List<DiagnosisCheck> checks, string name) => checks.Single(check => check.Name == name);

    private static ITrigger Trigger(Action<TriggerBuilder<IJob>>? configure = null, DateTimeOffset? next = null, bool noNextFire = false)
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity("at-three", "nightly")
            .ForJob("import", "batch")
            .StartAt(Now.AddDays(-1))
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever());
        configure?.Invoke(builder);

        IMutableTrigger trigger = (IMutableTrigger) builder.Build();
        trigger.NextFireTimeUtc = noNextFire ? null : next ?? Now.AddHours(1);
        return trigger;
    }

    private static DiagnosisSubject Subject(ITrigger trigger, TriggerState state, PauseInfo? pause = null, bool continuationParentExists = true, bool window = false)
    {
        return new DiagnosisSubject(SchedulerName, trigger, state, pause, continuationParentExists, Now) { IsWindow = window };
    }

    private static TriggerDiagnosisFacts Facts(
        ITrigger trigger,
        TriggerState state = TriggerState.Normal,
        PauseInfo? pause = null,
        bool continuationParentExists = true,
        bool window = false)
    {
        return new TriggerDiagnosisFacts(Subject(trigger, state, pause, continuationParentExists, window))
        {
            Scheduler = Answered(Scheduler(SchedulerStatus.Running)),
            PausedTriggerGroups = Answered(Set()),
            PausedJobGroups = Answered(Set()),
            JobFirings = Answered(Page()),
            Limits = Answered(new ExecutionLimitsDto([])),
            Misfires = Answered(new PagedResult<DashboardMisfireEntry>([], HasMore: false, TotalCount: 0)),
        };
    }

    private static SchedulerDetailDto Scheduler(SchedulerStatus status)
    {
        return new SchedulerDetailDto(NodeA, SchedulerName, status, Clustered: true, Persistent: true, "AdoJobStore", "DefaultThreadPool", 10, Now.AddDays(-3), 120, "4.9.0");
    }

    private static Answer<T> Answered<T>(T value) => new(value, Error: null, Unsupported: false);

    private static HashSet<string> Set(params string[] names) => new(names, StringComparer.Ordinal);

    private static PagedResult<FireInstanceDto> Page(params FireInstanceDto[] firings) => new([.. firings], HasMore: false, TotalCount: firings.Length);

    private static FireInstanceDto Firing(string id, string node, DateTimeOffset firedAt)
    {
        return new FireInstanceDto(id, new TriggerKeyDto("nightly", "at-three"), new JobKeyDto("batch", "import"), node, FireInstanceState.Executing, firedAt, firedAt, ExecutionGroup: null);
    }
}
