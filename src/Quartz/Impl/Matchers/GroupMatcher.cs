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

using Quartz.Util;

namespace Quartz.Impl.Matchers
{
    /// <summary>
    /// Matches on group (ignores name) property of Keys.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class GroupMatcher<TKey> : StringMatcher<TKey> where TKey : Key<TKey>
    {
        protected GroupMatcher(string compareTo, StringOperator compareWith) : base(compareTo, compareWith)
        {
        }

        /// <summary>
        /// Create a GroupMatcher that matches groups equaling the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static GroupMatcher<TKey> GroupEquals(string compareTo)
        {
            return new GroupMatcher<TKey>(compareTo, StringOperator.Equality);
        }

        /// <summary>
        /// Create a GroupMatcher that matches groups starting with the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static GroupMatcher<TKey> GroupStartsWith(string compareTo)
        {
            return new GroupMatcher<TKey>(compareTo, StringOperator.StartsWith);
        }

        /// <summary>
        /// Create a GroupMatcher that matches groups ending with the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static GroupMatcher<TKey> GroupEndsWith(string compareTo)
        {
            return new GroupMatcher<TKey>(compareTo, StringOperator.EndsWith);
        }

        /// <summary>
        /// Create a GroupMatcher that matches groups containing the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static GroupMatcher<TKey> GroupContains(string compareTo)
        {
            return new GroupMatcher<TKey>(compareTo, StringOperator.Contains);
        }

        /// <summary>
        /// Create a GroupMatcher that matches all.
        /// </summary>
        public static GroupMatcher<TKey> AnyGroup()
        {
            return new GroupMatcher<TKey>("", StringOperator.Anything);
        }

        protected override string GetValue(TKey key)
        {
            return key.Group;
        }
    }
}