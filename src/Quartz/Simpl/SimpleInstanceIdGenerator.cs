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
using System.Net;

using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary> 
	/// The default InstanceIdGenerator used by Quartz when instance id is to be
	/// automatically generated.  Instance id is of the form HOSTNAME + CURRENT_TIME.
	/// </summary>
	/// <seealso cref="IInstanceIdGenerator">
	/// </seealso>
	/// <seealso cref="HostnameInstanceIdGenerator">
	/// </seealso>
	public class SimpleInstanceIdGenerator : IInstanceIdGenerator
	{
		public virtual string GenerateInstanceId()
		{
			try
			{
				return
					Dns.GetHostByAddress(Dns.GetHostByName(Dns.GetHostName()).AddressList[0].ToString()).HostName +
					DateTime.Now.Ticks;
			}
			catch (Exception e)
			{
				throw new SchedulerException("Couldn't get host name!", e);
			}
		}
	}
}