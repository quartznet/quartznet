using System.Net;

using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Quartz.AspNetCore;
using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Action Log's Via (#4218): what a script did through this process's HTTP API is entered beside what
/// the pages did, says which surface it came through and who made it, and can be read alone.
/// </summary>
public class ActionLogViaTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void AnActionThroughTheHttpApiIsEnteredWithItsSurfaceAndItsUser()
    {
        HttpApiActionRecorder recorder = new(context.Services.GetRequiredService<DashboardActionLogService>(), context.Services);
        recorder.Record(new OperatorAction(OperatorChannel.HttpApi, "deploy-bot", TestData.SchedulerName, "PauseTrigger", "reports.nightly", Succeeded: true));
        recorder.Record(new OperatorAction(OperatorChannel.HttpApi, "intern", TestData.SchedulerName, "Shutdown", TestData.SchedulerName, Succeeded: false)
        {
            Refused = true,
            Message = "needs Operate"
        });
        context.ActionLog.Record(TestData.SchedulerName, "ResumeTrigger", "reports.nightly", succeeded: true);

        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();

        IReadOnlyList<IElement> rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(3);
        rows[0].QuerySelector("[data-testid=action-log-via]")!.TextContent.Should().Be("Dashboard", "the page's own button, the newest entry");
        rows[1].QuerySelector("[data-testid=action-log-via]")!.TextContent.Should().Be("HTTP API");
        rows[1].TextContent.Should().Contain("intern").And.Contain("Shutdown").And.Contain("Refused").And.Contain("needs Operate");
        rows[2].QuerySelector("[data-testid=action-log-via]")!.TextContent.Should().Be("HTTP API");
        rows[2].TextContent.Should().Contain("deploy-bot").And.Contain("PauseTrigger").And.Contain("reports.nightly");
        rows[2].QuerySelector("[data-testid=action-log-via]")!.ClassList.Should().Contain("qz-tag-via-api");
    }

    [Test]
    public void TheViaFilterReadsOneSurfaceAlone()
    {
        HttpApiActionRecorder recorder = new(context.Services.GetRequiredService<DashboardActionLogService>(), context.Services);
        recorder.Record(new OperatorAction(OperatorChannel.HttpApi, "deploy-bot", TestData.SchedulerName, "PauseTrigger", "reports.api", Succeeded: true));
        context.ActionLog.Record(TestData.SchedulerName, "ResumeTrigger", "reports.page", succeeded: true);

        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();
        page.Markup.Should().Contain("reports.api").And.Contain("reports.page");

        page.Find("[data-testid=action-log-via-filter]").Change(nameof(OperatorChannel.HttpApi));
        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("reports.api");
            page.Markup.Should().NotContain("reports.page");
        });

        page.Find("[data-testid=action-log-via-filter]").Change(nameof(OperatorChannel.Mcp));
        page.WaitForAssertion(() => page.Markup.Should().Contain("No recorded actions through MCP yet."));

        page.Find("[data-testid=action-log-via-filter]").Change("");
        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(2, "every surface is the default"));
    }

    [Test]
    public void AClientsNameIsShownAsItsOwnClaim()
    {
        HttpApiActionRecorder recorder = new(context.Services.GetRequiredService<DashboardActionLogService>(), context.Services);
        recorder.Record(new OperatorAction(OperatorChannel.Mcp, "ops", TestData.SchedulerName, "PauseTrigger", "reports.nightly", Succeeded: true)
        {
            Client = "claude-code/2.1"
        });

        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();

        IElement badge = page.Find("[data-testid=action-log-via]");
        badge.TextContent.Should().Be("MCP");
        badge.GetAttribute("title").Should().Be("Client, as stated: claude-code/2.1");
        page.Find(".qz-action-log-client").TextContent.Should().Be("claude-code/2.1 (as stated)",
            "the identity is the user; the client's name is what the client says it is");
    }

    /// <summary>
    /// End to end: a process that maps the dashboard and the HTTP API enters every change and refusal the
    /// API's audit records in the dashboard's Action Log.
    /// </summary>
    [Test]
    public async Task AProcessThatMapsTheDashboardEntersItsApiChangesInTheActionLog()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddQuartzDashboard();
        builder.Services.AddQuartzHttpApi();
        await using WebApplication app = builder.Build();

        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("orders");
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns("node-1");
        app.Services.GetRequiredService<ISchedulerRepository>().Bind(scheduler);

        app.MapQuartzHttpApi("/quartz-api").AllowAnonymous();
        app.MapQuartzDashboard().AllowAnonymous();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage response = await client.PostAsync("quartz-api/schedulers/orders/shutdown", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        DashboardActionLogEntry entry = app.Services.GetRequiredService<DashboardActionLogService>().GetLatest().Should().ContainSingle().Subject;
        entry.Via.Should().Be(OperatorChannel.HttpApi);
        entry.User.Should().Be("(anonymous)", "the API was mapped AllowAnonymous, which is the truth about who did it");
        entry.Action.Should().Be("Shutdown");
        entry.SchedulerName.Should().Be("orders");
        entry.Origin.Should().Be(SchedulerOrigin.Container);
        entry.NodeLocal.Should().BeTrue("a shutdown acts on the node that served it");
        entry.SchedulerInstanceId.Should().Be("node-1");
    }
}
