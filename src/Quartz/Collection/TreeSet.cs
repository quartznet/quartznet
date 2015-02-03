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
using System.Collections.Generic;

using C5;

namespace Quartz.Collection
{
    /// <summary>
    /// Simple C5 wrapper for common interface.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [Serializable]
    public class TreeSet<T> : C5.TreeSet<T>, ISortedSet<T>
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public TreeSet()
        {
            
        }

        /// <summary>
        /// Constructor that accepts comparer.
        /// </summary>
        /// <param name="comparer">Comparer to use.</param>
        public TreeSet(IComparer<T> comparer) : base(comparer)
        {

        }

        /// <summary>
        /// Constructor that prepopulates.
        /// </summary>
        /// <param name="items"></param>
        public TreeSet(IEnumerable<T> items)
        {
            AddAll(items);
        }

        /// <summary>
        /// Returns the first element.
        /// </summary>
        /// <returns></returns>
        public T First()
        {
            return Count > 0 ? this[0] : default(T);
        }

        /// <summary>
        /// Return items from given range.
        /// </summary>
        /// <param name="limit"></param>
        /// <returns></returns>
        public ISortedSet<T> TailSet(T limit)
        {
            TreeSet<T> retValue = new TreeSet<T>(Comparer);
            IDirectedCollectionValue<T> directedCollectionValue = RangeFrom(limit);
            retValue.AddAll(directedCollectionValue);
            return retValue;
        }

        /// <summary>
        /// Indexer.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        T ISortedSet<T>.this[int index]
        {
            get { return base[index]; }
        }
    }

    /// <summary>
    /// Only for backwards compatibility with serialization!
    /// </summary>
    [Serializable]
    public class TreeSet : System.Collections.ArrayList
    {
    }
}