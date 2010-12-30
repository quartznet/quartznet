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
    public class GroupMatcher<T> : StringMatcher<T> where T : Key<T>
    {
        protected GroupMatcher(string compareTo, StringOperatorName compareWith) : base(compareTo, compareWith)
        {
        }

        public static GroupMatcher<T> matchGroupEquals(string compareTo)
        {
            return new GroupMatcher<T>(compareTo, StringOperatorName.Equality);
        }

        public static GroupMatcher<T> matchGroupStartsWith(string compareTo)
        {
            return new GroupMatcher<T>(compareTo, StringOperatorName.StartsWith);
        }

        public static GroupMatcher<T> matchGroupEndsWith(string compareTo)
        {
            return new GroupMatcher<T>(compareTo, StringOperatorName.EndsWith);
        }

        public static GroupMatcher<T> matchGroupContains(string compareTo)
        {
            return new GroupMatcher<T>(compareTo, StringOperatorName.Contains);
        }

        protected override string getValue(T key)
        {
            return key.Group;
        }
    }
}