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

namespace Quartz.Collection
{
	/// <summary>
	/// A sorted set.
	/// </summary>
	/// <author>Marko Lahma (.NET)</author>
	public interface ISortedSet<T> : ISet<T>
	{
	    /// <summary>
	    /// Returns a portion of the list whose elements are greater than the limit object parameter.
	    /// </summary>
	    /// <param name="limit">The start element of the portion to extract.</param>
	    /// <returns>The portion of the collection whose elements are greater than the limit object parameter.</returns>
	    ISortedSet<T> TailSet(T limit);

        /// <summary>
        /// Returns the object in the specified index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        T this[int index] { get; }

        /// <summary>
        /// Returns the first item in the set.
        /// </summary>
        /// <returns>First object.</returns>
        T First();
	}
}