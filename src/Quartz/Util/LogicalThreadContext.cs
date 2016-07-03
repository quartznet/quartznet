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

#if REMOTING
using System.Runtime.Remoting.Messaging;
#else
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endif // REMOTING
using System.Security;
#if HTTPCONTEXT
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
#if !REMOTING
    // Async local dictionary can be used as a .NET Core-compliant substitute for CallContext
        static AsyncLocal<Dictionary<string, object>> AsyncLocalObjects = new AsyncLocal<Dictionary<string, object>>();
        static Dictionary<string, object> Data
        {
            get
            {
                if (AsyncLocalObjects.Value == null)
                {
                    AsyncLocalObjects.Value = new Dictionary<string, object>();
                }
                return AsyncLocalObjects.Value;
            }
        }        
#endif // !REMOTING

        /// <summary>
        /// Retrieves an object with the specified name.
        /// </summary>
        /// <param name="name">The name of the item.</param>
        /// <returns>The object in the call context associated with the specified name or null if no object has been stored previously</returns>
        public static T GetData<T>(string name)
        {
#if HTTPCONTEXT
            if (HttpContext.Current != null)
            {
                return (T) HttpContext.Current.Items[name];
            }
#endif

#if REMOTING
            return (T) CallContext.LogicalGetData(name);
#else // REMOTING
            return (T)Data.TryGetAndReturn(name);
#endif // REMOTING
        }

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item.</param>
        /// <param name="value">The object to store in the call context.</param>
        public static void SetData(string name, object value)
        {
#if HTTPCONTEXT
            if (HttpContext.Current != null)
            {
                HttpContext.Current.Items[name] = value;
            }
            else
#endif
            {
#if REMOTING
                CallContext.LogicalSetData(name, value);
#else // REMOTING
                Data[name] = value;
#endif // REMOTING
            }
        }

        /// <summary>
        /// Empties a data slot with the specified name.
        /// </summary>
        /// <param name="name">The name of the data slot to empty.</param>
        public static void FreeNamedDataSlot(string name)
        {
#if HTTPCONTEXT
            if (HttpContext.Current != null)
            {
                HttpContext.Current.Items.Remove(name);
            }
            else
#endif
            {
#if REMOTING
                CallContext.FreeNamedDataSlot(name);
#else // REMOTING
                if (Data.ContainsKey(name)) Data.Remove(name);
#endif // REMOTING
            }
        }
    }
}