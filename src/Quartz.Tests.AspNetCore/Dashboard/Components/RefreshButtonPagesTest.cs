using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The pages that read once and then only when the reader asks: each has a Refresh button beside
/// <em>Last updated</em>, which reads the page again and, when that read fails, keeps what the page showed and
/// marks it stale.
/// </summary>
/// <remarks>
/// The Jobs page's button, and the header status read with it, are in <see cref="JobsPageTest" />.
/// </remarks>
public class RefreshButtonPagesTest
{
    private const string Failure = "the store is not answering";

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
    public void TriggersReadsTheListingAgainAndKeepsItWhenTheReadFails()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.WaitForAssertion(() => page.TextOfAll(".qz-key-badge-value").Should().HaveCount(1));

        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() => page.TextOfAll(".qz-key-badge-value").Should().HaveCount(2, "the button reads the listing again"));

        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException(Failure));
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() =>
        {
            page.TextOfAll(".qz-key-badge-value").Should().HaveCount(2, "the listing stays until a new one arrives");
            StaleReason(page).Should().Be("The last refresh failed: " + Failure);
        });
    }

    [Test]
    public void JobDetailReadsTheJobAgainAndKeepsItWhenTheReadFails()
    {
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new List<TriggerHeaderDto>());
        GivenJob("Releases stale reservations");
        IRenderedComponent<JobDetail> page = context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "billing")
            .Add(x => x.Name, "release-stale"));
        page.WaitForAssertion(() => page.Markup.Should().Contain("Releases stale reservations"));

        GivenJob("Releases every stale reservation");
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Releases every stale reservation", "the button reads the job again"));

        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException(Failure));
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("Releases every stale reservation", "the job stays until a new read arrives");
            page.FindAll(".qz-error-alert").Should().BeEmpty();
            StaleReason(page).Should().Be("The last refresh failed: " + Failure);
        });
    }

    [Test]
    public void TriggerDetailReadsTheTriggerAgainAndKeepsItWhenTheReadFails()
    {
        A.CallTo(() => context.Api.GetTriggerState(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Normal);
        GivenTrigger("0/25 * * * * ?");
        IRenderedComponent<TriggerDetail> page = context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, "nightly")
            .Add(x => x.Name, "at-midnight"));
        page.WaitForAssertion(() => page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be("0/25 * * * * ?"));

        GivenTrigger("0/30 * * * * ?");
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() => page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be("0/30 * * * * ?",
            "the button reads the trigger again, and an editor nobody typed in follows it"));

        A.CallTo(() => context.Api.GetTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException(Failure));
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() =>
        {
            page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be("0/30 * * * * ?", "the trigger stays until a new read arrives");
            StaleReason(page).Should().Be("The last refresh failed: " + Failure);
        });
    }

    [Test]
    public void CalendarsReadsTheNamesAgainAndKeepsThemWhenTheReadFails()
    {
        GivenCalendarNames("holidays");
        IRenderedComponent<Calendars> page = context.Render<Calendars>();
        page.WaitForAssertion(() => page.TextOfAll("[data-testid=calendars-table] tbody a").Should().Equal(["holidays"]));

        GivenCalendarNames("holidays", "month-end");
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() => page.TextOfAll("[data-testid=calendars-table] tbody a").Should().Equal(["holidays", "month-end"], "the button reads the names again"));

        A.CallTo(() => context.Api.GetCalendarNames(A<string>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException(Failure));
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() =>
        {
            page.TextOfAll("[data-testid=calendars-table] tbody a").Should().Equal(["holidays", "month-end"], "the names stay until new ones arrive");
            StaleReason(page).Should().Be("The last refresh failed: " + Failure);
        });
    }

    [Test]
    public void CalendarDetailReadsTheCalendarAgainAndKeepsItWhenTheReadFails()
    {
        GivenCalendar("Office holidays");
        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters
            .Add(x => x.CalendarName, "holidays"));
        page.WaitForAssertion(() => page.Markup.Should().Contain("Office holidays"));

        GivenCalendar("Office and bank holidays");
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Office and bank holidays", "the button reads the calendar again"));

        A.CallTo(() => context.Api.GetCalendar(A<string>._, A<string>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException(Failure));
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("Office and bank holidays", "the calendar stays until a new read arrives");
            StaleReason(page).Should().Be("The last refresh failed: " + Failure);
        });
    }

    [Test]
    public void SchedulersReadsTheFleetAgainAndKeepsItWhenTheReadFails()
    {
        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();
        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(1));

        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._))
            .Returns(new List<SchedulerHeaderDto>
            {
                TestData.Dashboard.SchedulerHeader(),
                TestData.Dashboard.SchedulerHeader("reporting")
            });
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(2, "the button reads the fleet again"));

        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._))
            .Throws(new InvalidOperationException(Failure));
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() =>
        {
            page.FindAll("tbody tr").Should().HaveCount(2, "the fleet stays until a new one arrives");
            StaleReason(page).Should().Be("The last refresh failed: " + Failure);
        });
    }

    private static string? StaleReason<TComponent>(IRenderedComponent<TComponent> page)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        return page.Find("[data-testid=stale-marker]").GetAttribute("title");
    }

    private void GivenTriggers(IReadOnlyList<TriggerHeaderDto> triggers)
    {
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page(triggers));
    }

    private void GivenJob(string description)
    {
        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new JobDetailDto(
                "release-stale",
                "billing",
                "Quartz.Tests.AspNetCore.Support.DummyJob",
                description,
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: false,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap()));
    }

    private void GivenTrigger(string cronExpression)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("at-midnight", "nightly")
            .ForJob("release-stale", "billing")
            .WithCronSchedule(cronExpression)
            .Build();
        A.CallTo(() => context.Api.GetTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(trigger);
    }

    private void GivenCalendarNames(params string[] names)
    {
        A.CallTo(() => context.Api.GetCalendarNames(A<string>._, A<CancellationToken>._))
            .Returns(names.ToList());
    }

    private void GivenCalendar(string description)
    {
        A.CallTo(() => context.Api.GetCalendar(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(new CronCalendar("0 0 12 * * ?") { Description = description });
    }
}
