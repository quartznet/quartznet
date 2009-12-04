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

using System.Collections;

namespace Quartz.Collection
{
	/// <summary>
	/// Collection manipulation related utility methods.
	/// </summary>
	public sealed class CollectionUtil
	{
        private CollectionUtil()
        {
            
        }

		/// <summary>
		/// Removes the specified item from list of items and returns 
		/// whether removal was success.
		/// </summary>
		/// <param name="items">The items to remove from.</param>
		/// <param name="item">The item to remove.</param>
		/// <returns></returns>
		public static bool Remove(IList items, object item)
		{
			for (int i = 0; i < items.Count; ++i)
			{
				if (items[i] == item)
				{
					items.RemoveAt(i);
					return true;
				}
			}
			return false;
		}
	}
}