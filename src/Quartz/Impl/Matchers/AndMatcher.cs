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
    /// Matches using an AND operator on two Matcher operands. 
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class AndMatcher<T> : IMatcher<T> where T : Key<T>
    {
        protected readonly IMatcher<T> leftOperand;
        protected readonly IMatcher<T> rightOperand;

        protected AndMatcher(IMatcher<T> leftOperand, IMatcher<T> rightOperand)
        {
            if (leftOperand == null || rightOperand == null)
            {
                throw new ArgumentException("Two non-null operands required!");
            }

            this.leftOperand = leftOperand;
            this.rightOperand = rightOperand;
        }

        public static AndMatcher<U> And<U>(IMatcher<U> leftOperand, IMatcher<U> rightOperand) where U : Key<U>
        {
            return new AndMatcher<U>(leftOperand, rightOperand);
        }

        public bool IsMatch(T key)
        {
            return leftOperand.IsMatch(key) && rightOperand.IsMatch(key);
        }

        public IMatcher<T> getLeftOperand()
        {
            return leftOperand;
        }

        public IMatcher<T> getRightOperand()
        {
            return rightOperand;
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
            AndMatcher<T> other = (AndMatcher<T>) obj;
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