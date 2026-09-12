using FakeItEasy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.HttpApiContract;

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

    private static readonly DateTimeOffset firedAt = new(2026, 9, 12, 10, 29, 55, TimeSpan.Zero);

    private static SchedulerEvent Event(SchedulerEventKind kind) => new()
    {
        Kind = kind,
        SchedulerName = "TestScheduler",
        SchedulerInstanceId = "node-a",
        OccurredAtUtc = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero)
    };

    /// <summary>
    /// A forwarder over a stream a test pushes into and a hub a test can read.
    /// </summary>
    private sealed class Forwarding : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly DashboardHubForwarder forwarder;

        private Forwarding(FakeSchedulerEventSource source)
        {
            Source = source;

            IQuartzDashboardHubClient client = A.Fake<IQuartzDashboardHubClient>();
            Record(client);

            IHubClients<IQuartzDashboardHubClient> clients = A.Fake<IHubClients<IQuartzDashboardHubClient>>();
            A.CallTo(() => clients.Group(A<string>._)).Returns(client);

            ServiceCollection services = new();
            services.AddSingleton<ISchedulerEventSource>(source);
            services.AddSingleton<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>(new CapturingHubContext(clients));

            provider = services.BuildServiceProvider();
            forwarder = new DashboardHubForwarder(provider, NullLogger<DashboardHubForwarder>.Instance);
        }

        public static Forwarding Nothing() => new(new FakeSchedulerEventSource());

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
        }
    }
}
