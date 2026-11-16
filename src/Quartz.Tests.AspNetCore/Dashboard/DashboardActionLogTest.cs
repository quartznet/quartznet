using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The record of the actions taken from the dashboard, which the Overview and the Action Log page list.
/// </summary>
public class DashboardActionLogTest
{
    /// <summary>
    /// The latest entries for one scheduler are its own, however busy another scheduler was since.
    /// </summary>
    /// <remarks>
    /// The bound of 250 is still shared: entries older than the latest 250 are gone, whichever scheduler
    /// they were for.
    /// </remarks>
    [TestCase(10)]
    [TestCase(100)]
    public void OneSchedulersLatestAreFilteredBeforeTheyAreCounted(int maxCount)
    {
        DashboardActionLogService store = new();
        for (int index = 0; index < 3; index++)
        {
            store.Record("acme", "PauseJob", "acme.job-" + index, succeeded: true);
        }

        for (int index = 0; index < maxCount + 5; index++)
        {
            store.Record("globex", "PauseJob", "globex.job-" + index, succeeded: true);
        }

        store.GetLatest("ACME", maxCount).Select(entry => entry.Target).Should().Equal(
            ["acme.job-2", "acme.job-1", "acme.job-0"],
            "the newer entries for another scheduler are not this one's, and the name matches whatever its case");
        store.GetLatest("globex", maxCount).Should().HaveCount(maxCount);
        store.GetLatest(schedulerName: null, maxCount).Should().HaveCount(maxCount)
            .And.OnlyContain(entry => entry.SchedulerName == "globex", "no scheduler named is every scheduler, newest first");
    }
}
