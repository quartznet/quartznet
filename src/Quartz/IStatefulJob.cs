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

using System;

namespace Quartz
{
	/// <summary>
    /// A marker interface for <see cref="IJobDetail" /> s that
	/// wish to have their state maintained between executions.
	/// </summary>
	/// <remarks>
	/// <see cref="IStatefulJob" /> instances follow slightly different rules from
	/// regular <see cref="IJob" /> instances. The key difference is that their
	/// associated <see cref="JobDataMap" /> is re-persisted after every
	/// execution of the job, thus preserving state for the next execution. The
	/// other difference is that stateful jobs are not allowed to Execute
	/// concurrently, which means new triggers that occur before the completion of
	/// the <see cref="IJob.Execute" /> method will be delayed.
	/// </remarks>
    /// <seealso cref="DisallowConcurrentExecutionAttribute" />
    /// <seealso cref="PersistJobDataAfterExecutionAttribute" />
    /// <seealso cref="IJob" />
	/// <seealso cref="IJobDetail" />
	/// <seealso cref="JobDataMap" />
	/// <seealso cref="IScheduler" /> 
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
    [Obsolete("Use DisallowConcurrentExecutionAttribute and/or PersistJobDataAfterExecutionAttribute annotations instead.", true)]
	public interface IStatefulJob : IJob
	{
	}
}