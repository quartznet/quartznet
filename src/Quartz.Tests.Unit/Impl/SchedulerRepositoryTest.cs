using FakeItEasy;

using Quartz.Impl;
using Quartz.Spi;

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
        Assert.AreSame(scheduler, result);
    }

    [Test]
    public void Bind_TwoSchedulers_SameName_DifferentInstanceId_BothStored()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        var all = repository.LookupAll();
        Assert.AreEqual(2, all.Count);
        CollectionAssert.Contains(all, sched1);
        CollectionAssert.Contains(all, sched2);
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

        Assert.AreSame(sched1, repository.Lookup("Sched1"));
        Assert.AreSame(sched2, repository.Lookup("Sched2"));
    }

    [Test]
    public void Lookup_ByName_ReturnsFirstBound()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        IScheduler result = repository.Lookup("MyCluster");
        Assert.AreSame(sched1, result);
    }

    [Test]
    public void Lookup_ByNameAndInstanceId_ReturnsCorrectScheduler()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        Assert.AreSame(sched1, repository.Lookup("MyCluster", "node-1"));
        Assert.AreSame(sched2, repository.Lookup("MyCluster", "node-2"));
    }

    [Test]
    public void Lookup_ByNameAndInstanceId_NotFound_ReturnsNull()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(sched1);

        Assert.IsNull(repository.Lookup("MyCluster", "node-99"));
        Assert.IsNull(repository.Lookup("OtherName", "node-1"));
    }

    [Test]
    public void Remove_ByName_RemovesFirstScheduler()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        repository.Remove("MyCluster");

        // First one removed, second one is now the first
        Assert.AreSame(sched2, repository.Lookup("MyCluster"));
        Assert.AreEqual(1, repository.LookupAll().Count);
    }

    [Test]
    public void Remove_ByNameAndInstanceId_RemovesCorrectScheduler()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        IScheduler sched2 = CreateFakeScheduler("MyCluster", "node-2");

        repository.Bind(sched1);
        repository.Bind(sched2);

        bool removed = repository.Remove("MyCluster", "node-1");

        Assert.IsTrue(removed);
        Assert.IsNull(repository.Lookup("MyCluster", "node-1"));
        Assert.AreSame(sched2, repository.Lookup("MyCluster"));
    }

    [Test]
    public void Remove_ByNameAndInstanceId_LastOne_CleansUpKey()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(sched1);

        repository.Remove("MyCluster", "node-1");

        Assert.IsNull(repository.Lookup("MyCluster"));
        Assert.AreEqual(0, repository.LookupAll().Count);
    }

    [Test]
    public void Remove_ByNameAndInstanceId_NotFound_ReturnsFalse()
    {
        IScheduler sched1 = CreateFakeScheduler("MyCluster", "node-1");
        repository.Bind(sched1);

        Assert.IsFalse(repository.Remove("MyCluster", "node-99"));
        Assert.IsFalse(repository.Remove("OtherName", "node-1"));
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

        var byName = repository.LookupByName("MyCluster");
        Assert.AreEqual(2, byName.Count);
        CollectionAssert.Contains(byName, sched1);
        CollectionAssert.Contains(byName, sched2);
    }

    [Test]
    public void LookupByName_NotFound_ReturnsEmptyList()
    {
        var result = repository.LookupByName("NonExistent");
        Assert.AreEqual(0, result.Count);
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

        var all = repository.LookupAll();
        Assert.AreEqual(3, all.Count);
    }

    [Test]
    public void Lookup_IsCaseInsensitive()
    {
        IScheduler scheduler = CreateFakeScheduler("MyScheduler", "instance-1");
        repository.Bind(scheduler);

        Assert.AreSame(scheduler, repository.Lookup("myscheduler"));
        Assert.AreSame(scheduler, repository.Lookup("MYSCHEDULER"));
    }

    [Test]
    public void Bind_RemoteScheduler_WithRepositoryInstanceId_AllowsSameNameDifferentId()
    {
        RemoteScheduler remote1 = CreateFakeRemoteScheduler("MyCluster", "node-1");
        RemoteScheduler remote2 = CreateFakeRemoteScheduler("MyCluster", "node-2");

        repository.Bind(remote1);
        repository.Bind(remote2);

        Assert.AreSame(remote1, repository.Lookup("MyCluster", "node-1"));
        Assert.AreSame(remote2, repository.Lookup("MyCluster", "node-2"));
    }

    [Test]
    public void Bind_RemoteScheduler_WithoutRepositoryInstanceId_ThrowsOnDuplicate()
    {
        // Legacy path: RemoteScheduler without RepositoryInstanceId set
        IRemotableSchedulerProxyFactory proxyFactory = A.Fake<IRemotableSchedulerProxyFactory>();
        RemoteScheduler remote1 = new RemoteScheduler("uid1", proxyFactory, "MyCluster");
        RemoteScheduler remote2 = new RemoteScheduler("uid2", proxyFactory, "MyCluster");
        // RepositoryInstanceId not set — defaults to null

        repository.Bind(remote1);

        Assert.Throws<SchedulerException>(() => repository.Bind(remote2));
    }

    private static IScheduler CreateFakeScheduler(string name, string instanceId)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns(instanceId);
        return scheduler;
    }

    private static RemoteScheduler CreateFakeRemoteScheduler(string name, string repositoryInstanceId)
    {
        IRemotableSchedulerProxyFactory proxyFactory = A.Fake<IRemotableSchedulerProxyFactory>();
        RemoteScheduler remote = new RemoteScheduler("uid", proxyFactory, name);
        remote.RepositoryInstanceId = repositoryInstanceId;
        return remote;
    }
}
