using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What each of a scheduler's notifications becomes on the event stream.
/// </summary>
/// <remarks>
/// This is the mapping a reader depends on: the kind it filters by and the fields it renders. A
/// notification mapped to the wrong kind, or one that quietly stopped carrying a key, shows up nowhere
/// but here — a live view of an idle scheduler and a live view of a broken publisher look the same.
/// </remarks>
public class SchedulerEventPluginTest
{
    private const string SchedulerName = "TestScheduler";
    private const string NodeId = "node-a";

    private static readonly DateTimeOffset now = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
    private static readonly DateTimeOffset firedAt = new(2026, 2, 3, 4, 5, 0, TimeSpan.Zero);

    private SchedulerEventBroker broker = null!;
    private SchedulerEventPlugin plugin = null!;
    private IScheduler scheduler = null!;

    [SetUp]
    public async Task SetUp()
    {
        broker = new SchedulerEventBroker();
        plugin = new SchedulerEventPlugin(broker, new FakeTimeProvider(now));
        scheduler = Scheduler();

        await plugin.Initialize(SchedulerEventPlugin.PluginName, scheduler);
    }

    [TearDown]
    public ValueTask TearDown() => scheduler.DisposeAsync();

    /// <summary>
    /// The identity every event carries, read once when the plugin was initialized.
    /// </summary>
    [Test]
    public async Task EveryEventNamesTheSchedulerAndTheNodeItHappenedOn()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.JobPaused(scheduler, new JobKey("nightly", "reports"));

