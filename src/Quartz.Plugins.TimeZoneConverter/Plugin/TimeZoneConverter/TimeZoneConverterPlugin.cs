#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;

using Quartz.Spi;
using Quartz.Util;

using TimeZoneConverter;

namespace Quartz.Plugin.TimeZoneConverter
{
    /// <summary>
    /// This plugin provides the capability to obtain timezone information regardless of the platform and database being used.
    /// </summary>
    public class TimeZoneConverterPlugin : ISchedulerPlugin
    {
        /// <summary>
        /// Called during creation of the <see cref="IScheduler" /> in order to give
        /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
        /// </summary>
        public Task Initialize(
            string pluginName,
            IScheduler scheduler,
            CancellationToken cancellationToken = default)
        {
            TimeZoneUtil.CustomResolver = TZConvert.GetTimeZoneInfo;

            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler" /> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public Task Start(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public Task Shutdown(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }
    }
}