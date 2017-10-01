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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;

namespace Quartz
{
	/// <summary>
	/// Provides a mechanism for obtaining client-usable handles to <see cref="IScheduler" />
	/// instances.
	/// </summary>
	/// <seealso cref="IScheduler" />
	/// <seealso cref="StdSchedulerFactory" />
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface ISchedulerFactory
	{
		/// <summary>
		/// Returns handles to all known Schedulers (made by any SchedulerFactory
		/// within this app domain.).
		/// </summary>
		Task<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns a client-usable handle to a <see cref="IScheduler" />.
		/// </summary>
		Task<IScheduler> GetScheduler(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a handle to the Scheduler with the given name, if it exists.
        /// </summary>
        Task<IScheduler> GetScheduler(string schedName, CancellationToken cancellationToken = default);
	}
}