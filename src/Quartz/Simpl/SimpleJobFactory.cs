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
using System;
using Common.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl
{
	/// <summary> 
	/// The default JobFactory used by Quartz - simply calls 
	/// <code>NewInstance()</code> on the job class.
	/// </summary>
	/// <seealso cref="IJobFactory" />
	/// <seealso cref="PropertySettingJobFactory" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class SimpleJobFactory : IJobFactory
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (SimpleJobFactory));

		public virtual IJob NewJob(TriggerFiredBundle bundle)
		{
			JobDetail jobDetail = bundle.JobDetail;
			Type jobClass = jobDetail.JobClass;
			try
			{
				if (Log.IsDebugEnabled)
				{
					Log.Debug("Producing instance of Job '" + jobDetail.FullName + "', class=" + jobClass.FullName);
				}

				return (IJob) ObjectUtils.InstantiateType(jobClass);
			}
			catch (Exception e)
			{
				SchedulerException se = new SchedulerException("Problem instantiating class '" + jobDetail.JobClass.FullName + "'", e);
				throw se;
			}
		}
	}
}