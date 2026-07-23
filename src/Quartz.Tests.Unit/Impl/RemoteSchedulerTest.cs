using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl;

[NonParallelizable]
public class RemoteSchedulerTest
{
    [Test]
    public void SchedulerName_WithLocalName_ReturnsLocalNameInsteadOfRemote()
    {
        IRemotableSchedulerProxyFactory proxyFactory = A.Fake<IRemotableSchedulerProxyFactory>();
        IRemotableQuartzScheduler remoteProxy = A.Fake<IRemotableQuartzScheduler>();
        A.CallTo(() => proxyFactory.GetProxy()).Returns(remoteProxy);
        A.CallTo(() => remoteProxy.SchedulerName).Returns("RemoteServerName");

        RemoteScheduler scheduler = new RemoteScheduler("uid", proxyFactory, "LocalClientName");

        Assert.AreEqual("LocalClientName", scheduler.SchedulerName);
    }

    [Test]
    public void SchedulerName_WithoutLocalName_FallsBackToRemoteName()
    {
        IRemotableSchedulerProxyFactory proxyFactory = A.Fake<IRemotableSchedulerProxyFactory>();
        IRemotableQuartzScheduler remoteProxy = A.Fake<IRemotableQuartzScheduler>();
        A.CallTo(() => proxyFactory.GetProxy()).Returns(remoteProxy);
        A.CallTo(() => remoteProxy.SchedulerName).Returns("RemoteServerName");

        RemoteScheduler scheduler = new RemoteScheduler("uid", proxyFactory);

        Assert.AreEqual("RemoteServerName", scheduler.SchedulerName);
    }

    [Test]
    public void SchedulerName_WithLocalName_DoesNotContactRemote()
    {
        IRemotableSchedulerProxyFactory proxyFactory = A.Fake<IRemotableSchedulerProxyFactory>();

        RemoteScheduler scheduler = new RemoteScheduler("uid", proxyFactory, "LocalClientName");
        string name = scheduler.SchedulerName;

        Assert.AreEqual("LocalClientName", name);
        A.CallTo(() => proxyFactory.GetProxy()).MustNotHaveHappened();
    }

    [Test]
    public async Task Shutdown_RemovesSchedulerFromRepositoryByLocalName()
    {
        const string localName = "ShutdownTestScheduler";
        IRemotableSchedulerProxyFactory proxyFactory = A.Fake<IRemotableSchedulerProxyFactory>();
        IRemotableQuartzScheduler remoteProxy = A.Fake<IRemotableQuartzScheduler>();
        A.CallTo(() => proxyFactory.GetProxy()).Returns(remoteProxy);
        A.CallTo(() => remoteProxy.SchedulerName).Returns("RemoteServerName");

        RemoteScheduler scheduler = new RemoteScheduler("uid", proxyFactory, localName);

        SchedulerRepository repository = SchedulerRepository.Instance;
        repository.Bind(scheduler);
        try
        {
            Assert.IsNotNull(repository.Lookup(localName));

            await scheduler.Shutdown().ConfigureAwait(false);

            Assert.IsNull(repository.Lookup(localName));
        }
        finally
        {
            repository.Remove(localName);
        }
    }

    [Test]
    public async Task Shutdown_WithMultipleProxies_RemovesOnlyCorrectOne()
    {
        const string clusterName = "ShutdownMultiProxyCluster";
        SchedulerRepository repository = new SchedulerRepository();

        IRemotableSchedulerProxyFactory proxyFactory1 = A.Fake<IRemotableSchedulerProxyFactory>();
        IRemotableQuartzScheduler remoteProxy1 = A.Fake<IRemotableQuartzScheduler>();
        A.CallTo(() => proxyFactory1.GetProxy()).Returns(remoteProxy1);

        IRemotableSchedulerProxyFactory proxyFactory2 = A.Fake<IRemotableSchedulerProxyFactory>();
        IRemotableQuartzScheduler remoteProxy2 = A.Fake<IRemotableQuartzScheduler>();
        A.CallTo(() => proxyFactory2.GetProxy()).Returns(remoteProxy2);

        RemoteScheduler scheduler1 = new RemoteScheduler("uid1", proxyFactory1, clusterName);
        scheduler1.RepositoryInstanceId = "node-1";
        scheduler1.SchedulerRepositoryOverride = repository;

        RemoteScheduler scheduler2 = new RemoteScheduler("uid2", proxyFactory2, clusterName);
        scheduler2.RepositoryInstanceId = "node-2";
        scheduler2.SchedulerRepositoryOverride = repository;

        repository.Bind(scheduler1);
        repository.Bind(scheduler2);

        Assert.AreEqual(2, repository.LookupByName(clusterName).Count);

        // Shutdown scheduler1, scheduler2 should remain
        await scheduler1.Shutdown().ConfigureAwait(false);

        Assert.IsNull(repository.Lookup(clusterName, "node-1"));
        Assert.AreSame(scheduler2, repository.Lookup(clusterName, "node-2"));
        Assert.AreEqual(1, repository.LookupByName(clusterName).Count);
    }

    [Test]
    public async Task ShutdownWithWait_RemovesSchedulerFromRepository()
    {
        const string clusterName = "ShutdownWaitCluster";
        SchedulerRepository repository = new SchedulerRepository();

        IRemotableSchedulerProxyFactory proxyFactory = A.Fake<IRemotableSchedulerProxyFactory>();
        IRemotableQuartzScheduler remoteProxy = A.Fake<IRemotableQuartzScheduler>();
        A.CallTo(() => proxyFactory.GetProxy()).Returns(remoteProxy);

        RemoteScheduler scheduler = new RemoteScheduler("uid", proxyFactory, clusterName);
        scheduler.RepositoryInstanceId = "node-1";
        scheduler.SchedulerRepositoryOverride = repository;

        repository.Bind(scheduler);
        Assert.IsNotNull(repository.Lookup(clusterName));

        await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);

        Assert.IsNull(repository.Lookup(clusterName));
    }
}
