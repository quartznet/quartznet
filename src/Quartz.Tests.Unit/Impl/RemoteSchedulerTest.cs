using FakeItEasy;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl;

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
}
