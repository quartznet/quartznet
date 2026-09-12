using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// How one scheduler's events reach whoever is watching them, and what happens to a watcher that stops
/// reading.
/// </summary>
/// <remarks>
/// The publisher is a scheduler listener, so the one thing this must never do is make a scheduler wait:
/// every claim below is about a queue per subscriber rather than a handoff to one.
/// </remarks>
public class SchedulerEventBrokerTest
{
    private const string SchedulerName = "TestScheduler";

    [Test]
    public async Task AnEventReachesEverySubscriberOfThatScheduler()
    {
        SchedulerEventBroker broker = new();

        await using Subscriber first = await Subscriber.Watching(broker, SchedulerName);
        await using Subscriber second = await Subscriber.Watching(broker, SchedulerName);

        broker.Publish(Event(SchedulerEventKind.TriggerFired));

        (await first.Next()).Kind.Should().Be(SchedulerEventKind.TriggerFired);
        (await second.Next()).Kind.Should().Be(SchedulerEventKind.TriggerFired,
            "two browsers watching one scheduler are two subscribers, not one taking the events from the other");
    }

    [Test]
    public async Task ASubscriberHearsNothingOfAnotherScheduler()
    {
        SchedulerEventBroker broker = new();

        await using Subscriber reporting = await Subscriber.Watching(broker, "reporting");

        broker.Publish(Event(SchedulerEventKind.JobExecuting, schedulerName: "billing"));

        await reporting.NothingArrives(
            "a container runs several schedulers, and a page watching one of them is not a page watching all of them");
    }

    /// <summary>
    /// The name is matched the way the repository indexes it, so a subscriber that asked in another case
    /// is still fed.
    /// </summary>
    [Test]
    public async Task TheSchedulersNameIsMatchedIgnoringCase()
    {
        SchedulerEventBroker broker = new();

        await using Subscriber subscriber = await Subscriber.Watching(broker, "reporting");

        broker.Publish(Event(SchedulerEventKind.JobPaused, schedulerName: "Reporting"));

        (await subscriber.Next()).Kind.Should().Be(SchedulerEventKind.JobPaused);
    }

    [Test]
    public void PublishingWithNothingWatchingIsNoWork()
    {
        SchedulerEventBroker broker = new();

        broker.HasSubscribers(SchedulerName).Should().BeFalse("nothing has subscribed yet");

        broker.Publish(Event(SchedulerEventKind.TriggerFired));

        broker.DroppedEvents.Should().Be(0,
            "an event nobody asked for was not dropped, it was never queued - a process nobody is watching does no work");
    }

    /// <summary>
    /// A subscription carries what happens after it is made: there is no replay, and nothing is buffered
    /// for a reader that has not arrived.
    /// </summary>
    [Test]
    public async Task ASubscriberHearsWhatHappensAfterItSubscribes()
    {
        SchedulerEventBroker broker = new();

        broker.Publish(Event(SchedulerEventKind.SchedulerError));

        await using Subscriber subscriber = await Subscriber.Watching(broker, SchedulerName);

        await subscriber.NothingArrives(
            "a live view starts when it is opened; what a scheduler has already done is the history's question");
    }

    /// <summary>
    /// A subscriber that has stopped reading loses its oldest events rather than holding up the scheduler
    /// that is publishing.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The publisher is a listener on the firing path, so it must not wait for a queue
    /// to drain; and a live view is read from the top, so the events worth keeping when the queue is full
    /// are the newest ones.
    /// </remarks>
    [Test]
    public async Task ASubscriberThatStoppedReadingLosesItsOldestEvents()
    {
        SchedulerEventBroker broker = new();

        await using Subscriber subscriber = await Subscriber.Watching(broker, SchedulerName);

        // Reads the event the subscription's first read was already waiting for, which leaves nothing
        // reading the queue - so every publication below lands in it and the bound is what decides.
        broker.Publish(Event(SchedulerEventKind.TriggerFired, fireInstanceId: "primed"));
        (await subscriber.Next()).FireInstanceId.Should().Be("primed");

        const int Overflow = 10;
        for (int index = 0; index < SchedulerEventBroker.Capacity + Overflow; index++)
        {
            broker.Publish(Event(SchedulerEventKind.TriggerFired, fireInstanceId: index.ToString()));
        }

        broker.DroppedEvents.Should().Be(Overflow,
            "a queue at its bound drops its oldest event per write rather than refusing the write, which would make a listener wait");

        List<SchedulerEvent> read = await subscriber.Read(SchedulerEventBroker.Capacity);

        read[0].FireInstanceId.Should().Be(Overflow.ToString(),
            "the events that survive are the newest ones: a reader catching up wants what just happened");
        read[^1].FireInstanceId.Should().Be((SchedulerEventBroker.Capacity + Overflow - 1).ToString());
    }

