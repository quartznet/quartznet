using FakeItEasy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz;
using Quartz.Dashboard.Services;
using Quartz.Dashboard.Services.Timeline;
using Quartz.Tests.AspNetCore.Dashboard.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Timeline trigger mutations: success, no-op, failure and read-only short-circuit.
/// </summary>
public class TimelineActionsTest
{
    [Test]
    public async Task ResumeReloadsWhenTheApiReportsAChange()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        ToastService toasts = new();
        DashboardActionLogService store = new();
        DashboardActionLog actionLog = CreateActionLog(store);
        int reloads = 0;
        TimelineActions actions = new(
            api,
            toasts,
            actionLog,
            new QuartzDashboardOptions { ReadOnly = false },
            () =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        A.CallTo(() => api.ResumeTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(true);

        await actions.ResumeTriggerAsync("Default", "demo", "trig").ConfigureAwait(false);

        reloads.Should().Be(1);
        toasts.Messages.Should().ContainSingle(m => m.Level == ToastLevel.Success);
        store.GetLatest(1).Should().ContainSingle()
            .Which.Should().Match<DashboardActionLogEntry>(e =>
                e.Action == "ResumeTrigger" && e.Succeeded && e.Target == "demo.trig");
    }

    [Test]
    public async Task ResumeDoesNotReloadWhenTheApiReportsNoChange()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        ToastService toasts = new();
        int reloads = 0;
        TimelineActions actions = new(
            api,
            toasts,
            CreateActionLog(new DashboardActionLogService()),
            new QuartzDashboardOptions { ReadOnly = false },
            () =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        A.CallTo(() => api.ResumeTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(false);

        await actions.ResumeTriggerAsync("Default", "demo", "trig").ConfigureAwait(false);

        reloads.Should().Be(0);
        toasts.Messages.Should().ContainSingle(m => m.Level == ToastLevel.Error);
    }

    [Test]
    public async Task ReadOnlySkipsTheApi()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineActions actions = new(
            api,
            new ToastService(),
            CreateActionLog(new DashboardActionLogService()),
            new QuartzDashboardOptions { ReadOnly = true },
            () => Task.CompletedTask);

        await actions.RunNowAsync("Default", "demo", "job").ConfigureAwait(false);

        A.CallTo(() => api.TriggerJob(
                A<string>._,
                A<JobKeyDto>._,
                A<JobDataMap?>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task PauseJobReloadsWhenTheApiReportsAChange()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        ToastService toasts = new();
        int reloads = 0;
        TimelineActions actions = new(
            api,
            toasts,
            CreateActionLog(new DashboardActionLogService()),
            new QuartzDashboardOptions { ReadOnly = false },
            () =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        A.CallTo(() => api.PauseJobWith(A<string>._, A<JobKeyDto>._, A<PauseDetails?>._, A<CancellationToken>._))
            .Returns(true);

        await actions.PauseJobAsync("Default", "demo", "job", new PauseDetails { Reason = "maintenance" }).ConfigureAwait(false);

        reloads.Should().Be(1);
        toasts.Messages.Should().ContainSingle(m => m.Level == ToastLevel.Success);
        A.CallTo(() => api.PauseJobWith(
                "Default",
                A<JobKeyDto>.That.Matches(key => key.Group == "demo" && key.Name == "job"),
                A<PauseDetails?>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DeleteJobReloadsWhenTheApiReportsAChange()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        int reloads = 0;
        TimelineActions actions = new(
            api,
            new ToastService(),
            CreateActionLog(new DashboardActionLogService()),
            new QuartzDashboardOptions { ReadOnly = false },
            () =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        A.CallTo(() => api.DeleteJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(true);

        await actions.DeleteJobAsync("Default", "demo", "job").ConfigureAwait(false);

        reloads.Should().Be(1);
        A.CallTo(() => api.DeleteJob(
                "Default",
                A<JobKeyDto>.That.Matches(key => key.Group == "demo" && key.Name == "job"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ApiFailureIsLoggedAndToasted()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        ToastService toasts = new();
        DashboardActionLogService store = new();
        TimelineActions actions = new(
            api,
            toasts,
            CreateActionLog(store),
            new QuartzDashboardOptions { ReadOnly = false },
            () => Task.CompletedTask);

        A.CallTo(() => api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("gone"));

        await actions.UnscheduleTriggerAsync("Default", "demo", "trig").ConfigureAwait(false);

        store.GetLatest(1).Should().ContainSingle()
            .Which.Should().Match<DashboardActionLogEntry>(e =>
                e.Action == "UnscheduleTrigger" && !e.Succeeded && e.Message == "gone");
        toasts.Messages.Should().ContainSingle(m => m.Level == ToastLevel.Error && m.Message == "gone");
    }

    private static DashboardActionLog CreateActionLog(DashboardActionLogService store)
    {
        return new DashboardActionLog(
            store,
            NullLogger<DashboardActionLog>.Instance,
            new TestAuthenticationStateProvider(),
            new SchedulerState(A.Fake<IHttpContextAccessor>()));
    }
}
