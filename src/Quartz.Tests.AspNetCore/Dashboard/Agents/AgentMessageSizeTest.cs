using System.Globalization;

using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// A page too large for the channel is refused by the agent, with a word about what to do, rather than
/// sent and dropped by the hub — and the connection it would have broken stays up.
/// </summary>
public sealed class AgentMessageSizeTest
{
    [Test]
    public async Task APageLargerThanTheChannelAllowsIsRefusedAndTheConnectionSurvives()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start(agents => agents.MaxMessageBytes = 64 * 1024);
        AgentWorker worker = await dashboard.StartAgent("w1");

        JobKey job = new("bulk", "size");
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(job).StoreDurably().Build());
        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggers = new()
        {
            [await worker.Scheduler.GetJobDetail(job) ?? throw new InvalidOperationException("the job was just added")] = Enumerable.Range(0, 2000)
                .Select(index => (ITrigger) TriggerBuilder.Create()
                    .WithIdentity("trigger-" + index.ToString("D4", CultureInfo.InvariantCulture), "size")
                    .ForJob(job)
                    .WithDescription("a description that gives each row some width on the wire, so a thousand of them are more than the channel allows")
                    .StartAt(DateTimeOffset.UtcNow.AddDays(1))
                    .Build())
                .ToList()
        };
        await worker.Scheduler.ScheduleJobs(triggers, new ScheduleJobOptions { Replace = true });

        Func<Task> wholePage = () => dashboard.Client.QueryTriggers(worker.Key, new DashboardTriggerQuery { Take = 1000 }).AsTask();
        (await wholePage.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain(AgentCarrier.TooLargeDetail);

        dashboard.Registry.Find("w1")!.ConnectionId.Should().NotBeNull("the refusal travelled the channel the page would have broken");
        worker.Logs.WithEventId(9304).Should().ContainSingle();

        PagedResult<TriggerHeaderDto> smallPage = await dashboard.Client.QueryTriggers(worker.Key, new DashboardTriggerQuery { Take = 100 });
        smallPage.Items.Should().HaveCount(100, "a page that fits is answered");
        smallPage.HasMore.Should().BeTrue();
    }
}
