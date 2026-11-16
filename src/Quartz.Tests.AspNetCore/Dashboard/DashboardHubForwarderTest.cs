using FakeItEasy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// What the dashboard's hub receives now that Quartz owns the events: the same payloads, from a stream
/// rather than from a plugin writing into the hub.
/// </summary>
/// <remarks>
/// <para>
/// The hub and its client interface are public and someone's JavaScript is connected to them, so what a
/// connected client sees is a contract — which makes this the seam that has to be held. The state events
/// are the ones with history: the payload used to be a phrase chosen at each call site, so a running
/// scheduler was announced as <c>"Started"</c> here while the same state was <c>"Running"</c> everywhere
/// else.
/// </para>
/// <para>
/// It is also where the three kinds with no hub method are pinned as not forwarded. A client written
/// against 4.0's interface has no method for them, and inventing one would be a change to a contract this
/// work was not supposed to touch.
/// </para>
/// </remarks>
public class DashboardHubForwarderTest
{
    private static readonly TimeSpan waitForForwarding = TimeSpan.FromSeconds(10);

    [Test]
    public async Task EachLifecycleEventIsPushedAsTheStateTheSchedulerIsNowIn()
    {
        await using Forwarding forwarding = Forwarding.Of("TestScheduler");

        foreach (SchedulerStatus status in (SchedulerStatus[])
                 [SchedulerStatus.Running, SchedulerStatus.Standby, SchedulerStatus.ShuttingDown, SchedulerStatus.Shutdown])
        {
            forwarding.Publish(Event(SchedulerEventKind.SchedulerStateChanged) with { Status = status });
        }

        await Eventually.Until(() => forwarding.States.Count == 4);

        forwarding.States.Select(state => state.Status).Should().Equal(
            [
                SchedulerStatus.Running,
                SchedulerStatus.Standby,
                SchedulerStatus.ShuttingDown,
                SchedulerStatus.Shutdown
            ],
            "a live view shows the state the scheduler has arrived in, and there is one vocabulary for it");

        forwarding.States.Should().OnlyContain(state => state.SchedulerName == "TestScheduler");
        forwarding.States.Should().OnlyContain(state => state.SchedulerInstanceId == "node-a",
            "a cluster is one scheduler in several processes, each with a lifecycle of its own, so a state "
            + "change says nothing until it says which node changed");
    }

    /// <summary>
    /// One firing's four events, in the payload records each of them has always had.
    /// </summary>
    [Test]
    public async Task AFiringIsPushedAsTheFourPayloadsTheHubDeclares()
    {
        await using Forwarding forwarding = Forwarding.Of("TestScheduler");

        forwarding.Publish(Event(SchedulerEventKind.TriggerFired) with
        {
            TriggerKey = new KeyDto("at-midnight", "reports"),
            JobKey = new KeyDto("nightly", "reports"),
            FireTimeUtc = firedAt
        });
        forwarding.Publish(Event(SchedulerEventKind.JobExecuting) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            FireTimeUtc = firedAt,
            FireInstanceId = "fire-1"
        });
        forwarding.Publish(Event(SchedulerEventKind.JobExecuted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            FireTimeUtc = firedAt,
            RunTime = TimeSpan.FromMilliseconds(1500),
            Vetoed = false,
            ExceptionMessage = "the job threw"
        });
        forwarding.Publish(Event(SchedulerEventKind.TriggerCompleted) with
        {
            TriggerKey = new KeyDto("at-midnight", "reports"),
            JobKey = new KeyDto("nightly", "reports"),
            FireTimeUtc = firedAt
        });

        await Eventually.Until(() => forwarding.Methods.Count == 4);

        forwarding.Methods.Should().Equal(["TriggerFired", "JobExecuting", "JobExecuted", "TriggerCompleted"]);

        JobEventDto executing = forwarding.Executing.Should().ContainSingle().Subject;
        executing.JobKey.Should().Be(new JobKeyDto("reports", "nightly"),
            "the hub's keys lead with the group, where the wire's lead with the name");
        executing.TriggerKey.Should().Be(new TriggerKeyDto("reports", "at-midnight"));
        executing.FireTimeUtc.Should().Be(firedAt);
        executing.FireInstanceId.Should().Be("fire-1");

