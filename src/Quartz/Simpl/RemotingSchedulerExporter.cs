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

#if REMOTING
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using System.Security;

using Quartz.Logging;
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

        /// <summary>
        /// BinaryServerFormatterSinkProvider allowed properties.
        /// </summary>
        private static string[] formatProviderAllowedProperties = new string[] { "includeVersions", "strictBinding", "typeFilterLevel" };

        private static readonly Dictionary<string, object> registeredChannels = new Dictionary<string, object>();

        public RemotingSchedulerExporter()
        {
            ChannelType = ChannelTypeTcp;
#if REMOTING
            TypeFilterLevel = TypeFilterLevel.Full;
#endif // REMOTING
            ChannelName = DefaultChannelName;
            BindName = DefaultBindName;
            Log = LogProvider.GetLogger(GetType());
        }

        public virtual void Bind(IRemotableQuartzScheduler scheduler)
        {
            if (scheduler == null)
            {
                throw new ArgumentNullException(nameof(scheduler));
            }

#if REMOTING
            if (!(scheduler is MarshalByRefObject))
            {
                throw new ArgumentException("Exported scheduler must be of type MarshallByRefObject", nameof(scheduler));
            }

            RegisterRemotingChannelIfNeeded();

            try
            {
                RemotingServices.Marshal((MarshalByRefObject)scheduler, BindName);
                Log.Info($"Successfully marshalled remotable scheduler under name '{BindName}'");
            }
            catch (RemotingException ex)
            {
                Log.ErrorException("RemotingException during Bind", ex);
            }
            catch (SecurityException ex)
            {
                Log.ErrorException("SecurityException during Bind", ex);
            }
            catch (Exception ex)
            {
                Log.ErrorException("Exception during Bind", ex);
            }
#else // REMOTING
            // TODO (NetCore Port): Replace with HTTP communication
#endif // REMOTING
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

#if REMOTING
                // use binary formatter
                var formatProviderProps = ExtractFormatProviderConfiguration(props);
                var formatprovider = new BinaryServerFormatterSinkProvider(formatProviderProps, null);
                formatprovider.TypeFilterLevel = TypeFilterLevel;

                string channelRegistrationKey = ChannelType + "_" + Port;
                if (registeredChannels.ContainsKey(channelRegistrationKey))
                {
                    Log.Warn($"Channel '{ChannelType}' already registered for port {Port}, not registering again");
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

                Log.Info($"Registering remoting channel of type '{chan.GetType()}' to port ({Port}) with name ({chan.ChannelName})");

                ChannelServices.RegisterChannel(chan, false);

                registeredChannels.Add(channelRegistrationKey, new object());
#else // REMOTING
                // TODO (NetCore Port): Replace with HTTP communication
#endif // REMOTING
                Log.Info("Remoting channel registered successfully");
            }
            else
            {
                Log.Error("Cannot register remoting if port or channel type not specified");
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

        /// <summary>
        /// Extract BinaryServerFormatterSinkProvider allowed properties from configuration properties.
        /// </summary>
        /// <param name="props">Configuration properties.</param>
        /// <returns>BinaryServerFormatterSinkProvider allowed properties from configuration.</returns>
        protected virtual IDictionary ExtractFormatProviderConfiguration(IDictionary props)
        {
            IDictionary formatProviderAllowedProps = new Hashtable();

            foreach (var allowedProperty in formatProviderAllowedProps)
            {
                if (props.Contains(allowedProperty))
                {
                    formatProviderAllowedProps[allowedProperty] = props[allowedProperty];
                }
            }

            return formatProviderAllowedProps;
        }

        public virtual void UnBind(IRemotableQuartzScheduler scheduler)
        {
            if (scheduler == null)
            {
                throw new ArgumentNullException(nameof(scheduler));
            }
#if REMOTING
            if (!(scheduler is MarshalByRefObject))
            {
                throw new ArgumentException("Exported scheduler must be of type MarshallByRefObject", nameof(scheduler));
            }
#endif // REMOTING

            try
            {
#if REMOTING
                RemotingServices.Disconnect((MarshalByRefObject)scheduler);
                Log.Info("Successfully disconnected remotable scheduler");
#else // REMOTING
                // TODO (NetCore Port): Replace with HTTP communication
#endif // REMOTING
            }
            catch (ArgumentException ex)
            {
                Log.ErrorException("ArgumentException during Unbind", ex);
            }
            catch (SecurityException ex)
            {
                Log.ErrorException("SecurityException during Unbind", ex);
            }
            catch (Exception ex)
            {
                Log.ErrorException("Exception during Unbind", ex);
            }
        }

        internal ILog Log { get; }

        /// <summary>
        /// Gets or sets the port used for remoting.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets the name to use when exporting
        /// scheduler to remoting context.
        /// </summary>
        public string BindName { get; set; }

        /// <summary>
        /// Gets or sets the name to use when binding to
        /// tcp channel.
        /// </summary>
        public string ChannelName { get; set; }

        /// <summary>
        /// Sets the channel type when registering remoting.
        /// </summary>
        public string ChannelType { get; set; }

        /// <summary>
        /// Sets the <see cref="TypeFilterLevel" /> used when
        /// exporting to remoting context. Defaults to
        /// <see cref="System.Runtime.Serialization.Formatters.TypeFilterLevel.Full" />.
        /// </summary>
        public TypeFilterLevel TypeFilterLevel { get; set; }

        /// <summary>
        /// A Boolean value (true or false) that specifies whether to refuse requests from other computers.
        /// Specifying true allows only remoting calls from the local computer. The default is false.
        /// </summary>
        public bool RejectRemoteRequests { get; set; }
    }
}
#endif // REMOTING
