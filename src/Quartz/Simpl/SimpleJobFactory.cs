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
using System;
using System.Globalization;

using Common.Logging;

using Quartz;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl
{
	/// <summary> 
	/// The default JobFactory used by Quartz - simply calls 
	/// <see cref="ObjectUtils.InstantiateType" /> on the job class.
	/// </summary>
	/// <seealso cref="IJobFactory" />
	/// <seealso cref="PropertySettingJobFactory" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class SimpleJobFactory : IJobFactory
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (SimpleJobFactory));

		/// <summary>
		/// Called by the scheduler at the time of the trigger firing, in order to
		/// produce a <see cref="IJob" /> instance on which to call Execute.
		/// </summary>
		/// <remarks>
		/// It should be extremely rare for this method to throw an exception -
		/// basically only the the case where there is no way at all to instantiate
		/// and prepare the Job for execution.  When the exception is thrown, the
		/// Scheduler will move all triggers associated with the Job into the
		/// <see cref="TriggerState.Error" /> state, which will require human
		/// intervention (e.g. an application restart after fixing whatever
		/// configuration problem led to the issue wih instantiating the Job.
        /// </remarks>
		/// <param name="bundle">The TriggerFiredBundle from which the <see cref="JobDetail" />
		/// and other info relating to the trigger firing can be obtained.</param>
		/// <returns>the newly instantiated Job</returns>
		/// <throws>  SchedulerException if there is a problem instantiating the Job. </throws>
		public virtual IJob NewJob(TriggerFiredBundle bundle)
		{
			JobDetail jobDetail = bundle.JobDetail;
			Type jobType = jobDetail.JobType;
			try
			{
				if (Log.IsDebugEnabled)
				{
					Log.Debug(string.Format(CultureInfo.InvariantCulture, "Producing instance of Job '{0}', class={1}", jobDetail.FullName, jobType.FullName));
				}

				return (IJob) ObjectUtils.InstantiateType(jobType);
			}
			catch (Exception e)
			{
				SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Problem instantiating class '{0}'", jobDetail.JobType.FullName), e);
				throw se;
			}
		}
	}
}
