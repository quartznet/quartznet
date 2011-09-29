using System;

using Quartz.Simpl;

namespace Quartz
{
    /// <summary>
    /// Client Proxy to a IRemotableQuartzScheduler
    /// </summary>
    public interface ISchedulerRemotableProxy
    {
        /// <summary>
        /// Gets or sets the remote scheduler address.
        /// </summary>
        /// <value>The remote scheduler address.</value>
        string RemoteSchedulerAddress { get; set; }

        /// <summary>
        /// Get a client proxy to a remote IRemotableQuartzScheduler
        /// </summary>
        /// <returns></returns>
        IRemotableQuartzScheduler BuildProxy();
    }
}
