using Bunit;

using Microsoft.AspNetCore.Components.Web;

using Quartz.Dashboard.Components.Shared;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The annual calendar's grid in the editor is one tab stop (#4413's browser check): the arrow keys move between
/// its 366 days, Home and End go to the month's ends, and Space ticks the day focused, so Save is a few Tabs away
/// rather than 366.
/// </summary>
public sealed class AnnualDayGridTest
{
    private DashboardComponentContext context = null!;
    private HashSet<MonthDay> days = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        days = [];
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void TheGridIsOneTabStop()
    {
        IRenderedComponent<AnnualDayGrid> grid = RenderEditable();

        TabStops(grid).Should().Equal(["--01-01"], "Tab enters the grid once, at its first day, and leaves it on the next Tab");
        grid.FindAll("input").Should().HaveCount(366).And.OnlyContain(cell => cell.GetAttribute("tabindex") == "0" || cell.GetAttribute("tabindex") == "-1");
        context.JSInterop.Invocations.Should().Contain(invocation => invocation.Identifier == "quartzDashboardKeys.holdArrowKeys",
            "the arrow keys move focus here, so the page must not scroll with them");
    }

    [TestCase("ArrowRight", "--01-01", "--01-02")]
    [TestCase("ArrowLeft", "--01-01", "--01-01")]
    [TestCase("ArrowRight", "--01-31", "--02-01")]
    [TestCase("ArrowLeft", "--03-01", "--02-29")]
    [TestCase("ArrowDown", "--01-31", "--02-29")]
    [TestCase("ArrowUp", "--03-15", "--02-15")]
    [TestCase("ArrowUp", "--01-15", "--01-15")]
    [TestCase("ArrowDown", "--12-15", "--12-15")]
    [TestCase("Home", "--04-17", "--04-01")]
    [TestCase("End", "--04-17", "--04-30")]
    [TestCase("ArrowRight", "--12-31", "--12-31")]
    public void AnArrowKeyMovesTheTabStopAndFocus(string key, string from, string to)
    {
        IRenderedComponent<AnnualDayGrid> grid = RenderEditable();
        grid.Find($"input[data-month-day='{from}']").Focus();
        int focusesBefore = Focuses();

        grid.Find("table").KeyDown(new KeyboardEventArgs { Key = key });

        TabStops(grid).Should().Equal([to]);
        Focuses().Should().Be(from == to ? focusesBefore : focusesBefore + 1, "focus moves with the tab stop, and stays when the key moves nowhere");
    }

    [Test]
    public void SpaceOnTheFocusedDayTicksIt()
    {
        IRenderedComponent<AnnualDayGrid> grid = RenderEditable();
        grid.Find("input[data-month-day='--01-01']").Focus();
        grid.Find("table").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        // Space on a focused checkbox is the browser's own toggle, which arrives as a change.
        grid.Find("input[data-month-day='--01-02']").Change(true);

        days.Should().Equal([new MonthDay(1, 2)]);
    }

    [Test]
    public void TheReadOnlyGridHasNoTabStopAndNoKeys()
    {
        IRenderedComponent<AnnualDayGrid> grid = context.Render<AnnualDayGrid>(parameters => parameters
            .Add(x => x.Days, days)
            .Add(x => x.ReadOnly, true));

        grid.FindAll("input[tabindex]").Should().BeEmpty("a disabled check is not focusable, so it needs no tab stop");
        context.JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier == "quartzDashboardKeys.holdArrowKeys");
    }

    private IRenderedComponent<AnnualDayGrid> RenderEditable()
    {
        return context.Render<AnnualDayGrid>(parameters => parameters.Add(x => x.Days, days));
    }

    private static List<string?> TabStops(IRenderedComponent<AnnualDayGrid> grid)
    {
        return grid.FindAll("input[tabindex='0']").Select(cell => cell.GetAttribute("data-month-day")).ToList();
    }

    /// <summary>How many times a cell was given focus: Blazor's FocusAsync goes through the JavaScript runtime.</summary>
    private int Focuses()
    {
        return context.JSInterop.Invocations.Count(invocation => invocation.Identifier.EndsWith("focus", StringComparison.OrdinalIgnoreCase));
    }
}
