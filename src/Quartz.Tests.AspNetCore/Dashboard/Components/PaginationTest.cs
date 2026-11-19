using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components.Shared;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The pager under the History, Jobs and Triggers listings.
/// </summary>
public class PaginationTest
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

    [TestCase(23, 1, "1 2 3 4 5 … 23")]
    [TestCase(23, 3, "1 2 3 4 5 … 23")]
    [TestCase(23, 4, "1 2 3 4 5 … 23")]
    [TestCase(23, 5, "1 … 4 5 6 … 23")]
    [TestCase(23, 12, "1 … 11 12 13 … 23")]
    [TestCase(23, 19, "1 … 18 19 20 … 23")]
    [TestCase(23, 20, "1 … 19 20 21 22 23")]
    [TestCase(23, 21, "1 … 19 20 21 22 23")]
    [TestCase(23, 23, "1 … 19 20 21 22 23")]
    [TestCase(2, 1, "1 2")]
    [TestCase(7, 4, "1 2 3 4 5 6 7")]
    [TestCase(8, 1, "1 2 3 4 5 … 8")]
    [TestCase(8, 4, "1 2 3 4 5 … 8")]
    [TestCase(8, 5, "1 … 4 5 6 7 8")]
    [TestCase(8, 8, "1 … 4 5 6 7 8")]
    public void ThePagerKeepsTheFirstAndLastPagesInAFixedRowOfSlots(int totalPages, int currentPage, string expected)
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages, currentPage);

        List<string> slots = pager.TextOfAll(".qz-pagination-controls > *");
        slots.First().Should().Be("Prev");
        slots.Last().Should().Be("Next");
        string.Join(" ", slots.Skip(1).SkipLast(1)).Should().Be(expected,
            "the row keeps one width as the reader moves, so the buttons stay under the pointer");
    }

    [Test]
    public void ThePagerKeepsAWindowOfPagesWhenTheCurrentOneIsPastTheEnd()
    {
        List<int> requested = [];
        IRenderedComponent<Pagination> pager = context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, 23 * 25)
            .Add(x => x.PageSize, 25)
            .Add(x => x.CurrentPage, 30)
            .Add(x => x.OnPageChanged, EventCallback.Factory.Create<int>(this, requested.Add)));

        pager.TextOfAll(".qz-pagination-controls button").Should().Equal(["Prev", "1", "19", "20", "21", "22", "23", "Next"],
            "a listing that shrank under the reader still needs a way back to the pages that are left");
        pager.Find(".qz-pagination-info").TextContent.Should().Be("Page 23 of 23", "there is no page 30 of 23 to be on");
        pager.Find("[aria-current=page]").TextContent.Trim().Should().Be("23");

        pager.FindAll("button").First(button => button.TextContent.Trim() == "Prev").Click();
        requested.Should().Equal([22], "Prev steps back from the last page there is, not from one past it");
    }

    [Test]
    public void ThePagerGroupsItsButtonsAndSaysWhichPageOfHowMany()
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage: 5);

        pager.Find("nav.qz-pagination").GetAttribute("aria-label").Should().Be("Pagination");
        pager.FindAll("nav.qz-pagination > button").Should().BeEmpty(
            "a button straight under the bar is spread across the full width with the space between");
        pager.FindAll(".qz-pagination-controls button").Should().HaveCount(7, "Prev, five pages and Next");
        pager.Find(".qz-pagination-info").TextContent.Should().Be("Page 5 of 23");
    }

    [Test]
    public void OnlyTheCurrentPageIsMarkedCurrent()
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage: 12);

        pager.FindAll("[aria-current]").Select(button => (button.TextContent.Trim(), button.GetAttribute("aria-current")))
            .Should().Equal([("12", "page")], "a screen reader names the page the reader is on, and no other");
    }

    [Test]
    public void TheEllipsisIsNotSomethingToPress()
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage: 12);

        pager.FindAll(".qz-pagination-ellipsis").Should().HaveCount(2)
            .And.AllSatisfy(gap => gap.TagName.Should().Be("SPAN"));
        pager.TextOfAll(".qz-pagination-controls button").Should().NotContain("…",
            "the skipped pages are not one page, so there is nowhere for a press to go");
    }

    [TestCase(1, true, false)]
    [TestCase(12, false, false)]
    [TestCase(23, false, true)]
    public void PrevAndNextAreDisabledAtTheEnds(int currentPage, bool prevDisabled, bool nextDisabled)
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage);

        IReadOnlyList<IElement> buttons = pager.FindAll(".qz-pagination-controls button");
        buttons[0].HasAttribute("disabled").Should().Be(prevDisabled);
        buttons[^1].HasAttribute("disabled").Should().Be(nextDisabled);
    }

    [Test]
    public void ThePagerDisappearsWhenEverythingFitsOnOnePage()
    {
        IRenderedComponent<Pagination> pager = context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, 3)
            .Add(x => x.PageSize, 25));

        pager.Markup.Should().BeEmpty();
    }

    [Test]
    public void ThePagerRefusesToLeaveTheRange()
    {
        List<int> requested = [];
        IRenderedComponent<Pagination> pager = context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, 30)
            .Add(x => x.PageSize, 25)
            .Add(x => x.CurrentPage, 1)
            .Add(x => x.OnPageChanged, EventCallback.Factory.Create<int>(this, requested.Add)));

        pager.FindAll("button").First(button => button.TextContent.Trim() == "Prev").Click();
        pager.FindAll("button").First(button => button.TextContent.Trim() == "1").Click();
        requested.Should().BeEmpty("neither goes anywhere, so neither should reload the listing");

        pager.FindAll("button").First(button => button.TextContent.Trim() == "Next").Click();
        requested.Should().Equal([2]);
    }

    private IRenderedComponent<Pagination> RenderPager(int totalPages, int currentPage)
    {
        return context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, totalPages * 25)
            .Add(x => x.PageSize, 25)
            .Add(x => x.CurrentPage, currentPage));
    }
}
