using System;
using System.Globalization;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using System.Runtime.Remoting.Channels.Tcp;
using System.Security;

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Simpl
{

    /// <summary>
    /// Scheduler exporter that exports scheduler to remoting context.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class RemotingSchedulerExporter : ISchedulerExporter
    {
        private const string ChannelTypeTcp = "tcp";
        private const string ChannelTypeHttp = "http";

        private readonly ILog log;
        private int port = -1;
        private string bindName;
        private string channelType;

        public RemotingSchedulerExporter()
        {
            log = LogManager.GetLogger(GetType());
        }

        public virtual void Bind(IRemotableQuartzScheduler scheduler)
        {
            if (scheduler == null)
            {
                throw new ArgumentNullException("scheduler");
            }
            if (!typeof(MarshalByRefObject).IsAssignableFrom(scheduler.GetType()))
            {
                throw new ArgumentException("Exported scheduler must be of type MarshallByRefObject", "scheduler");
            }

            RegisterRemotingChannelIfNeeded();

            try
            {
                RemotingServices.Marshal((MarshalByRefObject) scheduler, bindName);
                Log.Info(string.Format(CultureInfo.InvariantCulture, "Successfully marhalled remotable scheduler under name '{0}'", bindName));
            }
            catch (RemotingException ex)
            {
                Log.Error("RemotingException during Bind", ex);
            }
            catch (SecurityException ex)
            {
                Log.Error("SecurityException during Bind", ex);
            }
            catch (Exception ex)
            {
                Log.Error("Exception during Bind", ex);
            } 
        }

        private void RegisterRemotingChannelIfNeeded()
        {
            if (port > -1 && channelType != null)
            {
                // try remoting bind
                IChannel chan;
                if (channelType == ChannelTypeHttp)
                {
                    chan = new HttpChannel(port);
                }
                else if (channelType == ChannelTypeTcp)
                {
                    chan = new TcpChannel(port);
                }
                else
                {
                    throw new ArgumentException("Unknown remoting channel type '" + channelType + "'");
                }

                Log.Info(string.Format(CultureInfo.InvariantCulture, "Registering remoting channel of type '{0}' to port ({1})", chan.GetType(), port));
                ChannelServices.RegisterChannel(chan);
                Log.Info("Remoting channel registered successfully");
            }
        }

        public virtual void UnBind(IRemotableQuartzScheduler scheduler)
        {
            if (scheduler == null)
            {
                throw new ArgumentNullException("scheduler");
            }
            if (!typeof(MarshalByRefObject).IsAssignableFrom(scheduler.GetType()))
            {
                throw new ArgumentException("Exported scheduler must be of type MarshallByRefObject", "scheduler");
            } 
            
            try
            {
                RemotingServices.Disconnect((MarshalByRefObject) scheduler);
                Log.Info("Successfully disconnected remotable sceduler");
            }
            catch (ArgumentException ex)
            {
                Log.Error("ArgumentException during Unbind", ex);
            }
            catch (SecurityException ex)
            {
                Log.Error("SecurityException during Unbind", ex);
            }
            catch (Exception ex)
            {
                Log.Error("Exception during Unbind", ex);
            } 
        }

        protected ILog Log
        {
            get { return log; }
        }

        public int Port
        {
            get { return port; }
            set { port = value; }
        }

        public string BindName
        {
            get { return bindName; }
            set { bindName = value; }
        }


        public string ChannelType
        {
            get { return channelType; }
            set { channelType = value; }
        }
    }
}
