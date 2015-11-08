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

using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Web.History
{
    /// <summary>
    /// Logs a history of all job and trigger executions.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class DatabaseExecutionHistoryPlugin : ISchedulerPlugin, IJobListener
    {
        public static JobHistoryDelegate Delegate { get; private set; }

        /// <summary>
        /// Get the name of the <see cref="IJobListener" />.
        /// </summary>
        /// <value></value>
        public virtual string Name { get; private set; }

        public string TablePrefix { get; set; }
        public string DataSource { get; set; }
        public string DriverDelegateType { get; set; }

        /// <summary>
        /// Called during creation of the <see cref="IScheduler" /> in order to give
        /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
        /// </summary>
        public virtual void Initialize(string pluginName, IScheduler scheduler)
        {
            Name = pluginName;
            Delegate = new JobHistoryDelegate(DataSource, DriverDelegateType, TablePrefix);
            scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler" /> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public virtual Task StartAsync()
        {
            // do nothing...
            return TaskUtil.CompletedTask;
        }

        /// <summary> 
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public virtual Task ShutdownAsync()
        {
            // nothing to do...
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        ///     Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/> is
        ///     about to be executed (an associated <see cref="ITrigger"/> has occurred). 
        ///     <para>
        ///         This method will not be invoked if the execution of the Job was vetoed by a
        ///         <see cref="ITriggerListener"/>.
        ///     </para>
        /// </summary>
        /// <seealso cref="JobExecutionVetoedAsync"/>
        public virtual Task JobToBeExecutedAsync(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> after a <see cref="IJobDetail" />
        /// has been executed, and be for the associated <see cref="ITrigger" />'s
        /// <see cref="IOperableTrigger.Triggered" /> method has been called.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="jobException"></param>
        public virtual Task JobWasExecutedAsync(IJobExecutionContext context, JobExecutionException jobException)
        {
            return Delegate.InsertJobHistoryEntry(context, jobException);
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
        /// was about to be executed (an associated <see cref="ITrigger" />
        /// has occurred), but a <see cref="ITriggerListener" /> vetoed it's
        /// execution.
        /// </summary>
        /// <param name="context"></param>
        /// <seealso cref="JobToBeExecutedAsync"/>
        public virtual Task JobExecutionVetoedAsync(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }
    }
}