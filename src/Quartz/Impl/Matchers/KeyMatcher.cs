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
    /// Matches on the complete key being equal (both name and group). 
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class KeyMatcher<T> : IMatcher<T> where T : Key<T>
    {
        protected readonly T compareTo;

        protected KeyMatcher(T compareTo)
        {
            this.compareTo = compareTo;
        }

        public static KeyMatcher<U> matchKey<U>(U compareTo) where U : Key<U>
        {
            return new KeyMatcher<U>(compareTo);
        }

        public bool IsMatch(T key)
        {
            return compareTo.Equals(key);
        }

        public T getCompareToValue()
        {
            return compareTo;
        }

        public override int GetHashCode()
        {
            const int prime = 31;
            int result = 1;
            result = prime*result
                     + ((compareTo == null) ? 0 : compareTo.GetHashCode());
            return result;
        }

        public override bool Equals(object obj)
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
            KeyMatcher<T> other = (KeyMatcher<T>) obj;
            if (compareTo == null)
            {
                if (other.compareTo != null)
                {
                    return false;
                }
            }
            else if (!compareTo.Equals(other.compareTo))
            {
                return false;
            }
            return true;
        }
    }
}