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
    public abstract class StringMatcher<T> : IMatcher<T> where T : Key<T>
    {
        protected readonly string compareTo;
        protected readonly StringOperatorName compareWith;

        protected StringMatcher(string compareTo, StringOperatorName compareWith)
        {
            if (compareTo == null)
            {
                throw new ArgumentNullException("compareTo", "CompareTo value cannot be null!");
            }
            if (compareWith == null)
            {
                throw new ArgumentNullException("compareWith", "CompareWith operator cannot be null!");
            }

            this.compareTo = compareTo;
            this.compareWith = compareWith;
        }

        protected abstract string getValue(T key);

        public bool IsMatch(T key)
        {
            switch (compareWith)
            {
                case StringOperatorName.EQUALS:
                    return getValue(key).Equals(compareTo);
                case StringOperatorName.STARTS_WITH:
                    return getValue(key).StartsWith(compareTo);
                case StringOperatorName.ENDS_WITH:
                    return getValue(key).EndsWith(compareTo);
                case StringOperatorName.CONTAINS:
                    return getValue(key).Contains(compareTo);
            }

            throw new InvalidOperationException("Unknown StringOperatorName: " + compareWith);
        }


        public override int GetHashCode()
        {
            int prime = 31;
            int result = 1;
            result = prime*result
                     + ((compareTo == null) ? 0 : compareTo.GetHashCode());
            result = prime*result
                     + ((compareWith == null) ? 0 : compareWith.GetHashCode());
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
            StringMatcher<T> other = (StringMatcher<T>) obj;
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
            if (compareWith == null)
            {
                if (other.compareWith != null)
                {
                    return false;
                }
            }
            else if (!compareWith.Equals(other.compareWith))
            {
                return false;
            }
            return true;
        }

        public string getCompareToValue()
        {
            return compareTo;
        }

        public StringOperatorName getCompareWithOperator()
        {
            return compareWith;
        }
    }
}