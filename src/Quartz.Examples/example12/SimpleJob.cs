#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
using Common.Logging;

namespace Quartz.Examples.Example12
{
	
	/// <summary>
	/// A dumb implementation of Job, for unittesting purposes.
	/// </summary>
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class SimpleJob : IJob
	{
		public const string MESSAGE = "msg";
		private static ILog _log = LogManager.GetLogger(typeof(SimpleJob));
		
	
		/// <summary> 
		/// Called by the <see cref="IScheduler" /> when a
		/// <see cref="Trigger" /> fires that is associated with
		/// the <see cref="IJob" />.
		/// </summary>
		public virtual void Execute(JobExecutionContext context)
		{
			
			// This job simply prints out its job name and the
			// date and time that it is running
			string jobName = context.JobDetail.FullName;
			
			string message = context.JobDetail.JobDataMap.GetString(MESSAGE);
			
			_log.Info("SimpleJob: " + jobName + " executing at " + DateTime.Now.ToString("r"));
			_log.Info("SimpleJob: msg: " + message);
		}

	}
}