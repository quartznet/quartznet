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
using Quartz.Core;
using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary>
	/// The interface to be implemented by classes that want to provide a mechanism
	/// by which the <see cref="QuartzScheduler" /> can  reliably determine the current time.
	/// <p>
	/// In general, the default implementation of this interface (<see cref="SimpleTimeBroker" />-
	/// which simply uses <see cref="DateTime.Ticks" /> )is
	/// sufficient. However situations may exist where this default scheme is
	/// lacking in its robustsness - especially when Quartz is used in a clustered
	/// configuration. For example, if one or more of the machines in the cluster
	/// has a system time that varies by more than a few seconds from the clocks on
	/// the other systems in the cluster, scheduling confusion will result.
	/// </p>
	/// </summary>
	/// <seealso cref="QuartzScheduler" /> 
	/// <author>James House</author>
	public class SimpleTimeBroker : ITimeBroker
	{
		/// <summary>
		/// Get the current time, simply using <see cref="DateTime.Now" />.
		/// </summary>
		public virtual DateTime CurrentTime
		{
			get { return DateTime.Now; }
		}

		/// <summary>
		/// Called by the QuartzScheduler before the <see cref="ITimeBroker" /> is
		/// used, in order to give the it a chance to Initialize.
		/// </summary>
		public virtual void Initialize()
		{
			// do nothing...
		}

		/// <summary>
		/// Called by the QuartzScheduler to inform the <see cref="ITimeBroker" />
		/// that it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </summary>
		public virtual void Shutdown()
		{
			// do nothing...
		}
	}
}