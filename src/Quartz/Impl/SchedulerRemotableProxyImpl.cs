using System;

using Quartz;
using Quartz.Simpl;

namespace Quartz.Impl
{
    /// <summary>
    /// Client Proxy to a IRemotableQuartzScheduler
    /// </summary>
    public class SchedulerRemotableProxyImpl : ISchedulerRemotableProxy
    {
        
        /// <summary>
        /// Gets or sets the remote scheduler address.
        /// </summary>
        /// <value>The remote scheduler address.</value>
        public string RemoteSchedulerAddress {get; set; }

        /// <summary>
        /// Get a client proxy to a remote IRemotableQuartzScheduler
        /// </summary>
        /// <returns></returns>
        public IRemotableQuartzScheduler BuildProxy()
        {
            return (IRemotableQuartzScheduler)Activator.GetObject(typeof(IRemotableQuartzScheduler), RemoteSchedulerAddress);
        }
    }
}
