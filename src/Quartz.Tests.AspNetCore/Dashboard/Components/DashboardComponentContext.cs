using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Services;
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
    public DashboardComponentContext(Action<QuartzDashboardOptions>? configure = null)
    {
        Options = new QuartzDashboardOptions();
        configure?.Invoke(Options);

        Api = A.Fake<IQuartzApiClient>();
        LiveConnections = new FakeDashboardLiveConnectionFactory();

        // KeyBadge copies a key to the clipboard through JS interop, so a strict runtime would fail
        // every page that lists one for a call no test is about.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Nothing is authorized against until a test sets QuartzDashboardOptions.SchedulerAuthorizationPolicy:
        // with it unset, SchedulerAuthorization answers "yes" without asking either of these, which is what
        // keeps every test that is about something else unaffected.
        AuthorizationService = new TestSchedulerAuthorizationService();
        AuthenticationState = new TestAuthenticationStateProvider();

        Services.AddSingleton(Api);
        Services.AddSingleton<IDashboardLiveConnectionFactory>(LiveConnections);
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        Services.AddSingleton(A.Fake<IHttpContextAccessor>());
        Services.AddSingleton<IAuthorizationService>(AuthorizationService);
        Services.AddSingleton<AuthenticationStateProvider>(AuthenticationState);
        Services.AddSingleton<SchedulerState>();
        Services.AddSingleton<SchedulerAuthorization>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<DashboardActionLogService>();

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

        SchedulerState = Services.GetRequiredService<SchedulerState>();
        SchedulerState.SelectedTimeZoneId = TimeZoneInfo.Utc.Id;
        Toasts = Services.GetRequiredService<ToastService>();
        ActionLog = Services.GetRequiredService<DashboardActionLogService>();
    }

    public IQuartzApiClient Api { get; }

    public TestSchedulerAuthorizationService AuthorizationService { get; }

    public TestAuthenticationStateProvider AuthenticationState { get; }

    public FakeDashboardLiveConnectionFactory LiveConnections { get; }

    public QuartzDashboardOptions Options { get; }

    public SchedulerState SchedulerState { get; }

    public ToastService Toasts { get; }

    public DashboardActionLogService ActionLog { get; }

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
    public DashboardComponentContext WithScheduler(
        string schedulerName = TestData.SchedulerName,
        SchedulerStatus status = SchedulerStatus.Running,
        bool clustered = false,
        bool persistent = false)
    {
        A.CallTo(() => Api.GetSchedulers(A<CancellationToken>._))
            .Returns(new List<SchedulerHeaderDto> { TestData.Dashboard.SchedulerHeader(schedulerName, status) });
        A.CallTo(() => Api.GetScheduler(schedulerName, A<CancellationToken>._))
            .Returns(TestData.Dashboard.SchedulerDetail(status, schedulerName, clustered, persistent));

        SchedulerState.ActiveSchedulerName = schedulerName;
        return this;
    }
}
