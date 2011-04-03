using NUnit.Framework;

using Quartz.Core;
using Quartz.Impl.Matchers;
using Quartz.Listener;

namespace Quartz.Tests.Unit.Core
{
    /// <summary>
    /// Tests for <see cref="ListenerManagerImpl" />. 
    /// </summary>
    [TestFixture]
    public class ListenerManagerTest
    {
        private class TestJobListener : JobListenerSupport
        {
            private readonly string name;

            public TestJobListener(string name)
            {
                this.name = name;
            }

            public override string Name
            {
                get { return name; }
            }
        }

        private class TestTriggerListener : TriggerListenerSupport
        {
            private readonly string name;

            public TestTriggerListener(string name)
            {
                this.name = name;
            }

            public override string Name
            {
                get { return name; }
            }
        }

        private class TestSchedulerListener : SchedulerListenerSupport
        {
        }

        [Test]
        public void TestManagementOfJobListeners()
        {
            TestJobListener tl1 = new TestJobListener("tl1");
            TestJobListener tl2 = new TestJobListener("tl2");

            ListenerManagerImpl manager = new ListenerManagerImpl();

            // test adding listener without matcher
            manager.AddJobListener(tl1);
            Assert.AreEqual(1, manager.GetJobListeners().Count, "Unexpected size of listener list");

            // test adding listener with matcher
            manager.AddJobListener(tl2, GroupMatcher<JobKey>.GroupEquals("foo"));
            Assert.AreEqual(2, manager.GetJobListeners().Count, "Unexpected size of listener list");

            // test removing a listener
            manager.RemoveJobListener("tl1");
            Assert.AreEqual(1, manager.GetJobListeners().Count, "Unexpected size of listener list");

            // test adding a matcher
            manager.AddJobListenerMatcher("tl2", NameMatcher<JobKey>.NameContains("foo"));
            Assert.AreEqual(2, manager.GetJobListenerMatchers("tl2").Count, "Unexpected size of listener's matcher list");
        }

        [Test]
        public void testManagementOfTriggerListeners()
        {
            TestTriggerListener tl1 = new TestTriggerListener("tl1");
            TestTriggerListener tl2 = new TestTriggerListener("tl2");

            ListenerManagerImpl manager = new ListenerManagerImpl();

            // test adding listener without matcher
            manager.AddTriggerListener(tl1);
            Assert.AreEqual(1, manager.GetTriggerListeners().Count, "Unexpected size of listener list");

            // test adding listener with matcher
            manager.AddTriggerListener(tl2, GroupMatcher<TriggerKey>.GroupEquals("foo"));
            Assert.AreEqual(2, manager.GetTriggerListeners().Count, "Unexpected size of listener list");

            // test removing a listener
            manager.RemoveTriggerListener("tl1");
            Assert.AreEqual(1, manager.GetTriggerListeners().Count, "Unexpected size of listener list");

            // test adding a matcher
            manager.AddTriggerListenerMatcher("tl2", NameMatcher<TriggerKey>.NameContains("foo"));
            Assert.AreEqual(2, manager.GetTriggerListenerMatchers("tl2").Count, "Unexpected size of listener's matcher list");
        }

        [Test]
        public void TestManagementOfSchedulerListeners()
        {
            TestSchedulerListener tl1 = new TestSchedulerListener();
            TestSchedulerListener tl2 = new TestSchedulerListener();

            ListenerManagerImpl manager = new ListenerManagerImpl();

            // test adding listener without matcher
            manager.AddSchedulerListener(tl1);
            Assert.AreEqual(1, manager.GetSchedulerListeners().Count, "Unexpected size of listener list");

            // test adding listener with matcher
            manager.AddSchedulerListener(tl2);
            Assert.AreEqual(2, manager.GetSchedulerListeners().Count, "Unexpected size of listener list");

            // test removing a listener
            manager.RemoveSchedulerListener(tl1);
            Assert.AreEqual(1, manager.GetSchedulerListeners().Count, "Unexpected size of listener list");
        }
    }
}