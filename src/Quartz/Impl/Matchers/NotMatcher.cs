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
    /// Matches using an NOT operator on another Matcher. 
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class NotMatcher<T> : IMatcher<T> where T : Key<T>
    {
        protected readonly IMatcher<T> operand;

        protected NotMatcher(IMatcher<T> operand)
        {
            if (operand == null)
            {
                throw new ArgumentNullException("operand", "Non-null operand required!");
            }

            this.operand = operand;
        }


        public static NotMatcher<U> and<U>(IMatcher<U> operand) where U : Key<U>
        {
            return new NotMatcher<U>(operand);
        }


        public bool IsMatch(T key)
        {
            return !operand.IsMatch(key);
        }

        public IMatcher<T> getOperand()
        {
            return operand;
        }

        public override int GetHashCode()
        {
            const int prime = 31;
            int result = 1;
            result = prime*result + ((operand == null) ? 0 : operand.GetHashCode());
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
            NotMatcher<T> other = (NotMatcher<T>) obj;
            if (operand == null)
            {
                if (other.operand != null)
                {
                    return false;
                }
            }
            else if (!operand.Equals(other.operand))
            {
                return false;
            }
            return true;
        }
    }
}