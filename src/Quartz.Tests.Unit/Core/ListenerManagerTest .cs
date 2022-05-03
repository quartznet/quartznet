using NUnit.Framework;

using Quartz.Core;
using Quartz.Impl.Matchers;
using Quartz.Listener;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Quartz.Tests.Unit.Core
{
    /// <summary>
    /// Tests for <see cref="ListenerManagerImpl" />. 
    /// </summary>
    [TestFixture]
    public class ListenerManagerTest
    {
        private ListenerManagerImpl _manager;

        private class TestJobListener : JobListenerSupport
        {
            public TestJobListener(string name)
            {
                Name = name;
            }

            public override string Name { get; }
        }

        private class TestTriggerListener : TriggerListenerSupport
        {
            public TestTriggerListener(string name)
            {
                Name = name;
            }

            public override string Name { get; }
        }
        private class TestSchedulerListener : SchedulerListenerSupport
        {
        }

        [SetUp]
        public void SetUp()
        {
            _manager = new ListenerManagerImpl();
        }

        [Test]
        public void AddJobListener_ArrayOfMatcherIsNull_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
        {
            const IMatcher<JobKey>[] setMatchers = null;

            var tl1a = new TestJobListener("tl1");
            var tl1b = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

            _manager.AddJobListener(tl1a, groupMatcher);
            _manager.AddJobListener(tl1b, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1b, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1b.Name);
            Assert.IsNull(matchers);
        }

        [Test]
        public void AddJobListener_ArrayOfMatcherIsNull_JobListenerDoesntAlreadyExist()
        {
            const IMatcher<JobKey>[] setMatchers = null;

            var tl1 = new TestJobListener("tl1");

            _manager.AddJobListener(tl1, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNull(matchers);
        }

        [Test]
        public void AddJobListener_ArrayOfMatcherIsEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
        {
            var tl1a = new TestJobListener("tl1");
            var tl1b = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var setMatchers = Array.Empty<IMatcher<JobKey>>();

            _manager.AddJobListener(tl1a, groupMatcher);
            _manager.AddJobListener(tl1b, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1b, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1b.Name);
            Assert.IsNull(matchers);
        }

        [Test]
        public void AddJobListener_ArrayOfMatcherIsEmpty_JobListenerDoesntAlreadyExist()
        {
            var tl1 = new TestJobListener("tl1");
            var setMatchers = Array.Empty<IMatcher<JobKey>>();

            _manager.AddJobListener(tl1, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNull(matchers);
        }

        [Test]
        public void AddJobListener_ArrayOfMatcherIsNotEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
        {
            var tl1a = new TestJobListener("tl1");
            var tl1b = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

            _manager.AddJobListener(tl1a, groupMatcher);

            var setMatchers = new IMatcher<JobKey>[] { nameMatcher };

            _manager.AddJobListener(tl1b, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1b, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1b.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(1, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(new IMatcher<JobKey>[] { nameMatcher }));
        }

        [Test]
        public void AddJobListener_ArrayOfMatcherIsNotEmpty_JobListenerDoesntAlreadyExist()
        {
            var tl1 = new TestJobListener("tl1");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");
            var setMatchers = new IMatcher<JobKey>[] { nameMatcher };

            _manager.AddJobListener(tl1, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(1, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(new IMatcher<JobKey>[] { nameMatcher }));
        }

        [Test]
        public void AddJobListener_ReadOnlyCollectionOfMatcherIsNull_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
        {
            const IReadOnlyCollection<IMatcher<JobKey>> setMatchers = null;

            var tl1a = new TestJobListener("tl1");
            var tl1b = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

            _manager.AddJobListener(tl1a, groupMatcher);
            _manager.AddJobListener(tl1b, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1b, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1b.Name);
            Assert.IsNull(matchers);
        }

        [Test]
        public void AddJobListener_ReadOnlyCollectionOfMatcherIsEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
        {
            var tl1a = new TestJobListener("tl1");
            var tl1b = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

            _manager.AddJobListener(tl1a, groupMatcher);

            IReadOnlyCollection<IMatcher<JobKey>> setMatchers = Array.Empty<IMatcher<JobKey>>();

            _manager.AddJobListener(tl1b, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1b, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1b.Name);
            Assert.IsNull(matchers);
        }

        [Test]
        public void AddJobListener_ReadOnlyCollectionOfMatcherIsNotEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
        {
            var tl1a = new TestJobListener("tl1");
            var tl1b = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

            _manager.AddJobListener(tl1a, groupMatcher);

            IReadOnlyCollection<IMatcher<JobKey>> setMatchers = new IMatcher<JobKey>[] { nameMatcher };

            _manager.AddJobListener(tl1b, setMatchers);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1b, jobListeners[0]);

            var matchers = _manager.GetJobListenerMatchers(tl1b.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(1, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(new IMatcher<JobKey>[] { nameMatcher }));
        }

        [Test]
        public void AddJobListenerMatcher_ListenerNameIsNull()
        {
            const string listenerName = null;

            var matcher = GroupMatcher<JobKey>.GroupEquals("foo");

            try
            {
                _manager.AddJobListenerMatcher(listenerName, matcher);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(listenerName), ex.ParamName);
            }
        }

        [Test]
        public void AddJobListenerMatcher_MatcherIsNull()
        {
            const IMatcher<JobKey> matcher = null;

            try
            {
                _manager.AddJobListenerMatcher("A", matcher);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(matcher), ex.ParamName);
            }
        }

        [Test]
        public void AddJobListenerMatcher_ListenerWasFirstRegisteredWithoutMatchers()
        {
            var tl1 = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

            _manager.AddJobListener(tl1);
            Assert.IsTrue(_manager.AddJobListenerMatcher(tl1.Name, groupMatcher));
            Assert.IsTrue(_manager.AddJobListenerMatcher(tl1.Name, nameMatcher));

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(2, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(new IMatcher<JobKey>[] { groupMatcher, nameMatcher }));
        }

        [Test]
        public void AddJobListenerMatcher_ListenerWasFirstRegisteredWithMatchers()
        {
            var tl1 = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

            _manager.AddJobListener(tl1, nameMatcher);
            Assert.IsTrue(_manager.AddJobListenerMatcher(tl1.Name, groupMatcher));

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(2, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(new IMatcher<JobKey>[] { nameMatcher, groupMatcher }));
        }

        [Test]
        public void GetJobListener_ShouldThrowKeyNotFoundExceptionWhenNoJobListenerExistsWithSpecifiedName()
        {
            const string name = "A";

            Assert.Throws<KeyNotFoundException>(() => _manager.GetJobListener(name));

            _manager.AddJobListener(new TestJobListener("B"));

            Assert.Throws<KeyNotFoundException>(() => _manager.GetJobListener(name));
        }

        [Test]
        public void GetJobListener_ShouldThrowArgumentNullExceptionWhenNameIsNull()
        {
            const string name = null;

            try
            {
                _manager.GetJobListener(name);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(name), ex.ParamName);
            }

            _manager.AddJobListener(new TestJobListener("B"));

            try
            {
                _manager.GetJobListener(name);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(name), ex.ParamName);
            }
        }

        [Test]
        public void GetJobListeners_ShouldReturnShallowClone()
        {
            var tl1 = new TestJobListener("tl1");
            var tl2 = new TestJobListener("tl2");

            _manager.AddJobListener(tl1);

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);

            jobListeners[0] = tl2;

            jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);
        }

        [Test]
        public void GetJobListeners_ShouldReturnEmptyArrayWhenNoJobListenersHaveBeenAdded()
        {
            var jobListeners = _manager.GetJobListeners();
            Assert.AreSame(Array.Empty<IJobListener>(), jobListeners);
        }

        [Test]
        public void GetJobListenerMatchers_ListenerNameIsNull()
        {
            const string listenerName = null;

            try
            {
                _manager.GetJobListenerMatchers(listenerName);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(listenerName), ex.ParamName);
            }
        }

        [Test]
        public void RemoveJobListener_NameIsNull()
        {
            const string name = null;

            try
            {
                _manager.RemoveJobListener(name);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(name), ex.ParamName);
            }
        }

        [Test]
        public void RemoveJobListener_NoMatchersRegisteredForSpecifiedJobListener()
        {
            var tl1 = new TestJobListener("tl1");
            var tl2 = new TestJobListener("tl2");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

            _manager.AddJobListener(tl1, groupMatcher);
            _manager.AddJobListener(tl2);

            Assert.IsTrue(_manager.RemoveJobListener(tl2.Name));
            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);

            var matchersTl2 = _manager.GetJobListenerMatchers(tl2.Name);
            Assert.IsNull(matchersTl2);

            var matchersTl1 = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchersTl1);
            Assert.IsTrue(matchersTl1.SequenceEqual(new[] { groupMatcher }));
        }

        [Test]
        public void RemoveJobListener_MatchersRegisteredForSpecifiedJobListener()
        {
            var tl1 = new TestJobListener("tl1");
            var tl2 = new TestJobListener("tl2");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

            _manager.AddJobListener(tl1, groupMatcher);
            _manager.AddJobListener(tl2, nameMatcher);

            Assert.IsTrue(_manager.RemoveJobListener(tl2.Name));

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);

            var matchersTl2 = _manager.GetJobListenerMatchers(tl2.Name);
            Assert.IsNull(matchersTl2);

            var matchersTl1 = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchersTl1);
            Assert.IsTrue(matchersTl1.SequenceEqual(new[] { groupMatcher }));

            // Ensure adding back the listener without matchers does not "magically" recover the
            // matchers that were registered before we removed the listener
            _manager.AddJobListener(tl2);

            jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(2, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);
            Assert.AreSame(tl2, jobListeners[1]);

            matchersTl2 = _manager.GetJobListenerMatchers(tl2.Name);
            Assert.IsNull(matchersTl2);
        }

        [Test]
        public void RemoveJobListener_NoJobListenerRegisteredWithSpecifiedName()
        {
            var tl1 = new TestJobListener("tl1");
            var tl2 = new TestJobListener("tl2");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

            _manager.AddJobListener(tl1, groupMatcher);

            Assert.IsFalse(_manager.RemoveJobListener(tl2.Name));

            var jobListeners = _manager.GetJobListeners();
            Assert.IsNotNull(jobListeners);
            Assert.AreEqual(1, jobListeners.Length);
            Assert.AreSame(tl1, jobListeners[0]);
        }

        [Test]
        public void RemoveJobListenerMatcher_ListenerNameIsNull()
        {
            const string listenerName = null;

            var matcher = GroupMatcher<JobKey>.GroupEquals("foo");

            try
            {
                _manager.RemoveJobListenerMatcher(listenerName, matcher);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(listenerName), ex.ParamName);
            }
        }

        [Test]
        public void RemoveJobListenerMatcher_MatcherIsNull()
        {
            const IMatcher<JobKey> matcher = null;

            try
            {
                _manager.RemoveJobListenerMatcher("A", matcher);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(matcher), ex.ParamName);
            }
        }

        [Test]
        public void RemoveJobListenerMatcher_MatcherWasAddedForSpecifiedListener()
        {
            var tl1 = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

            _manager.AddJobListener(tl1, groupMatcher, nameMatcher);

            Assert.IsTrue(_manager.RemoveJobListenerMatcher(tl1.Name, groupMatcher));

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(1, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(new[] { nameMatcher }));

            Assert.IsTrue(_manager.RemoveJobListenerMatcher(tl1.Name, nameMatcher));
            Assert.IsNull(_manager.GetJobListenerMatchers(tl1.Name));
        }

        [Test]
        public void RemoveJobListenerMatcher_MatcherWasNotAddedForSpecifiedListener()
        {
            var tl1 = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

            _manager.AddJobListener(tl1);

            Assert.False(_manager.RemoveJobListenerMatcher(tl1.Name, groupMatcher));
            Assert.IsNull(_manager.GetJobListenerMatchers(tl1.Name));

            _manager.AddJobListenerMatcher(tl1.Name, nameMatcher);

            Assert.False(_manager.RemoveJobListenerMatcher(tl1.Name, groupMatcher));

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(1, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(new[] { nameMatcher }));
        }

        [Test]
        public void RemoveJobListenerMatcher_JobListenerIsNotRegistered()
        {
            const string listenerName = "A";

            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

            Assert.False(_manager.RemoveJobListenerMatcher(listenerName, groupMatcher));
            Assert.IsNull(_manager.GetJobListenerMatchers(listenerName));
        }

        [Test]
        public void SetJobListenerMatchers_ListenerNameIsNull()
        {
            const string listenerName = null;

            var matchers = new[] { GroupMatcher<JobKey>.GroupEquals("foo") };

            try
            {
                _manager.SetJobListenerMatchers(listenerName, matchers);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(listenerName), ex.ParamName);
            }
        }

        [Test]
        public void SetJobListenerMatchers_MatchersIsNull()
        {
            const IReadOnlyCollection<IMatcher<JobKey>> matchers = null;

            try
            {
                _manager.SetJobListenerMatchers("A", matchers);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.IsNull(ex.InnerException);
                Assert.AreEqual(nameof(matchers), ex.ParamName);
            }
        }

        [Test]
        public void SetJobListenerMatchers_MatchersIsEmpty_ListenerDoesNotExist()
        {
            var tl1 = new TestJobListener("tl1");
            var setMatchers = Array.Empty<IMatcher<JobKey>>();

            Assert.IsFalse(_manager.SetJobListenerMatchers(tl1.Name, setMatchers));
            Assert.IsNull(_manager.GetJobListenerMatchers(tl1.Name));
        }

        [Test]
        public void SetJobListenerMatchers_MatchersIsEmpty_ListenerHasNoMatchers()
        {
            var tl1 = new TestJobListener("tl1");
            var setMatchers = Array.Empty<IMatcher<JobKey>>();

            _manager.AddJobListener(tl1);

            Assert.IsTrue(_manager.SetJobListenerMatchers(tl1.Name, setMatchers));
            Assert.IsNull(_manager.GetJobListenerMatchers(tl1.Name));
        }

        [Test]
        public void SetJobListenerMatchers_MatchersIsEmpty_ListenerHasMatchers()
        {
            var tl1 = new TestJobListener("tl1");

            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var setMatchers = Array.Empty<IMatcher<JobKey>>();

            _manager.AddJobListener(tl1, groupMatcher);

            Assert.IsTrue(_manager.SetJobListenerMatchers(tl1.Name, setMatchers));
            Assert.IsNull(_manager.GetJobListenerMatchers(tl1.Name));
        }

        [Test]
        public void SetJobListenerMatchers_MatchersIsNotEmpty_ListenerDoesNotExist()
        {
            var tl1 = new TestJobListener("tl1");

            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");
            var setMatchers = new IMatcher<JobKey>[] { groupMatcher, nameMatcher };

            Assert.IsFalse(_manager.SetJobListenerMatchers(tl1.Name, setMatchers));
            Assert.IsNull(_manager.GetJobListenerMatchers(tl1.Name));
        }

        [Test]
        public void SetJobListenerMatchers_MatchersIsNotEmpty_ListenerHasNoMatchers()
        {
            var tl1 = new TestJobListener("tl1");
            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");
            var setMatchers = new IMatcher<JobKey>[] { groupMatcher, nameMatcher };

            _manager.AddJobListener(tl1);

            Assert.IsTrue(_manager.SetJobListenerMatchers(tl1.Name, setMatchers));

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(setMatchers.Length, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(setMatchers));
        }

        [Test]
        public void SetJobListenerMatchers_MatchersIsNotEmpty_ListenerHasMatchers()
        {
            var tl1 = new TestJobListener("tl1");

            var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
            var nameMatcher = NameMatcher<JobKey>.NameContains("foo");
            var setMatchers = new IMatcher<JobKey>[] { nameMatcher };

            _manager.AddJobListener(tl1, groupMatcher);

            Assert.IsTrue(_manager.SetJobListenerMatchers(tl1.Name, setMatchers));

            var matchers = _manager.GetJobListenerMatchers(tl1.Name);
            Assert.IsNotNull(matchers);
            Assert.AreEqual(setMatchers.Length, matchers.Count);
            Assert.IsTrue(matchers.SequenceEqual(setMatchers));
        }

        [Test]
        public void testManagementOfTriggerListeners()
        {
            var tl1 = new TestTriggerListener("tl1");
            var tl2 = new TestTriggerListener("tl2");

            // test adding listener without matcher
            _manager.AddTriggerListener(tl1);
            Assert.AreEqual(1, _manager.GetTriggerListeners().Count, "Unexpected size of listener list");

            // test adding listener with matcher
            _manager.AddTriggerListener(tl2, GroupMatcher<TriggerKey>.GroupEquals("foo"));
            Assert.AreEqual(2, _manager.GetTriggerListeners().Count, "Unexpected size of listener list");

            // test removing a listener
            _manager.RemoveTriggerListener("tl1");
            Assert.AreEqual(1, _manager.GetTriggerListeners().Count, "Unexpected size of listener list");

            // test adding a matcher
            _manager.AddTriggerListenerMatcher("tl2", NameMatcher<TriggerKey>.NameContains("foo"));
            Assert.AreEqual(2, _manager.GetTriggerListenerMatchers("tl2").Count, "Unexpected size of listener's matcher list");
        }

        [Test]
        public void TestManagementOfSchedulerListeners()
        {
            var tl1 = new TestSchedulerListener();
            var tl2 = new TestSchedulerListener();

            // test adding listener without matcher
            _manager.AddSchedulerListener(tl1);
            Assert.AreEqual(1, _manager.GetSchedulerListeners().Count, "Unexpected size of listener list");

            // test adding listener with matcher
            _manager.AddSchedulerListener(tl2);
            Assert.AreEqual(2, _manager.GetSchedulerListeners().Count, "Unexpected size of listener list");

            // test removing a listener
            _manager.RemoveSchedulerListener(tl1);
            Assert.AreEqual(1, _manager.GetSchedulerListeners().Count, "Unexpected size of listener list");
        }
    }
}