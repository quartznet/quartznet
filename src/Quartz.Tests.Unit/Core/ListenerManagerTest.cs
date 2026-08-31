using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Tests for <see cref="ListenerManagerImpl" />.
/// </summary>
public class ListenerManagerTest
{
    private ListenerManagerImpl _manager;

    private sealed class TestJobListener : IJobListener
    {
        public TestJobListener(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class TestTriggerListener : ITriggerListener
    {
        public TestTriggerListener(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
    private sealed class TestSchedulerListener : ISchedulerListener;

    private sealed class OtherSchedulerListener : ISchedulerListener;

    [SetUp]
    public void SetUp()
    {
        _manager = new ListenerManagerImpl();
    }

    /// <summary>
    /// The matchers the named job listener was registered with, or none when no listener answers to
    /// that name — "matches everything" and "there is nothing to match" are both the absence of a
    /// restriction.
    /// </summary>
    private IReadOnlyList<IMatcher<JobKey>> JobMatchers(string listenerName)
    {
        foreach (AttachedListener<IJobListener, JobKey> attached in _manager.GetAttachedJobListeners())
        {
            if (attached.Name == listenerName)
            {
                return attached.Matchers;
            }
        }

        return [];
    }

    /// <inheritdoc cref="JobMatchers" />
    private IReadOnlyList<IMatcher<TriggerKey>> TriggerMatchers(string listenerName)
    {
        foreach (AttachedListener<ITriggerListener, TriggerKey> attached in _manager.GetAttachedTriggerListeners())
        {
            if (attached.Name == listenerName)
            {
                return attached.Matchers;
            }
        }

        return [];
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_JobListenerIsNull()
    {
        const IJobListener jobListener = null;
        var matchers = Array.Empty<IMatcher<JobKey>>();

        try
        {
            _manager.AddJobListener(jobListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(jobListener)));
            });
        }
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_NameOfJobListenerIsNull()
    {
        var jobListener = new TestJobListener(null);
        var matchers = Array.Empty<IMatcher<JobKey>>();

        try
        {
            _manager.AddJobListener(jobListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(jobListener)));
            });
        }
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_NameOfJobListenerIsEmpty()
    {
        var jobListener = new TestJobListener(String.Empty);
        var matchers = Array.Empty<IMatcher<JobKey>>();

        try
        {
            _manager.AddJobListener(jobListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(jobListener)));
            });
        }
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_MatchersIsNull_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        const IMatcher<JobKey>[] setMatchers = null;

        var tl1a = new TestJobListener("tl1");
        var tl1b = new TestJobListener("tl1");
        var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

        _manager.AddJobListener(tl1a, groupMatcher);
        _manager.AddJobListener(tl1b, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = JobMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_MatchersIsNull_JobListenerDoesntAlreadyExist()
    {
        const IMatcher<JobKey>[] setMatchers = null;

        var tl1 = new TestJobListener("tl1");

        _manager.AddJobListener(tl1, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>{
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });

        var matchers = JobMatchers(tl1.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_MatchersIsEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestJobListener("tl1");
        var tl1b = new TestJobListener("tl1");
        var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
        var setMatchers = Array.Empty<IMatcher<JobKey>>();

        _manager.AddJobListener(tl1a, groupMatcher);
        _manager.AddJobListener(tl1b, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = JobMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_MatchersIsEmpty_JobListenerDoesntAlreadyExist()
    {
        var tl1 = new TestJobListener("tl1");
        var setMatchers = Array.Empty<IMatcher<JobKey>>();

        _manager.AddJobListener(tl1, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });

        var matchers = JobMatchers(tl1.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_MatchersIsNotEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestJobListener("tl1");
        var tl1b = new TestJobListener("tl1");
        var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
        var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

        _manager.AddJobListener(tl1a, groupMatcher);

        var setMatchers = new IMatcher<JobKey>[] { nameMatcher };

        _manager.AddJobListener(tl1b, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = JobMatchers(tl1b.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchers, Is.Not.Null);
            Assert.That(matchers, Has.Count.EqualTo(1));
            Assert.That(matchers.SequenceEqual([nameMatcher]), Is.True);
        });
    }

    [Test]
    public void AddJobListener_ArrayOfMatcher_MatchersIsNotEmpty_JobListenerDoesntAlreadyExist()
    {
        var tl1 = new TestJobListener("tl1");
        var nameMatcher = NameMatcher<JobKey>.NameContains("foo");
        var setMatchers = new IMatcher<JobKey>[] { nameMatcher };

        _manager.AddJobListener(tl1, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });

        var matchers = JobMatchers(tl1.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchers, Is.Not.Null);
            Assert.That(matchers, Has.Count.EqualTo(1));
            Assert.That(matchers.SequenceEqual([nameMatcher]), Is.True);
        });
    }

    [Test]
    public void AddJobListener_ReadOnlyCollectionOfMatcher_JobListenerIsNull()
    {
        const IJobListener jobListener = null;
        IReadOnlyCollection<IMatcher<JobKey>> matchers = [];

        try
        {
            _manager.AddJobListener(jobListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(jobListener)));
            });
        }
    }

    [Test]
    public void AddJobListener_ReadOnlyCollectionOfMatcher_NameOfJobListenerIsNull()
    {
        var jobListener = new TestJobListener(null);
        IReadOnlyCollection<IMatcher<JobKey>> matchers = [];

        try
        {
            _manager.AddJobListener(jobListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(jobListener)));
            });
        }
    }

    [Test]
    public void AddJobListener_ReadOnlyCollectionOfMatcher_NameOfJobListenerIsEmpty()
    {
        var jobListener = new TestJobListener(String.Empty);
        IReadOnlyCollection<IMatcher<JobKey>> matchers = [];

        try
        {
            _manager.AddJobListener(jobListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(jobListener)));
            });
        }
    }

    [Test]
    public void AddJobListener_ReadOnlyCollectionOfMatcher_MatchersIsNull_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        const IReadOnlyCollection<IMatcher<JobKey>> setMatchers = null;

        var tl1a = new TestJobListener("tl1");
        var tl1b = new TestJobListener("tl1");
        var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

        _manager.AddJobListener(tl1a, groupMatcher);
        _manager.AddJobListener(tl1b, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = JobMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddJobListener_ReadOnlyCollectionOfMatcher_MatchersIsEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestJobListener("tl1");
        var tl1b = new TestJobListener("tl1");
        var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

        _manager.AddJobListener(tl1a, groupMatcher);

        IReadOnlyCollection<IMatcher<JobKey>> setMatchers = [];

        _manager.AddJobListener(tl1b, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = JobMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddJobListener_ReadOnlyCollectionOfMatcher_MatchersIsNotEmpty_JobListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestJobListener("tl1");
        var tl1b = new TestJobListener("tl1");
        var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");
        var nameMatcher = NameMatcher<JobKey>.NameContains("foo");

        _manager.AddJobListener(tl1a, groupMatcher);

        IReadOnlyCollection<IMatcher<JobKey>> setMatchers = [nameMatcher];

        _manager.AddJobListener(tl1b, setMatchers);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = JobMatchers(tl1b.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchers, Is.Not.Null);
            Assert.That(matchers, Has.Count.EqualTo(1));
            Assert.That(matchers.SequenceEqual([nameMatcher]), Is.True);
        });
    }

    [Test]
    public void GetJobListener_ShouldReturnNullWhenNoJobListenerExistsWithSpecifiedName()
    {
        const string name = "A";

        _manager.GetJobListener(name).Should().BeNull();

        _manager.AddJobListener(new TestJobListener("B"));

        _manager.GetJobListener(name).Should().BeNull("another listener's presence is not this listener's");
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
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(name)));
            });
        }

        _manager.AddJobListener(new TestJobListener("B"));

        try
        {
            _manager.GetJobListener(name);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(name)));
            });
        }
    }

    [Test]
    public void GetJobListeners_ShouldReturnSnapshot()
    {
        TestJobListener tl1 = new("tl1");
        TestJobListener tl2 = new("tl2");

        _manager.AddJobListener(tl1);

        IReadOnlyList<IJobListener> jobListeners = _manager.GetJobListeners();
        jobListeners.Should().ContainSingle().Which.Should().BeSameAs(tl1);

        _manager.AddJobListener(tl2);

        jobListeners.Should().ContainSingle("the returned list is a snapshot, not a live view of the manager")
            .Which.Should().BeSameAs(tl1);
        _manager.GetJobListeners().Should().HaveCount(2);
    }

    [Test]
    public void GetJobListeners_ShouldReturnEmptyArrayWhenNoJobListenersHaveBeenAdded()
    {
        var jobListeners = _manager.GetJobListeners();
        Assert.That(jobListeners, Is.SameAs(Array.Empty<IJobListener>()));
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
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(name)));
            });
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

        Assert.That(_manager.RemoveJobListener(tl2.Name), Is.True);
        var jobListeners = _manager.GetJobListeners();
        Assert.That(jobListeners, Is.Not.Null);
        Assert.That(jobListeners, Has.Count.EqualTo(1));
        Assert.That(jobListeners[0], Is.SameAs(tl1));

        var matchersTl2 = JobMatchers(tl2.Name);
        Assert.That(matchersTl2, Is.Empty);

        var matchersTl1 = JobMatchers(tl1.Name);
        Assert.That(matchersTl1, Is.Not.Null);
        Assert.That(matchersTl1.SequenceEqual([groupMatcher]), Is.True);
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

        Assert.That(_manager.RemoveJobListener(tl2.Name), Is.True);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });

        var matchersTl2 = JobMatchers(tl2.Name);
        Assert.That(matchersTl2, Is.Empty);

        var matchersTl1 = JobMatchers(tl1.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchersTl1, Is.Not.Null);
            Assert.That(matchersTl1.SequenceEqual([groupMatcher]), Is.True);
        });

        // Ensure adding back the listener without matchers does not "magically" recover the
        // matchers that were registered before we removed the listener
        _manager.AddJobListener(tl2);

        jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(2));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
            Assert.That(jobListeners[1], Is.SameAs(tl2));
        });

        matchersTl2 = JobMatchers(tl2.Name);
        Assert.That(matchersTl2, Is.Empty);
    }

    [Test]
    public void RemoveJobListener_NoJobListenerRegisteredWithSpecifiedName()
    {
        var tl1 = new TestJobListener("tl1");
        var tl2 = new TestJobListener("tl2");
        var groupMatcher = GroupMatcher<JobKey>.GroupEquals("foo");

        _manager.AddJobListener(tl1, groupMatcher);

        Assert.That(_manager.RemoveJobListener(tl2.Name), Is.False);

        var jobListeners = _manager.GetJobListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_TriggerListenerIsNull()
    {
        const ITriggerListener triggerListener = null;
        var matchers = Array.Empty<IMatcher<TriggerKey>>();

        try
        {
            _manager.AddTriggerListener(triggerListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(triggerListener)));
            });
        }
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_NameOfTriggerListenerIsNull()
    {
        var triggerListener = new TestTriggerListener(null);
        var matchers = Array.Empty<IMatcher<TriggerKey>>();

        try
        {
            _manager.AddTriggerListener(triggerListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(triggerListener)));
            });
        }
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_NameOfTriggerListenerIsEmpty()
    {
        var triggerListener = new TestTriggerListener(String.Empty);
        var matchers = Array.Empty<IMatcher<TriggerKey>>();

        try
        {
            _manager.AddTriggerListener(triggerListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(triggerListener)));
            });
        }
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_MatchersIsNull_TriggerListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        const IMatcher<TriggerKey>[] setMatchers = null;

        var tl1a = new TestTriggerListener("tl1");
        var tl1b = new TestTriggerListener("tl1");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");

        _manager.AddTriggerListener(tl1a, groupMatcher);
        _manager.AddTriggerListener(tl1b, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.That(jobListeners, Is.Not.Null);
        Assert.That(jobListeners, Has.Count.EqualTo(1));
        Assert.That(jobListeners[0], Is.SameAs(tl1b));

        var matchers = TriggerMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_MatchersIsNull_TriggerListenerDoesntAlreadyExist()
    {
        const IMatcher<TriggerKey>[] setMatchers = null;

        var tl1 = new TestTriggerListener("tl1");

        _manager.AddTriggerListener(tl1, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.That(jobListeners, Is.Not.Null);
        Assert.That(jobListeners, Has.Count.EqualTo(1));
        Assert.That(jobListeners[0], Is.SameAs(tl1));

        var matchers = TriggerMatchers(tl1.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_MatchersIsEmpty_TriggerListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestTriggerListener("tl1");
        var tl1b = new TestTriggerListener("tl1");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");
        var setMatchers = Array.Empty<IMatcher<TriggerKey>>();

        _manager.AddTriggerListener(tl1a, groupMatcher);
        _manager.AddTriggerListener(tl1b, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.That(jobListeners, Is.Not.Null);
        Assert.That(jobListeners, Has.Count.EqualTo(1));
        Assert.That(jobListeners[0], Is.SameAs(tl1b));

        var matchers = TriggerMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_MatchersIsEmpty_TriggerListenerDoesntAlreadyExist()
    {
        var tl1 = new TestTriggerListener("tl1");
        var setMatchers = Array.Empty<IMatcher<TriggerKey>>();

        _manager.AddTriggerListener(tl1, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.That(jobListeners, Is.Not.Null);
        Assert.That(jobListeners, Has.Count.EqualTo(1));
        Assert.That(jobListeners[0], Is.SameAs(tl1));

        var matchers = TriggerMatchers(tl1.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_MatchersIsNotEmpty_TriggerListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestTriggerListener("tl1");
        var tl1b = new TestTriggerListener("tl1");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");
        var nameMatcher = NameMatcher<TriggerKey>.NameContains("foo");

        _manager.AddTriggerListener(tl1a, groupMatcher);

        var setMatchers = new IMatcher<TriggerKey>[] { nameMatcher };

        _manager.AddTriggerListener(tl1b, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = TriggerMatchers(tl1b.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchers, Is.Not.Null);
            Assert.That(matchers, Has.Count.EqualTo(1));
            Assert.That(matchers.SequenceEqual([nameMatcher]), Is.True);
        });
    }

    [Test]
    public void AddTriggerListener_ArrayOfMatcher_MatchersIsNotEmpty_TriggerListenerDoesntAlreadyExist()
    {
        var tl1 = new TestTriggerListener("tl1");
        var nameMatcher = NameMatcher<TriggerKey>.NameContains("foo");
        var setMatchers = new IMatcher<TriggerKey>[] { nameMatcher };

        _manager.AddTriggerListener(tl1, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });

        var matchers = TriggerMatchers(tl1.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchers, Is.Not.Null);
            Assert.That(matchers, Has.Count.EqualTo(1));
            Assert.That(matchers.SequenceEqual([nameMatcher]), Is.True);
        });
    }

    [Test]
    public void AddTriggerListener_ReadOnlyCollectionOfMatcher_TriggerListenerIsNull()
    {
        const ITriggerListener triggerListener = null;
        IReadOnlyCollection<IMatcher<TriggerKey>> matchers = [];

        try
        {
            _manager.AddTriggerListener(triggerListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(triggerListener)));
            });
        }
    }

    [Test]
    public void AddTriggerListener_ReadOnlyCollectionOfMatcher_NameOfTriggerListenerIsNull()
    {
        var triggerListener = new TestTriggerListener(null);
        IReadOnlyCollection<IMatcher<TriggerKey>> matchers = [];

        try
        {
            _manager.AddTriggerListener(triggerListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(triggerListener)));
            });
        }
    }

    [Test]
    public void AddTriggerListener_ReadOnlyCollectionOfMatcher_NameOfTriggerListenerIsEmpty()
    {
        var triggerListener = new TestTriggerListener(String.Empty);
        IReadOnlyCollection<IMatcher<TriggerKey>> matchers = [];

        try
        {
            _manager.AddTriggerListener(triggerListener, matchers);
            Assert.Fail();
        }
        catch (ArgumentException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(triggerListener)));
            });
        }
    }

    [Test]
    public void AddTriggerListener_ReadOnlyCollectionOfMatcher_MatchersIsNull_TriggerListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        const IReadOnlyCollection<IMatcher<TriggerKey>> setMatchers = null;

        var tl1a = new TestTriggerListener("tl1");
        var tl1b = new TestTriggerListener("tl1");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");

        _manager.AddTriggerListener(tl1a, groupMatcher);
        _manager.AddTriggerListener(tl1b, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = TriggerMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddTriggerListener_ReadOnlyCollectionOfMatcher_MatchersIsEmpty_TriggerListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestTriggerListener("tl1");
        var tl1b = new TestTriggerListener("tl1");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");

        _manager.AddTriggerListener(tl1a, groupMatcher);

        IReadOnlyCollection<IMatcher<TriggerKey>> setMatchers = [];

        _manager.AddTriggerListener(tl1b, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = TriggerMatchers(tl1b.Name);
        Assert.That(matchers, Is.Empty);
    }

    [Test]
    public void AddTriggerListener_ReadOnlyCollectionOfMatcher_MatchersIsNotEmpty_TriggerListenerWithSameNameAlreadyExistsWithOneOrMoreMatchers()
    {
        var tl1a = new TestTriggerListener("tl1");
        var tl1b = new TestTriggerListener("tl1");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");
        var nameMatcher = NameMatcher<TriggerKey>.NameContains("foo");

        _manager.AddTriggerListener(tl1a, groupMatcher);

        IReadOnlyCollection<IMatcher<TriggerKey>> setMatchers = [nameMatcher];

        _manager.AddTriggerListener(tl1b, setMatchers);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1b));
        });

        var matchers = TriggerMatchers(tl1b.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchers, Is.Not.Null);
            Assert.That(matchers, Has.Count.EqualTo(1));
            Assert.That(matchers.SequenceEqual([nameMatcher]), Is.True);
        });
    }

    [Test]
    public void GetTriggerListener_ShouldReturnNullWhenNoTriggerListenerExistsWithSpecifiedName()
    {
        const string name = "A";

        _manager.GetTriggerListener(name).Should().BeNull();

        _manager.AddTriggerListener(new TestTriggerListener("B"));

        _manager.GetTriggerListener(name).Should().BeNull("another listener's presence is not this listener's");
    }

    [Test]
    public void GetTriggerListener_ShouldThrowArgumentNullExceptionWhenNameIsNull()
    {
        const string name = null;

        try
        {
            _manager.GetTriggerListener(name);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(name)));
            });
        }

        _manager.AddTriggerListener(new TestTriggerListener("B"));

        try
        {
            _manager.GetTriggerListener(name);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(name)));
            });
        }
    }

    [Test]
    public void GetTriggerListeners_ShouldReturnSnapshot()
    {
        TestTriggerListener tl1 = new("tl1");
        TestTriggerListener tl2 = new("tl2");

        _manager.AddTriggerListener(tl1);

        IReadOnlyList<ITriggerListener> triggerListeners = _manager.GetTriggerListeners();
        triggerListeners.Should().ContainSingle().Which.Should().BeSameAs(tl1);

        _manager.AddTriggerListener(tl2);

        triggerListeners.Should().ContainSingle("the returned list is a snapshot, not a live view of the manager")
            .Which.Should().BeSameAs(tl1);
        _manager.GetTriggerListeners().Should().HaveCount(2);
    }

    [Test]
    public void GetTriggerListeners_ShouldReturnEmptyArrayWhenNoTriggerListenersHaveBeenAdded()
    {
        var jobListeners = _manager.GetTriggerListeners();
        Assert.That(jobListeners, Is.SameAs(Array.Empty<ITriggerListener>()));
    }

    [Test]
    public void RemoveTriggerListener_NameIsNull()
    {
        const string name = null;

        try
        {
            _manager.RemoveTriggerListener(name);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ex.InnerException, Is.Null);
                Assert.That(ex.ParamName, Is.EqualTo(nameof(name)));
            });
        }
    }

    [Test]
    public void RemoveTriggerListener_NoMatchersRegisteredForSpecifiedTriggerListener()
    {
        var tl1 = new TestTriggerListener("tl1");
        var tl2 = new TestTriggerListener("tl2");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");

        _manager.AddTriggerListener(tl1, groupMatcher);
        _manager.AddTriggerListener(tl2);

        Assert.That(_manager.RemoveTriggerListener(tl2.Name), Is.True);
        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });

        var matchersTl2 = TriggerMatchers(tl2.Name);
        Assert.That(matchersTl2, Is.Empty);

        var matchersTl1 = TriggerMatchers(tl1.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchersTl1, Is.Not.Null);
            Assert.That(matchersTl1.SequenceEqual([groupMatcher]), Is.True);
        });
    }

    [Test]
    public void RemoveTriggerListener_MatchersRegisteredForSpecifiedTriggerListener()
    {
        var tl1 = new TestTriggerListener("tl1");
        var tl2 = new TestTriggerListener("tl2");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");
        var nameMatcher = NameMatcher<TriggerKey>.NameContains("foo");

        _manager.AddTriggerListener(tl1, groupMatcher);
        _manager.AddTriggerListener(tl2, nameMatcher);

        Assert.That(_manager.RemoveTriggerListener(tl2.Name), Is.True);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });

        var matchersTl2 = TriggerMatchers(tl2.Name);
        Assert.That(matchersTl2, Is.Empty);

        var matchersTl1 = TriggerMatchers(tl1.Name);
        Assert.Multiple(() =>
        {
            Assert.That(matchersTl1, Is.Not.Null);
            Assert.That(matchersTl1.SequenceEqual([groupMatcher]), Is.True);
        });

        // Ensure adding back the listener without matchers does not "magically" recover the
        // matchers that were registered before we removed the listener
        _manager.AddTriggerListener(tl2);

        jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(2));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
            Assert.That(jobListeners[1], Is.SameAs(tl2));
        });

        matchersTl2 = TriggerMatchers(tl2.Name);
        Assert.That(matchersTl2, Is.Empty);
    }

    [Test]
    public void RemoveTriggerListener_NoTriggerListenerRegisteredWithSpecifiedName()
    {
        var tl1 = new TestTriggerListener("tl1");
        var tl2 = new TestTriggerListener("tl2");
        var groupMatcher = GroupMatcher<TriggerKey>.GroupEquals("foo");

        _manager.AddTriggerListener(tl1, groupMatcher);

        Assert.That(_manager.RemoveTriggerListener(tl2.Name), Is.False);

        var jobListeners = _manager.GetTriggerListeners();
        Assert.Multiple(() =>
        {
            Assert.That(jobListeners, Is.Not.Null);
            Assert.That(jobListeners, Has.Count.EqualTo(1));
            Assert.That(jobListeners[0], Is.SameAs(tl1));
        });
    }

    [Test]
    public void TestManagementOfSchedulerListeners()
    {
        ISchedulerListener tl1 = new TestSchedulerListener();
        ISchedulerListener tl2 = new OtherSchedulerListener();

        _manager.AddSchedulerListener(tl1);
        _manager.GetSchedulerListeners().Should().ContainSingle("Unexpected size of listener list");

        _manager.AddSchedulerListener(tl2);
        _manager.GetSchedulerListeners().Should().HaveCount(2, "Unexpected size of listener list");

        _manager.RemoveSchedulerListener(tl1.Name);
        _manager.GetSchedulerListeners().Should().ContainSingle("Unexpected size of listener list");
    }


}