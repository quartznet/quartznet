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

using Quartz.Core;
using Quartz.Spi;

namespace Quartz.Impl
{
	/// <summary> 
	/// Responsible for creating the instances of <see cref="JobRunShell" />
	/// to be used within the <see cref="QuartzScheduler" /> instance.
	/// </summary>
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class StdJobRunShellFactory : IJobRunShellFactory
	{
		private IScheduler scheduler = null!;
		
		/// <summary>
		/// Initialize the factory, providing a handle to the <see cref="IScheduler" />
		/// that should be made available within the <see cref="JobRunShell" /> and
		/// the <see cref="IJobExecutionContext" /> s within it.
		/// </summary>
		public virtual void Initialize(IScheduler sched)
		{
			scheduler = sched;
		}

		/// <summary>
		/// Called by the <see cref="QuartzSchedulerThread" /> to obtain instances of 
		/// <see cref="JobRunShell" />.
		/// </summary>
        public virtual JobRunShell CreateJobRunShell(TriggerFiredBundle bndle)
		{
			return new JobRunShell(scheduler, bndle);
		}
	}
}