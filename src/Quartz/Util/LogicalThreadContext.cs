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

using System.Runtime.Remoting.Messaging;
using System.Security;

#if !ClientProfile
using System.Web;
#endif

namespace Quartz.Util
{
	/// <summary>
	/// Wrapper class to access thread local data.
	/// Data is either accessed from thread or HTTP Context's 
	/// data if HTTP Context is available.
	/// </summary>
	/// <author>Marko Lahma .NET</author>
	[SecurityCritical]
	public static class LogicalThreadContext
	{
		/// <summary>
		/// Retrieves an object with the specified name.
		/// </summary>
		/// <param name="name">The name of the item.</param>
		/// <returns>The object in the call context associated with the specified name or null if no object has been stored previously</returns>
        public static T GetData<T>(string name)
		{
#if !ClientProfile
		    if (HttpContext.Current != null)
			{
                return (T)HttpContext.Current.Items[name];
            }
#endif
            return (T)CallContext.GetData(name);
		}

		/// <summary>
		/// Stores a given object and associates it with the specified name.
		/// </summary>
		/// <param name="name">The name with which to associate the new item.</param>
		/// <param name="value">The object to store in the call context.</param>
        public static void SetData(string name, object value)
		{
#if !ClientProfile
            if (HttpContext.Current != null)
			{
                HttpContext.Current.Items[name] = value;
			}
			else
#endif
			{
                CallContext.SetData(name, value);
            }
		}

		/// <summary>
		/// Empties a data slot with the specified name.
		/// </summary>
		/// <param name="name">The name of the data slot to empty.</param>
        public static void FreeNamedDataSlot(string name)
		{
#if !ClientProfile
		    if (HttpContext.Current != null)
			{
                HttpContext.Current.Items.Remove(name);
            }
			else
#endif
			{
                CallContext.FreeNamedDataSlot(name);
			}
		}
	}
}
