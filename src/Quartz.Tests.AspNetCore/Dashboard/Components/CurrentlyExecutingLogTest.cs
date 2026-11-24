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

using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Currently Executing's <em>Show log</em> (#4437): a dialog that follows one running firing's log every two seconds,
/// read where the firing runs, and says plainly why it stops when it does.
/// </summary>
public sealed class CurrentlyExecutingLogTest
{
    private const string Firing = "fire-1";

    private DashboardComponentContext context = null!;
    private readonly List<DashboardFireInstanceLogQuery> reads = [];

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        GivenRows(Row(Firing, "node-a"));
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
        reads.Clear();
    }

    /// <summary>B1: every row offers Show log, to every visitor who sees the scheduler; Interrupt follows its role.</summary>
    [Test]
    public void EveryRowHasShowLogAndInterruptFollowsItsRole()
    {
        GivenRows(Row(Firing, "node-a"), Row("fire-2", "node-b"));

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-testid=executing-show-log]").Should().HaveCount(2);
            page.FindAll("button").Count(button => button.TextContent.Trim() == "Interrupt").Should().Be(2);
            page.FindAll("th").Select(header => header.TextContent.Trim()).Should().Contain("Actions");
        });
    }

    [Test]
    public void AViewerAndAReadOnlyDashboardGetShowLogButNoInterrupt()
    {
        using DashboardComponentContext readOnly = new(options => options.ReadOnly = true);
        readOnly.WithScheduler();
        A.CallTo(() => readOnly.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<FireInstanceDto>([Row(Firing, "node-a")]));

        context.WithOperatorRole(SchedulerAccess.View);

        foreach (DashboardComponentContext visitor in new[] { readOnly, context })
        {
            IRenderedComponent<CurrentlyExecuting> page = visitor.Render<CurrentlyExecuting>();

            page.WaitForAssertion(() =>
            {
                page.FindAll("[data-testid=executing-show-log]").Should().ContainSingle("Show log is a read, as Execution Detail is");
                page.FindAll("button").Should().NotContain(button => button.TextContent.Trim() == "Interrupt",
                    "interrupting changes what the scheduler is doing, and this visitor may not");
                page.FindAll("th").Select(header => header.TextContent.Trim()).Should().Contain("Actions",
                    "the column is always there, holding Show log");
            });
        }
    }

    /// <summary>
    /// B2: the dialog is named for the job and the trigger, takes focus, and gives it back to Show log, also when the
    /// page has read again since the row was drawn, as it does every second.
    /// </summary>
    [Test]
    public void TheDialogIsNamedForTheFiringAndEscapeClosesItGivingFocusBack()
    {
        GivenLog(Running(["line 1"], next: 1));
        IRenderedComponent<CurrentlyExecuting> page = Open(out string opener, refreshesFirst: 2);

        IElement dialog = page.Find("[data-testid=log-dialog]");
        dialog.GetAttribute("role").Should().Be("dialog");
        page.Find("#qz-log-dialog-title").TextContent.Should().Be("Log of reports.job-1 · nightly.trigger-1");
        page.Find("[data-testid=log-meta]").TextContent.Should().Contain("node-a").And.Contain("run time");

        JSRuntimeInvocation open = context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "quartzDashboardDialog.open");
        open.Arguments.Should().HaveCount(3, "the dialog is handed the control focus goes back to, and the heading for when its row has gone");
        ((ElementReference) open.Arguments[1]!).Id.Should().Be(opener, "focus goes back to the row's own Show log");

        page.Find("[data-testid=modal-overlay]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        page.WaitForAssertion(() => page.FindAll("[data-testid=log-dialog]").Should().BeEmpty("Escape closes it"));
        context.JSInterop.Invocations.Should().Contain(invocation => invocation.Identifier == "quartzDashboardDialog.close");
    }

    /// <summary>
    /// A firing listed above the opener's row after the dialog's row was drawn: focus goes back to that firing's own
    /// Show log, because each row keeps its elements with its firing rather than with its place in the listing.
    /// </summary>
    /// <remarks>
    /// Rows kept by position would hand the new firing the button drawn for the one below it, whose reference was
    /// captured for that other firing, and the new firing would have none of its own.
    /// </remarks>
    [Test]
    public void AFiringListedAboveAnotherGivesFocusBackToItsOwnShowLog()
    {
        GivenLog(Running(["line 1"], next: 1));
        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();
        page.WaitForElement("[data-testid=executing-show-log]");

        GivenRows(Row("fire-0", "node-a", trigger: "arrived"), Row(Firing, "node-a"));
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        IElement arrived = null!;
        page.WaitForAssertion(() =>
        {
            IReadOnlyList<IElement> rows = page.FindAll("tbody tr");
            rows.Should().HaveCount(2);
            rows[0].TextContent.Should().Contain("nightly.arrived");
            arrived = rows[0].QuerySelector("[data-testid=executing-show-log]")!;
        });
        string created = arrived.GetAttribute("blazor:elementreference")!;
        created.Should().NotBeNullOrEmpty("the arriving firing's row is drawn new, with a button of its own");

        arrived.Click();

        page.WaitForAssertion(() =>
        {
            JSRuntimeInvocation open = context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "quartzDashboardDialog.open");
            open.Arguments.Should().HaveCount(3, "the arriving firing's Show log is what focus goes back to");
            ((ElementReference) open.Arguments[1]!).Id.Should().Be(created);
        });
    }

    /// <summary>
    /// The opener's row gone by the time the dialog closes: the dialog was handed the page's heading to give focus to
    /// instead, so it never falls to the body.
    /// </summary>
    [Test]
    public void WhenTheOpenersRowHasGoneFocusFallsBackToTheHeading()
    {
        GivenLog(Running(["line 1"], next: 1));
        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();
        string heading = page.Find("h1").GetAttribute("blazor:elementreference")!;
        page.Find("h1").GetAttribute("tabindex").Should().Be("-1", "focusable from code, and not in the Tab order");
        page.WaitForElement("[data-testid=executing-show-log]").Click();
        page.WaitForElement("[data-testid=log-dialog]");

        GivenRows();
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        page.WaitForAssertion(() => page.Markup.Should().Contain("No jobs currently executing."));
        page.Find("[data-testid=modal-overlay]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        page.WaitForAssertion(() => page.FindAll("[data-testid=log-dialog]").Should().BeEmpty());
        JSRuntimeInvocation open = context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "quartzDashboardDialog.open");
        ((ElementReference) open.Arguments[2]!).Id.Should().Be(heading,
            "the dialog's close gives focus to the heading when the opener is no longer on the page");
        context.JSInterop.Invocations.Should().Contain(invocation => invocation.Identifier == "quartzDashboardDialog.close");
    }

    /// <summary>B3: the first read asks from 0 on the row's node; two seconds later the next asks after what it has.</summary>
    [Test]
    public void ItReadsFromTheStartThenAfterWhatItHasEveryTwoSeconds()
    {
        GivenLogs(Running(["line 1", "line 2"], next: 2), Running(["line 3"], next: 3));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => reads.Should().ContainSingle());
        reads[0].FireInstanceId.Should().Be(Firing);
        reads[0].After.Should().Be(0);
        reads[0].SchedulerInstanceId.Should().Be("node-a", "a cluster asks the node the row names, alone");
        page.WaitForAssertion(() => Lines(page).Should().Equal(["line 1", "line 2"]));

        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() =>
        {
            reads.Should().HaveCount(2);
            reads[1].After.Should().Be(2, "the reader has two entries");
            Lines(page).Should().Equal(["line 1", "line 2", "line 3"], "new lines are added below the ones read");
            Status(page).Should().Be("Following · 3 lines · updated 14:23:02");
        });
    }

    [Test]
    public void ARunningFiringThatHasLoggedNothingSaysSoAndDrawsNoEmptyBox()
    {
        GivenLog(Running([], next: 0));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() =>
        {
            Status(page).Should().Be("Nothing logged yet · updated 14:23:00");
            page.FindAll("[data-testid=log-scroller]").Should().BeEmpty("there is no line to put in a box yet");
        });
    }

    [Test]
    public void TheLogBoxAppearsWithTheFirstLine()
    {
        GivenLogs(Running([], next: 0), Running(["the first line"], next: 1));
        IRenderedComponent<CurrentlyExecuting> page = Open();
        page.WaitForAssertion(() => page.FindAll("[data-testid=log-scroller]").Should().BeEmpty());

        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() =>
        {
            Lines(page).Should().Equal(["the first line"]);
            page.Find("[data-testid=log-lines]").GetAttribute("tabindex").Should().Be("0", "the arrow keys scroll it");
        });
    }

    /// <summary>
    /// The live region holds the state alone: the count of lines and the time of the read change every two seconds,
    /// and a screen reader would read each change out.
    /// </summary>
    [Test]
    public void OnlyTheStateIsInTheLiveRegion()
    {
        GivenLogs(Running(["line 1"], next: 1), Running(["line 2"], next: 2));
        IRenderedComponent<CurrentlyExecuting> page = Open();
        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() =>
        {
            Status(page).Should().Be("Following · 2 lines · updated 14:23:02");
            page.Find("[data-testid=log-status] [role=status]").TextContent.Trim().Should().Be("Following");
            page.FindAll("[role=status]").Should().ContainSingle("one live region, the state's");
        });
    }

    /// <summary>
    /// 4: a reader who scrolls up is not followed, and is told what arrived below; Jump to latest scrolls down and
    /// follows again.
    /// </summary>
    [Test]
    public async Task AReaderWhoScrolledUpIsToldWhatArrivedBelowAndCanJumpToIt()
    {
        GivenLogs(Running(["line 1", "line 2"], next: 2), Running(["line 3"], next: 3));
        IRenderedComponent<CurrentlyExecuting> page = Open();
        page.WaitForAssertion(() => Lines(page).Should().HaveCount(2));
        string logRegion = page.Find("[data-testid=log-lines]").GetAttribute("blazor:elementreference")!;
        context.JSInterop.Invocations.Should().Contain(invocation => invocation.Identifier == "quartzDashboardLogFollow.observe",
            "the browser says when the reader scrolls off the newest line");
        page.FindAll("[data-testid=log-jump]").Should().BeEmpty("a reader on the newest line has nothing to jump to");

        IRenderedComponent<FireInstanceLogDialog> dialog = page.FindComponent<FireInstanceLogDialog>();
        await dialog.InvokeAsync(() => dialog.Instance.OnFollowingChanged(following: false));

        page.WaitForAssertion(() => Status(page).Should().Be("Paused · 0 new lines below"));
        context.Clock.Advance(TimeSpan.FromSeconds(2));
        page.WaitForAssertion(() =>
        {
            Status(page).Should().Be("Paused · 1 new line below");
            page.Find("[data-testid=log-status] [role=status]").TextContent.Trim().Should().Be("Paused");
            Lines(page).Should().Equal(["line 1", "line 2", "line 3"], "the lines still arrive while the reader is up");
        });

        page.Find("[data-testid=log-jump]").Click();

        page.WaitForAssertion(() =>
        {
            context.JSInterop.Invocations.Should().Contain(invocation => invocation.Identifier == "quartzDashboardLogFollow.toLatest");
            Status(page).Should().Be("Following · 3 lines · updated 14:23:02");
            page.FindAll("[data-testid=log-jump]").Should().BeEmpty();
        });

        // The button goes as the dialog follows again; focus goes to the log, never to the page behind the dialog.
        JSRuntimeInvocation focus = context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus");
        ((ElementReference) focus.Arguments[0]!).Id.Should().Be(logRegion, "the log region, which scrolls with the arrow keys");
    }

    /// <summary>
    /// A run that ends while the reader is scrolled up keeps Jump to latest: removing it with the reader's focus on it
    /// would drop focus to the page behind the dialog, where Escape no longer closes it.
    /// </summary>
    [Test]
    public async Task JumpToLatestStaysWhenTheRunEndsWhileTheReaderIsUp()
    {
        GivenLogs(Running(["line 1", "line 2"], next: 2), NotRunning("node-a", next: 2));
        GivenExecution(TestData.Dashboard.HistoryEntry(TimeSpan.FromSeconds(20)) with { EntryId = "entry-9", Log = "line 1\nline 2\nline 3" });
        IRenderedComponent<CurrentlyExecuting> page = Open();
        page.WaitForAssertion(() => Lines(page).Should().HaveCount(2));
        string logRegion = page.Find("[data-testid=log-lines]").GetAttribute("blazor:elementreference")!;
        IRenderedComponent<FireInstanceLogDialog> dialog = page.FindComponent<FireInstanceLogDialog>();
        await dialog.InvokeAsync(() => dialog.Instance.OnFollowingChanged(following: false));

        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() =>
        {
            Status(page).Should().StartWith("The run ended. This is its log as recorded.");
            page.FindAll("[data-testid=log-jump]").Should().ContainSingle("the reader is still up, and may have focus on the button");
        });

        page.Find("[data-testid=log-jump]").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=log-jump]").Should().BeEmpty());
        JSRuntimeInvocation focus = context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus");
        ((ElementReference) focus.Arguments[0]!).Id.Should().Be(logRegion, "focus goes to the log, which now holds the run's log as recorded");
    }

    /// <summary>
    /// 1: keys with no break in them, as TriggerJob names its triggers, never make the table wider than its card. The
    /// layout is fixed, every column but Job and Trigger is sized, a key wraps inside a run as the last resort, and the
    /// actions never shrink. bUnit has no layout, so the rules are held here and measured in a browser.
    /// </summary>
    [Test]
    public void LongKeysCannotWidenTheTable()
    {
        GivenRows(Enumerable.Range(0, 7).Select(i => new FireInstanceDto(
            "fire-" + i,
            new TriggerKeyDto("DEFAULT", "MT_641348493038929258" + i),
            new JobKeyDto("DEFAULT", "MT_job_641348493038929258" + i),
            "node-a",
            FireInstanceState.Executing,
            TestData.Dashboard.FiredAt,
            TestData.Dashboard.FiredAt,
            ExecutionGroup: null)).ToArray());
        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(7));
        IElement table = page.Find("[data-testid=executing-table]");
        table.ClassList.Should().Contain(["qz-executing-table", "qz-executing-table-interrupt"]);
        table.QuerySelectorAll("thead th").Select(th => th.ClassName).Should().Equal(
            "qz-col-key", "qz-col-key", "qz-col-node", "qz-col-group", "qz-col-fired", "qz-col-runtime", "qz-col-progress", "qz-col-history", "qz-col-actions");
        page.FindAll("tbody td.qz-col-key .qz-key-badge-value").Should().HaveCount(14);

        string css = File.ReadAllText(HistoryPageTest.DashboardStylesheet());
        css.Should().MatchRegex(@"\.qz-executing-table \{[^}]*table-layout: fixed;", "a fixed layout: content cannot push a column wider");
        foreach (string column in (string[]) ["node", "group", "fired", "runtime", "progress", "history", "actions"])
        {
            css.Should().MatchRegex(@"\.qz-executing-table \.qz-col-" + column + @" \{[^}]*width: [0-9.]+rem;",
                $"every column but Job and Trigger has its own width, so those two absorb the rest ({column})");
        }

        css.Should().MatchRegex(@"\.qz-executing-table td\.qz-col-key \.qz-key-badge-value \{[^}]*overflow-wrap: anywhere;",
            "a run with no break wraps inside itself as the last resort");
        css.Should().MatchRegex(@"\.qz-executing-table \.qz-col-actions \{[^}]*white-space: nowrap;", "the actions never shrink");
        css.Should().MatchRegex(@"\.qz-executing-table\.qz-executing-table-interrupt \.qz-col-actions \{[^}]*width: [0-9.]+rem;",
            "two buttons side by side have the width of two");
    }

    /// <summary>B4: an answer that says the reader is behind is followed at once, at most four more times a tick.</summary>
    [Test]
    public void AReaderBehindReadsAgainAtOnceAtMostFourMoreTimesATick()
    {
        long next = 0;
        A.CallTo(() => context.Api.GetFireInstanceLog(A<string>._, A<DashboardFireInstanceLogQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardFireInstanceLogQuery query, CancellationToken _) =>
            {
                reads.Add(query);
                next++;
                return new ValueTask<FireInstanceLogDto>(Running(["line " + next], next) with { HasMore = true });
            });

        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => reads.Should().HaveCount(5, "the tick's read and four more"));
        reads.Select(read => read.After).Should().Equal([0, 1, 2, 3, 4]);

        context.Clock.Advance(TimeSpan.FromSeconds(2));
        page.WaitForAssertion(() => reads.Should().HaveCount(10));
    }

    /// <summary>B5: a scheduler that does not capture is said once, naming the call, and nothing more is read.</summary>
    [Test]
    public async Task ASchedulerThatDoesNotCaptureIsSaidAndPollingStops()
    {
        GivenLog(new FireInstanceLogDto(FireInstanceLogStatus.NotCaptured, "node-a", [], 0));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().Be(
            "This scheduler keeps no log lines of its firings. Call UseExecutionLogCapture() on it to follow a firing here "
            + "and to keep its log on its execution page."));
        page.Find("[data-testid=log-status] code").TextContent.Should().Be("UseExecutionLogCapture()");

        context.Clock.Advance(TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => { });

        reads.Should().ContainSingle("the answer will not change while the firing runs");
    }

    /// <summary>B6: a window or a node no member fronts is said, and polling stops.</summary>
    [TestCase(null, "No process this dashboard reaches runs this firing: the scheduler is read through its store.")]
    [TestCase("node-c", "node-c runs this firing, and no member target of this cluster fronts it")]
    public async Task AFiringNoProcessHereRunsIsSaidAndPollingStops(string? node, string sentence)
    {
        GivenLog(new FireInstanceLogDto(FireInstanceLogStatus.Unreachable, node, [], 0));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().StartWith(sentence));

        context.Clock.Advance(TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => { });
        reads.Should().ContainSingle();
    }

    [Test]
    public async Task ATargetThatReachesAnotherNodeSaysWhichAndStops()
    {
        GivenRows(Row(Firing, "node-b"));
        GivenLog(new FireInstanceLogDto(FireInstanceLogStatus.NotRunning, "node-a", [], 0));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().Be(
            "node-b runs this firing, and this target reaches node-a. Open node-b's target to follow its log."));

        context.Clock.Advance(TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => { });
        reads.Should().ContainSingle("asking the same target again reaches the same node");
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// B7: the firing's own node no longer runs it, and the history row it wrote carries the log: the dialog shows the
    /// log as recorded, the lines logged after its last read among them, and the run time the row recorded.
    /// </summary>
    [Test]
    public void AFiringThatEndedShowsItsLogAsRecorded()
    {
        GivenLogs(Running(["Step 19 of 20"], next: 1), NotRunning("node-a", next: 1));
        GivenExecution(TestData.Dashboard.HistoryEntry(TimeSpan.FromMilliseconds(20_170)) with
        {
            EntryId = "entry-9",
            Log = "Step 1 of 20\nStep 19 of 20\nStep 20 of 20",
        });
        IRenderedComponent<CurrentlyExecuting> page = Open();

        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() =>
        {
            Status(page).Should().Be("The run ended. This is its log as recorded. Open its execution page");
            page.Find("[data-testid=log-execution-link]").GetAttribute("href").Should().Be("quartz/history/entry-9");
            Lines(page).Should().Equal(["Step 1 of 20", "Step 19 of 20", "Step 20 of 20"],
                "the line logged after the last read went with the firing's buffer, and the row has it");
            page.Find("[data-testid=log-meta]").TextContent.Should().Contain("run time 00:00:20",
                "the run time the row recorded, in the clock the running header and the table use, so the header does not jump");
        });
    }

    /// <summary>
    /// B7, a history whose listing leaves the log out, as the database's does: the read lines stay, and the notice says
    /// the full log, with what came after the last read, is on the execution page.
    /// </summary>
    [Test]
    public void AFiringThatEndedWithNoLogInItsRowLinksToItsExecutionPage()
    {
        GivenLogs(Running(["the last line read"], next: 1), NotRunning("node-a", next: 1));
        GivenExecution(TestData.Dashboard.HistoryEntry(TimeSpan.FromSeconds(20)) with { EntryId = "entry-9" });
        IRenderedComponent<CurrentlyExecuting> page = Open();

        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() =>
        {
            Status(page).Should().Be("The run ended. Its full log, with the lines logged after the last read, is on its execution page.");
            page.Find("[data-testid=log-execution-link]").GetAttribute("href").Should().Be("quartz/history/entry-9");
            Lines(page).Should().Equal(["the last line read"], "the lines read so far stay in the dialog");
        });
    }

    private void GivenExecution(DashboardHistoryEntry entry)
    {
        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.FireInstanceId == Firing && query.Take == 1), A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardHistoryEntry>([entry]));
    }

    [Test]
    public async Task AFiringThatEndedWithNoRowYetPointsToThePastRunsAfterFiveReads()
    {
        GivenLog(NotRunning(node: null, next: 0));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().Be("The run ended. Looking for its execution page…"));
        for (int tick = 0; tick < 4; tick++)
        {
            context.Clock.Advance(TimeSpan.FromSeconds(2));
            await page.InvokeAsync(() => { });
        }

        page.WaitForAssertion(() =>
        {
            Status(page).Should().StartWith("The run ended, and its execution is not in the history yet.");
            page.Find("[data-testid=log-past-runs-link]").GetAttribute("href").Should().Be("quartz/history?triggerGroup=nightly&triggerName=trigger-1");
        });
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .MustHaveHappened(5, Times.Exactly);

        context.Clock.Advance(TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => { });
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .MustHaveHappened(5, Times.Exactly);
        reads.Should().ContainSingle();
    }

    [Test]
    public void AFiringThatEndedOnASchedulerWithoutHistorySaysTheLogIsNotKept()
    {
        GivenLog(NotRunning("node-a", next: 0));
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("no history"));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().Be("This scheduler keeps no history, so the log is not kept after the run."));
    }

    /// <summary>B8: entries the capture dropped between two reads are said where they would have been.</summary>
    [Test]
    public void LinesTheCaptureDroppedAreSaidBeforeTheNewOnes()
    {
        GivenLogs(Running(["line 1"], next: 1), Running(["line 9"], next: 9) with { Missed = 7 });
        IRenderedComponent<CurrentlyExecuting> page = Open();

        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() => page.Find("[data-testid=log-lines]").TextContent.Split('\n').Should().Equal(
            "line 1",
            "[7 lines were dropped before they could be shown: the capture keeps only its newest lines]",
            "line 9"));
    }

    /// <summary>B9: closing the dialog stops its timer.</summary>
    [Test]
    public async Task ClosingTheDialogStopsReading()
    {
        GivenLog(Running(["line 1"], next: 1));
        IRenderedComponent<CurrentlyExecuting> page = Open();
        page.WaitForAssertion(() => reads.Should().ContainSingle());

        page.Find("[data-testid=log-close]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=log-dialog]").Should().BeEmpty());

        context.Clock.Advance(TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => { });
        reads.Should().ContainSingle("a closed dialog reads nothing");
    }

    [Test]
    public async Task ThePageGoingCancelsTheReadInFlightAndStopsTheTimer()
    {
        CancellationToken inFlight = default;
        TaskCompletionSource<FireInstanceLogDto> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => context.Api.GetFireInstanceLog(A<string>._, A<DashboardFireInstanceLogQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardFireInstanceLogQuery query, CancellationToken cancellationToken) =>
            {
                reads.Add(query);
                inFlight = cancellationToken;
                return new ValueTask<FireInstanceLogDto>(pending.Task);
            });
        IRenderedComponent<CurrentlyExecuting> page = Open();
        page.WaitForAssertion(() => reads.Should().ContainSingle());

        await context.DisposeComponentsAsync();

        inFlight.IsCancellationRequested.Should().BeTrue("a circuit that ends disposes the page, and the dialog with it");
        context.Clock.Advance(TimeSpan.FromSeconds(10));
        reads.Should().ContainSingle();
        pending.TrySetCanceled();
    }

    /// <summary>B10: a host older than 4.9 is said in the data source's words, and nothing is retried.</summary>
    [Test]
    public async Task AHostOlderThan49IsSaidAndNotRetried()
    {
        A.CallTo(() => context.Api.GetFireInstanceLog(A<string>._, A<DashboardFireInstanceLogQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardFireInstanceLogQuery query, CancellationToken _) =>
            {
                reads.Add(query);
                return ValueTask.FromException<FireInstanceLogDto>(FireInstanceLogs.HostTooOld());
            });
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().Be(
            "This target runs Quartz older than 4.9, which cannot show a running firing's log. Its log is on its execution page once it ends."));

        context.Clock.Advance(TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => { });
        reads.Should().ContainSingle();
    }

    [Test]
    public void ADataSourceThatCannotReadARunningLogSaysSoInItsWords()
    {
        A.CallTo(() => context.Api.GetFireInstanceLog(A<string>._, A<DashboardFireInstanceLogQuery>._, A<CancellationToken>._))
            .CallsBaseMethod();
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().StartWith("This dashboard's data source cannot read a running firing's log"));
    }

    [Test]
    public async Task ThreeFailedReadsInARowStopWithRetryWhichReadsAgain()
    {
        bool failing = true;
        A.CallTo(() => context.Api.GetFireInstanceLog(A<string>._, A<DashboardFireInstanceLogQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardFireInstanceLogQuery query, CancellationToken _) =>
            {
                reads.Add(query);
                return failing
                    ? ValueTask.FromException<FireInstanceLogDto>(new SchedulerException("the target is not answering"))
                    : new ValueTask<FireInstanceLogDto>(Running(["back again"], next: 1));
            });
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() => Status(page).Should().Be("The last read failed, and the next one is in two seconds: the target is not answering"));
        page.FindAll("[data-testid=log-retry]").Should().BeEmpty("one failure is not a stop");

        context.Clock.Advance(TimeSpan.FromSeconds(2));
        await page.InvokeAsync(() => { });
        context.Clock.Advance(TimeSpan.FromSeconds(2));

        page.WaitForAssertion(() => Status(page).Should().Be("Stopped after 3 failed reads in a row: the target is not answering"));
        context.Clock.Advance(TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => { });
        reads.Should().HaveCount(3, "a stopped dialog reads nothing until Retry");

        failing = false;
        page.Find("[data-testid=log-retry]").Click();

        page.WaitForAssertion(() =>
        {
            reads.Should().HaveCount(4, "Retry reads at once");
            Lines(page).Should().Equal(["back again"]);
            page.FindAll("[data-testid=log-retry]").Should().BeEmpty();
        });
    }

    /// <summary>B11: the page's own refresh dropping the finished row leaves the dialog open, with its copy of the row.</summary>
    [Test]
    public void ThePagesRefreshDroppingTheRowLeavesTheDialogOpen()
    {
        GivenLog(Running(["line 1"], next: 1));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        GivenRows();
        context.Clock.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("No jobs currently executing.", "the page's own once-a-second refresh goes on");
            page.Find("#qz-log-dialog-title").TextContent.Should().Be("Log of reports.job-1 · nightly.trigger-1");
        });
    }

    /// <summary>B12: a line is text, whatever it holds.</summary>
    [Test]
    public void ALineThatLooksLikeMarkupIsShownAsText()
    {
        GivenLog(Running(["<script>alert('x')</script> <b>bold</b>"], next: 1));
        IRenderedComponent<CurrentlyExecuting> page = Open();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=log-lines]").TextContent.Should().Be("<script>alert('x')</script> <b>bold</b>");
            page.Find("[data-testid=log-lines]").Children.Should().BeEmpty("nothing in a line becomes an element");
        });
    }

    private IRenderedComponent<CurrentlyExecuting> Open() => Open(out _);

    /// <param name="opener">The element reference Blazor gave the row's Show log.</param>
    /// <param name="refreshesFirst">How many of the page's once-a-second reads happen before Show log is pressed.</param>
    private IRenderedComponent<CurrentlyExecuting> Open(out string opener, int refreshesFirst = 0)
    {
        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        // As the button was created: the renderer writes the reference only on the render that creates the element.
        opener = page.WaitForElement("[data-testid=executing-show-log]").GetAttribute("blazor:elementreference")!;
        for (int refresh = 1; refresh <= refreshesFirst; refresh++)
        {
            context.Clock.Advance(TimeSpan.FromSeconds(1));
            int reads = refresh + 1;
            page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
                .MustHaveHappened(reads, Times.OrMore));
        }

        IElement button = page.WaitForElement("[data-testid=executing-show-log]");
        button.Click();
        page.WaitForElement("[data-testid=log-dialog]");
        return page;
    }

    private void GivenRows(params FireInstanceDto[] rows)
    {
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<FireInstanceDto>(rows));
    }

    private void GivenLog(FireInstanceLogDto answer) => GivenLogs(answer);

    /// <summary>
    /// The answers, one per read in order, the last one to every read after it.
    /// </summary>
    private void GivenLogs(params FireInstanceLogDto[] answers)
    {
        A.CallTo(() => context.Api.GetFireInstanceLog(A<string>._, A<DashboardFireInstanceLogQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardFireInstanceLogQuery query, CancellationToken _) =>
            {
                reads.Add(query);
                return new ValueTask<FireInstanceLogDto>(answers[Math.Min(reads.Count, answers.Length) - 1]);
            });
    }

    private static FireInstanceDto Row(string fireInstanceId, string node, string trigger = "trigger-1")
    {
        return new FireInstanceDto(
            fireInstanceId,
            new TriggerKeyDto("nightly", trigger),
            new JobKeyDto("reports", "job-1"),
            node,
            FireInstanceState.Executing,
            TestData.Dashboard.FiredAt,
            TestData.Dashboard.FiredAt,
            ExecutionGroup: null);
    }

    private static FireInstanceLogDto Running(List<string> entries, long next)
    {
        return new FireInstanceLogDto(FireInstanceLogStatus.Running, "node-a", entries, next);
    }

    private static FireInstanceLogDto NotRunning(string? node, long next)
    {
        return new FireInstanceLogDto(FireInstanceLogStatus.NotRunning, node, [], next);
    }

    private static string[] Lines(IRenderedComponent<CurrentlyExecuting> page)
    {
        IElement? lines = page.FindAll("[data-testid=log-lines]").SingleOrDefault();
        return lines is null || lines.TextContent.Length == 0 ? [] : lines.TextContent.Split('\n');
    }

    private static string Status(IRenderedComponent<CurrentlyExecuting> page)
    {
        return string.Join(' ', page.Find("[data-testid=log-status]").TextContent.Split((char[]) [' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
    }
}
