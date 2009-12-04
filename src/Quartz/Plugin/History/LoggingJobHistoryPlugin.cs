/* 
* Copyright 2004-2009 James House 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/
using System;
using System.Globalization;

using Common.Logging;
using Quartz.Spi;

namespace Quartz.Plugin.History
{
    /// <summary>
    /// Logs a history of all job executions (and execution vetos) via common
    /// logging.
    /// </summary>
    /// <remarks>
    /// 	<p>
    /// The logged message is customizable by setting one of the following message
    /// properties to a string that conforms to the syntax of <see cref="string.Format(string,object)"/>.
    /// </p>
    /// 	<p>
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
    /// 				<td>The Triggers's group.</td>
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
    /// The default message text is <i>"Job {1}.{0} fired (by trigger {4}.{3}) at:
    /// {2, date, HH:mm:ss MM/dd/yyyy"</i>
    /// 	</p>
    /// 	<p>
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
    /// 				<td>The Triggers's group.</td>
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
    /// The default message text is <i>"Job {1}.{0} execution complete at {2, date,
    /// HH:mm:ss MM/dd/yyyy} and reports: {8"</i>
    /// 	</p>
    /// 	<p>
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
    /// 				<td>The Triggers's group.</td>
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
    /// The default message text is <i>"Job {1}.{0} execution failed at {2, date,
    /// HH:mm:ss MM/dd/yyyy} and reports: {8"</i>
    /// 	</p>
    /// 	<p>
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
    /// 				<td>The Triggers's group.</td>
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
    /// (by trigger {4}.{3}) at: {2, date, HH:mm:ss MM/dd/yyyy"</i>
    /// 	</p>
    /// </remarks>
    public class LoggingJobHistoryPlugin : ISchedulerPlugin, IJobListener
    {
        private string name;
        private string jobToBeFiredMessage = "Job {1}.{0} fired (by trigger {4}.{3}) at: {2:HH:mm:ss MM/dd/yyyy}";
        private string jobSuccessMessage = "Job {1}.{0} execution complete at {2:HH:mm:ss MM/dd/yyyy} and reports: {8}";
        private string jobFailedMessage = "Job {1}.{0} execution failed at {2:HH:mm:ss MM/dd/yyyy} and reports: {8}";

        private string jobWasVetoedMessage =
            "Job {1}.{0} was vetoed.  It was to be fired (by trigger {4}.{3}) at: {2:HH:mm:ss MM/dd/yyyy}";

        private ILog log = LogManager.GetLogger(typeof (LoggingJobHistoryPlugin));

        /// <summary>
        /// Logger instance to use. Defaults to common logging.
        /// </summary>
        public ILog Log
        {
            get { return log; }
            set { log = value; }
        }

        /// <summary> 
        /// Get or sets the message that is logged when a Job successfully completes its 
        /// execution.
        /// </summary>
        public virtual string JobSuccessMessage
        {
            get { return jobSuccessMessage; }
            set { jobSuccessMessage = value; }
        }

        /// <summary> 
        /// Get or sets the message that is logged when a Job fails its 
        /// execution.
        /// </summary>
        public virtual string JobFailedMessage
        {
            get { return jobFailedMessage; }
            set { jobFailedMessage = value; }
        }

        /// <summary> 
        /// Gets or sets the message that is logged when a Job is about to Execute.
        /// </summary>
        public virtual string JobToBeFiredMessage
        {
            get { return jobToBeFiredMessage; }
            set { jobToBeFiredMessage = value; }
        }

        /// <summary> 
        /// Gets or sets the message that is logged when a Job execution is vetoed by a
        /// trigger listener.
        /// </summary>
        public virtual string JobWasVetoedMessage
        {
            get { return jobWasVetoedMessage; }
            set { jobWasVetoedMessage = value; }
        }

        /// <summary>
        /// Get the name of the <see cref="IJobListener" />.
        /// </summary>
        /// <value></value>
        public virtual string Name
        {
            get { return name; }
			set { name = value; }
        }

        /// <summary>
        /// Called during creation of the <see cref="IScheduler" /> in order to give
        /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
        /// </summary>
        public virtual void Initialize(String pluginName, IScheduler sched)
        {
            name = pluginName;
            sched.AddGlobalJobListener(this);
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler" /> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public virtual void Start()
        {
            // do nothing...
        }

        /// <summary> 
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public virtual void Shutdown()
        {
            // nothing to do...
        }

        /// <summary>
        ///     Called by the <see cref="IScheduler"/> when a <see cref="JobDetail"/> is
        ///     about to be executed (an associated <see cref="Trigger"/> has occurred). 
        ///     <para>
        ///         This method will not be invoked if the execution of the Job was vetoed by a
        ///         <see cref="ITriggerListener"/>.
        ///     </para>
        /// </summary>
        /// <seealso cref="JobExecutionVetoed(JobExecutionContext)"/>
        public virtual void JobToBeExecuted(JobExecutionContext context)
        {
            if (!Log.IsInfoEnabled)
            {
                return;
            }

            Trigger trigger = context.Trigger;

            object[] args =
                new object[]
                    {
                        context.JobDetail.Name, context.JobDetail.Group, DateTime.UtcNow, trigger.Name, trigger.Group,
                        trigger.GetPreviousFireTimeUtc(), trigger.GetNextFireTimeUtc(), context.RefireCount
                    };

            Log.Info(String.Format(CultureInfo.InvariantCulture, JobToBeFiredMessage, args));
        }


        /// <summary>
        /// Called by the <see cref="IScheduler" /> after a <see cref="JobDetail" />
        /// has been executed, and be for the associated <see cref="Trigger" />'s
        /// <see cref="Trigger.Triggered" /> method has been called.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="jobException"></param>
        public virtual void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException)
        {
            Trigger trigger = context.Trigger;

            object[] args;

            if (jobException != null)
            {
                if (!Log.IsWarnEnabled)
                {
                    return;
                }

                string errMsg = jobException.Message;
                args =
                    new object[]
                        {
                            context.JobDetail.Name, context.JobDetail.Group, DateTime.UtcNow, trigger.Name, trigger.Group,
                            trigger.GetPreviousFireTimeUtc(), trigger.GetNextFireTimeUtc(), context.RefireCount, errMsg
                        };

                Log.Warn(String.Format(CultureInfo.InvariantCulture, JobFailedMessage, args), jobException);
            }
            else
            {
                if (!Log.IsInfoEnabled)
                {
                    return;
                }

                string result = Convert.ToString(context.Result, CultureInfo.InvariantCulture);
                args =
                    new object[]
                        {
                            context.JobDetail.Name, context.JobDetail.Group, DateTime.UtcNow, trigger.Name, trigger.Group,
                            trigger.GetPreviousFireTimeUtc(), trigger.GetNextFireTimeUtc(), context.RefireCount, result
                        };

                Log.Info(String.Format(CultureInfo.InvariantCulture, JobSuccessMessage, args));
            }
        }

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="JobDetail" />
        /// was about to be executed (an associated <see cref="Trigger" />
        /// has occured), but a <see cref="ITriggerListener" /> vetoed it's
        /// execution.
        /// </summary>
        /// <param name="context"></param>
        /// <seealso cref="JobToBeExecuted(JobExecutionContext)"/>
        public virtual void JobExecutionVetoed(JobExecutionContext context)
        {
            if (!Log.IsInfoEnabled)
            {
                return;
            }

            Trigger trigger = context.Trigger;

            object[] args =
                new object[]
                    {
                        context.JobDetail.Name, context.JobDetail.Group, DateTime.UtcNow, trigger.Name, trigger.Group,
                        trigger.GetPreviousFireTimeUtc(), trigger.GetNextFireTimeUtc(), context.RefireCount
                    };

            Log.Info(String.Format(CultureInfo.InvariantCulture, JobWasVetoedMessage, args));
        }
    }
}