    [Test]
    public async Task CancellingTheTokenEndsTheEnumerationRatherThanFailingIt()
    {
        SchedulerEventBroker broker = new();

        await using Subscriber subscriber = await Subscriber.Watching(broker, SchedulerName);

        await subscriber.Cancel();

        (await subscriber.Ended()).Should().BeTrue(
            "a reader that has gone away is the ordinary end of a subscription, not a failure of one");
        broker.HasSubscribers(SchedulerName).Should().BeFalse(
            "a subscription whose reader has gone must not go on being written to");
    }

    [Test]
    public async Task LeavingTheEnumerationRemovesTheSubscriber()
    {
        SchedulerEventBroker broker = new();

        await using Subscriber subscriber = await Subscriber.Watching(broker, SchedulerName);

        broker.Publish(Event(SchedulerEventKind.TriggerFired));
        await subscriber.Next();

        await subscriber.Leave();

        broker.HasSubscribers(SchedulerName).Should().BeFalse(
            "the queue belongs to the enumeration, so leaving the loop is unsubscribing");
    }

    private static SchedulerEvent Event(
        SchedulerEventKind kind,
        string schedulerName = SchedulerName,
        string fireInstanceId = null) => new()
    {
        Kind = kind,
        SchedulerName = schedulerName,
        SchedulerInstanceId = "node-a",
        OccurredAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        FireInstanceId = fireInstanceId
    };

    /// <summary>
    /// One reader of the broker, held at a read a test controls.
    /// </summary>
    /// <remarks>
    /// A subscription's queue is created when its enumeration is first read, so <see cref="Watching" />
    /// makes that read and waits for the queue before it returns: a test that published first would be
    /// exercising the no-subscriber path by accident. That first read is then the one
    /// <see cref="Next" /> answers from, which is what leaves nothing reading the queue in between.
    /// </remarks>
    private sealed class Subscriber : IAsyncDisposable
    {
        private static readonly TimeSpan waitForEvent = TimeSpan.FromSeconds(10);

        private readonly CancellationTokenSource cancellation = new();
        private readonly IAsyncEnumerator<SchedulerEvent> events;

        private Task<bool> pending;

        private Subscriber(SchedulerEventBroker broker, string schedulerName)
        {
            events = broker.Subscribe(schedulerName, cancellation.Token).GetAsyncEnumerator(CancellationToken.None);
        }

        public static async Task<Subscriber> Watching(SchedulerEventBroker broker, string schedulerName)
        {
            Subscriber subscriber = new(broker, schedulerName);
            subscriber.pending = subscriber.events.MoveNextAsync().AsTask();

            using CancellationTokenSource timeout = new(waitForEvent);
            while (!broker.HasSubscribers(schedulerName))
            {
                await Task.Delay(5, timeout.Token);
            }

            return subscriber;
        }

        public async Task<SchedulerEvent> Next()
        {
            Task<bool> read = Read();
            pending = null;

            (await read.WaitAsync(waitForEvent)).Should().BeTrue("an event was published and this subscriber watches it");
            return events.Current;
        }

        public async Task<List<SchedulerEvent>> Read(int count)
        {
            List<SchedulerEvent> read = [];
            for (int index = 0; index < count; index++)
            {
                read.Add(await Next());
            }

            return read;
        }

        public async Task NothingArrives(string because)
        {
            Task<bool> read = Read();
            await Task.Delay(100);

            read.IsCompleted.Should().BeFalse(because);
        }

        public Task Cancel() => cancellation.CancelAsync();

        /// <summary>
        /// Leaves the loop, which is what a page navigating away does. There is no read in flight by the
        /// time this is called, which is the only state an enumeration may be disposed in.
        /// </summary>
        public ValueTask Leave() => events.DisposeAsync();

        /// <summary>
        /// Whether the enumeration has ended, which a completed read reports as "no more events".
        /// </summary>
        public async Task<bool> Ended() => !await Read().WaitAsync(waitForEvent);

        private Task<bool> Read() => pending ??= events.MoveNextAsync().AsTask();

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
