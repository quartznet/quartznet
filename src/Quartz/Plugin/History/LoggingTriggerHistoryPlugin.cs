/* 
* Copyright 2004-2005 OpenSymphony 
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

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Plugins.History
{
	/// <summary> Logs a history of all trigger firings via the Jakarta Commons-Logging
	/// framework.
	/// 
	/// <p>
	/// The logged message is customizable by setting one of the following message
	/// properties to a string that conforms to the syntax of <code>java.util.MessageFormat</code>.
	/// </p>
	/// 
	/// <p>
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
	/// </p>
	/// 
	/// <p>
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
	/// </p>
	/// 
	/// <p>
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
	/// </p>
	/// 
	/// </summary>
	/// <author>  James House
	/// </author>
	public class LoggingTriggerHistoryPlugin : ISchedulerPlugin, ITriggerListener
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (LoggingTriggerHistoryPlugin));

		/// <summary> 
		/// Get or set the message that is printed upon the completion of a trigger's
		/// firing.
		/// </summary>
		public virtual string TriggerCompleteMessage
		{
			get { return triggerCompleteMessage; }
			set { triggerCompleteMessage = value; }
		}

		/// <summary> 
		/// Get or set the message that is printed upon a trigger's firing.
		/// </summary>
		public virtual string TriggerFiredMessage
		{
			get { return triggerFiredMessage; }
			set { triggerFiredMessage = value; }
		}

		/// <summary> 
		/// Get or set the message that is printed upon a trigger's mis-firing.
		/// </summary>
		public virtual string TriggerMisfiredMessage
		{
			get { return triggerMisfiredMessage; }
			set { triggerMisfiredMessage = value; }
		}

        /// <summary>
        /// Get the name of the <code>TriggerListener</code>.
        /// </summary>
        /// <value></value>
		public virtual string Name
		{
			/*
			* object[] arguments = { new Integer(7), new
			* Date(System.currentTimeMillis()), "a disturbance in the Force" };
			* 
			* string result = MessageFormat.format( "At {1,time} on {1,date}, there
			* was {2} on planet {0,number,integer}.", arguments);
			*/

			get { return name; }
		}


		private string name;
		private string triggerFiredMessage = "Trigger {1}.{0} fired job {6}.{5} at: {4, date, HH:mm:ss MM/dd/yyyy}";
		private string triggerMisfiredMessage = "Trigger {1}.{0} misfired job {6}.{5}  at: {4, date, HH:mm:ss MM/dd/yyyy}.  Should have fired at: {3, date, HH:mm:ss MM/dd/yyyy}";
		private string triggerCompleteMessage = "Trigger {1}.{0} completed firing job {6}.{5} at {4, date, HH:mm:ss MM/dd/yyyy} with resulting trigger instruction code: {9}";


		/// <summary>
		/// Called during creation of the <code>Scheduler</code> in order to give
		/// the <code>SchedulerPlugin</code> a chance to Initialize.
		/// </summary>
		public virtual void Initialize(String pluginName, IScheduler sched)
		{
			name = pluginName;
			sched.AddGlobalTriggerListener(this);
		}

        /// <summary>
        /// Called when the associated <code>Scheduler</code> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
		public virtual void Start()
		{
			// do nothing...
		}

		/// <summary>
		/// Called in order to inform the <code>SchedulerPlugin</code> that it
		/// should free up all of it's resources because the scheduler is shutting
		/// down.
		/// </summary>
		public virtual void Shutdown()
		{
			// nothing to do...
		}

        /// <summary>
        /// Called by the <code>IScheduler}</code> when a <code>Trigger</code>
        /// has fired, and it's associated <code>JobDetail</code>
        /// is about to be executed.
        /// <p>
        /// It is called before the <code>VetoJobExecution(..)</code> method of this
        /// interface.
        /// </p>
        /// </summary>
        /// <param name="trigger">The <code>Trigger</code> that has fired.</param>
        /// <param name="context">The <code>JobExecutionContext</code> that will be passed to the <code>IJob</code>'s<code>Execute(xx)</code> method.</param>
		public virtual void TriggerFired(Trigger trigger, JobExecutionContext context)
		{
			if (!Log.IsInfoEnabled)
			{
				return;
			}

			object[] args =
				new object[]
					{
						trigger.Name, trigger.Group, trigger.GetPreviousFireTime(), trigger.GetNextFireTime(), DateTime.Now,
						context.JobDetail.Name, context.JobDetail.Group, context.RefireCount
					};

			Log.Info(String.Format(TriggerFiredMessage, args));
		}

        /// <summary>
        /// Called by the <code>IScheduler</code> when a <code>Trigger</code>
        /// has misfired.
        /// <p>
        /// Consideration should be given to how much time is spent in this method,
        /// as it will affect all triggers that are misfiring.  If you have lots
        /// of triggers misfiring at once, it could be an issue it this method
        /// does a lot.
        /// </p>
        /// </summary>
        /// <param name="trigger">The <code>Trigger</code> that has misfired.</param>
		public virtual void TriggerMisfired(Trigger trigger)
		{
			if (!Log.IsInfoEnabled)
			{
				return;
			}

			object[] args =
				new object[]
					{
						trigger.Name, trigger.Group, trigger.GetPreviousFireTime(), trigger.GetNextFireTime(), DateTime.Now,
						trigger.JobGroup, trigger.JobGroup
					};

			Log.Info(String.Format(TriggerMisfiredMessage, args));
		}

        /// <summary>
        /// Called by the <code>IScheduler</code> when a <code>Trigger</code>
        /// has fired, it's associated <code>JobDetail</code>
        /// has been executed, and it's <code>Triggered(xx)</code> method has been
        /// called.
        /// </summary>
        /// <param name="trigger">The <code>Trigger</code> that was fired.</param>
        /// <param name="context">The <code>JobExecutionContext</code> that was passed to the
        /// <code>Job</code>'s<code>Execute(xx)</code> method.</param>
        /// <param name="triggerInstructionCode">The result of the call on the <code>Trigger</code>'s<code>triggered(xx)</code>  method.</param>
		public virtual void TriggerComplete(Trigger trigger, JobExecutionContext context, int triggerInstructionCode)
		{
			if (!Log.IsInfoEnabled)
			{
				return;
			}

			String instrCode = "UNKNOWN";
			if (triggerInstructionCode == Trigger.INSTRUCTION_DELETE_TRIGGER)
			{
				instrCode = "DELETE TRIGGER";
			}
			else if (triggerInstructionCode == Trigger.INSTRUCTION_NOOP)
			{
				instrCode = "DO NOTHING";
			}
			else if (triggerInstructionCode == Trigger.INSTRUCTION_RE_EXECUTE_JOB)
			{
				instrCode = "RE-EXECUTE JOB";
			}
			else if (triggerInstructionCode == Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_COMPLETE)
			{
				instrCode = "SET ALL OF JOB'S TRIGGERS COMPLETE";
			}
			else if (triggerInstructionCode == Trigger.INSTRUCTION_SET_TRIGGER_COMPLETE)
			{
				instrCode = "SET THIS TRIGGER COMPLETE";
			}

			object[] args =
				new object[]
					{
						trigger.Name, trigger.Group, trigger.GetPreviousFireTime(), trigger.GetNextFireTime(), DateTime.Now,
						context.JobDetail.Name, context.JobDetail.Group, context.RefireCount, triggerInstructionCode, instrCode
					};

			Log.Info(String.Format(TriggerCompleteMessage, args));
		}

        /// <summary>
        /// Called by the <code>IScheduler</code> when a <code>Trigger</code>
        /// has fired, and it's associated <code>JobDetail</code>
        /// is about to be executed.
        /// <p>
        /// It is called after the <code>TriggerFired(..)</code> method of this
        /// interface.
        /// </p>
        /// </summary>
        /// <param name="trigger">The <code>Trigger</code> that has fired.</param>
        /// <param name="context">The <code>JobExecutionContext</code> that will be passed to
        /// the <code>Job</code>'s<code>Execute(xx)</code> method.</param>
        /// <returns></returns>
		public virtual bool VetoJobExecution(Trigger trigger, JobExecutionContext context)
		{
			return false;
		}
	}
}