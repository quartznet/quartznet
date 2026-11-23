using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components.Web;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

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
        context.WithScheduler();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [TestCase("Delete", "Enter")]
    [TestCase("Delete", " ")]
    [TestCase("Pause", "Enter")]
    public void AKeyOnAJobRowsButtonIsTheButtonsAndDoesNotOpenTheJob(string button, string key)
    {
        IRenderedComponent<Jobs> page = RenderJobs();
        string listing = context.CurrentUri;

        IElement action = page.FindAll("tr.qz-row-link button").First(element => element.TextContent.Trim() == button);
        Press(action, key);

        context.CurrentUri.Should().Be(listing, "the key was pressed on the row's button, not on the row");
        action.Click();
        page.WaitForAssertion(() => page.FindAll(".qz-confirm-dialog").Should().ContainSingle("the button's own action runs"));
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

    [TestCase("Unschedule")]
    [TestCase("Pause")]
    public void EnterOnATriggerRowsButtonIsTheButtonsAndDoesNotOpenTheTrigger(string button)
    {
        IRenderedComponent<Triggers> page = RenderTriggers();
        string listing = context.CurrentUri;

        IElement action = page.FindAll("tr.qz-row-link button").First(element => element.TextContent.Trim() == button);
        Press(action, "Enter");

        context.CurrentUri.Should().Be(listing, "the key was pressed on the row's button, not on the row");
        action.Click();
        page.WaitForAssertion(() => page.FindAll(".qz-confirm-dialog").Should().ContainSingle("the button's own action runs"));
    }

    [Test]
    public void EnterOnATriggerRowItselfOpensTheTrigger()
    {
        IRenderedComponent<Triggers> page = RenderTriggers();

        page.Find("tr.qz-row-link").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        context.CurrentUri.Should().Contain("quartz/triggers/nightly/");
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

    private IRenderedComponent<Jobs> RenderJobs()
    {
        List<JobKeyDto> jobs = TestData.Dashboard.JobKeys("reports", 1);
        A.CallTo(() => context.Api.QueryJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<JobKeyDto>(jobs, jobs.Count));
        A.CallTo(() => context.Api.QueryJobGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<JobGroupDto>([new JobGroupDto("reports", false)]));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();
        page.WaitForAssertion(() => page.FindAll("tr.qz-row-link").Should().ContainSingle());
        return page;
    }

    private IRenderedComponent<Triggers> RenderTriggers()
    {
        List<TriggerHeaderDto> triggers = TestData.Dashboard.TriggerHeaders("nightly", 1, TriggerState.Normal);
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<TriggerHeaderDto>(triggers, triggers.Count));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.WaitForAssertion(() => page.FindAll("tr.qz-row-link").Should().ContainSingle());
        return page;
    }
}
