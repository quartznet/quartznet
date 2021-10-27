#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
using System.Globalization;

namespace Quartz.Util
{
    /// <summary>
    /// Object representing an immutable job or trigger key.
    /// </summary>
    /// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class Key<T> : IComparable<Key<T>>
    {
        /// <summary>
        /// The default group for scheduling entities, with the value "DEFAULT".
        /// </summary>
        public const string DefaultGroup = "DEFAULT";

        private readonly string name;
        private readonly string group;

        /// <summary>
        /// Construct a new key with the given name and <see cref="DefaultGroup"/> as group.
        /// </summary>
        /// <param name="name">the name</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        public Key(string name) : this(name, DefaultGroup)
        {
        }

        /// <summary>
        /// Construct a new key with the given name and group.
        /// </summary>
        /// <param name="name">the name</param>
        /// <param name="group">the group</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
        public Key(string name, string group)
        {
            if (name == null)
                ExceptionHelper.ThrowArgumentNullException(nameof(name));
            if (group == null)
                ExceptionHelper.ThrowArgumentNullException(nameof(group));

            this.name = name;
            this.group = group;
        }

        /// <summary>
        /// Get the name portion of the key.
        /// </summary>
        /// <returns>
        /// The name.
        /// </returns>
        public string Name
        {
            get { return name; }
        }

        /// <summary>
        /// Get the group portion of the key.
        /// </summary>
        /// <value>
        /// The group.
        /// </value>
        public string Group
        {
            get { return group; }
        }

        /// <summary> <para>
        /// Return the string representation of the key. The format will be:
        /// &lt;group&gt;.&lt;name&gt;.
        /// </para>
        /// </summary>
        /// <returns>
        /// The string representation of the key.
        /// </returns>
        public override string ToString()
        {
            return Group + '.' + Name;
        }

        public override int GetHashCode()
        {
            const int Prime = 31;
            int result = 1;
            result = Prime*result + group.GetHashCode();
            result = Prime*result + name.GetHashCode();
            return result;
        }

        public override bool Equals(object? obj)
        {
            if (this == obj)
            {
                return true;
            }
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            Key<T> other = (Key<T>)obj;
            return group.Equals(other.group) && name.Equals(other.name);
        }

        public int CompareTo(Key<T>? o)
        {
            if (o is null)
            {
                return 1;
            }

            if (!ReferenceEquals(group, o.group))
            {
                if (DefaultGroup.Equals(group) && !DefaultGroup.Equals(o.group))
                {
                    return -1;
                }
                if (!DefaultGroup.Equals(group) && DefaultGroup.Equals(o.group))
                {
                    return 1;
                }

                int r = CultureInfo.CurrentCulture.CompareInfo.Compare(group, o.group, CompareOptions.None);
                if (r != 0)
                {
                    return r;
                }
            }

            if (ReferenceEquals(name, o.name))
            {
                return 0;
            }

            return CultureInfo.CurrentCulture.CompareInfo.Compare(name, o.name, CompareOptions.None);
        }
    }
}