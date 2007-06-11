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
	/// A marker interface for <see cref="JobDetail" /> s that
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
	/// </p>
	/// </remarks>
	/// <seealso cref="IJob" />
	/// <seealso cref="JobDetail" />
	/// <seealso cref="JobDataMap" />
	/// <seealso cref="IScheduler" /> 
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public interface IStatefulJob : IJob
	{
	}
}