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

using System;
using System.Collections;
using System.Collections.Generic;

namespace Quartz.Collection
{
    /// <summary>
    /// Only for backwards compatibility with serialization!
    /// </summary>
    /// <typeparam name="T"></typeparam>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    internal class TreeSet<T> : SortedSet<T>
    {
        // No non-binary-formatter alternative is needed since this will not be deserialized by new .NET Core versions of Quartz.Net
#if BINARY_SERIALIZATION
        protected override void OnDeserialization(object sender)
        {
            base.OnDeserialization(sender);
        }
#endif // BINARY_SERIALIZATION

    }

    /// <summary>
    /// Only for backwards compatibility with serialization!
    /// </summary>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    internal class TreeSet : ArrayList
    {
    }
}