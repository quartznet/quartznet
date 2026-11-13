using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Job Detail page's table of the triggers that fire the job: each schedule in words, as the Triggers listing
/// says it (#4243).
/// </summary>
public class JobDetailTriggersTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();

        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new JobDetailDto(
                "release-stale",
                "billing",
                "Quartz.Tests.AspNetCore.Support.DummyJob",
                Description: null,
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: false,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap()));
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void EachTriggerSaysItsScheduleInWordsWithItsExpressionOnHover()
    {
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new List<TriggerHeaderDto>
            {
                new("billing", "weekday-mornings", "Cron", "0 0 7 ? * MON-FRI", TriggerState.Normal, ExecutionGroup: null)
                {
                    ScheduleDescription = "Every weekday at 07:00 (America/Chicago)"
                },
                new("billing", "every-30-seconds", "Simple", ScheduleSummary: null, TriggerState.Normal, ExecutionGroup: null)
                {
                    ScheduleDescription = "Every 30 seconds"
                },
                new("billing", "custom", "MyTrigger", ScheduleSummary: null, TriggerState.Normal, ExecutionGroup: null)
            });

        IRenderedComponent<JobDetail> page = context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "billing")
            .Add(x => x.Name, "release-stale"));

        page.WaitForAssertion(() =>
        {
            List<AngleSharp.Dom.IElement> cells = page.FindAll("td.qz-col-schedule").ToList();
            cells.Select(cell => cell.TextContent.Trim()).Should().Equal(
                "Every weekday at 07:00 (America/Chicago)",
                "Every 30 seconds",
                "—");
            cells[0].QuerySelector(".qz-schedule")!.GetAttribute("title").Should().Be("0 0 7 ? * MON-FRI");
            cells[1].QuerySelector(".qz-schedule")!.HasAttribute("title").Should().BeFalse("a simple trigger has no expression to show");
        });
    }
}
