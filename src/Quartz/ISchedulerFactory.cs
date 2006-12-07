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

using System.Collections;
using Quartz.Impl;

namespace Quartz
{
	/// <summary> <p>
	/// Provides a mechanism for obtaining client-usable handles to <code>Scheduler</code>
	/// instances.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IScheduler">
	/// </seealso>
	/// <seealso cref="StdSchedulerFactory">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	public interface ISchedulerFactory
	{
		/// <summary> <p>
		/// Returns handles to all known Schedulers (made by any SchedulerFactory
		/// within this jvm.).
		/// </p>
		/// </summary>
		ICollection AllSchedulers { get; }

		/// <summary>
		/// Returns a client-usable handle to a <code>Scheduler</code>.
		/// </summary>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if there is a problem with the underlying <code>Scheduler</code>.
		/// </summary>
		IScheduler GetScheduler();

		/// <summary>
		/// Returns a handle to the Scheduler with the given name, if it exists.
		/// </summary>
		IScheduler GetScheduler(string schedName);
	}
}