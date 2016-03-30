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
using System.Runtime.Serialization;

namespace Quartz.Collection
{
    /// <summary>
    /// Only for backwards compatibility with serialization!
    /// </summary>
    /// <typeparam name="T"></typeparam>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    [DataContract]  // TODO (NetCore Port): Confirm that data contract serialization works as expected here
    internal class TreeSet<T> : SortedSet<T>
    {
        protected
#if BINARY_SERIALIZATION
        override
#else // BINARY_SERIALIZATION
        virtual
#endif // BINARY_SERIALIZATION
        void OnDeserialization(object sender)
        {
            base.OnDeserialization(sender);
        }

        [OnDeserialized]
        internal void OnDeserializedCallback(StreamingContext context)
        {
            OnDeserialization(null);
        }
    }

    /// <summary>
    /// Only for backwards compatibility with serialization!
    /// </summary>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    [DataContract]
    internal class TreeSet : ArrayList
    {
    }
}