using System.Reflection;
using System.Text.Json;

using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

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
    /// An interrupt that failed says so in a toast, as a failed action on every other page does.
    /// </summary>
    /// <remarks>
    /// It raised none. The failure set an error on the page, which the refresh a second later cleared, so the
    /// reader was never told.
    /// </remarks>
    [Test]
    public void AnInterruptThatFailedRaisesAToast()
    {
        GivenRunningJob();
        A.CallTo(() => context.Api.InterruptJob(A<string>._, "reports", "job-1"))
            .Throws(new SchedulerException("the target is unreachable"));

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();
        page.FindAll("button").First(button => button.TextContent.Trim() == "Interrupt").Click();

        page.WaitForAssertion(() =>
        {
            context.Toasts.Messages.Should().ContainSingle()
                .Which.Message.Should().Be("the target is unreachable");
            page.FindAll(".qz-error-alert").Should().BeEmpty("the toast is the report, and the listing is still true");
        });
    }

    /// <summary>
    /// A job's page disables both of its trigger buttons while a trigger is out, so a double click fires the
    /// job once.
    /// </summary>
    [Test]
    public void TriggeringAJobFromItsPageIsDisabledWhileTheRequestIsOut()
    {
        GivenJob();
        TaskCompletionSource triggering = new(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => context.Api.TriggerJob(A<string>._, A<string>._, A<string>._))
            .Returns(triggering.Task);
        IRenderedComponent<JobDetail> page = RenderJobDetail();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Trigger now").Click();

        page.WaitForAssertion(() => page.FindAll("button")
            .Where(button => button.TextContent.Trim() is "Trigger now" or "Trigger with overrides")
            .Should().HaveCount(2).And.OnlyContain(button => button.HasAttribute("disabled"),
                "both fire the same job, and its trigger is still out"));

        triggering.SetResult();

        page.WaitForAssertion(() => page.FindAll("button").First(button => button.TextContent.Trim() == "Trigger now")
            .HasAttribute("disabled").Should().BeFalse("the request has answered"));
        A.CallTo(() => context.Api.TriggerJob(A<string>._, A<string>._, A<string>._))
            .MustHaveHappenedOnceExactly();
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

    /// <summary>
    /// A job that is running, which is what the Currently Executing page offers to interrupt.
    /// </summary>
    private void GivenRunningJob()
    {
        context.Executing.Add(new CurrentlyExecutingJobDto(
            new JobKeyDto("reports", "job-1"),
            new TriggerKeyDto("nightly", "trigger-1"),
            DashboardComponentContext.FiredAt,
            "fire-1"));
    }

    private void GivenJob()
    {
        A.CallTo(() => context.Api.GetJob(A<string>._, A<string>._, A<string>._))
            .Returns(new JobDetailDto(
                "job-1",
                "reports",
                "Quartz.Tests.AspNetCore.Support.DummyJob",
                "Dummy job description",
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: true,
                PersistJobDataAfterExecution: false,
                JobDataMap: JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["TestKey"] = "TestValue" })));
        A.CallTo(() => context.Api.GetJobTriggers(A<string>._, A<string>._, A<string>._))
            .Returns([new TriggerHeaderDto("nightly", "trigger-1")]);
    }

    private IRenderedComponent<JobDetail> RenderJobDetail()
    {
        return context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "reports")
            .Add(x => x.Name, "job-1"));
    }
}
