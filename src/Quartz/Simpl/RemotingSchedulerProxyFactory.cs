using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// A <see cref="IRemotableSchedulerProxyFactory" /> implementation that creates
    /// connection to remote scheduler using remoting.
    /// </summary>
    public class RemotingSchedulerProxyFactory : IRemotableSchedulerProxyFactory
    {
        /// <summary>
        /// Gets or sets the remote scheduler address.
        /// </summary>
        /// <value>The remote scheduler address.</value>
        public string Address { private get; set; }

        /// <summary>
        /// Returns a client proxy to a remote <see cref="IRemotableQuartzScheduler" />.
        /// </summary>
        public IRemotableQuartzScheduler GetProxy()
        {
#if REMOTING
            return (IRemotableQuartzScheduler) System.Activator.GetObject(typeof(IRemotableQuartzScheduler), Address);
#else // REMOTING
            // TODO (NetCore Port): Return a new 'HttpQuartzScheduler' type which is the client that will make requests to a remote scheduler
            //                      This new type would then be what is wrapped by RemoteScheduler to make remote calls.
            return null;
#endif // REMOTING
        }
    }
}
