namespace Quartz.Spi;

/// <summary>
/// Client Proxy to a IScheduler.
/// </summary>
public interface ISchedulerProxyFactory
{
    /// <summary>
    /// Returns a client proxy to a remote <see cref="IScheduler" />.
    /// </summary>
    IScheduler GetProxy(string schedulerName, string schedulerInstanceId);
}