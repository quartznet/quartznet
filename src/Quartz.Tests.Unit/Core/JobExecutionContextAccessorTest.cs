#nullable enable

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The firing a piece of code is serving can be read without being handed the execution context — and
/// cannot be read anywhere else.
/// </summary>
/// <remarks>
/// The interesting assertions here are the negative ones. An ambient context that outlives its firing
/// would be worse than no ambient context at all, so the window it is set for is pinned at both ends:
/// nothing outside a firing sees one, and work a job leaves running past the end of its execution sees
/// the firing end rather than a context whose scope has been disposed.
/// </remarks>
public sealed class JobExecutionContextAccessorTest
{
    [Test]
    public async Task TheFiringIsAmbientForItsWholeExecutionAndNowhereElse()
    {
        Recorder recorder = new();

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddQuartz(q =>
        {
            q.AddJobListener<AmbientReadingListener>();
            q.ScheduleJob<AmbientReadingJob>(
                trigger => trigger.WithIdentity("ambient").StartNow(),
                job => job.WithIdentity("ambient"));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IJobExecutionContextAccessor accessor = provider.GetRequiredService<IJobExecutionContextAccessor>();
        accessor.Current.Should().BeNull("nothing is firing on the thread that merely built a container");

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        await scheduler.Start();
        try
        {
            await recorder.Wait(recorder.Finished);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        recorder.AtEntry.Should().BeSameAs(recorder.Context, "the context the job is handed is the one the accessor reports");
        recorder.AfterYield.Should().BeSameAs(recorder.Context, "the value travels with the execution context, so it survives an await");
        recorder.InsideTaskRun.Should().BeSameAs(recorder.Context, "Task.Run captures the execution context, so work dispatched from a job stays inside its firing");
        recorder.InListener.Should().BeSameAs(recorder.Context, "the firing is ambient before the listeners are notified, not only once Execute is called");

        accessor.Current.Should().BeNull("the scheduling thread was never inside the firing");

        // Only now, with the execution long over, is the work the job left running allowed to look.
        recorder.ReleaseDetached.TrySetResult();
        (await recorder.Detached!).Should().BeNull(
            "an accessor that went on reporting a finished firing would hand out a context whose scope "
            + "has been disposed and whose cancellation handle is gone");
    }

    [Test]
    public async Task TwoFiringsAtOnceEachReadTheirOwn()
    {
        Recorder recorder = new();

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddQuartz(q =>
        {
            q.AddJob<InterleavingJob>(job => job.WithIdentity("one"));
            q.AddJob<InterleavingJob>(job => job.WithIdentity("two"));
            q.AddTrigger<InterleavingJob>(trigger => trigger.WithIdentity("one").ForJob("one").StartNow());
            q.AddTrigger<InterleavingJob>(trigger => trigger.WithIdentity("two").ForJob("two").StartNow());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        await scheduler.Start();
        try
        {
            await recorder.Wait(recorder.Finished);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        recorder.Interleaved.Should().HaveCount(2);
        foreach ((IJobExecutionContext own, IJobExecutionContext? read) in recorder.Interleaved)
        {
            read.Should().BeSameAs(own,
                "both jobs were held at the same point on purpose, so a value carried by the thread "
                + "rather than by the flow would have shown up as one reading the other's");
        }
    }

    public sealed class Recorder
    {
        private readonly Lock gate = new();
        private readonly List<(IJobExecutionContext Own, IJobExecutionContext? Read)> interleaved = [];
        private int arrived;

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BothArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDetached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IJobExecutionContext? Context { get; set; }

        public IJobExecutionContext? AtEntry { get; set; }

        public IJobExecutionContext? AfterYield { get; set; }

        public IJobExecutionContext? InsideTaskRun { get; set; }

        public IJobExecutionContext? InListener { get; set; }

        public Task<IJobExecutionContext?>? Detached { get; set; }

        public IReadOnlyList<(IJobExecutionContext Own, IJobExecutionContext? Read)> Interleaved
        {
            get
            {
                lock (gate)
                {
                    return [.. interleaved];
                }
            }
        }

        /// <summary>
        /// Holds each firing until both have arrived, so the two executions genuinely overlap rather
        /// than happening to run one after the other.
        /// </summary>
        public Task Arrive()
        {
            lock (gate)
            {
                if (++arrived == 2)
                {
                    BothArrived.TrySetResult();
                }
            }

            return BothArrived.Task;
        }

        public void RecordInterleaved(IJobExecutionContext own, IJobExecutionContext? read)
        {
            lock (gate)
            {
                interleaved.Add((own, read));
                if (interleaved.Count == 2)
                {
                    Finished.TrySetResult();
                }
            }
        }

        public async Task Wait(TaskCompletionSource source)
        {
            Task finished = await Task.WhenAny(source.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(source.Task, "the scheduled jobs should have run");
        }
    }

    public sealed class AmbientReadingJob : IJob
    {
        private readonly IJobExecutionContextAccessor accessor;
        private readonly Recorder recorder;

        public AmbientReadingJob(IJobExecutionContextAccessor accessor, Recorder recorder)
        {
            this.accessor = accessor;
            this.recorder = recorder;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.Context = context;
            recorder.AtEntry = accessor.Current;

            await Task.Yield();
            recorder.AfterYield = accessor.Current;

            recorder.InsideTaskRun = await Task.Run(() => accessor.Current, cancellationToken);

            // Left running deliberately, and released only once the firing is over.
            recorder.Detached = Task.Run(async () =>
            {
                await recorder.ReleaseDetached.Task.ConfigureAwait(false);
                return accessor.Current;
            }, CancellationToken.None);

            recorder.Finished.TrySetResult();
        }
    }

    public sealed class InterleavingJob : IJob
    {
        private readonly IJobExecutionContextAccessor accessor;
        private readonly Recorder recorder;

        public InterleavingJob(IJobExecutionContextAccessor accessor, Recorder recorder)
        {
            this.accessor = accessor;
            this.recorder = recorder;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            await recorder.Arrive().ConfigureAwait(false);
            recorder.RecordInterleaved(context, accessor.Current);
        }
    }

    /// <summary>
    /// Reads the accessor from the listener notification that runs before the job does.
    /// </summary>
    public sealed class AmbientReadingListener : IJobListener
    {
        private readonly IJobExecutionContextAccessor accessor;
        private readonly Recorder recorder;

        public AmbientReadingListener(IJobExecutionContextAccessor accessor, Recorder recorder)
        {
            this.accessor = accessor;
            this.recorder = recorder;
        }

        public string Name => "ambient-reading";

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.InListener = accessor.Current;
            return default;
        }

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default) => default;
    }
}
