using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// The dashboard's trigger listing of an agent's scheduler carries each schedule in words (#4243): the agent
/// describes the page it lists, and the dashboard reads the words off that one answer.
/// </summary>
public sealed class AgentScheduleDescriptionTest
{
    [Test]
    public async Task TheListingOfAnAgentsSchedulerSaysEachScheduleInWords()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1");

        JobKey job = new("report", "nightly");
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(job).StoreDurably().Build());
        await worker.Scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity("monthly", "nightly")
            .ForJob(job)
            .WithSchedule(RecurrenceScheduleBuilder.Create("FREQ=MONTHLY;BYDAY=2TU").InTimeZone(TimeZoneInfo.Utc))
            .Build());

        PagedResult<TriggerHeaderDto> page = await dashboard.Client.QueryTriggers(worker.Key, new DashboardTriggerQuery { Take = 10 });

        TriggerHeaderDto row = page.Items.Should().ContainSingle().Subject;
        row.ScheduleDescription.Should().Be("Monthly on the second Tuesday (UTC)");
        row.ScheduleExpression.Should().Be("FREQ=MONTHLY;BYDAY=2TU");
    }
}
