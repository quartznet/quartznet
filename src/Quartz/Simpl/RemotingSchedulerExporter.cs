#region License
/* 
 * Copyright 2009- Marko Lahma
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
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
        private const string DefaultChannelName = "http";

        private readonly ILog log;
        private static readonly Dictionary<string, object> registeredChannels = new Dictionary<string, object>();

        public RemotingSchedulerExporter()
        {
            ChannelType = ChannelTypeTcp;
            TypeFilterLevel = TypeFilterLevel.Full;
            ChannelName = DefaultChannelName;
            BindName = DefaultBindName;
            log = LogManager.GetLogger(GetType());
        }

        public virtual void Bind(IRemotableQuartzScheduler scheduler)
        {
            if (scheduler == null)
            {
                throw new ArgumentNullException("scheduler");
            }
            if (!(scheduler is MarshalByRefObject))
            {
                throw new ArgumentException("Exported scheduler must be of type MarshallByRefObject", "scheduler");
            }

            RegisterRemotingChannelIfNeeded();

            try
            {
                RemotingServices.Marshal((MarshalByRefObject)scheduler, BindName);
                Log.Info(string.Format(CultureInfo.InvariantCulture, "Successfully marshalled remotable scheduler under name '{0}'", BindName));
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
            if (Port > 0 && ChannelType != null)
            {
                // try remoting bind
                var props = CreateConfiguration();

                // use binary formatter
                var formatprovider = new BinaryServerFormatterSinkProvider(props, null);
                formatprovider.TypeFilterLevel = TypeFilterLevel;

                string channelRegistrationKey = ChannelType + "_" + Port;
                if (registeredChannels.ContainsKey(channelRegistrationKey))
                {
                    Log.Warn(string.Format("Channel '{0}' already registered for port {1}, not registering again", ChannelType, Port));
                    return;
                }
                IChannel chan;
                if (ChannelType == ChannelTypeHttp)
                {
                    chan = new HttpChannel(props, null, formatprovider);
                }
                else if (ChannelType == ChannelTypeTcp)
                {
                    chan = new TcpChannel(props, null, formatprovider);
                }
                else
                {
                    throw new ArgumentException("Unknown remoting channel type '" + ChannelType + "'");
                }

                if (RejectRemoteRequests)
                {
                    Log.Info("Remoting is NOT accepting remote calls");
                }
                else
                {
                    Log.Info("Remoting is allowing remote calls");
                }

                Log.Info(string.Format(CultureInfo.InvariantCulture, "Registering remoting channel of type '{0}' to port ({1}) with name ({2})", chan.GetType(), Port, chan.ChannelName));

                ChannelServices.RegisterChannel(chan, false);

                registeredChannels.Add(channelRegistrationKey, new object());
                Log.Info("Remoting channel registered successfully");
            }
            else
            {
                log.Error("Cannot register remoting if port or channel type not specified");
            }
        }

        protected virtual IDictionary CreateConfiguration()
        {
            IDictionary props = new Hashtable();
            props["port"] = Port;
            props["name"] = ChannelName;
            if (RejectRemoteRequests)
            {
                props["rejectRemoteRequests"] = "true";
            }
            return props;
        }

        public virtual void UnBind(IRemotableQuartzScheduler scheduler)
        {
            if (scheduler == null)
            {
                throw new ArgumentNullException("scheduler");
            }
            if (!(scheduler is MarshalByRefObject))
            {
                throw new ArgumentException("Exported scheduler must be of type MarshallByRefObject", "scheduler");
            }

            try
            {
                RemotingServices.Disconnect((MarshalByRefObject)scheduler);
                Log.Info("Successfully disconnected remotable scheduler");
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
        public virtual int Port { get; set; }

        /// <summary>
        /// Gets or sets the name to use when exporting
        /// scheduler to remoting context.
        /// </summary>
        public virtual string BindName { get; set; }

        /// <summary>
        /// Gets or sets the name to use when binding to 
        /// tcp channel.
        /// </summary>
        public virtual string ChannelName { get; set; }

        /// <summary>
        /// Sets the channel type when registering remoting.
        /// </summary>
        public virtual string ChannelType { get; set; }

        /// <summary>
        /// Sets the <see cref="TypeFilterLevel" /> used when
        /// exporting to remoting context. Defaults to
        /// <see cref="System.Runtime.Serialization.Formatters.TypeFilterLevel.Full" />.
        /// </summary>
        public virtual TypeFilterLevel TypeFilterLevel { get; set; }

        /// <summary>
        /// A Boolean value (true or false) that specifies whether to refuse requests from other computers. 
        /// Specifying true allows only remoting calls from the local computer. The default is false.
        /// </summary>
        public virtual bool RejectRemoteRequests { get; set; }
    }
}
