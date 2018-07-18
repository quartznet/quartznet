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

using System;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl
{
	/// <summary> 
	/// The default JobFactory used by Quartz - simply calls 
	/// <see cref="ObjectUtils.InstantiateType{T}" /> on the job class.
	/// </summary>
	/// <seealso cref="IJobFactory" />
	/// <seealso cref="PropertySettingJobFactory" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class SimpleJobFactory : IJobFactory
	{
		private static readonly ILog log = LogProvider.GetLogger(typeof (SimpleJobFactory));

	    /// <inheritdoc />
	    public virtual IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
		{
			IJobDetail jobDetail = bundle.JobDetail;
			Type jobType = jobDetail.JobType;
			try
			{
				if (log.IsDebugEnabled())
				{
					log.Debug($"Producing instance of Job '{jobDetail.Key}', class={jobType.FullName}");
				}

				return ObjectUtils.InstantiateType<IJob>(jobType);
			}
			catch (Exception e)
			{
				SchedulerException se = new SchedulerException($"Problem instantiating class '{jobDetail.JobType.FullName}'", e);
				throw se;
			}
		}

	    /// <inheritdoc />
	    public virtual void ReturnJob(IJob job)
	    {
	        var disposable = job as IDisposable;
	        disposable?.Dispose();
	    }
	}
}
