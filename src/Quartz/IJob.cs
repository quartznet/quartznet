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

namespace Quartz
{
	/// <summary> 
	/// The interface to be implemented by classes which represent a 'job' to be
	/// performed.
	/// <p>
	/// Instances of <code>Job</code> must have a <code>public</code>
	/// no-argument constructor. <code>JobDataMap</code> provides a mechanism for 'instance member data'
	/// that may be required by some implementations of this interface.
	/// </p>
	/// </summary>
	/// <seealso cref="JobDetail" />
	/// <seealso cref="IStatefulJob" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="IScheduler" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public interface IJob
	{
		/// <summary>
		/// Called by the <code>Scheduler</code> when a <code>Trigger</code>
		/// fires that is associated with the <code>Job</code>.
		/// <p>
		/// The implementation may wish to set a  result object on the 
		/// JobExecutionContext before this method exits.  The result itself
		/// is meaningless to Quartz, but may be informative to 
		/// <code>JobListeners</code> or 
		/// <code>TriggerListeners</code> that are watching the job's 
		/// execution.
		/// </p>
		/// <param name="context">The execution context.</param>
		/// </summary>
		void Execute(JobExecutionContext context);
	}
}