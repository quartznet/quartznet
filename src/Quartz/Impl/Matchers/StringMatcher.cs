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
    /// An abstract base class for some types of matchers.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public abstract class StringMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
    {
        private readonly string compareTo;
        private readonly StringOperator compareWith;

        protected StringMatcher(string compareTo, StringOperator compareWith)
        {
            if (compareTo == null)
            {
                throw new ArgumentNullException("compareTo", "CompareTo value cannot be null!");
            }

            this.compareTo = compareTo;
            this.compareWith = compareWith;
        }

        protected abstract string GetValue(TKey key);

        public bool IsMatch(TKey key)
        {
            return compareWith.Evaluate(GetValue(key), compareTo);
        }

        public override int GetHashCode()
        {
            const int Prime = 31;
            int result = 1;
            result = Prime*result + ((compareTo == null) ? 0 : compareTo.GetHashCode());
            result = Prime*result + compareWith.GetHashCode();
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
            StringMatcher<TKey> other = (StringMatcher<TKey>) obj;
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
            if (!compareWith.Equals(other.compareWith))
            {
                return false;
            }
            return true;
        }

        public string CompareToValue
        {
            get { return compareTo; }
        }

        public StringOperator CompareWithOperator
        {
            get { return compareWith; }
        }
    }
}