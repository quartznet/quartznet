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
    /// Matches on name (ignores group) property of Keys.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class NameMatcher<TKey> : StringMatcher<TKey> where TKey : Key<TKey>
    {
        protected NameMatcher(string compareTo, StringOperator compareWith) : base(compareTo, compareWith)
        {
        }

        /// <summary>
        /// Create a NameMatcher that matches names equaling the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TKey> NameEquals(string compareTo)
        {
            return new NameMatcher<TKey>(compareTo, StringOperator.Equality);
        }

        /// <summary>
        /// Create a NameMatcher that matches names starting with the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TKey> NameStartsWith(string compareTo)
        {
            return new NameMatcher<TKey>(compareTo, StringOperator.StartsWith);
        }

        /// <summary>
        /// Create a NameMatcher that matches names ending with the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TKey> NameEndsWith(string compareTo)
        {
            return new NameMatcher<TKey>(compareTo, StringOperator.EndsWith);
        }

        /// <summary>
        /// Create a NameMatcher that matches names containing the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TKey> NameContains(string compareTo)
        {
            return new NameMatcher<TKey>(compareTo, StringOperator.Contains);
        }

        protected override string GetValue(TKey key)
        {
            return key.Name;
        }
    }
}