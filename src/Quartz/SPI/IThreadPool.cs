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

using Quartz.Core;

namespace Quartz.Spi
{
	/// <summary>
	/// The interface to be implemented by classes that want to provide a thread
	/// pool for the <code>IQuartzScheduler</code>'s use.
	/// </summary>
	/// <seealso cref="QuartzScheduler" />
	/// <author>James House</author>
	public interface IThreadPool
	{
		int PoolSize { get; }


		/// <summary> <p>
		/// Execute the given <code>{@link java.lang.Runnable}</code> in the next
		/// available <code>Thread</code>.
		/// </p>
		/// 
		/// <p>
		/// The implementation of this interface should not throw exceptions unless
		/// there is a serious problem (i.e. a serious misconfiguration). If there
		/// are no available threads, rather it should either queue the Runnable, or
		/// block until a thread is available, depending on the desired strategy.
		/// </p>
		/// </summary>
		bool RunInThread(IThreadRunnable runnable);

		/// <summary> <p>
		/// Called by the QuartzScheduler before the <code>ThreadPool</code> is
		/// used, in order to give the it a chance to initialize.
		/// </p>
		/// </summary>
		void Initialize();

		/// <summary> <p>
		/// Called by the QuartzScheduler to inform the <code>ThreadPool</code>
		/// that it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </p>
		/// </summary>
		void Shutdown(bool waitForJobsToComplete);
	}
}