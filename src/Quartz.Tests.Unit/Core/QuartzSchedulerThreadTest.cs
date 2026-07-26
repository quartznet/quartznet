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

    private static IEnumerable<TimeSpan> ValidIdleWaitTimes()
    {
        return QuartzSchedulerResourcesTest.ValidIdleWaitTimes();
    }
}