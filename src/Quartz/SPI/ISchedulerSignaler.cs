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

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

using Quartz.Core;

namespace Quartz.Spi
{
	/// <summary> 
	/// An interface to be used by <see cref="IJobStore" /> instances in order to
	/// communicate signals back to the <see cref="QuartzScheduler" />.
	/// </summary>
	/// <author>James House</author>
	public interface ISchedulerSignaler
	{
		/// <summary>
		/// Notifies the scheduler about misfired trigger.
		/// </summary>
		/// <param name="trigger">The trigger that misfired.</param>
		void NotifyTriggerListenersMisfired(Trigger trigger);

        /// <summary>
        /// Notifies the scheduler about finalized trigger.
        /// </summary>
        /// <param name="trigger">The trigger that has finalized.</param>
        void NotifySchedulerListenersFinalized(Trigger trigger);

		/// <summary>
		/// Signals the scheduling change.
		/// </summary>
        void SignalSchedulingChange(NullableDateTime candidateNewNextFireTimeUtc);
	}
}