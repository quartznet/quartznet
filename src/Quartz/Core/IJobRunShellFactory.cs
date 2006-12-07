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

namespace Quartz.Core
{
	/// <summary>
	/// Responsible for creating the instances of <code>JobRunShell</code>
	/// to be used within the <code>QuartzScheduler</code> instance.
	/// <p>
	/// Although this interface looks a lot like an 'object pool', implementations
	/// do not have to support the re-use of instances. If an implementation does
	/// not wish to pool instances, then the <code>BorrowJobRunShell()</code>
	/// method would simply create a new instance, and the <code>ReturnJobRunShell
	/// </code> method would do nothing.
	/// </p>
	/// </summary>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public interface IJobRunShellFactory
	{
		/// <summary>
		/// Initialize the factory, providing a handle to the <code>Scheduler</code>
		/// that should be made available within the <code>JobRunShell</code> and
		/// the <code>JobExecutionContext</code> s within it, and a handle to the
		/// <code>SchedulingContext</code> that the shell will use in its own
		/// operations with the <code>JobStore</code>.
		/// </summary>
		void Initialize(IScheduler sched, SchedulingContext ctx);

		/// <summary>
		/// Called by the <code>QuartzSchedulerThread</code>
		/// to obtain instances of <code>JobRunShell</code>.
		/// </summary>
		JobRunShell BorrowJobRunShell();

		/// <summary>
		/// Called by the <code>QuartzSchedulerThread</code>
		/// to return instances of <code>JobRunShell</code>.
		/// </summary>
		void ReturnJobRunShell(JobRunShell jobRunShell);
	}
}