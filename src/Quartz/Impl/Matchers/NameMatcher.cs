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
    public class NameMatcher<T> : StringMatcher<T> where T : Key<T>
    {
        protected NameMatcher(string compareTo, StringOperatorName compareWith) : base(compareTo, compareWith)
        {
        }

        public static NameMatcher<T> matchNameEquals(string compareTo)
        {
            return new NameMatcher<T>(compareTo, StringOperatorName.EQUALS);
        }

        public static NameMatcher<T> matchNameStartsWith(string compareTo)
        {
            return new NameMatcher<T>(compareTo, StringOperatorName.STARTS_WITH);
        }

        public static NameMatcher<T> matchNameEndsWith(string compareTo)
        {
            return new NameMatcher<T>(compareTo, StringOperatorName.ENDS_WITH);
        }

        public static NameMatcher<T> matchNameContains(string compareTo)
        {
            return new NameMatcher<T>(compareTo, StringOperatorName.CONTAINS);
        }

        protected override string getValue(T key)
        {
            return key.Name;
        }
    }
}