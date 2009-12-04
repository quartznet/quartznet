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
using System.Net;

using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary>
	/// <see cref="IInstanceIdGenerator" /> that names the scheduler instance using 
	/// just the machine hostname.
	/// <p>
	/// This class is useful when you know that your scheduler instance will be the 
	/// only one running on a particular machine.  Each time the scheduler is 
	/// restarted, it will get the same instance id as long as the machine is not 
	/// renamed.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IInstanceIdGenerator" />
	/// <seealso cref="SimpleInstanceIdGenerator" />
	public class HostnameInstanceIdGenerator : IInstanceIdGenerator
	{
		/// <summary>
		/// Generate the instance id for a <see cref="IScheduler"/>
		/// </summary>
		/// <returns>The clusterwide unique instance id.</returns>
		public virtual string GenerateInstanceId()
		{
			try
			{
#if NET_20
                return Dns.GetHostName();
#else
				return
					Dns.GetHostByAddress(Dns.GetHostByName(Dns.GetHostName()).AddressList[0].ToString()).HostName;
#endif
			}
			catch (Exception e)
			{
				throw new SchedulerException("Couldn't get host name!", e);
			}
		}
	}
}