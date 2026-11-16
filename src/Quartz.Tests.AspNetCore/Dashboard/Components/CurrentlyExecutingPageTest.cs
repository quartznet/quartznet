using Bunit;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Currently Executing: the firings the scheduler is running, and how long each has run.
/// </summary>
public class CurrentlyExecutingPageTest
{
    /// <summary>
    /// A run of a day or more shows its days, rather than its run time less whole days.
    /// </summary>
    [Test]
    public void ARunOfMoreThanADayShowsItsDays()
    {
        using DashboardComponentContext context = new();
        context.Executing.Add(new CurrentlyExecutingJobDto(
            new JobKeyDto("reports", "job-1"),
            new TriggerKeyDto("reports", "trigger-1"),
            DateTimeOffset.UtcNow.AddHours(-26),
            "fire-1"));

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("1.02:00:0",
            "a job that has run for 26 hours has not run for 2"));
    }
}
