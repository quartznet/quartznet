using System.Net.Http;

using FakeItEasy;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

[NonParallelizable]
public sealed class SchedulerRepositoryTest
{
    private SchedulerRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        repository = new SchedulerRepository();
    }

    [Test]
    public void Bind_SingleScheduler_CanLookupByName()
    {
        IScheduler scheduler = CreateFakeScheduler("TestSched", "instance-1");
        repository.Bind(scheduler);

        IScheduler result = repository.Lookup("TestSched");
        Assert.That(result, Is.SameAs(scheduler));
    }

    [Test]
    public void Bind_TwoSchedulers_SameName_DifferentInstanceId_BothStored()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        List<IScheduler> all = repository.LookupAll();
        Assert.That(all, Has.Count.EqualTo(2));
        Assert.That(all, Does.Contain(sched1));
        Assert.That(all, Does.Contain(sched2));
    }

    [Test]
    public void Bind_TwoSchedulers_SameName_SameInstanceId_Throws()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-1");

        repository.Bind(sched1);

        Assert.Throws<SchedulerException>(() => repository.Bind(sched2));
    }

    [Test]
    public void Bind_TwoSchedulers_DifferentName_BothStored()
    {
        IScheduler sched1 = CreateFakeScheduler("Sched1", "instance-1");
        IScheduler sched2 = CreateFakeScheduler("Sched2", "instance-1");

        repository.Bind(sched1);
        repository.Bind(sched2);

        Assert.That(repository.Lookup("Sched1"), Is.SameAs(sched1));
        Assert.That(repository.Lookup("Sched2"), Is.SameAs(sched2));
    }

    [Test]
    public void Bind_WithExplicitInstanceId_CanLookupByNameAndInstanceId()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "irrelevant");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "irrelevant");

        // Explicit instance ID overrides SchedulerInstanceId for repository keying
        repository.Bind(sched1, "node-1");
        repository.Bind(sched2, "node-2");

        Assert.That(repository.Lookup("MyCluster", "node-1"), Is.SameAs(sched1));
        Assert.That(repository.Lookup("MyCluster", "node-2"), Is.SameAs(sched2));
    }

    [Test]
    public void Bind_WithExplicitInstanceId_SameId_Throws()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "irrelevant");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "irrelevant");

        repository.Bind(sched1, "node-1");

        Assert.Throws<SchedulerException>(() => repository.Bind(sched2, "node-1"));
    }

    [Test]
    public void Lookup_ByName_ReturnsFirstBound()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        Assert.That(repository.Lookup("MyCluster"), Is.SameAs(sched1));
    }

    [Test]
    public void Lookup_ByNameAndInstanceId_ReturnsCorrectScheduler()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        Assert.That(repository.Lookup("MyCluster", "node-1"), Is.SameAs(sched1));
        Assert.That(repository.Lookup("MyCluster", "node-2"), Is.SameAs(sched2));
    }

    [Test]
    public void Lookup_ByNameAndInstanceId_NotFound_ReturnsNull()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(sched1);

        Assert.That(repository.Lookup("MyCluster", "node-99"), Is.Null);
        Assert.That(repository.Lookup("OtherName", "node-1"), Is.Null);
    }

    [Test]
    public void Remove_ByName_RemovesFirstScheduler()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        repository.Remove("MyCluster");

        Assert.That(repository.Lookup("MyCluster"), Is.SameAs(sched2));
        Assert.That(repository.LookupAll(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Remove_ByNameAndInstanceId_RemovesCorrectScheduler()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        bool removed = repository.Remove("MyCluster", "node-1");

        Assert.That(removed, Is.True);
        Assert.That(repository.Lookup("MyCluster", "node-1"), Is.Null);
        Assert.That(repository.Lookup("MyCluster"), Is.SameAs(sched2));
    }

    [Test]
    public void Remove_ByNameAndInstanceId_LastOne_CleansUpKey()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(sched1);

        repository.Remove("MyCluster", "node-1");

        Assert.That(repository.Lookup("MyCluster"), Is.Null);
        Assert.That(repository.LookupAll(), Is.Empty);
    }

    [Test]
    public void Remove_ByNameAndInstanceId_NotFound_ReturnsFalse()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(sched1);

        Assert.That(repository.Remove("MyCluster", "node-99"), Is.False);
        Assert.That(repository.Remove("OtherName", "node-1"), Is.False);
    }

    [Test]
    public void LookupByName_ReturnsAllSchedulersWithName()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");
        IScheduler sched3 = CreateFakeScheduler("OtherSched", "instance-1");

        repository.Bind(sched1);
        repository.Bind(sched2);
        repository.Bind(sched3);

        List<IScheduler> byName = repository.LookupByName("MyCluster");
        Assert.That(byName, Has.Count.EqualTo(2));
        Assert.That(byName, Does.Contain(sched1));
        Assert.That(byName, Does.Contain(sched2));
    }

    [Test]
    public void LookupByName_NotFound_ReturnsEmptyList()
    {
        Assert.That(repository.LookupByName("NonExistent"), Is.Empty);
    }

    [Test]
    public void LookupAll_ReturnsAllSchedulers()
    {
        IScheduler sched1 = CreateFakeScheduler("Cluster1", "node-1");
        IScheduler sched2 = CreateFakeScheduler("Cluster1", "node-2");
        IScheduler sched3 = CreateFakeScheduler("Cluster2", "instance-1");

        repository.Bind(sched1);
        repository.Bind(sched2);
        repository.Bind(sched3);

        Assert.That(repository.LookupAll(), Has.Count.EqualTo(3));
    }

    [Test]
    public void Lookup_IsCaseInsensitive()
    {
        IScheduler scheduler = CreateFakeScheduler("MyScheduler", "instance-1");
        repository.Bind(scheduler);

        Assert.That(repository.Lookup("myscheduler"), Is.SameAs(scheduler));
        Assert.That(repository.Lookup("MYSCHEDULER"), Is.SameAs(scheduler));
    }

    [Test]
    public void ShutDownSchedulerIsNoLongerFoundByLookup()
    {
        IScheduler scheduler = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(scheduler);

        repository.Lookup("MyCluster").Should().BeSameAs(scheduler, "a live scheduler is what a lookup is for");
        repository.LookupAll().Should().ContainSingle().Which.Should().BeSameAs(scheduler);
        repository.LookupByName("MyCluster").Should().ContainSingle().Which.Should().BeSameAs(scheduler);

        A.CallTo(() => scheduler.Status).Returns(SchedulerStatus.Shutdown);

        repository.LookupAll().Should().BeEmpty(
            "a scheduler bound here by hand never unbinds itself, so a read is what has to notice it died");
        repository.Lookup("MyCluster").Should().BeNull();
        repository.LookupByName("MyCluster").Should().BeEmpty();
    }

    [Test]
    public void ShutDownSchedulerIsEvictedRatherThanFilteredOut()
    {
        IScheduler dead = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(dead);
        A.CallTo(() => dead.Status).Returns(SchedulerStatus.Shutdown);

        repository.LookupAll().Should().BeEmpty();

        // Filtering would leave the entry in place, and the name and instance id are still taken.
        IScheduler replacement = CreateFakeScheduler("MyCluster", "node-1");
        Action act = () => repository.Bind(replacement);

        act.Should().NotThrow<SchedulerException>(
            "the dead entry was dropped, so its name is free for a scheduler that replaces it");
        repository.Lookup("MyCluster").Should().BeSameAs(replacement);
    }

    [Test]
    public void BindingAReplacementEvictsTheDeadEntryItWouldCollideWith()
    {
        IScheduler dead = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(dead);
        A.CallTo(() => dead.Status).Returns(SchedulerStatus.Shutdown);

        // No read between the shutdown and the re-bind: Bind itself has to notice the corpse,
        // because shutting a scheduler down and binding its successor is exactly the sequence
        // with no Lookup in the middle.
        IScheduler replacement = CreateFakeScheduler("MyCluster", "node-1");
        Action act = () => repository.Bind(replacement);

        act.Should().NotThrow<SchedulerException>(
            "a dead scheduler must not hold its name against the scheduler that replaces it");
        repository.Lookup("MyCluster").Should().BeSameAs(replacement);
    }

    [Test]
    public void ShutDownSchedulerIsEvictedWithoutDisturbingItsLiveSiblings()
    {
        IScheduler dead = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler alive = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(dead);
        repository.Bind(alive);

        A.CallTo(() => dead.Status).Returns(SchedulerStatus.Shutdown);

        repository.LookupAll().Should().ContainSingle().Which.Should().BeSameAs(alive);
        repository.Lookup("MyCluster", "node-1").Should().BeNull();
        repository.Lookup("MyCluster", "node-2").Should().BeSameAs(alive,
            "one node of a cluster going away must not take the others with it");
    }

    [Test]
    public void ASchedulerThatCannotAnswerIsKept()
    {
        IScheduler unreachable = CreateFakeScheduler("Remote", "node-1");
        A.CallTo(() => unreachable.Status).Throws(new HttpRequestException("no route to host"));

        repository.Bind(unreachable);

        repository.LookupAll().Should().ContainSingle().Which.Should().BeSameAs(unreachable,
            "a remote scheduler answers Status over the network, and unreachable is not shut down");
        repository.Lookup("Remote").Should().BeSameAs(unreachable);
    }

    /// <summary>
    /// A proxy for a scheduler in another process is never asked whether it has shut down, because the
    /// question is a request and the repository asks it with its lock held.
    /// </summary>
    /// <remarks>
    /// The blocked getter stands for an unreachable target, whose <c>Status</c> takes as long as the
    /// client's timeout to fail — 100 seconds by default. Reading it under <c>syncRoot</c> stops every
    /// lookup in the process for that long, the HTTP API's own scheduler resolution included, so the
    /// assertion is about the calls that have nothing to do with the proxy as much as about the sweep
    /// that met it.
    /// </remarks>
    [Test]
    public async Task AnUnreachableProxyBlocksNoLookup()
    {
        ManualResetEventSlim unreachable = new(initialState: false);
        try
        {
            IScheduler target = A.Fake<IScheduler>();
            A.CallTo(() => target.SchedulerName).Returns("Remote");
            A.CallTo(() => target.Status).ReturnsLazily(() =>
            {
                unreachable.Wait();
                return SchedulerStatus.Running;
            });
            IScheduler proxy = new BlockingProxyScheduler(target);

            repository.Bind(proxy, "Remote");
            repository.Bind(CreateFakeScheduler("Local", "local-1"));

            Task<List<IScheduler>> sweep = Task.Run(() => repository.LookupAll());
            Task<IScheduler> unrelated = Task.Run(() => repository.Lookup("Local"));

            Task both = Task.WhenAll(sweep, unrelated);
            Task finished = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

            finished.Should().BeSameAs(both,
                "reading a proxy's Status under the repository's lock stalls every lookup in the process for as long as the target takes to answer");

            sweep.Result.Should().Contain(proxy, "unreachable is not shut down, so the entry stays");
            unrelated.Result.Should().NotBeNull();
        }
        finally
        {
            // Releases the getter whether or not it was ever called, so a failing assertion leaves no
            // thread parked on it.
            unreachable.Set();
        }
    }

    private static IScheduler CreateFakeScheduler(string name, string instanceId)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns(instanceId);
        return scheduler;
    }

    /// <summary>
    /// Stands for <c>HttpScheduler</c>: a scheduler whose every member is a request. The marker is what
    /// the repository tests for, and the forwarding base is what keeps this two lines long.
    /// </summary>
    private sealed class BlockingProxyScheduler : DelegatingScheduler, IProxyScheduler
    {
        public BlockingProxyScheduler(IScheduler scheduler) : base(scheduler)
        {
        }
    }
}
