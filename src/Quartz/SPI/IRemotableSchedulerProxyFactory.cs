using Quartz.Simpl;

namespace Quartz.Spi
{
    /// <summary>
    /// Client Proxy to a IRemotableQuartzScheduler
    /// </summary>
    public interface IRemotableSchedulerProxyFactory
    {
        /// <summary>
        /// Returns a client proxy to a remote <see cref="IRemotableQuartzScheduler" />.
        /// </summary>
        IRemotableQuartzScheduler GetProxy();
    }
}
