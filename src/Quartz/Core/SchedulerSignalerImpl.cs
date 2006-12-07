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
using log4net;

using Quartz.Spi;

namespace Quartz.Core
{
	/// <summary> 
	/// An interface to be used by <code>JobStore</code> instances in order to
	/// communicate signals back to the <code>QuartzScheduler</code>.
	/// </summary>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class SchedulerSignalerImpl : ISchedulerSignaler
	{
		private ILog log = LogManager.GetLogger(typeof (SchedulerSignalerImpl));
		private QuartzScheduler sched;

		internal SchedulerSignalerImpl(QuartzScheduler sched)
		{
			this.sched = sched;
		}

		public virtual void NotifyTriggerListenersMisfired(Trigger trigger)
		{
			try
			{
				sched.NotifyTriggerListenersMisfired(trigger);
			}
			catch (SchedulerException se)
			{
				log.Error("Error notifying listeners of trigger misfire.", se);
				sched.NotifySchedulerListenersError("Error notifying listeners of trigger misfire.", se);
			}
		}

		public virtual void SignalSchedulingChange()
		{
			sched.NotifySchedulerThread();
		}
	}
}