using System.Security.Claims;

using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Trigger Detail page's editor: a patch over <c>UpdateTriggerDetails</c> that sends what changed,
/// shows what the scheduler refused beside the fields, and records who changed what.
/// </summary>
public sealed class TriggerDetailsEditorTest
{
    private const string TriggerGroup = "reports";
    private const string TriggerName = "nightly";

    private static readonly TriggerKeyDto TriggerKey = new(TriggerGroup, TriggerName);

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        context.AuthenticationState.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "operator@example.com")], authenticationType: "test"));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Normal);
        A.CallTo(() => context.Api.GetCalendarNames(TestData.SchedulerName, A<CancellationToken>._))
            .Returns(new List<string> { "holidays", "month-end" });
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .Returns(true);
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void TheEditorStartsFromWhatTheTriggerHolds()
    {
        GivenTrigger(CronTrigger());

        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-description").GetAttribute("value").Should().Be("before");
        page.Find("#trigger-edit-priority").GetAttribute("value").Should().Be("5");
        page.Find("#trigger-edit-calendar").GetAttribute("value").Should().Be("holidays");
        page.Find("#trigger-edit-retry-policy").GetAttribute("value").Should().Be("fixed;3;00:05:00");
        page.Find("#trigger-edit-overlap").GetAttribute("value").Should().Be("Default");
        page.FindAll("#trigger-edit-misfire option").Select(option => option.TextContent)
            .Should().BeEquivalentTo(["SmartPolicy", "IgnoreMisfires", "FireAndProceed", "DoNothing"],
                "a cron trigger's instructions are offered by the names its family gives them");
        page.FindAll("#trigger-edit-calendars option").Select(option => option.GetAttribute("value"))
            .Should().Equal(["holidays", "month-end"], "the scheduler's calendars are suggested");
        page.FindAll(".qz-job-data-key").Select(input => input.GetAttribute("value")).Should().Equal(["region"]);
    }

    [Test]
    public void SavingSendsWhatChangedAndNothingElse()
    {
        GivenTrigger(CronTrigger());
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-priority").Change("7");
        page.Find("#trigger-edit-misfire").Change(((int) CronTriggerMisfireInstruction.DoNothing).ToString(System.Globalization.CultureInfo.InvariantCulture));
        page.Find("#trigger-edit-overlap").Change("Skip");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.UpdateTriggerDetails(
                TestData.SchedulerName,
                TriggerKey,
                A<TriggerDetailsUpdate>.That.Matches(update =>
                    update.HasPriority && update.Priority == 7
                    && update.HasMisfireInstruction && update.MisfireInstructionCode == (int) CronTriggerMisfireInstruction.DoNothing
                    && update.MisfireInstructionFamily == TriggerFamily.Cron
                    && update.HasOverlapPolicy && update.OverlapPolicy == OverlapPolicy.Skip
                    && !update.HasDescription && !update.HasCalendarName && !update.HasRetryPolicy
                    && !update.HasExecutionGroup && !update.HasPreferredNode && !update.HasJobDataMap),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-editor]").Should().BeEmpty(
            "a saved edit closes the editor and the page reads the trigger again"));
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, TriggerKey, A<CancellationToken>._))
            .MustHaveHappenedTwiceOrMore();
    }

    [Test]
    public void ASaveIsRecordedWithWhoMadeItAndWhatItChanged()
    {
        GivenTrigger(CronTrigger());
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-priority").Change("7");
        page.Find("#trigger-edit-calendar").Change("");
        page.Find("#trigger-edit-description").Change("after");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Should().Match<DashboardActionLogEntry>(entry =>
                entry.Action == "UpdateTriggerDetails"
                && entry.Target == "reports.nightly"
                && entry.Succeeded
                && entry.User == "operator@example.com"
                && entry.Message == "description; priority 5 → 7; calendar holidays → (none)",
                "an edit is only auditable if the log says who made it and what it changed"));
        context.Toasts.Messages[^1].Message.Should().Be(
            "Updated trigger reports.nightly: description; priority 5 → 7; calendar holidays → (none).");
    }

    [Test]
    public void AFieldTheEditorCannotReadIsRefusedBesideItBeforeAnythingIsSent()
    {
        GivenTrigger(CronTrigger());
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-priority").Change("high");
        page.Find("#trigger-edit-retry-policy").Change("twice");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() =>
        {
            string errors = page.Find("[data-testid=trigger-editor-errors]").TextContent;
            errors.Should().Contain("Priority must be a whole number.");
            errors.Should().Contain("Retry policy: 'twice' is not a retry policy",
                "the policy's own parser says what is wrong with it");
        });
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void TheSchedulersRefusalIsShownBesideTheFieldsAndTheEditorStaysOpen()
    {
        GivenTrigger(CronTrigger());
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .Throws(new JobPersistenceException("Calendar 'no-such' was not found."));
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-calendar").Change("no-such");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-editor-errors]").TextContent
            .Should().Contain("Calendar 'no-such' was not found.",
                "the scheduler validates what it is sent, and what it says belongs next to the field it is about"));
        page.Find("#trigger-edit-calendar").GetAttribute("value").Should().Be("no-such",
            "the reader corrects the field rather than typing the edit again");
        page.FindAll(".qz-error-alert").Should().BeEmpty("a refused edit is not a page that failed to load");
        context.ActionLog.GetLatest(1).Should().ContainSingle().Which.Succeeded.Should().BeFalse();
    }

    [Test]
    public void ATriggerGoneBeforeTheSaveIsSaidSo()
    {
        GivenTrigger(CronTrigger());
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .Returns(false);
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-priority").Change("1");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-editor-errors]").TextContent
            .Should().Contain("Trigger reports.nightly was not updated - it no longer exists."));
    }

    [Test]
    public void SavingWithNothingChangedSendsNothing()
    {
        GivenTrigger(CronTrigger());
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForElement("[data-testid=trigger-editor-unchanged]");
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void AnEntryAddedToTheMapIsSavedWithTheOnesNobodyTouched()
    {
        Guid correlation = Guid.NewGuid();
        GivenTrigger(CronTrigger(map => map["correlation"] = correlation));
        TriggerDetailsUpdate? sent = null;
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .Invokes((string _, TriggerKeyDto _, TriggerDetailsUpdate update, CancellationToken _) => sent = update)
            .Returns(true);
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find(".qz-job-data-add").Click();
        page.WaitForAssertion(() => page.FindAll(".qz-job-data-key").Should().HaveCount(3));
        page.FindAll(".qz-job-data-key")[2].Input("attempt");
        page.FindAll(".qz-job-data-type")[2].Change("int");
        page.FindAll(".qz-job-data-value")[2].Input("2");
        page.Find("#trigger-edit-description").Change("with an extra entry");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() => sent.Should().NotBeNull());
        sent!.HasJobDataMap.Should().BeTrue();
        JobDataMap saved = sent.JobDataMap!;
        saved.GetInt("attempt").Should().Be(2, "the new row was typed as an int");
        saved.GetString("region").Should().Be("eu");
        saved["correlation"].Should().Be(correlation,
            "a value of a type the editor has no row type for is written back as itself, not as the text it was shown as");
    }

    [Test]
    public void ANamedNodeIsSentAsANamedPin()
    {
        GivenTrigger(CronTrigger());
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-node-mode").Change("node");
        page.WaitForElement("#trigger-edit-node").Change("node-b");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.UpdateTriggerDetails(
                TestData.SchedulerName,
                TriggerKey,
                A<TriggerDetailsUpdate>.That.Matches(update => update.HasPreferredNode && update.PreferredNode == PreferredNode.For("node-b")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
    }

    [Test]
    public void ThePageShowsTheFieldsTheEditorEdits()
    {
        GivenTrigger(TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("export", "reports")
            .WithPreferredNode(PreferredNode.ClaimedBy("node-b"))
            .WithCronSchedule("0 0 1 * * ?", x => x.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .Build());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("<th>Preferred Node</th><td>auto (claimed by node-b)</td>",
                "an automatic pin a node has claimed says which node, and that it will be released");
            page.Markup.Should().Contain("<th>Misfire Instruction</th><td>DoNothing</td>",
                "the instruction is shown by the name its family gives it, not as a number");
            page.Markup.Should().Contain("<th>Calendar</th><td>—</td>");
        });
    }

    [Test]
    public void ReadOnlyModeOffersNoEditor()
    {
        context.Options.ReadOnly = true;
        GivenTrigger(CronTrigger());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Markup.Should().Contain("<th>Priority</th><td>5</td>",
            "a read-only dashboard still shows what the editor would edit"));
        page.HasButton("Edit details").Should().BeFalse("read-only hides every mutating action, and this is one");
        page.FindAll("[data-testid=trigger-editor]").Should().BeEmpty();
    }

    /// <summary>
    /// An <see cref="IQuartzApiClient" /> of an application's own, written against 4.2, answers the
    /// member's default: unavailable. The page disables the editor and says why.
    /// </summary>
    [Test]
    public void ADataSourceThatCannotEditInPlaceDisablesTheEditorWithTheReason()
    {
        GivenTrigger(CronTrigger());
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .CallsBaseMethod();
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-priority").Change("1");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=trigger-editor-unavailable]").TextContent.Should().Contain("cannot edit a trigger in place");
            page.FindAll("button").Single(button => button.TextContent.Trim() == "Edit details")
                .HasAttribute("disabled").Should().BeTrue("the reason stays true for as long as the page is open");
            page.FindAll("[data-testid=trigger-editor]").Should().BeEmpty();
            page.FindAll(".qz-error-alert").Should().BeEmpty("an operation a target cannot do is not an error page");
        });
    }

    [Test]
    public void ATriggerOutsideTriggerBaseHasItsOverlapPolicyDisabledWithTheReason()
    {
        ICronTrigger foreign = A.Fake<ICronTrigger>();
        A.CallTo(() => foreign.Key).Returns(new Quartz.TriggerKey(TriggerName, TriggerGroup));
        A.CallTo(() => foreign.JobDataMap).Returns(new JobDataMap());
        A.CallTo(() => foreign.CronExpressionString).Returns("0 0 1 * * ?");
        GivenTrigger(foreign);

        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-overlap").HasAttribute("disabled").Should().BeTrue();
        page.Find("[data-testid=trigger-editor-overlap-unavailable]").TextContent.Should().Contain("does not derive from TriggerBase",
            "the store would refuse it, so it is not offered");
    }

    [Test]
    public void ATriggerInNoFamilyIsGivenItsMisfireCodeToEditAsANumber()
    {
        ITrigger custom = A.Fake<ITrigger>();
        A.CallTo(() => custom.Key).Returns(new Quartz.TriggerKey(TriggerName, TriggerGroup));
        A.CallTo(() => custom.JobDataMap).Returns(new JobDataMap());
        A.CallTo(() => custom.MisfireInstructionCode).Returns(0);
        GivenTrigger(custom);
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-misfire").TagName.Should().Be("INPUT", "there are no names to offer for a family it is not in");
        page.Find("#trigger-edit-misfire").Change("2");
        page.Find(".qz-trigger-editor-save").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.UpdateTriggerDetails(
                TestData.SchedulerName,
                TriggerKey,
                A<TriggerDetailsUpdate>.That.Matches(update =>
                    update.HasMisfireInstruction && update.MisfireInstructionCode == 2 && update.MisfireInstructionFamily == null),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
    }

    [Test]
    public void CancellingClosesTheEditorWithoutSaving()
    {
        GivenTrigger(CronTrigger());
        IRenderedComponent<TriggerDetail> page = OpenEditor();

        page.Find("#trigger-edit-priority").Change("9");
        page.Find(".qz-trigger-editor-cancel").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-editor]").Should().BeEmpty());
        A.CallTo(() => context.Api.UpdateTriggerDetails(A<string>._, A<TriggerKeyDto>._, A<TriggerDetailsUpdate>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    private IRenderedComponent<TriggerDetail> OpenEditor()
    {
        IRenderedComponent<TriggerDetail> page = Render();
        page.WaitForAssertion(() => page.HasButton("Edit details").Should().BeTrue());
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Edit details").Click();
        page.WaitForElement("[data-testid=trigger-editor]");
        return page;
    }

    private IRenderedComponent<TriggerDetail> Render()
    {
        return context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, TriggerGroup)
            .Add(x => x.Name, TriggerName));
    }

    private void GivenTrigger(ITrigger trigger)
    {
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(trigger);
    }

    private static ITrigger CronTrigger(Action<JobDataMap>? addToMap = null)
    {
        JobDataMap map = new() { ["region"] = "eu" };
        addToMap?.Invoke(map);

        return TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("export", "reports")
            .WithDescription("before")
            .WithPriority(5)
            .WithCalendarName("holidays")
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)))
            .UsingJobData(map)
            .WithCronSchedule("0 0 1 * * ?")
            .Build();
    }
}
