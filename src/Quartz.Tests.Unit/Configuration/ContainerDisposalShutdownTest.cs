#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Collections.Concurrent;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Disposing an application's own container shuts down every scheduler it built, with no host to do it.
/// </summary>
/// <remarks>
/// <see cref="StandaloneSchedulerFactoryDisposalTest" /> covers the container the standalone builder
/// owns. Here the application builds and disposes the container itself. Before, only a container that
/// had injected <see cref="IScheduler" /> and used it shut the scheduler down, so a scheduler built
/// through <see cref="ISchedulerFactory.GetScheduler" /> went on firing into the disposed container.
/// </remarks>
public sealed class ContainerDisposalShutdownTest
{
    private static readonly ConcurrentDictionary<string, int> fires = new();

    [TestCase(null)]
    [TestCase("reporting")]
    public async Task DisposingTheContainerShutsDownASchedulerBuiltThroughItsFactory(string schedulerName)
    {
        string runId = $"{nameof(DisposingTheContainerShutsDownASchedulerBuiltThroughItsFactory)}-{schedulerName ?? "default"}";
        ServiceProvider provider = Container(runId, schedulerName);
        IScheduler scheduler = await StartFiring(provider, runId, schedulerName);

        await provider.DisposeAsync();

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown, "the container built the scheduler, so disposing it ends the scheduler's lifetime");
        await AssertNoLongerFiring(runId);
    }

    [Test]
    public async Task DisposingTheContainerSynchronouslyShutsTheSchedulerDownToo()
    {
        string runId = nameof(DisposingTheContainerSynchronouslyShutsTheSchedulerDownToo);
        ServiceProvider provider = Container(runId);
        IScheduler scheduler = await StartFiring(provider, runId);

        Action dispose = provider.Dispose;

        dispose.Should().NotThrow("what shuts the scheduler down is disposable both ways, so it adds nothing that only IAsyncDisposable would break");
        scheduler.Status.Should().Be(SchedulerStatus.Shutdown);
        await AssertNoLongerFiring(runId);
    }

    [Test]
    public async Task TheSchedulerIsShutDownBeforeTheContainerDisposesWhatItWasBuiltFrom()
    {
        DisposalOrder order = new();
        ServiceCollection services = new();
        services.AddSingleton(order);
        services.AddQuartz(q => q
            .ConfigureScheduler(options => options.InstanceName = nameof(TheSchedulerIsShutDownBeforeTheContainerDisposesWhatItWasBuiltFrom))
            .UseJobFactory<OrderRecordingJobFactory>()
            .UseInMemoryStore()
            .AddSchedulerListener(new ShutdownRecorder(order)));

        ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        await provider.DisposeAsync();

        order.Events.Should().Equal(
            ["scheduler shut down", "job factory disposed"],
            "the container disposes in the reverse order of creation, and the shutdown is resolved after every part of the scheduler, its job factory among them");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AFailedShutdownIsLoggedAndTheRestOfTheContainerIsStillDisposed(bool throughTheInjectedScheduler)
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.Shutdown(A<CancellationToken>._))
            .Throws(new InvalidOperationException("the store refused to shut down"));

        RecordingLoggerProvider log = new();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(log));
        services.AddSingleton<DisposalProbe>();
        services.AddQuartz(q => q
            .ConfigureScheduler(options => options.InstanceName = nameof(AFailedShutdownIsLoggedAndTheRestOfTheContainerIsStillDisposed))
            .UseJobStore(store));

        ServiceProvider provider = services.BuildServiceProvider();
        DisposalProbe probe = provider.GetRequiredService<DisposalProbe>();
        await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        if (throughTheInjectedScheduler)
        {
            // Resolved and used after the build, as an injected IScheduler usually is, so the container
            // disposes it before the scheduler's own shutdown, and it is the one that meets the failure.
            await provider.GetRequiredService<IScheduler>().GetJobGroupNames();
        }

        Func<Task> dispose = async () => await provider.DisposeAsync();

        await dispose.Should().NotThrowAsync("the container stops at the first disposal that throws, which would leave the application's services undisposed");
        probe.Disposed.Should().BeTrue("the probe was created before the scheduler, so it is disposed after the failed shutdown");
        log.Entries.Should().ContainSingle(entry => entry.EventId.Id == 4033, "a shutdown that failed is reported, not swallowed")
            .Which.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Test]
    public async Task DisposingAContainerThatNeverBuiltASchedulerBuildsNone()
    {
        bool listenerConstructed = false;
        ServiceCollection services = new();
        services.AddQuartz(q => q
            .ConfigureScheduler(options => options.InstanceName = nameof(DisposingAContainerThatNeverBuiltASchedulerBuildsNone))
            .UseInMemoryStore()
            .AddSchedulerListener(_ =>
            {
                listenerConstructed = true;
                return new ShutdownRecorder(new DisposalOrder());
            }));

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISchedulerFactory>();

        await provider.DisposeAsync();

        listenerConstructed.Should().BeFalse("nothing asked for a scheduler, so disposal must not build one just to tear it down");
    }

    [Test]
    public async Task AFiringThatEndsWhileDisposalShutsTheSchedulerDownIsStillRecorded()
    {
        DisposalOrder order = new();
        IExecutionHistoryStore history = A.Fake<IExecutionHistoryStore>(options => options.Implements<IAsyncDisposable>());
        A.CallTo(() => history.AddExecution(A<ExecutionHistoryEntry>._, A<CancellationToken>._))
            .Invokes((ExecutionHistoryEntry entry, CancellationToken _) => order.Record("recorded " + entry.JobName));
        A.CallTo(() => ((IAsyncDisposable) history).DisposeAsync())
            .Invokes(() => order.Record("history store disposed"));

        ServiceCollection services = new();

        // Through a factory, so the container creates the store and disposes it; an instance it would leave alone.
        services.AddSingleton(_ => history);
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q => q
            .ConfigureScheduler(options => options.InstanceName = nameof(AFiringThatEndsWhileDisposalShutsTheSchedulerDownIsStillRecorded))
            .UseJobFactory<SimpleJobFactory>()
            .UseInMemoryStore()
            .AddSchedulerListener(new ShutdownRecorder(order)));

        ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        // A firing that ends before the disposal, at which a store resolved per event used to be created:
        // after the scheduler's shutdown handle, and so disposed before it.
        await scheduler.ScheduleJob(JobBuilder.Create<GatedJob>().WithIdentity("before").Build(), TriggerBuilder.Create().StartNow().Build());
        await WaitUntil(() => order.Events.Contains("recorded before"), "the first firing is recorded");

        // And one still running when the disposal begins, which ends during the shutdown it runs.
        await scheduler.ScheduleJob(
            JobBuilder.Create<GatedJob>().WithIdentity("during").UsingJobData(GatedJob.GatedKey, true).Build(),
            TriggerBuilder.Create().StartNow().Build());
        await GatedJob.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        ValueTask disposal = provider.DisposeAsync();
        GatedJob.Release.TrySetResult();
        await disposal;

        order.Events.Should().Equal(
            ["recorded before", "recorded during", "scheduler shut down", "history store disposed"],
            "the plugin resolves the store while the scheduler is built, so the container shuts the scheduler down before it disposes the store, and the plugin holds the store rather than asking a container that is being disposed");
    }

    private static ServiceProvider Container(string runId, string schedulerName = null)
    {
        ServiceCollection services = new();

        void Configure(IQuartzBuilder q) => q
            .ConfigureScheduler(options => options.InstanceName = runId)
            // Nothing on the firing path may need the container, or "still firing" stops being observable
            // once the container is disposed: the default job factory builds each job from a scope of it.
            .UseJobFactory<SimpleJobFactory>()
            .UseInMemoryStore();

        if (schedulerName is null)
        {
            services.AddQuartz(Configure);
        }
        else
        {
            services.AddQuartz(schedulerName, Configure);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Hands back a started scheduler whose repeating trigger has fired at least once, so that "it stopped
    /// firing" is a statement about the disposal rather than about a trigger that never ran.
    /// </summary>
    private static async Task<IScheduler> StartFiring(ServiceProvider provider, string runId, string schedulerName = null)
    {
        ISchedulerFactory factory = schedulerName is null
            ? provider.GetRequiredService<ISchedulerFactory>()
            : provider.GetRequiredKeyedService<ISchedulerFactory>(schedulerName);

        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Start();

        await scheduler.ScheduleJob(
            JobBuilder.Create<CountingJob>().WithIdentity(runId).UsingJobData(CountingJob.RunIdKey, runId).Build(),
            TriggerBuilder.Create()
                .WithIdentity(runId)
                .StartNow()
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMilliseconds(50)).RepeatForever())
                .Build());

        for (int attempt = 0; attempt < 400 && FireCount(runId) == 0; attempt++)
        {
            await Task.Delay(50);
        }

        FireCount(runId).Should().BePositive("the trigger has to be firing before disposal can be shown to stop it");
        return scheduler;
    }

    private static async Task AssertNoLongerFiring(string runId)
    {
        // A job already running when the shutdown began still finishes and counts itself, so the count
        // settles before it is pinned.
        await Task.Delay(500);
        int settled = FireCount(runId);

        await Task.Delay(1000);

        FireCount(runId).Should().Be(settled, "the trigger repeats every 50 ms, so a scheduler still running would have fired many times over");
    }

    private static int FireCount(string runId) => fires.TryGetValue(runId, out int count) ? count : 0;

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        for (int attempt = 0; attempt < 600 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        condition().Should().BeTrue(because);
    }

    /// <summary>
    /// Returns at once, or with <see cref="GatedKey" /> in its data waits for <see cref="Release" /> once it has said it started.
    /// </summary>
    public sealed class GatedJob : IJob
    {
        public const string GatedKey = "gated";

        public static readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.MergedJobDataMap.ContainsKey(GatedKey))
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public sealed class CountingJob : IJob
    {
        public const string RunIdKey = "runId";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fires.AddOrUpdate(context.MergedJobDataMap.GetString(RunIdKey)!, 1, static (_, count) => count + 1);
            return default;
        }
    }

    /// <summary>
    /// What happened during the disposal, in order.
    /// </summary>
    private sealed class DisposalOrder
    {
        private readonly ConcurrentQueue<string> events = new();

        public List<string> Events => [.. events];

        public void Record(string what) => events.Enqueue(what);
    }

    /// <summary>
    /// A part of the scheduler the container creates, and so disposes, and one a build resolves after the
    /// scheduler itself.
    /// </summary>
    /// <remarks>
    /// Not a part the shutdown depends on, such as the thread pool: the container creates those while it
    /// creates the shutdown itself, so they come out in the right order wherever the build resolves it.
    /// </remarks>
    private sealed class OrderRecordingJobFactory(DisposalOrder order) : SimpleJobFactory, IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            order.Record("job factory disposed");
            return default;
        }
    }

    private sealed class ShutdownRecorder(DisposalOrder order) : ISchedulerListener
    {
        public ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            order.Record("scheduler shut down");
            return default;
        }
    }

    private sealed class DisposalProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
