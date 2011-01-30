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
    /// Matches using an OR operator on two Matcher operands. 
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class OrMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
    {
        private readonly IMatcher<TKey> leftOperand;
        private readonly IMatcher<TKey> rightOperand;

        protected OrMatcher(IMatcher<TKey> leftOperand, IMatcher<TKey> rightOperand)
        {
            if (leftOperand == null || rightOperand == null)
            {
                throw new ArgumentException("Two non-null operands required!");
            }

            this.leftOperand = leftOperand;
            this.rightOperand = rightOperand;
        }

        /// <summary>
        /// Create an OrMatcher that depends upon the result of at least one of the given matchers.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="leftOperand"></param>
        /// <param name="rightOperand"></param>
        /// <returns></returns>
        public static OrMatcher<T> Or<T>(IMatcher<T> leftOperand, IMatcher<T> rightOperand) where T : Key<T>
        {
            return new OrMatcher<T>(leftOperand, rightOperand);
        }

        public bool IsMatch(TKey key)
        {
            return leftOperand.IsMatch(key) || rightOperand.IsMatch(key);
        }

        public IMatcher<TKey> LeftOperand
        {
            get { return leftOperand; }
        }

        public IMatcher<TKey> RightOperand
        {
            get { return rightOperand; }
        }

        public override int GetHashCode()
        {
            const int prime = 31;
            int result = 1;
            result = prime*result
                     + ((leftOperand == null) ? 0 : leftOperand.GetHashCode());
            result = prime*result
                     + ((rightOperand == null) ? 0 : rightOperand.GetHashCode());
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
            OrMatcher<TKey> other = (OrMatcher<TKey>) obj;
            if (leftOperand == null)
            {
                if (other.leftOperand != null)
                {
                    return false;
                }
            }
            else if (!leftOperand.Equals(other.leftOperand))
            {
                return false;
            }
            if (rightOperand == null)
            {
                if (other.rightOperand != null)
                {
                    return false;
                }
            }
            else if (!rightOperand.Equals(other.rightOperand))
            {
                return false;
            }
            return true;
        }
    }
}