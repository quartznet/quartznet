using NUnit.Framework;
using Quartz.Core;

namespace Quartz.Tests.Unit.Core
{
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
            Assert.IsTrue(thread.Paused);
            Assert.IsFalse(thread.Halted);
            Assert.AreEqual((int) (idleWaitTime.TotalMilliseconds * 0.2), thread.IdleWaitVariableness);
        }

        private static IEnumerable<TimeSpan> ValidIdleWaitTimes()
        {
            return QuartzSchedulerResourcesTest.ValidIdleWaitTimes();
        }
    }
}
