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

using Quartz.Util;

namespace Quartz.Impl.Matchers
{
    /// <summary>
    /// Matches on the complete key being equal (both name and group).
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class KeyMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
    {
        // ReSharper disable once UnusedMember.Local
        private KeyMatcher()
        {
        }

        protected KeyMatcher(TKey compareTo)
        {
            CompareToValue = compareTo;
        }

        /// <summary>
        /// Create a KeyMatcher that matches Keys that equal the given key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="compareTo"></param>
        /// <returns></returns>
        public static KeyMatcher<T> KeyEquals<T>(T compareTo) where T : Key<T>
        {
            return new KeyMatcher<T>(compareTo);
        }

        public bool IsMatch(TKey key)
        {
            return CompareToValue.Equals(key);
        }

        public TKey CompareToValue { get; private set; } = null!;

        public override int GetHashCode()
        {
            const int Prime = 31;
            int result = 1;
            result = Prime*result + (CompareToValue == null ? 0 : CompareToValue.GetHashCode());
            return result;
        }

        public override bool Equals(object? obj)
        {
            if (this == obj)
            {
                return true;
            }
            if (obj == null)
            {
                return false;
            }
            if (GetType() != obj.GetType())
            {
                return false;
            }
            KeyMatcher<TKey> other = (KeyMatcher<TKey>) obj;
            if (CompareToValue == null)
            {
                if (other.CompareToValue != null)
                {
                    return false;
                }
            }
            else if (!CompareToValue.Equals(other.CompareToValue))
            {
                return false;
            }
            return true;
        }
    }
}