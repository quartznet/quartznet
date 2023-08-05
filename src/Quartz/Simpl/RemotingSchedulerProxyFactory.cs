#if REMOTING

using Quartz.Core;
using Quartz.Impl;
using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// A <see cref="IRemotableSchedulerProxyFactory" /> implementation that creates
/// connection to remote scheduler using remoting.
/// </summary>
public sealed class RemotingSchedulerProxyFactory : IRemotableSchedulerProxyFactory
{
    /// <summary>
    /// Gets or sets the remote scheduler address.
    /// </summary>
    /// <value>The remote scheduler address.</value>
    public string? Address { private get; set; }

    /// <summary>
    /// Returns a client proxy to a remote <see cref="IScheduler" />.
    /// </summary>
    public IScheduler GetProxy(string schedulerName, string schedulerInstanceId)
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            ThrowHelper.ThrowInvalidOperationException("Address hasn't been configured");
        }

        string uid = QuartzSchedulerResources.GetUniqueIdentifier(schedulerName, schedulerInstanceId);
        var remoteScheduler = new RemoteScheduler(uid, () => (IRemotableQuartzScheduler) Activator.GetObject(typeof(IRemotableQuartzScheduler), Address));

        return remoteScheduler;
    }
}
#endif // REMOTING