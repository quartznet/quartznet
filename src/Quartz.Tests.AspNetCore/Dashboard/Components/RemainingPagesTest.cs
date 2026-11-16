using System.Reflection;

using Bunit;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The pages whose own logic is small enough not to earn a file of their own, rendered and pinned where
/// they say something.
/// </summary>
public class RemainingPagesTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    /// <summary>
    /// The page lists the active scheduler's latest hundred, however many actions another scheduler took
    /// since.
    /// </summary>
    /// <remarks>
    /// The log is one list for every scheduler in the process. Taking the latest hundred of that list and
    /// then keeping the active scheduler's left nothing once another scheduler had been busier.
    /// </remarks>
    [Test]
    public void TheActionLogFindsTheActiveSchedulersActionsBehindABusierScheduler()
    {
        context.ActionLog.Record(DashboardComponentContext.SchedulerName, "PauseJob", "reports.job-1", succeeded: true);
        for (int index = 0; index < 100; index++)
        {
            context.ActionLog.Record("other", "PauseJob", "elsewhere.job-" + index, succeeded: true);
        }

        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();

        page.FindAll("tbody td").Select(cell => cell.TextContent).Should().Contain("reports.job-1",
            "the log still holds the entry, and a hundred newer ones on another scheduler are not this page's");
        page.Markup.Should().NotContain("elsewhere.job-");
    }

    /// <summary>
    /// Leaving the page stops it listening for scheduler changes.
    /// </summary>
    /// <remarks>
    /// The page subscribes to <see cref="SchedulerState.OnSchedulerChanged" /> and unsubscribes in
    /// <c>Dispose</c>, which Blazor only calls on a component that declares <see cref="IDisposable" />.
    /// Without it each visit left one more handler on the circuit's state.
    /// </remarks>
    [Test]
    public async Task LeavingTheActionLogStopsItListeningForSchedulerChanges()
    {
        int before = SchedulerChangedHandlers(context.SchedulerState);
        context.Render<ActionLog>();
        SchedulerChangedHandlers(context.SchedulerState).Should().Be(before + 1, "the page listens while it is shown");

        await context.DisposeComponentsAsync();

        SchedulerChangedHandlers(context.SchedulerState).Should().Be(before,
            "a page that is gone has nothing to refresh when the scheduler changes");
        Action switchScheduler = () => context.SchedulerState.ActiveSchedulerName = "other";
        switchScheduler.Should().NotThrow();
    }

    /// <summary>
    /// How many handlers <see cref="SchedulerState.OnSchedulerChanged" /> has. A field-like event's
    /// delegate is a private field of the same name.
    /// </summary>
    private static int SchedulerChangedHandlers(SchedulerState state)
    {
        FieldInfo? field = typeof(SchedulerState).GetField(nameof(SchedulerState.OnSchedulerChanged), BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("a field-like event's delegate is a field of its name; an event with its own accessors needs another way to count");
        return (field!.GetValue(state) as EventHandler)?.GetInvocationList().Length ?? 0;
    }
}
