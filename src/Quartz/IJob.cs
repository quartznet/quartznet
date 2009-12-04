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

namespace Quartz
{
	/// <summary> 
	/// The interface to be implemented by classes which represent a 'job' to be
	/// performed.
	/// </summary>
	/// <remarks>
	/// Instances of this interface must have a <see langword="public" />
	/// no-argument constructor. <see cref="JobDataMap" /> provides a mechanism for 'instance member data'
	/// that may be required by some implementations of this interface.
    /// </remarks>
	/// <seealso cref="JobDetail" />
	/// <seealso cref="IStatefulJob" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="IScheduler" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public interface IJob
	{
		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="Trigger" />
		/// fires that is associated with the <see cref="IJob" />.
        /// </summary>
		/// <remarks>
		/// The implementation may wish to set a  result object on the 
		/// JobExecutionContext before this method exits.  The result itself
		/// is meaningless to Quartz, but may be informative to 
		/// <see cref="IJobListener" />s or 
		/// <see cref="ITriggerListener" />s that are watching the job's 
		/// execution.
		/// </remarks>
		/// <param name="context">The execution context.</param>
		void Execute(JobExecutionContext context);
	}
}