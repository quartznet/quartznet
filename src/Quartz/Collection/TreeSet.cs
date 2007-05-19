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
using System.Collections;

namespace Quartz.Collection
{
	/// <summary>
	/// SupportClass for the TreeSet class.
	/// </summary>
	[Serializable]
	public class TreeSet : ArrayList, ISortedSet
	{
		private IComparer comparator = Comparer.Default;

		/// <summary>
		/// Initializes a new instance of the <see cref="TreeSet"/> class.
		/// </summary>
		public TreeSet() : base()
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="TreeSet"/> class.
		/// </summary>
		/// <param name="c">The <see cref="T:System.Collections.ICollection"/> whose elements are copied to the new list.</param>
		/// <exception cref="T:System.ArgumentNullException">
		/// 	<paramref name="c"/> is <see langword="null"/>.</exception>
		public TreeSet(ICollection c) : base()
		{
			AddAll(c);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="TreeSet"/> class.
		/// </summary>
		/// <param name="c">The c.</param>
		public TreeSet(IComparer c) : base()
		{
			comparator = c;
		}

		/// <summary>
		/// Unmodifiables the tree set.
		/// </summary>
		/// <param name="collection">The collection.</param>
		/// <returns></returns>
		public static TreeSet UnmodifiableTreeSet(ICollection collection)
		{
			ArrayList items = new ArrayList(collection);
			items = ArrayList.ReadOnly(items);
			return new TreeSet(items);
		}

		/// <summary>
		/// Gets the IComparator object used to sort this set.
		/// </summary>
		public IComparer Comparator
		{
			get { return comparator; }
		}

		/// <summary>
		/// Adds a new element to the ArrayList if it is not already present and sorts the ArrayList.
		/// </summary>
		/// <param name="obj">Element to insert to the ArrayList.</param>
		/// <returns>TRUE if the new element was inserted, FALSE otherwise.</returns>
		public new bool Add(object obj)
		{
			bool inserted;
			if ((inserted = Contains(obj)) == false)
			{
				base.Add(obj);
				Sort(comparator);
			}
			return !inserted;
		}

		/// <summary>
		/// Adds all the elements of the specified collection that are not present to the list.
		/// </summary>		
		/// <param name="c">Collection where the new elements will be added</param>
		/// <returns>Returns true if at least one element was added to the collection.</returns>
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
			Sort(comparator);
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
		/// Determines whether an element is in the the current TreeSetSupport collection. The IComparer defined for 
		/// the current set will be used to make comparisons between the elements already inserted in the collection and 
		/// the item specified.
		/// </summary>
		/// <param name="item">The object to be locatet in the current collection.</param>
		/// <returns>true if item is found in the collection; otherwise, false.</returns>
		public override bool Contains(object item)
		{
			IEnumerator tempEnumerator = GetEnumerator();
			while (tempEnumerator.MoveNext())
			{
				if (comparator.Compare(tempEnumerator.Current, item) == 0)
				{
					return true;
				}
			}
			return false;
		}


		/// <summary>
		/// Returns a portion of the list whose elements are greater than the limit object parameter.
		/// </summary>
		/// <param name="limit">The start element of the portion to extract.</param>
		/// <returns>The portion of the collection whose elements are greater than the limit object parameter.</returns>
		public ISortedSet TailSet(object limit)
		{
			ISortedSet newList = new TreeSet();
			int i = 0;
            while ((i < this.Count) && (comparator.Compare(this[i], limit) < 0))
            {
                i++;
            }
			for (; i < Count; i++)
			{
				newList.Add(this[i]);
			}
			return newList;
		}
	}
}
