using System.Reflection;
using System.Text.Json;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// A bUnit context carrying what the dashboard's pages inject, with <see cref="SchedulerName" /> the active
/// scheduler, so a page can be rendered by naming it.
/// </summary>
/// <remarks>
/// <para>
/// The data source is a stand-in <see cref="IQuartzApiClient" />: a scheduler with no jobs and no triggers,
/// running the firings a test puts in <see cref="Executing" />. <see cref="Api" /> is a fake wrapping it, so
/// a test can answer or fail one call of its own and ask which calls were made; a call no test answers goes
/// to the stand-in. The rest are the real services, because they are the ones the pages talk to and they
/// are cheap.
/// </para>
/// <para>
/// The time zone is pinned to UTC: <see cref="SchedulerState" /> formats every timestamp in the selected
/// zone, which otherwise is the machine's and makes rendered text machine-dependent. The clock is a fake
/// one that a test moves, which is what the Overview's and Execution History's once-a-second refreshes
/// tick on.
/// </para>
/// </remarks>
internal sealed class DashboardComponentContext : BunitContext
{
    public const string SchedulerName = "acme";

    /// <summary>When every firing these tests describe fired: before the clock's 14:23.</summary>
    public static readonly DateTimeOffset FiredAt = new(2026, 10, 5, 14, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions historySerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DashboardComponentContext()
    {
        IQuartzApiClient api = DispatchProxy.Create<IQuartzApiClient, EmptySchedulerApiClient>();
        Executing = ((EmptySchedulerApiClient) (object) api).Executing;
        Api = A.Fake<IQuartzApiClient>(options => options.Wrapping(api));
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 14, 23, 0, TimeSpan.Zero));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Api);
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddSingleton(Options.Create(new QuartzDashboardOptions()));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<SchedulerState>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<DashboardActionLogService>();

        SchedulerState = Services.GetRequiredService<SchedulerState>();
        SchedulerState.SelectedTimeZoneId = TimeZoneInfo.Utc.Id;
        SchedulerState.ActiveSchedulerName = SchedulerName;
        Toasts = Services.GetRequiredService<ToastService>();
        ActionLog = Services.GetRequiredService<DashboardActionLogService>();
    }

    /// <summary>
    /// The data source the pages talk to: the stand-in scheduler, unless a test answers a call itself.
    /// </summary>
    public IQuartzApiClient Api { get; }

    /// <summary>The pages' clock: 14:23 UTC on 5 October 2026 until a test moves it.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>
    /// The firings the scheduler reports as executing. Set before the page is rendered.
    /// </summary>
    public List<CurrentlyExecutingJobDto> Executing { get; }

    public SchedulerState SchedulerState { get; }

    public ToastService Toasts { get; }

    public DashboardActionLogService ActionLog { get; }

    /// <summary>
    /// The keys of <paramref name="count" /> jobs in <paramref name="group" />, named <c>job-1</c> onwards.
    /// </summary>
    public static List<JobKeyDto> JobKeys(string group, int count)
    {
        return [.. Enumerable.Range(1, count).Select(index => new JobKeyDto(group, "job-" + index))];
    }

    /// <summary>
    /// The keys of <paramref name="count" /> triggers in <paramref name="group" />, named <c>trigger-1</c> onwards.
    /// </summary>
    public static List<TriggerKeyDto> TriggerKeys(string group, int count)
    {
        return [.. Enumerable.Range(1, count).Select(index => new TriggerKeyDto(group, "trigger-" + index))];
    }

    /// <summary>
    /// A page of execution history in the shape the in-process client answers with, all of it on one page.
    /// </summary>
    public static JobHistoryPageDto HistoryPage(IReadOnlyCollection<HistoryEntry> entries)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(
            new HistoryPagePayload(Page: 1, PageSize: 25, TotalCount: entries.Count, Entries: entries),
            historySerializerOptions);
        return new JobHistoryPageDto(payload);
    }

    /// <summary>
    /// Answers the reads the pages make of a scheduler with no jobs and no triggers, and fails any other call,
    /// so a test that starts making one says so.
    /// </summary>
    /// <remarks>
    /// A proxy rather than a class, because the interface has some thirty members and the pages read a
    /// handful. A job group is not paused and a trigger's state is Normal, for the jobs and triggers a test
    /// lists through <see cref="Api" />.
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
                nameof(IQuartzApiClient.IsJobGroupPaused) => Task.FromResult(false),
                nameof(IQuartzApiClient.GetTriggerState) => Task.FromResult("Normal"),
                nameof(IQuartzApiClient.GetJobTriggers) => Task.FromResult(new List<TriggerHeaderDto>()),
                _ => throw new NotSupportedException($"No test here expects the page to call {targetMethod?.Name}."),
            };
        }
    }

    private sealed record HistoryPagePayload(int Page, int PageSize, int TotalCount, IReadOnlyCollection<HistoryEntry> Entries);
}

/// <summary>
/// One row of execution history, written with the names the in-process client writes it with.
/// </summary>
internal sealed record HistoryEntry(
    string SchedulerName,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    long DurationMs,
    bool Succeeded,
    string? ExceptionMessage);
