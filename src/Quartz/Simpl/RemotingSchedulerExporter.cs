using System;
using System.Collections;
using System.Globalization;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
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
        public const string ChannelTypeTcp = "tcp";
        public const string ChannelTypeHttp = "http";
        private const string DefaultBindName = "QuartzScheduler";

        private readonly ILog log;
        private int port = -1;
        private string bindName = DefaultBindName;
        private string channelType = ChannelTypeTcp;
        private TypeFilterLevel typeFilgerLevel = TypeFilterLevel.Full;

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

        /// <summary>
        /// Registers remoting channel if needed. This is determined
        /// by checking whether there is a positive value for port.
        /// </summary>
        protected virtual void RegisterRemotingChannelIfNeeded()
        {
            if (port > -1 && channelType != null)
            {
                // try remoting bind

                IDictionary props = new Hashtable();
                props["port"] = port;
                
                // use binary formatter
                BinaryServerFormatterSinkProvider formatprovider = new BinaryServerFormatterSinkProvider(props, null);
                formatprovider.TypeFilterLevel = typeFilgerLevel;

                IChannel chan;
                if (channelType == ChannelTypeHttp)
                {
                    chan = new HttpChannel(props, null, formatprovider);
                }
                else if (channelType == ChannelTypeTcp)
                {
                    chan = new TcpChannel(props, null, formatprovider);
                }
                else
                {
                    throw new ArgumentException("Unknown remoting channel type '" + channelType + "'");
                }
               
                Log.Info(string.Format(CultureInfo.InvariantCulture, "Registering remoting channel of type '{0}' to port ({1})", chan.GetType(), port));
                ChannelServices.RegisterChannel(chan);
                Log.Info("Remoting channel registered successfully");
            }
            else
            {
                log.Error("Cannot register remoting if port or channel type not specified");
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

        protected virtual ILog Log
        {
            get { return log; }
        }

        /// <summary>
        /// Gets or sets the port used for remoting.
        /// </summary>
        public virtual int Port
        {
            get { return port; }
            set { port = value; }
        }

        /// <summary>
        /// Gets or sets the name to use when exporting
        /// scheduler to remoting context.
        /// </summary>
        public virtual string BindName
        {
            get { return bindName; }
            set { bindName = value; }
        }

        /// <summary>
        /// Sets the channel type when registering remoting.
        /// 
        /// </summary>
        public virtual string ChannelType
        {
            get { return channelType; }
            set { channelType = value; }
        }

        /// <summary>
        /// Sets the <see cref="TypeFilterLevel" /> used when
        /// exporting to remoting context. Defaults to
        /// <see cref="System.Runtime.Serialization.Formatters.TypeFilterLevel.Full" />.
        /// </summary>
        public virtual TypeFilterLevel TypeFilterLevel
        {
            set { typeFilgerLevel = value; }
            get { return typeFilgerLevel; }
        }
    }
}
