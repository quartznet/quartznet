#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
#if !APPDOMAINS
using System.Reflection;
using System.Runtime.Loader;
#endif // !APPDOMAINS
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Plugin.Management
{
    /// <summary> 
    /// This plugin catches the event of the VM terminating (such as upon a CRTL-C)
    /// and tells the scheduler to Shutdown.
    /// </summary>
    /// <seealso cref="IScheduler.Shutdown(bool)" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class ShutdownHookPlugin : ISchedulerPlugin
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (ShutdownHookPlugin));

        public ShutdownHookPlugin()
        {
            CleanShutdown = true;
        }

        /// <summary> 
        /// Determine whether or not the plug-in is configured to cause a clean
        /// Shutdown of the scheduler.
        /// <para>
        /// The default value is <see langword="true" />.
        /// </para>
        /// </summary>
        /// <seealso cref="IScheduler.Shutdown(bool)" />
        public bool CleanShutdown { get; set; }

        /// <summary>
        /// Called during creation of the <see cref="IScheduler" /> in order to give
        /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
        /// </summary>
        public virtual void Initialize(string pluginName, IScheduler scheduler)
        {
            log.InfoFormat("Registering Quartz Shutdown hook '{0}.", pluginName);
#if APPDOMAINS
            AppDomain.CurrentDomain.ProcessExit += (sender, ea) =>
#else // APPDOMAINS
            AssemblyLoadContext.GetLoadContext(typeof(IScheduler).GetTypeInfo().Assembly).Unloading += (ctx) =>
#endif // APPDOMAINS
                                            {
                                                log.Info("Shutting down Quartz...");
                                                try
                                                {
                                                    scheduler.Shutdown(CleanShutdown);
                                                }
                                                catch (SchedulerException e)
                                                {
                                                    log.InfoException("Error shutting down Quartz: " + e.Message, e);
                                                }
                                            };
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler" /> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public virtual Task Start()
        {
            // do nothing.
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public virtual Task Shutdown()
        {
            // nothing to do in this case (since the scheduler is already shutting
            // down)
            return TaskUtil.CompletedTask;
        }
    }
}