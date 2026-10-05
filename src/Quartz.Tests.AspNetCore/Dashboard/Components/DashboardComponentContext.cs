using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Dashboard.Support;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// A bUnit context carrying everything the dashboard's components inject, so a page can be rendered by
/// naming it and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The data source is a fake <see cref="IQuartzApiClient" /> answering with the DTOs in
/// <see cref="TestData.Dashboard" />; the rest are the real services, because they are the ones the
/// pages actually talk to and they are cheap. bUnit supplies the navigation manager and the JavaScript
/// runtime.
/// </para>
/// <para>
/// The time zone is pinned to UTC: <see cref="SchedulerState" /> formats every timestamp in the
/// selected zone, which otherwise is the machine's and makes rendered text machine-dependent.
/// </para>
/// </remarks>
internal sealed class DashboardComponentContext : BunitContext
{
    /// <param name="configure">Configures the dashboard's options, as an application would.</param>
    /// <param name="registerEventSource">
    /// Whether the container holds an event stream at all. <see langword="false" /> is an application that
    /// registered its own <see cref="IQuartzApiClient" /> without calling <c>AddQuartzDashboard()</c>, which
    /// the Live Logs page has something to say about.
    /// </param>
    /// <param name="remoteEventSourceFor">
    /// The scheduler whose events come from another process, keyed by its name the way
    /// <c>AddQuartzHttpClient</c> registers its target's reader. Named here rather than added later because
    /// bUnit freezes its services the first time one is resolved.
    /// </param>
    public DashboardComponentContext(
        Action<QuartzDashboardOptions>? configure = null,
        bool registerEventSource = true,
        string? remoteEventSourceFor = null)
    {
        Options = new QuartzDashboardOptions();
        configure?.Invoke(Options);

        Api = A.Fake<IQuartzApiClient>();
        Events = new FakeSchedulerEventSource();

        // KeyBadge copies a key to the clipboard through JS interop, so a strict runtime would fail
        // every page that lists one for a call no test is about.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Nothing is authorized against until a test sets QuartzDashboardOptions.SchedulerAuthorizationPolicy:
        // with it unset, SchedulerAuthorization answers "yes" without asking either of these, which is what
        // keeps every test that is about something else unaffected.
        AuthorizationService = new TestSchedulerAuthorizationService();
        AuthenticationState = new TestAuthenticationStateProvider();

        Services.AddSingleton(Api);

        // The clock the History and Job Detail pages measure their chart's window on, so a case can say which
        // instant "the last 24 hours" ends at.
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 14, 23, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(Clock);

        // The process's event stream, which is what the Live Logs page reads for a scheduler this container
        // runs. A context told of a scheduler somewhere else holds a keyed one beside it, which is what the
        // page reads instead for that name.
        if (registerEventSource)
        {
            Services.AddSingleton<ISchedulerEventSource>(Events);
        }

        if (remoteEventSourceFor is not null)
        {
            RemoteEvents = new FakeSchedulerEventSource();
            Services.AddKeyedSingleton<ISchedulerEventSource>(remoteEventSourceFor, RemoteEvents);
        }

        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        Services.AddSingleton(A.Fake<IHttpContextAccessor>());
        Services.AddSingleton<IAuthorizationService>(AuthorizationService);
        Services.AddSingleton<AuthenticationStateProvider>(AuthenticationState);
        Services.AddSingleton<SchedulerState>();
        Services.AddSingleton<SchedulerAuthorization>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<DashboardActionLogService>();
        Services.AddSingleton<DashboardActionLog>();

        // The reads whose answer is a shape rather than a number: a page and a limits snapshot. Every
        // test that overrides one of these does so with its own A.CallTo, which wins; what these are for
        // is the tests that are about something else, so that a component reading one gets an honest
        // empty answer rather than whatever a dummy would have invented.
        A.CallTo(() => Api.GetExecutionLimits(A<string>._, A<CancellationToken>._))
            .Returns(new ExecutionLimitsDto([]));
        A.CallTo(() => Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => Api.QueryTriggerGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroupDto>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => Api.QueryJobGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobGroupDto>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardHistoryEntry>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => Api.QueryMisfires(A<DashboardMisfireQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardMisfireEntry>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => Api.CountMisfires(A<string>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(0);

        // The pauses that carry a reason answer through the reasonless members, as the default interface
        // members do for a data source written against 4.2, so a case about what a pause did still sees
        // the call it always saw. A case about the reason asserts on the *With member itself.
        A.CallTo(() => Api.PauseTriggerWith(A<string>._, A<TriggerKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => Api.PauseJobWith(A<string>._, A<JobKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => Api.PauseAllWith(A<string>._, A<PauseDetails>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => Api.PauseTriggersWith(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => Api.PauseJobsWith(A<string>._, A<IReadOnlyCollection<JobKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .CallsBaseMethod();

        // A data source that keeps no per-job run status, as the default interface members report, so the
        // pages that show one leave it out unless a case is about it.
        A.CallTo(() => Api.GetJobRunStatus(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => Api.GetJobRunStatuses(A<string>._, A<IReadOnlyCollection<JobKeyDto>>._, A<CancellationToken>._))
            .CallsBaseMethod();

        // Nor counts runs over time, so the pages leave the chart out and the History page's stat cards stay
        // over the page, unless a case is about the chart.
        A.CallTo(() => Api.QueryExecutionStatistics(A<ExecutionStatisticsQuery>._, A<CancellationToken>._))
            .CallsBaseMethod();

        // And no pause recorded anything, rather than the dummy record a fake would invent.
        A.CallTo(() => Api.GetTriggerPause(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(new ValueTask<PauseInfo?>((PauseInfo?) null));
        A.CallTo(() => Api.GetJobGroupPause(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(new ValueTask<PauseInfo?>((PauseInfo?) null));

        SchedulerState = Services.GetRequiredService<SchedulerState>();
        SchedulerState.SelectedTimeZoneId = TimeZoneInfo.Utc.Id;
        Toasts = Services.GetRequiredService<ToastService>();
        ActionLog = Services.GetRequiredService<DashboardActionLog>();
    }

    public IQuartzApiClient Api { get; }

    /// <summary>The pages' clock: 14:23 UTC on 5 October 2026 until a case moves it.</summary>
    public FakeTimeProvider Clock { get; }

    public TestSchedulerAuthorizationService AuthorizationService { get; }

    public TestAuthenticationStateProvider AuthenticationState { get; }

    /// <summary>
    /// The stream this process's schedulers publish into, which a test pushes events onto.
    /// </summary>
    public FakeSchedulerEventSource Events { get; }

    /// <summary>
    /// The stream of the scheduler this context was told runs in another process, or <see langword="null" />
    /// when it was told of none.
    /// </summary>
    /// <remarks>
    /// Which source answers is the thing several tests are about: a page that read the process's own stream
    /// for a remote scheduler would be showing another scheduler's events.
    /// </remarks>
    public FakeSchedulerEventSource? RemoteEvents { get; }

    public QuartzDashboardOptions Options { get; }

    public SchedulerState SchedulerState { get; }

    public ToastService Toasts { get; }

    public DashboardActionLog ActionLog { get; }

    /// <summary>
    /// Puts the browser on <paramref name="relativeUri" /> before a page is rendered, which is the only
    /// way to supply a <c>[SupplyParameterFromQuery]</c> parameter — the pages read their filters and
    /// their page number from the query string, so that a filtered listing is a link someone can share.
    /// </summary>
    public DashboardComponentContext Navigate(string relativeUri)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUri);
        return this;
    }

    /// <summary>
    /// Where the browser is now, which is what a page writing its filters into the query string moves.
    /// </summary>
    public string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    /// <summary>
    /// Turns the per-scheduler policy on and says which schedulers the visitor passes for. Every other
    /// scheduler is one they may not see.
    /// </summary>
    public DashboardComponentContext WithSchedulerPolicy(params string[] allowedSchedulers)
    {
        Options.SchedulerAuthorizationPolicy = SchedulerPolicyName;
        foreach (string schedulerName in allowedSchedulers)
        {
            AuthorizationService.Allowed.Add(schedulerName);
        }

        return this;
    }

    /// <summary>
    /// The policy name <see cref="WithSchedulerPolicy" /> configures, which is what the dashboard is
    /// expected to pass to <c>IAuthorizationService</c>.
    /// </summary>
    public const string SchedulerPolicyName = "SchedulerOwner";

    /// <summary>
    /// Points the pages at a scheduler that exists and is running, which is what all but the
    /// no-scheduler-selected tests want.
    /// </summary>
    /// <param name="origin">
    /// Where the scheduler is. <see cref="SchedulerOrigin.Remote" /> is one in another process, which
    /// several pages say something about: its live events are not streamed here, and its history is kept
    /// where it runs.
    /// </param>
    public DashboardComponentContext WithScheduler(
        string schedulerName = TestData.SchedulerName,
        SchedulerStatus status = SchedulerStatus.Running,
        bool clustered = false,
        bool persistent = false,
        SchedulerOrigin origin = SchedulerOrigin.Container)
    {
        A.CallTo(() => Api.GetSchedulers(A<CancellationToken>._))
            .Returns(new List<SchedulerHeaderDto> { TestData.Dashboard.SchedulerHeader(schedulerName, status, origin) });
        A.CallTo(() => Api.GetScheduler(schedulerName, A<CancellationToken>._))
            .Returns(TestData.Dashboard.SchedulerDetail(status, schedulerName, clustered, persistent));

        SchedulerState.ActiveSchedulerName = schedulerName;
        return this;
    }
}
