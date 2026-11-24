using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

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
        badge.ClassList.Should().Contain("qz-tag-via-mcp", "its text colour is the one ViaBadgeTextPassesAAInEveryTheme holds to 4.5:1");
        badge.GetAttribute("title").Should().Be("Client, as stated: claude-code/2.1");

        // The identity is the user; the client's name is what the client says it is. That is read out and in the
        // tooltip, not shown: shown, it wrapped the Via column to three lines.
        IElement line = page.Find(".qz-action-log-client");
        line.FirstChild!.TextContent.Should().Be("claude-code/2.1", "the line shows the client alone");
        line.QuerySelector(".qz-sr-only")!.TextContent.Should().Be(" (as stated)");
        line.TextContent.Should().Be("claude-code/2.1 (as stated)", "what a screen reader reads");
        line.GetAttribute("title").Should().Be("Client, as stated: claude-code/2.1", "the tooltip shows what the ellipsis cut");
    }

    /// <summary>
    /// The table scrolls inside its card rather than pushing the page wider, and a timestamp stays on one line.
    /// </summary>
    [Test]
    public void TheTableScrollsInsideItsCardAndTheTimeStaysOnOneLine()
    {
        context.ActionLog.Record(TestData.SchedulerName, "PauseTrigger", "reports.nightly", succeeded: true);

        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();

        page.FindAll(".qz-table-scroll > table.qz-table").Should().ContainSingle();
        page.FindAll("th.qz-action-log-time").Should().ContainSingle();
        page.FindAll("td.qz-action-log-time").Should().ContainSingle();
    }

    /// <summary>
    /// The scheduler column stays on one line and the message keeps a readable width, which the browser check of
    /// #4432 found wrapping to five lines at 1024 px and a word a line at 1440 px. The line is capped and the key cut
    /// short, which the browser check of #4435 found holding the table at 1455 px, Status and Message out of view at
    /// 1440 px; the client stays on one line under its badge, which it found wrapping to three.
    /// </summary>
    [Test]
    public void TheSchedulerIsCutShortOnOneLineAndTheMessageKeepsItsWidth()
    {
        context.ActionLog.Record(TestData.SchedulerName, "PauseTrigger", "reports.nightly", succeeded: true);

        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();

        page.FindAll("th.qz-action-log-scheduler").Should().ContainSingle();
        IElement name = page.Find("td.qz-action-log-scheduler .qz-action-log-scheduler-line > .qz-action-log-scheduler-name");
        name.TextContent.Should().Be(TestData.SchedulerName, "the cell's text is the whole key, which a screen reader reads");
        name.GetAttribute("title").Should().Be(TestData.SchedulerName, "the tooltip shows what the ellipsis cut");
        page.FindAll("th.qz-action-log-message").Should().ContainSingle();
        page.FindAll("td.qz-action-log-message").Should().ContainSingle();

        string css = File.ReadAllText(HistoryPageTest.DashboardStylesheet());
        Regex.IsMatch(css, @"\.qz-action-log-scheduler\s*\{[^}]*white-space:\s*nowrap").Should().BeTrue();
        Regex.IsMatch(css, @"\.qz-action-log-scheduler-line\s*\{[^}]*max-width:\s*15rem").Should().BeTrue();
        Regex.IsMatch(css, @"@media \(max-width: 1599px\)\s*\{\s*\.qz-action-log-scheduler-line\s*\{[^}]*max-width:\s*8rem").Should().BeTrue(
            "eight columns fit 1440 px beside the sidebar only with the key cut to 8rem");
        Regex.IsMatch(css, @"\.qz-action-log-scheduler-name\s*\{[^}]*text-overflow:\s*ellipsis").Should().BeTrue();
        Regex.IsMatch(css, @"\.qz-action-log-client\s*\{[^}]*text-overflow:\s*ellipsis[^}]*white-space:\s*nowrap").Should().BeTrue();
        Regex.IsMatch(css, @"\.qz-action-log-message\s*\{[^}]*min-width:").Should().BeTrue();
    }

    /// <summary>
    /// The Via badges' text is at least 4.5:1 against their tint in every theme: 12 px text is normal text to
    /// WCAG AA. The info and warning colours themselves are not (4.24:1 and 2.74:1 in light, 3.13:1 for info in
    /// dark), so each badge has a text colour of its own.
    /// </summary>
    [Test]
    public void ViaBadgeTextPassesAAInEveryTheme()
    {
        string css = File.ReadAllText(HistoryPageTest.DashboardStylesheet());

        List<string> themes = [];
        foreach (Match block in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
        {
            string body = block.Groups[2].Value;
            // A block that declares the tokens, not a rule that uses them.
            if (!body.Contains("--qz-via-api-text:", StringComparison.Ordinal))
            {
                continue;
            }

            string theme = block.Groups[1].Value.Trim();
            themes.Add(theme);
            Contrast(Token(body, "--qz-via-api-text"), Token(body, "--qz-color-info-bg")).Should().BeGreaterThanOrEqualTo(4.5,
                $"the HTTP API badge's text in {theme}");
            Contrast(Token(body, "--qz-via-mcp-text"), Token(body, "--qz-color-warning-bg")).Should().BeGreaterThanOrEqualTo(4.5,
                $"the MCP badge's text in {theme}");
        }

        themes.Should().HaveCount(4, "light, dark by preference, and the two explicit theme overrides each set the tokens");
        css.Should().Contain("color: var(--qz-via-api-text);").And.Contain("color: var(--qz-via-mcp-text);");
    }

    /// <summary>
    /// What an API change says in the Action Log is what the dashboard's own button would have said: a pause's
    /// reason as <c>reason: …</c>, a backfill's slots, an interrupt that found nothing running entered as not done.
    /// </summary>
    [Test]
    public async Task AnApiChangeSaysWhatTheDashboardsOwnButtonWouldHaveSaid()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddQuartzDashboard();
        builder.Services.AddQuartzHttpApi();
        await using WebApplication app = builder.Build();

        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("orders");
        A.CallTo(() => scheduler.PauseTriggerWith(A<TriggerKey>._, A<PauseDetails?>._, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => scheduler.InterruptFireInstance(A<string>._, A<CancellationToken>._)).Returns(false);
        app.Services.GetRequiredService<ISchedulerRepository>().Bind(scheduler);

        app.MapQuartzHttpApi("/quartz-api").AllowAnonymous();
        app.MapQuartzDashboard().AllowAnonymous();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage paused = await client.PostAsJsonAsync("quartz-api/schedulers/orders/triggers/reports/nightly/pause", new { reason = "api check 4432" });
        using HttpResponseMessage interrupted = await client.PostAsync("quartz-api/schedulers/orders/jobs/interrupt/fire-1", content: null);
        paused.StatusCode.Should().Be(HttpStatusCode.OK, await paused.Content.ReadAsStringAsync());
        interrupted.StatusCode.Should().Be(HttpStatusCode.OK);

        IReadOnlyList<DashboardActionLogEntry> entries = app.Services.GetRequiredService<DashboardActionLogService>().GetLatest();
        DashboardActionLogEntry pause = entries.Should().ContainSingle(entry => entry.Action == "PauseTrigger").Subject;
        pause.Message.Should().Be("reason: api check 4432", "the dashboard's own pause writes its reason so, and the API's is the same action");
        pause.Succeeded.Should().BeTrue();

        DashboardActionLogEntry interrupt = entries.Should().ContainSingle(entry => entry.Action == "InterruptJobInstance").Subject;
        interrupt.Succeeded.Should().BeFalse("the answer said nothing was interrupted, which the dashboard enters as not done");
        interrupt.Message.Should().Be("the firing is no longer running");
        interrupt.Target.Should().Be("fire-1");
    }

    private static string Token(string body, string name)
    {
        return Regex.Match(body, Regex.Escape(name) + @":\s*(#[0-9a-fA-F]{6})").Groups[1].Value;
    }

    private static double Contrast(string foreground, string background)
    {
        double lighter = Math.Max(Luminance(foreground), Luminance(background));
        double darker = Math.Min(Luminance(foreground), Luminance(background));
        return (lighter + 0.05) / (darker + 0.05);

        static double Luminance(string hex)
        {
            double Channel(int offset)
            {
                double value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
                return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }
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
