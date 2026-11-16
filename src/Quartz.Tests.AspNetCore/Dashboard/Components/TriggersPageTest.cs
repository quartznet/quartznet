using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Services;

using TriggersPage = Quartz.Dashboard.Components.Pages.Triggers;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Triggers page: what a failed reload leaves on it.
/// </summary>
public class TriggersPageTest
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

    /// <summary>
    /// A read that fails once the listing has been read keeps it, marked stale beside the time it was read,
    /// rather than putting the error in its place.
    /// </summary>
    [Test]
    public void AFailedReloadKeepsTheListingAndMarksItStale()
    {
        GivenTriggers(DashboardComponentContext.TriggerKeys("nightly", 2));
        A.CallTo(() => context.Api.ResumeTrigger(A<string>._, A<string>._, A<string>._)).Returns(Task.CompletedTask);
        IRenderedComponent<TriggersPage> page = context.Render<TriggersPage>();

        // The resume answers; the read of the listing after it does not.
        A.CallTo(() => context.Api.GetTriggerKeys(A<string>._, A<string?>._))
            .Throws(new InvalidOperationException("the store is not answering"));
        page.FindAll("button").First(button => button.TextContent.Trim() == "Resume").Click();

        page.WaitForAssertion(() =>
        {
            context.Toasts.Messages.Should().ContainSingle()
                .Which.Message.Should().Be("Resumed trigger nightly.trigger-1.");
            page.TextOfAll(".qz-key-badge-value").Should().Equal(["nightly.trigger-1", "nightly.trigger-2"],
                "the listing stays until a new one arrives");
            page.FindAll(".qz-error-alert").Should().BeEmpty("a page with a listing to show is not a page that failed to load");
            page.Find("[data-testid=stale-marker]").GetAttribute("title").Should().Be(
                "The last refresh failed: the store is not answering");
        });
    }

    /// <summary>
    /// A read of a trigger's state that fails after the listing itself answered keeps the listing with the
    /// states it was read with, marked stale, rather than a listing whose states say Unknown.
    /// </summary>
    [Test]
    public void AFailedStateReadKeepsTheStatesAndMarksThemStale()
    {
        GivenTriggers(DashboardComponentContext.TriggerKeys("nightly", 2));
        A.CallTo(() => context.Api.GetTriggerState(A<string>._, "nightly", "trigger-1")).Returns("Paused");
        A.CallTo(() => context.Api.ResumeTrigger(A<string>._, A<string>._, A<string>._)).Returns(Task.CompletedTask);
        IRenderedComponent<TriggersPage> page = context.Render<TriggersPage>();
        page.WaitForAssertion(() => page.TextOfAll(".qz-col-state .qz-state-label").Should().Equal(["Paused", "Normal"]));

        // The resume and the listing answer; the read of a trigger's state after them does not.
        A.CallTo(() => context.Api.GetTriggerState(A<string>._, A<string>._, A<string>._))
            .Throws(new InvalidOperationException("the store is not answering"));
        page.FindAll("button").First(button => button.TextContent.Trim() == "Resume").Click();

        page.WaitForAssertion(() =>
        {
            context.Toasts.Messages.Should().ContainSingle()
                .Which.Message.Should().Be("Resumed trigger nightly.trigger-1.");
            page.TextOfAll(".qz-col-state .qz-state-label").Should().Equal(["Paused", "Normal"],
                "the states on screen are the ones last read, and the mark beside the time says so");
            page.FindAll(".qz-error-alert").Should().BeEmpty("a page with a listing to show is not a page that failed to load");
            page.Find("[data-testid=stale-marker]").GetAttribute("title").Should().Be(
                "The last refresh failed: the store is not answering");
        });
    }

    private void GivenTriggers(IReadOnlyList<TriggerKeyDto> triggers)
    {
        A.CallTo(() => context.Api.GetTriggerKeys(A<string>._, A<string?>._)).Returns([.. triggers]);
    }
}
