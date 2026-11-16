using System.Reflection;

using Bunit;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// A bUnit context carrying what the Overview, Currently Executing and the Action Log inject, with
/// <see cref="SchedulerName" /> the active scheduler, so one of those pages can be rendered by naming it.
/// </summary>
/// <remarks>
/// <para>
/// The data source is a stand-in <see cref="IQuartzApiClient" />: a scheduler with no jobs and no triggers,
/// running the firings a test puts in <see cref="Executing" />. The rest are the real services, because they
/// are the ones the pages talk to and they are cheap.
/// </para>
/// <para>
/// The time zone is pinned to UTC: <see cref="SchedulerState" /> formats every timestamp in the selected
/// zone, which otherwise is the machine's and makes rendered text machine-dependent.
/// </para>
/// </remarks>
internal sealed class DashboardComponentContext : BunitContext
{
    public const string SchedulerName = "acme";

    public DashboardComponentContext()
    {
        IQuartzApiClient api = DispatchProxy.Create<IQuartzApiClient, EmptySchedulerApiClient>();
        Executing = ((EmptySchedulerApiClient) (object) api).Executing;

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(api);
        Services.AddSingleton(Options.Create(new QuartzDashboardOptions()));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<SchedulerState>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<DashboardActionLogService>();

        SchedulerState = Services.GetRequiredService<SchedulerState>();
        SchedulerState.SelectedTimeZoneId = TimeZoneInfo.Utc.Id;
        SchedulerState.ActiveSchedulerName = SchedulerName;
        ActionLog = Services.GetRequiredService<DashboardActionLogService>();
    }

    /// <summary>
    /// The firings the scheduler reports as executing. Set before the page is rendered.
    /// </summary>
    public List<CurrentlyExecutingJobDto> Executing { get; }

    public SchedulerState SchedulerState { get; }

    public DashboardActionLogService ActionLog { get; }

    /// <summary>
    /// Answers the reads the Overview and Currently Executing make, and fails any other call, so a test that
    /// starts making one says so.
    /// </summary>
    /// <remarks>
    /// A proxy rather than a class, because the interface has some thirty members and these pages read four.
    /// </remarks>
    public class EmptySchedulerApiClient : DispatchProxy
    {
        public List<CurrentlyExecutingJobDto> Executing { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IQuartzApiClient.GetScheduler) => Task.FromResult(new SchedulerDetailDto(SchedulerName + "-instance", SchedulerName, "Started")),
                nameof(IQuartzApiClient.GetJobKeys) => Task.FromResult(new List<JobKeyDto>()),
                nameof(IQuartzApiClient.GetTriggerKeys) => Task.FromResult(new List<TriggerKeyDto>()),
                nameof(IQuartzApiClient.GetCurrentlyExecutingJobs) => Task.FromResult(new List<CurrentlyExecutingJobDto>(Executing)),
                _ => throw new NotSupportedException($"No test here expects the page to call {targetMethod?.Name}."),
            };
        }
    }
}
