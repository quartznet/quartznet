using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;

using TriggersPage = Quartz.Dashboard.Components.Pages.Triggers;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// A listing row is a link a keyboard opens with Enter or Space, and its buttons are its own: a key pressed on a
/// row's Delete, Pause or copy button is that button's, and does not also open the row (#4413's browser check).
/// </summary>
/// <remarks>
/// The browser turns Enter or Space on a focused button into a click; what the row must not do is take the same
/// key as it bubbles up and navigate away from the dialog the click opened.
/// </remarks>
public sealed class RowKeyboardTest
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

    [TestCase("Enter")]
    [TestCase(" ")]
    public void AKeyOnAJobRowsDeleteButtonIsTheButtonsAndDoesNotOpenTheJob(string key)
    {
        IRenderedComponent<Jobs> page = RenderJobs();
        string listing = context.CurrentUri;

        IElement action = RowButton(page, "Delete");
        Press(action, key);

        context.CurrentUri.Should().Be(listing, "the key was pressed on the row's button, not on the row");
        action.Click();
        page.WaitForAssertion(() => page.FindAll(".qz-confirm-dialog").Should().ContainSingle("the button's own action runs"));
    }

    /// <remarks>
    /// Pause pauses at once on this line, where 4.x asks first, so its own action is the pause the toast reports.
    /// </remarks>
    [Test]
    public void EnterOnAJobRowsPauseButtonIsTheButtonsAndDoesNotOpenTheJob()
    {
        A.CallTo(() => context.Api.PauseJob(A<string>._, A<string>._, A<string>._)).Returns(Task.CompletedTask);
        IRenderedComponent<Jobs> page = RenderJobs();
        string listing = context.CurrentUri;

        IElement action = RowButton(page, "Pause");
        Press(action, "Enter");

        context.CurrentUri.Should().Be(listing, "the key was pressed on the row's button, not on the row");
        action.Click();
        page.WaitForAssertion(() => context.Toasts.Messages.Should().ContainSingle("the button's own action runs")
            .Which.Message.Should().Be("Paused job reports.job-1."));
    }

    [Test]
    public void AKeyOnAJobRowsCopyButtonDoesNotOpenTheJob()
    {
        IRenderedComponent<Jobs> page = RenderJobs();
        string listing = context.CurrentUri;

        Press(page.Find("tr.qz-row-link .qz-key-badge-copy"), "Enter");

        context.CurrentUri.Should().Be(listing);
    }

    [TestCase("Enter")]
    [TestCase(" ")]
    public void AKeyOnAJobRowItselfOpensTheJob(string key)
    {
        IRenderedComponent<Jobs> page = RenderJobs();

        page.Find("tr.qz-row-link").KeyDown(new KeyboardEventArgs { Key = key });

        context.CurrentUri.Should().EndWith("quartz/jobs/reports/job-1");
    }

    [Test]
    public void EnterOnATriggerRowsUnscheduleButtonIsTheButtonsAndDoesNotOpenTheTrigger()
    {
        IRenderedComponent<TriggersPage> page = RenderTriggers();
        string listing = context.CurrentUri;

        IElement action = RowButton(page, "Unschedule");
        Press(action, "Enter");

        context.CurrentUri.Should().Be(listing, "the key was pressed on the row's button, not on the row");
        action.Click();
        page.WaitForAssertion(() => page.FindAll(".qz-confirm-dialog").Should().ContainSingle("the button's own action runs"));
    }

    /// <remarks>
    /// Pause pauses at once on this line, where 4.x asks first, so its own action is the pause the toast reports.
    /// </remarks>
    [Test]
    public void EnterOnATriggerRowsPauseButtonIsTheButtonsAndDoesNotOpenTheTrigger()
    {
        A.CallTo(() => context.Api.PauseTrigger(A<string>._, A<string>._, A<string>._)).Returns(Task.CompletedTask);
        IRenderedComponent<TriggersPage> page = RenderTriggers();
        string listing = context.CurrentUri;

        IElement action = RowButton(page, "Pause");
        Press(action, "Enter");

        context.CurrentUri.Should().Be(listing, "the key was pressed on the row's button, not on the row");
        action.Click();
        page.WaitForAssertion(() => context.Toasts.Messages.Should().ContainSingle("the button's own action runs")
            .Which.Message.Should().Be("Paused trigger nightly.trigger-1."));
    }

    [Test]
    public void EnterOnATriggerRowItselfOpensTheTrigger()
    {
        IRenderedComponent<TriggersPage> page = RenderTriggers();

        page.Find("tr.qz-row-link").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        context.CurrentUri.Should().EndWith("quartz/triggers/nightly/trigger-1");
    }

    /// <summary>
    /// A key pressed on <paramref name="element" />, bubbling as the browser bubbles it. bUnit reports a key that no
    /// handler on the way up takes as a missing handler, where a browser does nothing: that is the outcome these
    /// cases want for a button, and the navigation the row would have made is what they look at.
    /// </summary>
    private static void Press(IElement element, string key)
    {
        try
        {
            element.KeyDown(new KeyboardEventArgs { Key = key });
        }
        catch (MissingEventHandlerException)
        {
            // Nothing on the way up took the key: the row did not.
        }
    }

    private static IElement RowButton<TComponent>(IRenderedComponent<TComponent> page, string label)
        where TComponent : IComponent
    {
        return page.FindAll("tr.qz-row-link button").First(element => element.TextContent.Trim() == label);
    }

    private IRenderedComponent<Jobs> RenderJobs()
    {
        List<JobKeyDto> jobs = DashboardComponentContext.JobKeys("reports", 1);
        A.CallTo(() => context.Api.GetJobKeys(A<string>._, A<string?>._)).Returns(jobs);

        IRenderedComponent<Jobs> page = context.Render<Jobs>();
        page.WaitForAssertion(() => page.FindAll("tr.qz-row-link").Should().ContainSingle());
        return page;
    }

    private IRenderedComponent<TriggersPage> RenderTriggers()
    {
        List<TriggerKeyDto> triggers = DashboardComponentContext.TriggerKeys("nightly", 1);
        A.CallTo(() => context.Api.GetTriggerKeys(A<string>._, A<string?>._)).Returns(triggers);

        IRenderedComponent<TriggersPage> page = context.Render<TriggersPage>();
        page.WaitForAssertion(() => page.FindAll("tr.qz-row-link").Should().ContainSingle());
        return page;
    }
}
