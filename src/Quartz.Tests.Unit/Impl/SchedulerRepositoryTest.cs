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

    private static IScheduler CreateFakeScheduler(string name, string instanceId)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns(instanceId);
        return scheduler;
    }
}
