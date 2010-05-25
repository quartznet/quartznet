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

using System.Runtime.Remoting.Messaging;
using System.Web;

namespace Quartz.Util
{
	/// <summary>
	/// Wrapper class to access thread local data.
	/// Data is either accessed from thread or HTTP Context's 
	/// data if HTTP Context is avaiable.
	/// </summary>
	/// <author>Marko Lahma .NET</author>
	public sealed class LogicalThreadContext
	{
		private LogicalThreadContext()
		{
		}
		
		/// <summary>
		/// Retrieves an object with the specified name.
		/// </summary>
		/// <param name="name">The name of the item.</param>
		/// <returns>The object in the call context associated with the specified name or null if no object has been stored previously</returns>
		public static T GetData<T>(string name)
		{
			HttpContext ctx = HttpContext.Current;
			if (ctx == null)
			{
				return (T) CallContext.GetData(name);
			}
			else
			{
				return (T) ctx.Items[name];
			}
		}

		/// <summary>
		/// Stores a given object and associates it with the specified name.
		/// </summary>
		/// <param name="name">The name with which to associate the new item.</param>
		/// <param name="value">The object to store in the call context.</param>
		public static void SetData(string name, object value)
		{
			HttpContext ctx = HttpContext.Current;
			if (ctx == null)
			{
				CallContext.SetData(name, value);
			}
			else
			{
				ctx.Items[name] = value;
			}
		}

		/// <summary>
		/// Empties a data slot with the specified name.
		/// </summary>
		/// <param name="name">The name of the data slot to empty.</param>
		public static void FreeNamedDataSlot(string name)
		{
			HttpContext ctx = HttpContext.Current;
			if (ctx == null)
			{
				CallContext.FreeNamedDataSlot(name);
			}
			else
			{
				ctx.Items.Remove(name);
			}
		}
	}
}
