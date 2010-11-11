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

namespace Quartz.Core
{
	/// <summary>
	/// Responsible for creating the instances of <see cref="JobRunShell" />
	/// to be used within the <see cref="QuartzScheduler" /> instance.
	/// </summary>
	/// <remarks>
	/// Although this interface looks a lot like an 'object pool', implementations
	/// do not have to support the re-use of instances. If an implementation does
	/// not wish to pool instances, then the <see cref="BorrowJobRunShell()" />
	/// method would simply create a new instance, and the <see cref="ReturnJobRunShell" /> 
	/// method would do nothing.
    /// </remarks>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public interface IJobRunShellFactory
	{
		/// <summary>
		/// Initialize the factory, providing a handle to the <see cref="IScheduler" />
		/// that should be made available within the <see cref="JobRunShell" />.
		/// </summary>
		void Initialize(IScheduler sched);

		/// <summary>
		/// Called by the <see cref="QuartzSchedulerThread" />
		/// to obtain instances of <see cref="JobRunShell" />.
		/// </summary>
		JobRunShell BorrowJobRunShell();

		/// <summary>
		/// Called by the <see cref="QuartzSchedulerThread" />
		/// to return instances of <see cref="JobRunShell" />.
		/// </summary>
		void ReturnJobRunShell(JobRunShell jobRunShell);
	}
}