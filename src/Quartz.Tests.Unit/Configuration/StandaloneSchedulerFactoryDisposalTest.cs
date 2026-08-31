using System.Collections.Concurrent;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Disposing the factory <see cref="QuartzSchedulerBuilder.Build"/> hands back has to shut the scheduler
/// down, on both of the paths a container can take through it.
/// </summary>
/// <remarks>
/// The factory owns the container, and the container is the only thing holding the scheduler, so
/// disposal is the whole of a standalone scheduler's teardown story. Both branches used to be wrong, and
/// wrong in opposite ways: with nothing having resolved <see cref="IScheduler"/> the container had
/// nothing to dispose and the scheduler kept firing, and with something having resolved it the container
/// held an <see cref="IAsyncDisposable"/>-only singleton and synchronous disposal threw.
/// </remarks>
[NonParallelizable]
public class StandaloneSchedulerFactoryDisposalTest
{
    private static readonly ConcurrentDictionary<string, int> fires = new();

    public sealed class CountingJob : IJob
    {
        public const string RunIdKey = "runId";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            string runId = context.MergedJobDataMap.GetString(RunIdKey)!;
            fires.AddOrUpdate(runId, 1, static (_, count) => count + 1);
            return default;
        }
    }

    /// <summary>
    /// A listener that wants the scheduler, which is the ordinary way a container ends up holding the
    /// <see cref="IScheduler"/> singleton.
    /// </summary>
    public sealed class SchedulerAwareListener : ISchedulerListener
    {
        public SchedulerAwareListener(IScheduler scheduler)
        {
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }
    }

    /// <summary>
    /// A listener whose construction says a scheduler was built, since the container resolves the
    /// listeners only while building one.
    /// </summary>
    public sealed class ProbeListener : ISchedulerListener
    {
    }

    [Test]
    public async Task DisposeAsyncShutsTheSchedulerDown()
    {
        string runId = nameof(DisposeAsyncShutsTheSchedulerDown);
        StandaloneSchedulerFactory factory = Builder(runId).Build();
        IScheduler scheduler = await StartFiring(factory, runId);

        await factory.DisposeAsync();

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown, "the factory owns the scheduler's lifetime, so disposing it has to end that lifetime");
        await AssertNoLongerFiring(runId);
    }

    [Test]
    public async Task DisposeShutsTheSchedulerDown()
    {
        string runId = nameof(DisposeShutsTheSchedulerDown);
        StandaloneSchedulerFactory factory = Builder(runId).Build();
        IScheduler scheduler = await StartFiring(factory, runId);

        factory.Dispose();

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown, "a caller that wrote 'using' rather than 'await using' still disposed the factory");
        await AssertNoLongerFiring(runId);
    }

    [Test]
    public async Task DisposeAsyncShutsTheSchedulerDownWhenTheContainerHandedOutItsScheduler()
    {
        string runId = nameof(DisposeAsyncShutsTheSchedulerDownWhenTheContainerHandedOutItsScheduler);
        StandaloneSchedulerFactory factory = BuilderWithSchedulerAwareListener(runId).Build();
        IScheduler scheduler = await StartFiring(factory, runId);

        await factory.DisposeAsync();

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown, "the scheduler the container handed to a listener is the same one the factory built");
        await AssertNoLongerFiring(runId);
    }

    [Test]
    public async Task DisposeShutsTheSchedulerDownWhenTheContainerHandedOutItsScheduler()
    {
        string runId = nameof(DisposeShutsTheSchedulerDownWhenTheContainerHandedOutItsScheduler);
        StandaloneSchedulerFactory factory = BuilderWithSchedulerAwareListener(runId).Build();
        IScheduler scheduler = await StartFiring(factory, runId);

        Action act = factory.Dispose;

        act.Should().NotThrow("a container holding the IAsyncDisposable-only scheduler handle used to make synchronous disposal throw");
        scheduler.Status.Should().Be(SchedulerStatus.Shutdown);
        await AssertNoLongerFiring(runId);
    }

    [Test]
    public async Task DisposingTwiceIsHarmless()
    {
        string runId = nameof(DisposingTwiceIsHarmless);
        StandaloneSchedulerFactory factory = Builder(runId).Build();
        IScheduler scheduler = await StartFiring(factory, runId);

        await factory.DisposeAsync();

        Func<Task> again = async () => await factory.DisposeAsync();
        await again.Should().NotThrowAsync("disposal has to be idempotent, and disposing twice is a normal accident");

        Action synchronously = factory.Dispose;
        synchronously.Should().NotThrow();

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown);
    }

    [Test]
    public async Task DisposingAfterAnExplicitShutdownIsHarmless()
    {
        string runId = nameof(DisposingAfterAnExplicitShutdownIsHarmless);
        StandaloneSchedulerFactory factory = Builder(runId).Build();
        IScheduler scheduler = await StartFiring(factory, runId);

        await scheduler.Shutdown(waitForJobsToComplete: true);

        Func<Task> act = async () => await factory.DisposeAsync();

        await act.Should().NotThrowAsync("a caller that wants to wait for its jobs shuts down itself, and then disposes");
        scheduler.Status.Should().Be(SchedulerStatus.Shutdown);
    }

    [Test]
    public async Task AFailedShutdownIsReportedAndTheContainerIsDisposedAnyway()
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.Shutdown(A<CancellationToken>._))
            .Throws(new InvalidOperationException("the store refused to shut down"));

        StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = nameof(AFailedShutdownIsReportedAndTheContainerIsDisposedAnyway))
                .UseJobStore(store))
            .Build();

        await factory.GetScheduler();

        Func<Task> dispose = async () => await factory.DisposeAsync();

        (await dispose.Should().ThrowAsync<AggregateException>("a shutdown that failed is not a shutdown, and swallowing it would hide it"))
            .WithInnerException<InvalidOperationException>();

        Func<Task> afterwards = async () => await factory.GetScheduler();
        await afterwards.Should().ThrowAsync<ObjectDisposedException>(
            "the container is the factory's to release, and holding on to it after a failed shutdown would leak the thread pool and the store too");
    }

    [Test]
    public async Task DisposingAFactoryThatNeverBuiltASchedulerBuildsNothing()
    {
        bool listenerConstructed = false;
        StandaloneSchedulerFactory factory = Builder(
                nameof(DisposingAFactoryThatNeverBuiltASchedulerBuildsNothing),
                q => q.AddSchedulerListener(_ =>
                {
                    listenerConstructed = true;
                    return new ProbeListener();
                }))
            .Build();

        Func<Task> act = async () => await factory.DisposeAsync();

        await act.Should().NotThrowAsync();
        listenerConstructed.Should().BeFalse(
            "nothing asked for a scheduler, so disposal must not build one just to tear it down");
    }

    private static QuartzSchedulerBuilder Builder(string instanceName, Action<IQuartzBuilder> also = null)
    {
        return QuartzSchedulerBuilder.Create(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = instanceName)
                .UseDefaultThreadPool(maxConcurrency: 2)
                // Nothing on the firing path may need the container, or "still firing" stops being
                // observable the moment the container is disposed — which is the state under test. The
                // default factory builds each job from a dependency injection scope, so a scheduler left
                // running by a broken disposal would fail every firing rather than count it, and the test
                // would pass for the wrong reason.
                .UseJobFactory<SimpleJobFactory>()
                .UseInMemoryStore();

            also?.Invoke(q);
        });
    }

    private static QuartzSchedulerBuilder BuilderWithSchedulerAwareListener(string instanceName)
    {
        return Builder(instanceName, q => q
            .AddSchedulerListener(provider => new SchedulerAwareListener(provider.GetRequiredService<IScheduler>())));
    }

    /// <summary>
    /// Hands back a started scheduler whose repeating trigger has fired at least once, so that "it
    /// stopped firing" is a statement about the disposal rather than about a trigger that never ran.
    /// </summary>
    private static async Task<IScheduler> StartFiring(StandaloneSchedulerFactory factory, string runId)
    {
        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Start();

        IJobDetail job = JobBuilder.Create<CountingJob>()
            .WithIdentity(runId)
            .UsingJobData(CountingJob.RunIdKey, runId)
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(runId)
            .StartNow()
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMilliseconds(50)).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        for (int attempt = 0; attempt < 400 && FireCount(runId) == 0; attempt++)
        {
            await Task.Delay(50);
        }

        FireCount(runId).Should().BePositive("the trigger has to be firing before disposal can be shown to stop it");
        return scheduler;
    }

    private static async Task AssertNoLongerFiring(string runId)
    {
        // A job already running when the shutdown began still gets to finish and count itself, so the
        // count is allowed to settle before it is pinned. Generously, because a loaded build agent is
        // the one machine where a straggler takes its time.
        await Task.Delay(500);
        int settled = FireCount(runId);

        await Task.Delay(1000);

        FireCount(runId).Should().Be(settled,
            "the trigger repeats every 50 ms, so a scheduler still running would have fired many times over");
    }

    private static int FireCount(string runId)
    {
        return fires.GetValueOrDefault(runId);
    }
}
