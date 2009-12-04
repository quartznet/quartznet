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
using System.Collections;

namespace Quartz.Collection
{
	/// <summary>
	/// A hash based set.
	/// </summary>
	[Serializable]
	public class HashSet : ArrayList, ISet
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="HashSet"/> class.
		/// </summary>
		public HashSet() : base()
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="HashSet"/> class.
		/// </summary>
		/// <param name="c">The <see cref="T:System.Collections.ICollection"/> whose elements are copied to the new list.</param>
		/// <exception cref="T:System.ArgumentNullException">
		/// 	<paramref name="c"/> is <see langword="null"/>.</exception>
		public HashSet(ICollection c)
		{
			AddAll(c);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="HashSet"/> class.
		/// </summary>
		/// <param name="capacity">The capacity.</param>
		public HashSet(int capacity) : base(capacity)
		{
		}

		/// <summary>
		/// Unmodifiables the hash set.
		/// </summary>
		/// <param name="collection">The collection.</param>
		/// <returns></returns>
		public static HashSet UnmodifiableHashSet(ICollection collection)
		{
			ArrayList items = new ArrayList(collection);
			items = ArrayList.ReadOnly(items);
			return new HashSet(items);
		}

		/// <summary>
		/// Adds a new element to the ArrayList if it is not already present.
		/// </summary>		
		/// <param name="obj">Element to insert to the ArrayList.</param>
		/// <returns>Returns true if the new element was inserted, false otherwise.</returns>
		public new virtual bool Add(object obj)
		{
			bool inserted;

			if ((inserted = Contains(obj)) == false)
			{
				base.Add(obj);
			}

			return !inserted;
		}

		/// <summary>
		/// Adds all the elements of the specified collection that are not present to the list.
		/// </summary>
		/// <param name="c">Collection where the new elements will be added</param>
		/// <returns>Returns true if at least one element was added, false otherwise.</returns>
		public bool AddAll(ICollection c)
		{
			IEnumerator e = new ArrayList(c).GetEnumerator();
			bool added = false;

			while (e.MoveNext() == true)
			{
				if (Add(e.Current) == true)
				{
					added = true;
				}
			}

			return added;
		}

		/// <summary>
		/// Returns the first item in the set.
		/// </summary>
		/// <returns>First object.</returns>
		public object First()
		{
			return this[0];
		}

		/// <summary>
		/// Returns a copy of the HashSet instance.
		/// </summary>		
		/// <returns>Returns a shallow copy of the current HashSet.</returns>
		public override object Clone()
		{
			return MemberwiseClone();
		}
	}
}