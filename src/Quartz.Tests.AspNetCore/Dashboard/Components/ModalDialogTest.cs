using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Components.Shared;

using TriggersPage = Quartz.Dashboard.Components.Pages.Triggers;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The dashboard's confirmations are modal for a keyboard: focus moves into the dialog as it opens, Tab and
/// Shift+Tab stay inside it, Escape cancels it, and focus goes back to what opened it as it closes.
/// </summary>
/// <remarks>
/// The focus moves themselves are the browser's, made by <c>quartzDashboardDialog</c> in the dashboard's script;
/// what is held here is that the dialog asks for each one at the right moment, with the dialog it means.
/// </remarks>
public sealed class ModalDialogTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
    }

    [TearDown]
    public async Task TearDown()
    {
        await context.DisposeAsync();
    }

    [Test]
    public void AConfirmDialogTakesFocusAsItOpensAndGivesItBackAsItCloses()
    {
        IRenderedComponent<ConfirmDialog> dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Delete calendar"));

        Invocations("quartzDashboardDialog.open").Should().ContainSingle("focus moves into the dialog as it opens");
        Invocations("quartzDashboardDialog.close").Should().BeEmpty();
        dialog.Find("[role=dialog]").GetAttribute("aria-modal").Should().Be("true");
        dialog.Find("[role=dialog]").GetAttribute("aria-labelledby").Should().Be(dialog.Find("h3").Id,
            "the dialog is named by its heading");

        dialog.Render(parameters => parameters.Add(x => x.IsOpen, false));

        Invocations("quartzDashboardDialog.close").Should().ContainSingle("focus goes back to what opened the dialog as it closes");
    }

    [Test]
    public void TwoConfirmDialogsAreNamedByTheirOwnHeadings()
    {
        IRenderedComponent<ConfirmDialog> first = context.Render<ConfirmDialog>(parameters => parameters.Add(x => x.IsOpen, true));
        IRenderedComponent<ConfirmDialog> second = context.Render<ConfirmDialog>(parameters => parameters.Add(x => x.IsOpen, true));

        first.Find("h3").Id.Should().NotBe(second.Find("h3").Id, "a page can hold several dialogs, and an id names one");
    }

    [Test]
    public void TabAndShiftTabWrapInsideTheDialog()
    {
        IRenderedComponent<ConfirmDialog> dialog = context.Render<ConfirmDialog>(parameters => parameters.Add(x => x.IsOpen, true));
        string opened = ElementOf(Invocations("quartzDashboardDialog.open").Single());

        dialog.Find("[data-testid=modal-guard-end]").Focus();
        dialog.Find("[data-testid=modal-guard-start]").Focus();

        ElementOf(Invocations("quartzDashboardDialog.focusFirst").Should().ContainSingle(
            "Tab past the last action lands on the closing guard, which sends focus back to the first").Subject).Should().Be(opened);
        ElementOf(Invocations("quartzDashboardDialog.focusLast").Should().ContainSingle(
            "Shift+Tab before the first action lands on the opening guard, which sends focus round to the last").Subject).Should().Be(opened);
    }

    [Test]
    public void EscapeIsTheDialogsCancel()
    {
        int cancelled = 0;
        IRenderedComponent<ConfirmDialog> dialog = context.Render<ConfirmDialog>(parameters => parameters
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnCancel, () => cancelled++));

        dialog.Find("[data-testid=modal-overlay]").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cancelled.Should().Be(0, "only Escape cancels");

        dialog.Find("[data-testid=modal-overlay]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cancelled.Should().Be(1);
    }

    [Test]
    public void TheJobsListsDeleteQuestionClosesOnEscapeAndReturnsFocus()
    {
        A.CallTo(() => context.Api.GetJobKeys(A<string>._, A<string?>._)).Returns([.. DashboardComponentContext.JobKeys("reports", 1)]);
        IRenderedComponent<Jobs> page = context.Render<Jobs>();
        page.WaitForAssertion(() => page.FindAll("button").First(button => button.TextContent.Trim() == "Delete").Click());

        page.WaitForAssertion(() => Invocations("quartzDashboardDialog.open").Should().ContainSingle());
        page.Find("[data-testid=modal-overlay]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        page.WaitForAssertion(() => page.FindAll(".qz-confirm-dialog").Should().BeEmpty("Escape is Cancel"));
        Invocations("quartzDashboardDialog.close").Should().ContainSingle();
        A.CallTo(() => context.Api.DeleteJob(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public void TheTriggersListsUnscheduleQuestionClosesOnEscapeAndReturnsFocus()
    {
        A.CallTo(() => context.Api.GetTriggerKeys(A<string>._, A<string?>._)).Returns([.. DashboardComponentContext.TriggerKeys("nightly", 1)]);
        IRenderedComponent<TriggersPage> page = context.Render<TriggersPage>();
        page.WaitForAssertion(() => page.FindAll("button").First(button => button.TextContent.Trim() == "Unschedule").Click());

        page.WaitForAssertion(() => Invocations("quartzDashboardDialog.open").Should().ContainSingle());
        page.Find("[data-testid=modal-overlay]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        page.WaitForAssertion(() => page.FindAll(".qz-confirm-dialog").Should().BeEmpty("Escape is Cancel"));
        Invocations("quartzDashboardDialog.close").Should().ContainSingle();
        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
    }

    private List<JSRuntimeInvocation> Invocations(string identifier)
    {
        return context.JSInterop.Invocations.Where(invocation => invocation.Identifier == identifier).ToList();
    }

    /// <summary>The element an invocation was given, by the id Blazor gives an element reference.</summary>
    private static string ElementOf(JSRuntimeInvocation invocation)
    {
        return invocation.Arguments.Should().ContainSingle().Which.Should().BeOfType<ElementReference>().Subject.Id;
    }
}
