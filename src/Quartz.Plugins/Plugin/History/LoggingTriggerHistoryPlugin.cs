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

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Plugin.History
{
    /// <summary>
    /// Logs a history of all trigger firings via the Jakarta Commons-Logging
    /// framework.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The logged message is customizable by setting one of the following message
    /// properties to a string that conforms to the syntax of <see cref="string.Format(string, object[])" />.
    /// </para>
    ///
    /// <para>
    /// TriggerFiredMessage - available message data are: <table>
    /// <tr>
    /// <th>Element</th>
    /// <th>Data Type</th>
    /// <th>Description</th>
    /// </tr>
    /// <tr>
    /// <td>0</td>
    /// <td>String</td>
    /// <td>The Trigger's Name.</td>
    /// </tr>
    /// <tr>
    /// <td>1</td>
    /// <td>String</td>
    /// <td>The Trigger's Group.</td>
    /// </tr>
    /// <tr>
    /// <td>2</td>
    /// <td>Date</td>
    /// <td>The scheduled fire time.</td>
    /// </tr>
    /// <tr>
    /// <td>3</td>
    /// <td>Date</td>
    /// <td>The next scheduled fire time.</td>
    /// </tr>
    /// <tr>
    /// <td>4</td>
    /// <td>Date</td>
    /// <td>The actual fire time.</td>
    /// </tr>
    /// <tr>
    /// <td>5</td>
    /// <td>String</td>
    /// <td>The Job's name.</td>
    /// </tr>
    /// <tr>
    /// <td>6</td>
    /// <td>String</td>
    /// <td>The Job's group.</td>
    /// </tr>
    /// <tr>
    /// <td>7</td>
    /// <td>Integer</td>
    /// <td>The re-fire count from the JobExecutionContext.</td>
    /// </tr>
    /// </table>
    ///
    /// The default message text is <i>"Trigger {1}.{0} fired job {6}.{5} at: {4,
    /// date, HH:mm:ss MM/dd/yyyy}"</i>
    /// </para>
    ///
    /// <para>
    /// TriggerMisfiredMessage - available message data are: <table>
    /// <tr>
    /// <th>Element</th>
    /// <th>Data Type</th>
    /// <th>Description</th>
    /// </tr>
    /// <tr>
    /// <td>0</td>
    /// <td>String</td>
    /// <td>The Trigger's Name.</td>
    /// </tr>
    /// <tr>
    /// <td>1</td>
    /// <td>String</td>
    /// <td>The Trigger's Group.</td>
    /// </tr>
    /// <tr>
    /// <td>2</td>
    /// <td>Date</td>
    /// <td>The scheduled fire time.</td>
    /// </tr>
    /// <tr>
    /// <td>3</td>
    /// <td>Date</td>
    /// <td>The next scheduled fire time.</td>
    /// </tr>
    /// <tr>
    /// <td>4</td>
    /// <td>Date</td>
    /// <td>The actual fire time. (the time the misfire was detected/handled)</td>
    /// </tr>
    /// <tr>
    /// <td>5</td>
    /// <td>String</td>
    /// <td>The Job's name.</td>
    /// </tr>
    /// <tr>
    /// <td>6</td>
    /// <td>String</td>
    /// <td>The Job's group.</td>
    /// </tr>
    /// </table>
    ///
    /// The default message text is <i>"Trigger {1}.{0} misfired job {6}.{5} at:
    /// {4, date, HH:mm:ss MM/dd/yyyy}. Should have fired at: {3, date, HH:mm:ss
    /// MM/dd/yyyy}"</i>
    /// </para>
    ///
    /// <para>
    /// TriggerCompleteMessage - available message data are: <table>
    /// <tr>
    /// <th>Element</th>
    /// <th>Data Type</th>
    /// <th>Description</th>
    /// </tr>
    /// <tr>
    /// <td>0</td>
    /// <td>String</td>
    /// <td>The Trigger's Name.</td>
    /// </tr>
    /// <tr>
    /// <td>1</td>
    /// <td>String</td>
    /// <td>The Trigger's Group.</td>
    /// </tr>
    /// <tr>
    /// <td>2</td>
    /// <td>Date</td>
    /// <td>The scheduled fire time.</td>
    /// </tr>
    /// <tr>
    /// <td>3</td>
    /// <td>Date</td>
    /// <td>The next scheduled fire time.</td>
    /// </tr>
    /// <tr>
    /// <td>4</td>
    /// <td>Date</td>
    /// <td>The job completion time.</td>
    /// </tr>
    /// <tr>
    /// <td>5</td>
    /// <td>String</td>
    /// <td>The Job's name.</td>
    /// </tr>
    /// <tr>
    /// <td>6</td>
    /// <td>String</td>
    /// <td>The Job's group.</td>
    /// </tr>
    /// <tr>
    /// <td>7</td>
    /// <td>Integer</td>
    /// <td>The re-fire count from the JobExecutionContext.</td>
    /// </tr>
    /// <tr>
    /// <td>8</td>
    /// <td>Integer</td>
    /// <td>The trigger's resulting instruction code.</td>
    /// </tr>
    /// <tr>
    /// <td>9</td>
    /// <td>String</td>
    /// <td>A human-readable translation of the trigger's resulting instruction
    /// code.</td>
    /// </tr>
    /// </table>
    ///
    /// The default message text is <i>"Trigger {1}.{0} completed firing job
    /// {6}.{5} at {4, date, HH:mm:ss MM/dd/yyyy} with resulting trigger instruction
    /// code: {9}"</i>
    /// </para>
    /// </remarks>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class LoggingTriggerHistoryPlugin : ISchedulerPlugin, ITriggerListener
    {
        /// <summary>
        /// Logger instance to use. Defaults to common logging.
        /// </summary>
        private ILog Log { get; } = LogProvider.GetLogger(typeof(LoggingTriggerHistoryPlugin));

        /// <summary>
        /// Get or set the message that is printed upon the completion of a trigger's
        /// firing.
        /// </summary>
        public virtual string TriggerCompleteMessage { get; set; } = "Trigger {1}.{0} completed firing job {6}.{5} at {4:HH:mm:ss MM/dd/yyyy} with resulting trigger instruction code: {9}";

        /// <summary>
        /// Get or set the message that is printed upon a trigger's firing.
        /// </summary>
        public virtual string TriggerFiredMessage { get; set; } = "Trigger {1}.{0} fired job {6}.{5} at: {4:HH:mm:ss MM/dd/yyyy}";

        /// <summary>
        /// Get or set the message that is printed upon a trigger's mis-firing.
        /// </summary>
        public virtual string TriggerMisfiredMessage { get; set; } = "Trigger {1}.{0} misfired job {6}.{5} at: {4:HH:mm:ss MM/dd/yyyy}.  Should have fired at: {3:HH:mm:ss MM/dd/yyyy}";

        /// <summary>
        /// Get the name of the <see cref="ITriggerListener" />.
        /// </summary>
        /// <value></value>
        public virtual string Name { get; set; } = null!;

        /// <summary>
        /// Called during creation of the <see cref="IScheduler" /> in order to give
        /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
        /// </summary>
        public virtual Task Initialize(
            string pluginName,
            IScheduler scheduler,
            CancellationToken cancellationToken = default)
        {
            Name = pluginName;
            scheduler.ListenerManager.AddTriggerListener(this, EverythingMatcher<TriggerKey>.AllTriggers());
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler" /> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public virtual Task Start(CancellationToken cancellationToken = default)
        {
            // do nothing...
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public virtual Task Shutdown(CancellationToken cancellationToken = default)
        {
            // nothing to do...
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// has fired, and it's associated <see cref="IJobDetail" />
        /// is about to be executed.
        /// <para>
        /// It is called before the <see cref="VetoJobExecution" /> method of this
        /// interface.
        /// </para>
        /// </summary>
        /// <param name="trigger">The <see cref="ITrigger" /> that has fired.</param>
        /// <param name="context">The <see cref="IJobExecutionContext" /> that will be passed to the <see cref="IJob" />'s <see cref="IJob.Execute" /> method.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task TriggerFired(
            ITrigger trigger,
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            if (!IsInfoEnabled)
            {
                return Task.CompletedTask;
            }

            object?[] args =
            {
                trigger.Key.Name,
                trigger.Key.Group,
                trigger.GetPreviousFireTimeUtc(),
                trigger.GetNextFireTimeUtc(),
                SystemTime.UtcNow(),
                context.JobDetail.Key.Name,
                context.JobDetail.Key.Group,
                context.RefireCount
            };

            WriteInfo(string.Format(CultureInfo.InvariantCulture, TriggerFiredMessage, args));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// has misfired.
        /// <para>
        /// Consideration should be given to how much time is spent in this method,
        /// as it will affect all triggers that are misfiring.  If you have lots
        /// of triggers misfiring at once, it could be an issue it this method
        /// does a lot.
        /// </para>
        /// </summary>
        /// <param name="trigger">The <see cref="ITrigger" /> that has misfired.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task TriggerMisfired(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            if (!IsInfoEnabled)
            {
                return Task.CompletedTask;
            }

            object?[] args =
            {
                trigger.Key.Name,
                trigger.Key.Group,
                trigger.GetPreviousFireTimeUtc(),
                trigger.GetNextFireTimeUtc(),
                SystemTime.UtcNow(),
                trigger.JobKey.Name,
                trigger.JobKey.Group
            };

            WriteInfo(string.Format(CultureInfo.InvariantCulture, TriggerMisfiredMessage, args));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// has fired, it's associated <see cref="IJobDetail" />
        /// has been executed, and it's <see cref="IOperableTrigger.Triggered" /> method has been
        /// called.
        /// </summary>
        /// <param name="trigger">The <see cref="ITrigger" /> that was fired.</param>
        /// <param name="context">The <see cref="IJobExecutionContext" /> that was passed to the
        /// <see cref="IJob" />'s <see cref="IJob.Execute" /> method.</param>
        /// <param name="triggerInstructionCode">The result of the call on the <see cref="IOperableTrigger" />'s <see cref="IOperableTrigger.Triggered" />  method.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task TriggerComplete(
            ITrigger trigger,
            IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            if (!IsInfoEnabled)
            {
                return Task.CompletedTask;
            }

            string instrCode = "UNKNOWN";
            if (triggerInstructionCode == SchedulerInstruction.DeleteTrigger)
            {
                instrCode = "DELETE TRIGGER";
            }
            else if (triggerInstructionCode == SchedulerInstruction.NoInstruction)
            {
                instrCode = "DO NOTHING";
            }
            else if (triggerInstructionCode == SchedulerInstruction.ReExecuteJob)
            {
                instrCode = "RE-EXECUTE JOB";
            }
            else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersComplete)
            {
                instrCode = "SET ALL OF JOB'S TRIGGERS COMPLETE";
            }
            else if (triggerInstructionCode == SchedulerInstruction.SetTriggerComplete)
            {
                instrCode = "SET THIS TRIGGER COMPLETE";
            }

            object?[] args =
            {
                trigger.Key.Name,
                trigger.Key.Group,
                trigger.GetPreviousFireTimeUtc(),
                trigger.GetNextFireTimeUtc(),
                SystemTime.UtcNow(),
                context.JobDetail.Key.Name,
                context.JobDetail.Key.Group,
                context.RefireCount,
                triggerInstructionCode,
                instrCode
            };

            WriteInfo(string.Format(CultureInfo.InvariantCulture, TriggerCompleteMessage, args));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// has fired, and it's associated <see cref="IJobDetail" />
        /// is about to be executed.
        /// <para>
        /// It is called after the <see cref="TriggerFired" /> method of this
        /// interface.
        /// </para>
        /// </summary>
        /// <param name="trigger">The <see cref="ITrigger" /> that has fired.</param>
        /// <param name="context">The <see cref="IJobExecutionContext" /> that will be passed to
        /// the <see cref="IJob" />'s <see cref="IJob.Execute" /> method.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task<bool> VetoJobExecution(
            ITrigger trigger,
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        protected virtual bool IsInfoEnabled => Log.IsInfoEnabled();

        protected virtual void WriteInfo(string message)
        {
            Log.Info(message);
        }
    }
}