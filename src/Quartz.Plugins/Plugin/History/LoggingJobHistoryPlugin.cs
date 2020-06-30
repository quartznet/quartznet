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

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Plugin.History
{
    /// <summary>
    /// Logs a history of all job executions (and execution vetoes) via common
    /// logging.
    /// </summary>
    /// <remarks>
    /// 	<para>
    /// The logged message is customizable by setting one of the following message
    /// properties to a string that conforms to the syntax of <see cref="string.Format(string,object)"/>.
    /// </para>
    /// 	<para>
    /// JobToBeFiredMessage - available message data are: <table>
    /// 			<tr>
    /// 				<th>Element</th>
    /// 				<th>Data Type</th>
    /// 				<th>Description</th>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>0</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>1</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>2</td>
    /// 				<td>Date</td>
    /// 				<td>The current time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>3</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>4</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>5</td>
    /// 				<td>Date</td>
    /// 				<td>The scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>6</td>
    /// 				<td>Date</td>
    /// 				<td>The next scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>7</td>
    /// 				<td>Integer</td>
    /// 				<td>The re-fire count from the JobExecutionContext.</td>
    /// 			</tr>
    /// 		</table>
    /// The default message text is <i>"Job {1}.{0} fired (by trigger {4}.{3}) at: {2:HH:mm:ss MM/dd/yyyy}"</i>
    /// 	</para>
    /// 	<para>
    /// JobSuccessMessage - available message data are: <table>
    /// 			<tr>
    /// 				<th>Element</th>
    /// 				<th>Data Type</th>
    /// 				<th>Description</th>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>0</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>1</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>2</td>
    /// 				<td>Date</td>
    /// 				<td>The current time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>3</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>4</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>5</td>
    /// 				<td>Date</td>
    /// 				<td>The scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>6</td>
    /// 				<td>Date</td>
    /// 				<td>The next scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>7</td>
    /// 				<td>Integer</td>
    /// 				<td>The re-fire count from the JobExecutionContext.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>8</td>
    /// 				<td>Object</td>
    /// 				<td>The string value (toString() having been called) of the result (if any)
    /// that the Job set on the JobExecutionContext, with on it.  "NULL" if no
    /// result was set.</td>
    /// 			</tr>
    /// 		</table>
    /// The default message text is <i>"Job {1}.{0} execution complete at {2:HH:mm:ss MM/dd/yyyy} and reports: {8}"</i>
    /// 	</para>
    /// 	<para>
    /// JobFailedMessage - available message data are: <table>
    /// 			<tr>
    /// 				<th>Element</th>
    /// 				<th>Data Type</th>
    /// 				<th>Description</th>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>0</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>1</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>2</td>
    /// 				<td>Date</td>
    /// 				<td>The current time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>3</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>4</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>5</td>
    /// 				<td>Date</td>
    /// 				<td>The scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>6</td>
    /// 				<td>Date</td>
    /// 				<td>The next scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>7</td>
    /// 				<td>Integer</td>
    /// 				<td>The re-fire count from the JobExecutionContext.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>8</td>
    /// 				<td>String</td>
    /// 				<td>The message from the thrown JobExecution Exception.
    /// </td>
    /// 			</tr>
    /// 		</table>
    /// The default message text is <i>"Job {1}.{0} execution failed at {2:HH:mm:ss MM/dd/yyyy} and reports: {8}"</i>
    /// 	</para>
    /// 	<para>
    /// JobWasVetoedMessage - available message data are: <table>
    /// 			<tr>
    /// 				<th>Element</th>
    /// 				<th>Data Type</th>
    /// 				<th>Description</th>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>0</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>1</td>
    /// 				<td>String</td>
    /// 				<td>The Job's Group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>2</td>
    /// 				<td>Date</td>
    /// 				<td>The current time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>3</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's name.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>4</td>
    /// 				<td>String</td>
    /// 				<td>The Trigger's group.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>5</td>
    /// 				<td>Date</td>
    /// 				<td>The scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>6</td>
    /// 				<td>Date</td>
    /// 				<td>The next scheduled fire time.</td>
    /// 			</tr>
    /// 			<tr>
    /// 				<td>7</td>
    /// 				<td>Integer</td>
    /// 				<td>The re-fire count from the JobExecutionContext.</td>
    /// 			</tr>
    /// 		</table>
    /// The default message text is <i>"Job {1}.{0} was vetoed.  It was to be fired
    /// (by trigger {4}.{3}) at: {2:HH:mm:ss MM/dd/yyyy}"</i>
    /// 	</para>
    /// </remarks>
    /// <author>Marko Lahma (.NET)</author>
    public class LoggingJobHistoryPlugin : ISchedulerPlugin, IJobListener
    {
        /// <summary>
        /// Logger instance to use. Defaults to common logging.
        /// </summary>
        private ILog Log { get; set; } = LogProvider.GetLogger(typeof(LoggingJobHistoryPlugin));

        /// <summary>
        /// Get or sets the message that is logged when a Job successfully completes its
        /// execution.
        /// </summary>
        public virtual string JobSuccessMessage { get; set; } = "Job {1}.{0} execution complete at {2:HH:mm:ss MM/dd/yyyy} and reports: {8}";

        /// <summary>
        /// Get or sets the message that is logged when a Job fails its
        /// execution.
        /// </summary>
        public virtual string JobFailedMessage { get; set; } = "Job {1}.{0} execution failed at {2:HH:mm:ss MM/dd/yyyy} and reports: {8}";

        /// <summary>
        /// Gets or sets the message that is logged when a Job is about to Execute.
        /// </summary>
        public virtual string JobToBeFiredMessage { get; set; } = "Job {1}.{0} fired (by trigger {4}.{3}) at: {2:HH:mm:ss MM/dd/yyyy}";

        /// <summary>
        /// Gets or sets the message that is logged when a Job execution is vetoed by a
        /// trigger listener.
        /// </summary>
        public virtual string JobWasVetoedMessage { get; set; } = "Job {1}.{0} was vetoed.  It was to be fired (by trigger {4}.{3}) at: {2:HH:mm:ss MM/dd/yyyy}";

        /// <summary>
        /// Get the name of the <see cref="IJobListener" />.
        /// </summary>
        /// <value></value>
        public string Name { get; set; } = null!;

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
            scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler" /> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public virtual Task Start(CancellationToken cancellationToken = default)
        {
            // do nothing...
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public virtual Task Shutdown(CancellationToken cancellationToken = default)
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
        /// <seealso cref="JobExecutionVetoed"/>
        public virtual Task JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            if (!IsInfoEnabled)
            {
                return TaskUtil.CompletedTask;
            }

            ITrigger trigger = context.Trigger;

            object?[] args =
            {
                context.JobDetail.Key.Name,
                context.JobDetail.Key.Group,
                SystemTime.UtcNow(),
                trigger.Key.Name,
                trigger.Key.Group,
                trigger.GetPreviousFireTimeUtc(),
                trigger.GetNextFireTimeUtc(),
                context.RefireCount
            };

            WriteInfo(string.Format(CultureInfo.InvariantCulture, JobToBeFiredMessage, args));
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> after a <see cref="IJobDetail" />
        /// has been executed, and be for the associated <see cref="ITrigger" />'s
        /// <see cref="IOperableTrigger.Triggered" /> method has been called.
        /// </summary>
        public virtual Task JobWasExecuted(IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            ITrigger trigger = context.Trigger;

            object?[] args;

            if (jobException != null)
            {
                if (!IsWarnEnabled)
                {
                    return TaskUtil.CompletedTask;
                }

                string errMsg = jobException.Message;
                args = new object?[]
                {
                    context.JobDetail.Key.Name, context.JobDetail.Key.Group, SystemTime.UtcNow(), trigger.Key.Name, trigger.Key.Group,
                    trigger.GetPreviousFireTimeUtc(), trigger.GetNextFireTimeUtc(), context.RefireCount, errMsg
                };

                WriteWarning(string.Format(CultureInfo.InvariantCulture, JobFailedMessage, args), jobException);
            }
            else
            {
                if (!IsInfoEnabled)
                {
                    return TaskUtil.CompletedTask;
                }

                var result = Convert.ToString(context.Result, CultureInfo.InvariantCulture);
                args = new object?[]
                {
                    context.JobDetail.Key.Name, context.JobDetail.Key.Group, SystemTime.UtcNow(), trigger.Key.Name, trigger.Key.Group,
                    trigger.GetPreviousFireTimeUtc(), trigger.GetNextFireTimeUtc(), context.RefireCount, result
                };

                WriteInfo(string.Format(CultureInfo.InvariantCulture, JobSuccessMessage, args));
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
        /// was about to be executed (an associated <see cref="ITrigger" />
        /// has occurred), but a <see cref="ITriggerListener" /> vetoed it's
        /// execution.
        /// </summary>
        /// <seealso cref="JobToBeExecuted"/>
        public virtual Task JobExecutionVetoed(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            if (!IsInfoEnabled)
            {
                return TaskUtil.CompletedTask;
            }

            ITrigger trigger = context.Trigger;

            object?[] args =
            {
                context.JobDetail.Key.Name,
                context.JobDetail.Key.Group,
                SystemTime.UtcNow(),
                trigger.Key.Name,
                trigger.Key.Group,
                trigger.GetPreviousFireTimeUtc(),
                trigger.GetNextFireTimeUtc(),
                context.RefireCount
            };

            WriteInfo(string.Format(CultureInfo.InvariantCulture, JobWasVetoedMessage, args));
            return TaskUtil.CompletedTask;
        }

        protected virtual bool IsInfoEnabled => Log.IsInfoEnabled();

        protected virtual void WriteInfo(string message)
        {
            Log.Info(message);
        }

        protected virtual bool IsWarnEnabled => Log.IsWarnEnabled();

        protected virtual void WriteWarning(string message, Exception ex)
        {
            Log.WarnException(message, ex);
        }
    }
}