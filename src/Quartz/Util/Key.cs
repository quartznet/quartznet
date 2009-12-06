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

namespace Quartz.Util
{
	/// <summary>
	/// object representing a job or trigger key.
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>Marko Lahma (.NET)</author>
    public class Key : Pair
	{
		/// <summary>
		/// Get the name portion of the key.
		/// </summary>
		/// <returns> the name
		/// </returns>
		public virtual string Name
		{
			get { return (string) First; }
		}

		/// <summary> <p>
		/// Get the group portion of the key.
		/// </p>
		/// 
		/// </summary>
		/// <returns> the group
		/// </returns>
		public virtual string Group
		{
			get { return (string) Second; }
		}

		/// <summary> Construct a new key with the given name and group.
		/// 
		/// </summary>
		/// <param name="name">
		/// the name
		/// </param>
		/// <param name="group">
		/// the group
		/// </param>
		public Key(string name, string group) : base()
		{
			base.First = name;
			base.Second = group;
		}

		/// <summary> <p>
		/// Return the string representation of the key. The format will be:
		/// &lt;group&gt;.&lt;name&gt;.
		/// </p>
		/// 
		/// </summary>
		/// <returns> the string representation of the key
		/// </returns>
		public override string ToString()
		{
			return Group + '.' + Name;
		}
	}
}