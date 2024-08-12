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

    private static IEnumerable<TimeSpan> ValidIdleWaitTimes()
    {
        return QuartzSchedulerResourcesTest.ValidIdleWaitTimes();
    }
}