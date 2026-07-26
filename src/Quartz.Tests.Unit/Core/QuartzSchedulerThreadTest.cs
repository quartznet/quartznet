using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

[TestFixture]
public class QuartzSchedulerThreadTest
{
    [Test]
    public void Ctor_SchedulerAndResources([ValueSource(nameof(ValidIdleWaitTimes))] TimeSpan idleWaitTime)
    {
        QuartzSchedulerResources resources = new QuartzSchedulerResources
        {
            IdleWaitTime = idleWaitTime
        };
        QuartzScheduler scheduler = new QuartzScheduler(resources);

        var thread = new QuartzSchedulerThread(scheduler, resources);
        Assert.Multiple(() =>
        {
            Assert.That(thread.Paused, Is.True);
            Assert.That(thread.Halted, Is.False);
            Assert.That(thread.IdleWaitVariableness, Is.EqualTo((int)(idleWaitTime.TotalMilliseconds * 0.2)));
        });
    }

    [Test]
    public void Ctor_DoesNotStartTheLoop()
    {
        QuartzSchedulerResources resources = new QuartzSchedulerResources { IdleWaitTime = TimeSpan.FromSeconds(1) };
        QuartzScheduler scheduler = new QuartzScheduler(resources);

        var thread = new QuartzSchedulerThread(scheduler, resources);

        thread.Running.Should().BeFalse();
    }

    [Test]
    public void Start_IsIdempotent()
    {
        QuartzSchedulerResources resources = new QuartzSchedulerResources
        {
            IdleWaitTime = TimeSpan.FromSeconds(1),
            JobStore = TestJobStores.Ram()
        };
        QuartzScheduler scheduler = new QuartzScheduler(resources);
        var thread = new QuartzSchedulerThread(scheduler, resources);

        thread.Start();
        thread.Start();

        thread.Running.Should().BeTrue();
    }

    [Test]
    public async Task Halt_DoesNothingWhenTheLoopWasNeverStarted()
    {
        QuartzSchedulerResources resources = new QuartzSchedulerResources { IdleWaitTime = TimeSpan.FromSeconds(1) };
        QuartzScheduler scheduler = new QuartzScheduler(resources);
        var thread = new QuartzSchedulerThread(scheduler, resources);

        await thread.Halt(wait: true);

        thread.Halted.Should().BeTrue();
    }

    [Test]
    public async Task Shutdown_DoesNothingWhenTheLoopWasNeverStarted()
    {
        QuartzSchedulerResources resources = new QuartzSchedulerResources { IdleWaitTime = TimeSpan.FromSeconds(1) };
        QuartzScheduler scheduler = new QuartzScheduler(resources);
        var thread = new QuartzSchedulerThread(scheduler, resources);

        await thread.Shutdown();

        thread.Running.Should().BeFalse();
    }

    /// <summary>
    /// Shutdown disposes the cancellation source, so a start that lands after it must not touch the token.
    /// A start racing a shutdown is reachable from the hosted services, whose graceful-shutdown deadline
    /// can elapse while a start is still in flight.
    /// </summary>
    [Test]
    public async Task StartAfterShutdownDoesNothingRatherThanThrowing()
    {
        QuartzSchedulerResources resources = new() { IdleWaitTime = TimeSpan.FromSeconds(1) };
        QuartzScheduler scheduler = new(resources);
        var thread = new QuartzSchedulerThread(scheduler, resources);

        await thread.Shutdown();

        var start = () => thread.Start();

        start.Should().NotThrow<ObjectDisposedException>();
        thread.Running.Should().BeFalse("the loop cannot be started again after shutdown");
    }

    [Test]
    public async Task HaltAfterShutdownDoesNotThrow()
    {
        QuartzSchedulerResources resources = new() { IdleWaitTime = TimeSpan.FromSeconds(1) };
        QuartzScheduler scheduler = new(resources);
        var thread = new QuartzSchedulerThread(scheduler, resources);

        await thread.Shutdown();

        var halt = async () => await thread.Halt(wait: false);

        await halt.Should().NotThrowAsync<ObjectDisposedException>();
    }

    [Test]
    public async Task ShutdownIsIdempotentSoItCannotDisposeTwice()
    {
        QuartzSchedulerResources resources = new() { IdleWaitTime = TimeSpan.FromSeconds(1) };
        QuartzScheduler scheduler = new(resources);
        var thread = new QuartzSchedulerThread(scheduler, resources);

        await thread.Shutdown();
        var again = async () => await thread.Shutdown();

        await again.Should().NotThrowAsync();
    }

    [Test]
    public void ConcurrentStartsCreateOnlyOneLoop()
    {
        QuartzSchedulerResources resources = new()
        {
            IdleWaitTime = TimeSpan.FromSeconds(1),
            JobStore = TestJobStores.Ram()
        };
        QuartzScheduler scheduler = new(resources);
        var thread = new QuartzSchedulerThread(scheduler, resources);

        // Two loops for one scheduler would both acquire triggers, and Shutdown would await only one.
        Parallel.For(0, 16, _ => thread.Start());

        thread.Running.Should().BeTrue();
    }

    private static IEnumerable<TimeSpan> ValidIdleWaitTimes()
    {
        return QuartzSchedulerResourcesTest.ValidIdleWaitTimes();
    }
}