        JobExecutionResultDto executed = forwarding.Executed.Should().ContainSingle().Subject;
        executed.RunTime.Should().Be(TimeSpan.FromMilliseconds(1500), "the duration is carried unrounded");
        executed.Vetoed.Should().BeFalse();
        executed.ExceptionMessage.Should().Be("the job threw");
    }

    [Test]
    public async Task AMisfireIsPushedWithNoFireTime()
    {
        await using Forwarding forwarding = Forwarding.Of("TestScheduler");

        forwarding.Publish(Event(SchedulerEventKind.TriggerMisfired) with
        {
            TriggerKey = new KeyDto("at-midnight", "reports"),
            JobKey = new KeyDto("nightly", "reports")
        });

        await Eventually.Until(() => forwarding.Methods.Count == 1);

        TriggerEventDto misfired = forwarding.TriggerEvents.Should().ContainSingle().Subject;
        misfired.FireTimeUtc.Should().BeNull("there was no firing to time");
        misfired.JobKey.Should().Be(new JobKeyDto("reports", "nightly"), "a misfire says which job did not run");
    }

    /// <summary>
    /// The kinds the hub has no method for are not forwarded, and neither is the transport's own heartbeat.
    /// </summary>
    [Test]
    public async Task TheKindsTheHubHasNoMethodForAreNotForwarded()
    {
        await using Forwarding forwarding = Forwarding.Of("TestScheduler");

        forwarding.Publish(Event(SchedulerEventKind.JobInterrupted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            FireInstanceId = "fire-1"
        });
        forwarding.Publish(Event(SchedulerEventKind.TriggerInError) with { TriggerKey = new KeyDto("at-midnight", "reports") });
        forwarding.Publish(Event(SchedulerEventKind.Heartbeat));

        // Something that is forwarded, behind them: it is what says the three above were passed over rather
        // than merely late.
        forwarding.Publish(Event(SchedulerEventKind.JobPaused) with { JobKey = new KeyDto("nightly", "reports") });

        await Eventually.Until(() => forwarding.Methods.Count == 1);

        forwarding.Methods.Should().Equal(["JobPaused"],
            "a client written against the hub's interface has no method for the other three");
    }

    /// <summary>
    /// Forwarding starts when a scheduler is joined and not before, which is what keeps a hub nobody has
    /// connected to free.
    /// </summary>
    [Test]
    public async Task NothingIsSubscribedToUntilASchedulerIsJoined()
    {
        await using Forwarding forwarding = Forwarding.Nothing();

        forwarding.Source.Subscribed.Should().BeEmpty(
            "a subscriber is what makes a scheduler build its events, so a hub with no connections costs nothing");

        forwarding.Forward("TestScheduler");
        forwarding.Forward("TestScheduler");

        await Eventually.Until(() => forwarding.Source.Subscribed.Count > 0);

        forwarding.Source.Subscribed.Should().Equal(["TestScheduler"],
            "one subscription feeds every connection in the group, so joining twice subscribes once");
    }

    /// <summary>
    /// A stream that ends while the dashboard still lists its scheduler is subscribed to again, so the
    /// connections already in the group go on receiving without joining again.
    /// </summary>
    /// <remarks>
    /// An agent redeployed under the same target is the common case: the registry completes the old entry's
    /// stream once the new entry is bound, and the key resolves to the new one.
    /// </remarks>
    [Test]
    public async Task APumpWhoseStreamEndsSubscribesAgainAndForwardsTheNextEvent()
    {
        await using Forwarding forwarding = Forwarding.Of("TestScheduler");
        await Eventually.Until(() => forwarding.Source.Subscribed.Count == 1);

        forwarding.Source.End("TestScheduler");

        // The pump waits before it subscribes again, on the clock the container holds; the clock is moved
        // on until it has, rather than a test waiting seconds of real time.
        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.FirstRestartDelay);
            return forwarding.Source.Subscribed.Count == 2;
        });

        forwarding.Publish(Event(SchedulerEventKind.JobPaused) with { JobKey = new KeyDto("nightly", "reports") });

        await Eventually.Until(() => forwarding.Methods.Count == 1);

        forwarding.Methods.Should().Equal(["JobPaused"],
            "a client that joined before the stream ended is still in the group, and nothing tells it to join again");
    }

    /// <summary>
    /// A stream that ends once the dashboard no longer lists its scheduler ends its pump, and the pump
    /// leaves the forwarder.
    /// </summary>
    /// <remarks>
    /// An agent forgotten after <c>ForgetAfter</c> is the case: the registry removes it and then completes its
    /// stream. Subscribing again would subscribe to nothing that will ever deliver.
    /// </remarks>
    [Test]
    public async Task APumpWhoseSchedulerIsNoLongerListedEndsAndLeavesTheForwarder()
    {
        await using Forwarding forwarding = Forwarding.Of("TestScheduler");
        await Eventually.Until(() => forwarding.Source.Subscribed.Count == 1);

        forwarding.Unlist("TestScheduler");
        forwarding.Source.End("TestScheduler");

        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.FirstRestartDelay);
            return !forwarding.IsForwarding("TestScheduler");
        });

        forwarding.Source.Subscribed.Should().Equal(["TestScheduler"],
            "a scheduler the dashboard no longer lists is not subscribed to again");
    }

    /// <summary>
    /// A join for a name the dashboard does not list starts nothing, and the same name starts a pump on the
    /// first join after it is listed.
    /// </summary>
    /// <remarks>
    /// With no scheduler or operation policy any name passes the hub's check, so the name is the client's to
    /// choose; the dashboard's own listing is what bounds the forwarder.
    /// </remarks>
    [Test]
    public async Task AJoinForANameTheDashboardDoesNotListStartsNoPump()
    {
        await using Forwarding forwarding = Forwarding.Nothing();

        forwarding.Forward("made-up");

        forwarding.IsForwarding("made-up").Should().BeFalse(
            "a pump for a name no scheduler has would hold a subscription on the broker for the life of the process");
        forwarding.Source.Subscribed.Should().BeEmpty();

        forwarding.List("made-up");
        forwarding.Forward("made-up");

        await Eventually.Until(() => forwarding.Source.Subscribed.Count > 0);
        forwarding.Source.Subscribed.Should().Equal(["made-up"], "a name that becomes known starts its pump on the next join");
    }

    /// <summary>
    /// The pump the next join starts once the last one has ended is the one that forwards: the ended pump's
    /// leaving does not take its replacement with it.
    /// </summary>
    [Test]
    public async Task TheReplacementOfAnEndedPumpIsNotRemovedByIt()
    {
        await using Forwarding forwarding = Forwarding.Of("TestScheduler");
        await Eventually.Until(() => forwarding.Source.Subscribed.Count == 1);

        forwarding.Unlist("TestScheduler");
        forwarding.Source.End("TestScheduler");
        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.FirstRestartDelay);
            return !forwarding.IsForwarding("TestScheduler");
        });

        forwarding.List("TestScheduler");
        forwarding.Forward("TestScheduler");
        await Eventually.Until(() => forwarding.Source.Subscribed.Count == 2);

        forwarding.IsForwarding("TestScheduler").Should().BeTrue("the replacement is the scheduler's pump now");

        forwarding.Forward("TestScheduler");
        forwarding.Publish(Event(SchedulerEventKind.JobPaused) with { JobKey = new KeyDto("nightly", "reports") });

        await Eventually.Until(() => forwarding.Methods.Count == 1);

        forwarding.Source.Subscribed.Should().HaveCount(2,
            "a join while the replacement runs is a join to it, not a reason for a third subscription");
        forwarding.Methods.Should().Equal(["JobPaused"], "one pump feeds the group, so each event arrives once");
    }

    /// <summary>
    /// A stream that fails is subscribed to again, and the failure is logged once for a run of them rather
    /// than once per retry; a subscription that delivers ends the run.
    /// </summary>
    [Test]
    public async Task AFailingStreamIsSubscribedToAgainAndLoggedOncePerRunOfFailures()
    {
        await using Forwarding forwarding = Forwarding.Nothing();
        forwarding.Source.Failure = new InvalidOperationException("the target answered 500");

        forwarding.Forward("TestScheduler");

        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.MaxRestartDelay);
            return forwarding.Source.Subscribed.Count >= 3;
        });

        forwarding.Logs.WithEventId(9104).Should().ContainSingle(
            "a target that keeps failing is asked again after each wait, and one warning says so");

        // The target recovers: an event queued for the scheduler is what the next subscription delivers.
        forwarding.Source.Failure = null;
        forwarding.Source.Push(Event(SchedulerEventKind.JobPaused) with { JobKey = new KeyDto("nightly", "reports") });

        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.MaxRestartDelay);
            return forwarding.Methods.Count == 1;
        });

        forwarding.Source.Fail("TestScheduler", new InvalidOperationException("the connection broke"));

        await Eventually.Until(() => forwarding.Logs.WithEventId(9104).Count == 2);

        forwarding.Logs.WithEventId(9104).Select(entry => entry.Message).Should().AllSatisfy(message =>
            message.Should().Be("Forwarding scheduler TestScheduler events to the dashboard hub failed; it subscribes again while the dashboard lists the scheduler"));
    }

    /// <summary>
    /// The wait before subscribing again doubles from five seconds, stops growing at a minute, and starts
    /// over at five seconds once a subscription has delivered an event.
    /// </summary>
    [Test]
    public async Task TheWaitBeforeSubscribingAgainDoublesUpToAMinuteAndAnEventStartsItOver()
    {
        await using Forwarding forwarding = Forwarding.Nothing();
        forwarding.Source.Failure = new InvalidOperationException("the target answered 500");

        forwarding.Forward("TestScheduler");

        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.MaxRestartDelay);
            return forwarding.Clock.Waits.Count >= 6;
        });

        forwarding.Clock.Waits.Take(6).Should().Equal(
            [
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(40),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60)
            ],
            "a target that keeps failing is asked less often each time, and never left for longer than a minute");

        // The target recovers, and the subscription after it delivers the event queued for the scheduler.
        forwarding.Source.Failure = null;
        forwarding.Source.Push(Event(SchedulerEventKind.JobPaused) with { JobKey = new KeyDto("nightly", "reports") });

        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.MaxRestartDelay);
            return forwarding.Methods.Count == 1;
        });

        int waitsBeforeTheBreak = forwarding.Clock.Waits.Count;
        forwarding.Source.Fail("TestScheduler", new InvalidOperationException("the connection broke"));

        await Eventually.Until(() => forwarding.Clock.Waits.Count > waitsBeforeTheBreak);

        forwarding.Clock.Waits[waitsBeforeTheBreak].Should().Be(TimeSpan.FromSeconds(5),
            "a stream that delivered was working, so its failure is the first of a new run");
    }

    /// <summary>
    /// A stream cancelled by something other than the forwarder, such as a request that timed out, is a
    /// failure like any other: it is logged and subscribed to again.
    /// </summary>
    [Test]
    public async Task AStreamCancelledByAnythingButTheForwarderIsSubscribedToAgain()
    {
        await using Forwarding forwarding = Forwarding.Nothing();
        forwarding.Source.Failure = new OperationCanceledException("the request timed out");

        forwarding.Forward("TestScheduler");

        await Eventually.Until(() =>
        {
            forwarding.Clock.Advance(DashboardHubForwarder.FirstRestartDelay);
            return forwarding.Source.Subscribed.Count >= 2;
        });

        forwarding.IsForwarding("TestScheduler").Should().BeTrue("only the forwarder's own cancellation ends a pump");
        forwarding.Logs.WithEventId(9104).Should().ContainSingle();
    }

    /// <summary>
    /// A target that serves no event stream ends its pump without retrying, and the pump leaves, although
    /// the scheduler is still listed: there is nothing to forward from it.
    /// </summary>
    [Test]
    public async Task ATargetThatServesNoEventStreamEndsItsPumpWithoutRetrying()
    {
        await using Forwarding forwarding = Forwarding.Nothing();
        forwarding.Source.Failure = new NotSupportedException("the target does not serve an event stream");

        forwarding.Forward("TestScheduler");

        await Eventually.Until(() => forwarding.Source.Subscribed.Count == 1 && !forwarding.IsForwarding("TestScheduler"));

        forwarding.Clock.Advance(DashboardHubForwarder.MaxRestartDelay * 10);

        forwarding.Source.Subscribed.Should().ContainSingle("asking again would be told the same");
        forwarding.Logs.WithEventId(9104).Should().BeEmpty("the page a visitor is looking at reads the same source and says so");
    }

    /// <summary>
    /// A listed scheduler with no event source at all, such as a window onto an attached store, ends its pump
    /// at once, and the pump leaves.
    /// </summary>
    [Test]
    public async Task ASchedulerWithNoEventSourceEndsItsPumpAtOnce()
    {
        await using Forwarding forwarding = Forwarding.WithoutEventSource();

        forwarding.Forward("TestScheduler");

        await Eventually.Until(() => !forwarding.IsForwarding("TestScheduler"));

        forwarding.Logs.WithEventId(9104).Should().BeEmpty("a scheduler that keeps no feed has nothing to fail at");
    }

    private static readonly DateTimeOffset firedAt = new(2026, 9, 12, 10, 29, 55, TimeSpan.Zero);

    private static SchedulerEvent Event(SchedulerEventKind kind) => new()
    {
        Kind = kind,
        SchedulerName = "TestScheduler",
        SchedulerInstanceId = "node-a",
        OccurredAtUtc = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero)
    };

    /// <summary>
    /// A clock a test moves, which also keeps every wait asked of it, in order.
    /// </summary>
    /// <remarks>
    /// A wait on a <see cref="TimeProvider" /> other than the system's is a timer created on it, so the due
    /// time of each timer is the length of each wait: what the forwarder chose, read rather than inferred
    /// from when it subscribed again.
    /// </remarks>
    private sealed class RecordingClock : FakeTimeProvider
    {
        private readonly List<TimeSpan> waits = [];

        /// <summary>
        /// The waits asked for so far, oldest first.
        /// </summary>
        public List<TimeSpan> Waits
        {
            get
            {
                lock (waits)
                {
                    return [.. waits];
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (waits)
            {
                waits.Add(dueTime);
            }

            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    /// <summary>
    /// A forwarder over a stream a test pushes into, a hub a test can read, a repository that says which
    /// schedulers the dashboard lists, and a clock a test moves.
    /// </summary>
    private sealed class Forwarding : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly LoggerFactory loggerFactory;
        private readonly DashboardHubForwarder forwarder;
        private readonly SchedulerRepository repository = new();

        private Forwarding(FakeSchedulerEventSource source, bool registerSource = true)
        {
            Source = source;

            IQuartzDashboardHubClient client = A.Fake<IQuartzDashboardHubClient>();
            Record(client);

            IHubClients<IQuartzDashboardHubClient> clients = A.Fake<IHubClients<IQuartzDashboardHubClient>>();
            A.CallTo(() => clients.Group(A<string>._)).Returns(client);

            // The scheduler every test is about is listed, as a scheduler of this process is once its host
            // has started.
            List("TestScheduler");

            ServiceCollection services = new();
            if (registerSource)
            {
                services.AddSingleton<ISchedulerEventSource>(source);
            }

            services.AddSingleton<ISchedulerRepository>(repository);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>(new CapturingHubContext(clients));

            provider = services.BuildServiceProvider();
            loggerFactory = new LoggerFactory([Logs]);
            forwarder = new DashboardHubForwarder(provider, loggerFactory.CreateLogger<DashboardHubForwarder>());
        }

        public static Forwarding Nothing() => new(new FakeSchedulerEventSource());

        /// <summary>
        /// A forwarder in a container that holds no event source, as a window's key resolves to none.
        /// </summary>
        public static Forwarding WithoutEventSource() => new(new FakeSchedulerEventSource(), registerSource: false);

        /// <summary>
        /// A forwarder already watching <paramref name="schedulerName" />, as the hub's first join leaves it.
        /// </summary>
        public static Forwarding Of(string schedulerName)
        {
            Forwarding forwarding = new(new FakeSchedulerEventSource());
            forwarding.Forward(schedulerName);
            return forwarding;
        }

        public FakeSchedulerEventSource Source { get; }

        /// <summary>
        /// The clock the forwarder waits on between two subscriptions.
        /// </summary>
        public RecordingClock Clock { get; } = new();

        /// <summary>
        /// What the forwarder logged.
        /// </summary>
        public RecordingLoggerProvider Logs { get; } = new();

        /// <summary>
        /// Puts a scheduler of this name on the dashboard, as building one binds it into the repository.
        /// </summary>
        public void List(string schedulerName)
        {
            IScheduler scheduler = A.Fake<IScheduler>();
            A.CallTo(() => scheduler.SchedulerName).Returns(schedulerName);
            A.CallTo(() => scheduler.SchedulerInstanceId).Returns("node-a");
            repository.Bind(scheduler);
        }

        /// <summary>
        /// Takes the scheduler of this name off the dashboard, as forgetting an agent does.
        /// </summary>
        public void Unlist(string schedulerName) => repository.Remove(schedulerName);

        /// <summary>
        /// Whether the forwarder holds a pump for <paramref name="schedulerName" />.
        /// </summary>
        public bool IsForwarding(string schedulerName) => forwarder.IsForwarding(schedulerName);

        /// <summary>
        /// The hub methods that were called, in order, which is what a connected client receives.
        /// </summary>
        public List<string> Methods { get; } = [];

        public List<SchedulerStateDto> States { get; } = [];

        public List<JobEventDto> Executing { get; } = [];

        public List<JobExecutionResultDto> Executed { get; } = [];

        public List<TriggerEventDto> TriggerEvents { get; } = [];

        public void Forward(string schedulerName) => forwarder.Forward(schedulerName);

        /// <summary>
        /// Publishes one event, once the forwarder is reading — a stream carries what happens after a
        /// subscription is made.
        /// </summary>
        public void Publish(SchedulerEvent schedulerEvent)
        {
            Eventually.Until(() => Source.Subscribed.Count > 0).GetAwaiter().GetResult();
            Source.Push(schedulerEvent);
        }

        public static async Task Until(Func<bool> condition)
        {
            using CancellationTokenSource timeout = new(waitForForwarding);
            while (!condition())
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        private void Record(IQuartzDashboardHubClient client)
        {
            A.CallTo(() => client.SchedulerStateChanged(A<SchedulerStateDto>._))
                .Invokes((SchedulerStateDto state) => Add("SchedulerStateChanged", () => States.Add(state)))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.JobExecuting(A<JobEventDto>._))
                .Invokes((JobEventDto jobEvent) => Add("JobExecuting", () => Executing.Add(jobEvent)))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.JobExecuted(A<JobExecutionResultDto>._))
                .Invokes((JobExecutionResultDto result) => Add("JobExecuted", () => Executed.Add(result)))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.TriggerFired(A<TriggerEventDto>._))
                .Invokes((TriggerEventDto triggerEvent) => Add("TriggerFired", () => TriggerEvents.Add(triggerEvent)))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.TriggerCompleted(A<TriggerEventDto>._))
                .Invokes((TriggerEventDto triggerEvent) => Add("TriggerCompleted", () => TriggerEvents.Add(triggerEvent)))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.TriggerMisfired(A<TriggerEventDto>._))
                .Invokes((TriggerEventDto triggerEvent) => Add("TriggerMisfired", () => TriggerEvents.Add(triggerEvent)))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.TriggerPaused(A<TriggerLifecycleDto>._))
                .Invokes(() => Add("TriggerPaused", () => { }))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.TriggerResumed(A<TriggerLifecycleDto>._))
                .Invokes(() => Add("TriggerResumed", () => { }))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.JobPaused(A<JobLifecycleDto>._))
                .Invokes(() => Add("JobPaused", () => { }))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.JobResumed(A<JobLifecycleDto>._))
                .Invokes(() => Add("JobResumed", () => { }))
                .Returns(Task.CompletedTask);
            A.CallTo(() => client.SchedulerError(A<SchedulerErrorDto>._))
                .Invokes(() => Add("SchedulerError", () => { }))
                .Returns(Task.CompletedTask);
        }

        private void Add(string method, Action record)
        {
            lock (Methods)
            {
                Methods.Add(method);
                record();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await forwarder.DisposeAsync();
            await provider.DisposeAsync();
            loggerFactory.Dispose();
        }
    }
}
