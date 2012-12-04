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

using Common.Logging;

namespace Quartz.Examples.Example9
{
	/// <summary>
	/// This is just a simple job that gets fired off by example 9.
	/// </summary>
	/// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class SimpleJob2 : IJob
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(SimpleJob2));
		
		public virtual void Execute(IJobExecutionContext context)
		{
			// This job simply prints out its job name and the
			// date and time that it is running
			JobKey jobKey = context.JobDetail.Key;
			log.InfoFormat("SimpleJob2 says: {0} executing at {1}", jobKey, System.DateTime.Now.ToString("r"));
		}
	}
}