        SchedulerEvent published = await watcher.Next();
        published.SchedulerName.Should().Be(SchedulerName);
        published.SchedulerInstanceId.Should().Be(NodeId,
            "a cluster is one scheduler in several processes, so an event that does not say which node raised it cannot be read");
        published.OccurredAtUtc.Should().Be(now, "the instant is this scheduler's own clock, not the reader's");
    }

    [Test]
    public async Task AJobStartingIsJobExecuting()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.JobToBeExecuted(ExecutionContext());

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.JobExecuting);
        published.JobKey.Should().Be(new KeyDto("nightly", "reports"));
        published.TriggerKey.Should().Be(new KeyDto("at-midnight", "reports"));
        published.FireTimeUtc.Should().Be(firedAt);
        published.FireInstanceId.Should().Be("fire-1",
            "a job without DisallowConcurrentExecution has several firings at once, and a reader has to be able to tell them apart");
        published.RunTime.Should().BeNull("a job that has just started has not run for any time yet");
    }

    [Test]
    public async Task AJobFinishingIsJobExecutedAndSaysHowItWent()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.JobWasExecuted(ExecutionContext(), new JobExecutionException("the job threw"));

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.JobExecuted);
        published.RunTime.Should().Be(TimeSpan.FromSeconds(1));
        published.Vetoed.Should().BeFalse();
        published.ExceptionMessage.Should().Be("the job threw");
    }

    /// <summary>
    /// A vetoed execution is reported as one that finished, because a reader watching the job will hear no
    /// completion otherwise.
    /// </summary>
    [Test]
    public async Task AVetoedExecutionIsJobExecutedWithVetoedSet()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.JobExecutionVetoed(ExecutionContext());

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.JobExecuted);
        published.Vetoed.Should().BeTrue("the job never ran, which is a different ending from a job that returned");
        published.ExceptionMessage.Should().BeNull();
    }

    [Test]
    public async Task ATriggerFiringIsTriggerFired()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.TriggerFired(Trigger(), ExecutionContext());

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.TriggerFired);
        published.TriggerKey.Should().Be(new KeyDto("at-midnight", "reports"));
        published.JobKey.Should().Be(new KeyDto("nightly", "reports"));
        published.FireTimeUtc.Should().Be(firedAt);
    }

    [Test]
    public async Task ATriggerCompletingIsTriggerCompleted()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.TriggerComplete(Trigger(), ExecutionContext(), SchedulerInstruction.NoInstruction);

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.TriggerCompleted);
        published.TriggerKey.Should().Be(new KeyDto("at-midnight", "reports"));
        published.JobKey.Should().Be(new KeyDto("nightly", "reports"));
    }

    [Test]
    public async Task AMisfireIsTriggerMisfiredAndCarriesNoFireTime()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.TriggerMisfired(Trigger(), scheduler);

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.TriggerMisfired);
        published.TriggerKey.Should().Be(new KeyDto("at-midnight", "reports"));
        published.JobKey.Should().Be(new KeyDto("nightly", "reports"), "a misfire says which job did not run");
        published.FireTimeUtc.Should().BeNull("there was no firing to time");
    }

    [TestCase(SchedulerStatus.Running)]
    [TestCase(SchedulerStatus.Standby)]
    [TestCase(SchedulerStatus.ShuttingDown)]
    [TestCase(SchedulerStatus.Shutdown)]
    public async Task ALifecycleNotificationIsTheStateTheSchedulerIsNowIn(SchedulerStatus status)
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await (status switch
        {
            SchedulerStatus.Running => plugin.SchedulerStarted(scheduler),
            SchedulerStatus.Standby => plugin.SchedulerInStandbyMode(scheduler),
            SchedulerStatus.ShuttingDown => plugin.SchedulerShuttingDown(scheduler),
            _ => plugin.SchedulerShutdown(scheduler)
        });

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.SchedulerStateChanged);
        published.Status.Should().Be(status,
            "a listener event names the state the scheduler has arrived in, and a live view shows that state");
    }

    [Test]
    public async Task ASchedulerThatIsStartingPublishesNothing()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.SchedulerStarting(scheduler);

        await watcher.NothingArrives(
            "starting is an event rather than a state, and the state it leads to arrives a moment later as Running");
    }

    [Test]
    public async Task APausedTriggerIsTriggerPausedAndAResumedOneIsTriggerResumed()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.TriggerPaused(scheduler, new TriggerKey("at-midnight", "reports"));
        await plugin.TriggerResumed(scheduler, new TriggerKey("at-midnight", "reports"));

        (await watcher.Next()).Kind.Should().Be(SchedulerEventKind.TriggerPaused);

        SchedulerEvent resumed = await watcher.Next();
        resumed.Kind.Should().Be(SchedulerEventKind.TriggerResumed);
        resumed.TriggerKey.Should().Be(new KeyDto("at-midnight", "reports"));
    }

    [Test]
    public async Task APausedJobIsJobPausedAndAResumedOneIsJobResumed()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.JobPaused(scheduler, new JobKey("nightly", "reports"));
        await plugin.JobResumed(scheduler, new JobKey("nightly", "reports"));

        (await watcher.Next()).Kind.Should().Be(SchedulerEventKind.JobPaused);

        SchedulerEvent resumed = await watcher.Next();
        resumed.Kind.Should().Be(SchedulerEventKind.JobResumed);
        resumed.JobKey.Should().Be(new KeyDto("nightly", "reports"));
    }

    /// <summary>
    /// An interrupted firing, named by its fire instance id — which the dashboard's hub never carried at
    /// all.
    /// </summary>
    [Test]
    public async Task AnInterruptedFiringIsJobInterruptedAndNamesTheFiring()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.JobInterrupted(scheduler, new JobKey("nightly", "reports"), "fire-1");

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.JobInterrupted);
        published.JobKey.Should().Be(new KeyDto("nightly", "reports"));
        published.FireInstanceId.Should().Be("fire-1",
            "Interrupt(jobKey) cancels every firing of the job, and each of them is an event of its own");
    }

    /// <summary>
    /// The key-only overload publishes nothing of its own, so an interruption is one event rather than
    /// two.
    /// </summary>
    [Test]
    public async Task TheKeyOnlyInterruptionOverloadPublishesNothing()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await ((ISchedulerListener) plugin).JobInterrupted(scheduler, new JobKey("nightly", "reports"));

        await watcher.NothingArrives(
            "the scheduler raises the overload that names the firing, and the default body of that one calls this");
    }

    [Test]
    public async Task ATriggerParkedInErrorIsTriggerInError()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.TriggerInError(scheduler, new TriggerKey("at-midnight", "reports"));

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.TriggerInError);
        published.TriggerKey.Should().Be(new KeyDto("at-midnight", "reports"));
    }

    [Test]
    public async Task AnErrorTheSchedulerHandledIsSchedulerErrorWithWhatItWasAbout()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        await plugin.SchedulerError(scheduler, new SchedulerErrorContext
        {
            Message = "the job could not be built",
            Exception = new SchedulerException("no such type"),
            TriggerKey = new TriggerKey("at-midnight", "reports"),
            JobKey = new JobKey("nightly", "reports"),
            FireInstanceId = "fire-1"
        });

        SchedulerEvent published = await watcher.Next();
        published.Kind.Should().Be(SchedulerEventKind.SchedulerError);
        published.Message.Should().Be("the job could not be built");
        published.Cause.Should().Be("no such type", "the underlying failure is what an operator reads first");
        published.TriggerKey.Should().Be(new KeyDto("at-midnight", "reports"));
        published.JobKey.Should().Be(new KeyDto("nightly", "reports"));
        published.FireInstanceId.Should().Be("fire-1");
    }

    /// <summary>
    /// The notifications that say nothing a live view of a scheduler's work shows publish nothing at all.
    /// </summary>
    /// <remarks>
    /// A stream is not a mirror of <see cref="ISchedulerListener" />: scheduling, unscheduling and the
    /// group-level pauses are things a reader learns by reloading the page they are about, and putting
    /// them on the wire would spend a reader's ring buffer on them.
    /// </remarks>
    [Test]
    public async Task TheNotificationsWithNoLiveViewPublishNothing()
    {
        await using Watcher watcher = await Watcher.Watching(broker);

        // Through the interface, because the plugin leaves every one of these at its default body: a
        // member it does not declare is a member it has nothing to say about.
        ISchedulerListener listener = plugin;
        await listener.JobScheduled(scheduler, Trigger());
        await listener.JobUnscheduled(scheduler, new TriggerKey("at-midnight", "reports"));
        await listener.TriggerFinalized(scheduler, Trigger());
        await listener.JobAdded(scheduler, JobDetail());
        await listener.JobDeleted(scheduler, new JobKey("nightly", "reports"));
        await listener.JobsPaused(scheduler, "reports");
        await listener.JobsResumed(scheduler, "reports");
        await listener.TriggersPaused(scheduler, "reports");
        await listener.TriggersResumed(scheduler, "reports");
        await listener.TriggersInError(scheduler, new JobKey("nightly", "reports"));
        await listener.SchedulingDataCleared(scheduler);

        await watcher.NothingArrives("the stream carries what a scheduler did, not every notification it raises");
    }

    /// <summary>
    /// Nothing is built while nobody is watching, which is what lets every process that maps the HTTP API
    /// carry the publisher.
    /// </summary>
    [Test]
    public async Task NothingIsPublishedWhileNobodyIsWatching()
    {
        await plugin.JobToBeExecuted(ExecutionContext());

        broker.HasSubscribers(SchedulerName).Should().BeFalse();
        broker.DroppedEvents.Should().Be(0, "an event is not built at all, let alone queued and dropped");
    }

    private static IScheduler Scheduler()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(SchedulerName);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns(NodeId);
        A.CallTo(() => scheduler.ListenerManager).Returns(A.Fake<IListenerManager>());
        return scheduler;
    }

    private static IJobDetail JobDetail() => JobBuilder.Create<NoOpJob>().WithIdentity("nightly", "reports").Build();

    private static ITrigger Trigger() => TriggerBuilder.Create()
        .WithIdentity("at-midnight", "reports")
        .ForJob("nightly", "reports")
        .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(1)))
        .Build();

    private IJobExecutionContext ExecutionContext()
    {
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.Scheduler).Returns(scheduler);
        A.CallTo(() => context.JobDetail).Returns(JobDetail());
        A.CallTo(() => context.Trigger).Returns(Trigger());
        A.CallTo(() => context.FireTimeUtc).Returns(firedAt);
        A.CallTo(() => context.JobRunTime).Returns(TimeSpan.FromSeconds(1));
        A.CallTo(() => context.FireInstanceId).Returns("fire-1");
        return context;
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A reader of the broker, held at a read the test controls.
    /// </summary>
    /// <remarks>
    /// The publisher asks the broker whether anything is watching before it builds an event, so a test
    /// that publishes first is testing nothing. The subscription's queue exists once its enumeration has
    /// been read, which is what <see cref="Watching" /> waits for.
    /// </remarks>
    private sealed class Watcher : IAsyncDisposable
    {
        private static readonly TimeSpan waitForEvent = TimeSpan.FromSeconds(10);

        private readonly CancellationTokenSource cancellation = new();
        private readonly IAsyncEnumerator<SchedulerEvent> events;

        private Task<bool> pending;

        private Watcher(SchedulerEventBroker broker)
        {
            events = broker.Subscribe(SchedulerName, cancellation.Token).GetAsyncEnumerator(CancellationToken.None);
        }

        public static async Task<Watcher> Watching(SchedulerEventBroker broker)
        {
            Watcher watcher = new(broker);
            watcher.pending = watcher.events.MoveNextAsync().AsTask();

            using CancellationTokenSource timeout = new(waitForEvent);
            while (!broker.HasSubscribers(SchedulerName))
            {
                await Task.Delay(5, timeout.Token);
            }

            return watcher;
        }

        public async Task<SchedulerEvent> Next()
        {
            Task<bool> read = pending ?? events.MoveNextAsync().AsTask();
            pending = null;

            (await read.WaitAsync(waitForEvent)).Should().BeTrue("the publisher published something");
            return events.Current;
        }

        public async Task NothingArrives(string because)
        {
            Task<bool> read = pending ??= events.MoveNextAsync().AsTask();
            await Task.Delay(100);

            read.IsCompleted.Should().BeFalse(because);
        }

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();

            if (pending is not null)
            {
                await pending;
            }

            await events.DisposeAsync();
            cancellation.Dispose();
        }
    }
}
