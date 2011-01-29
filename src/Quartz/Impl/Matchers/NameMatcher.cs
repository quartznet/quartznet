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
    public class NameMatcher<TTarget> : StringMatcher<TTarget> where TTarget : Key<TTarget>
    {
        protected NameMatcher(string compareTo, StringOperatorName compareWith) : base(compareTo, compareWith)
        {
        }

        /// <summary>
        /// Create a NameMatcher that matches names equaling the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TTarget> NameEquals(string compareTo)
        {
            return new NameMatcher<TTarget>(compareTo, StringOperatorName.Equality);
        }

        /// <summary>
        /// Create a NameMatcher that matches names starting with the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TTarget> NameStartsWith(string compareTo)
        {
            return new NameMatcher<TTarget>(compareTo, StringOperatorName.StartsWith);
        }

        /// <summary>
        /// Create a NameMatcher that matches names ending with the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TTarget> NameEndsWith(string compareTo)
        {
            return new NameMatcher<TTarget>(compareTo, StringOperatorName.EndsWith);
        }

        /// <summary>
        /// Create a NameMatcher that matches names containing the given string.
        /// </summary>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static NameMatcher<TTarget> NameContains(string compareTo)
        {
            return new NameMatcher<TTarget>(compareTo, StringOperatorName.Contains);
        }

        protected override string GetValue(TTarget key)
        {
            return key.Name;
        }
    }
}