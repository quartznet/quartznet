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

namespace Quartz.Impl.Matchers
{
    /// <summary>
    /// Operators available for comparing string values.
    /// </summary>
    [Serializable]
    public abstract class StringOperator : IEquatable<StringOperator>
    {
        public static readonly StringOperator Equality = new EqualityOperator();
        public static readonly StringOperator StartsWith = new StartsWithOperator();
        public static readonly StringOperator EndsWith = new EndsWithOperator();
        public static readonly StringOperator Contains = new ContainsOperator();
        public static readonly StringOperator Anything = new AnythingOperator();

        public abstract bool Evaluate(string value, string compareTo);

        [Serializable]
        private class EqualityOperator : StringOperator
        {
            public override bool Evaluate(string value, string compareTo) {
                return value.Equals(compareTo);
            }
        }

        [Serializable]
        private class StartsWithOperator : StringOperator
        {
            public override bool Evaluate(string value, string compareTo) {
                return value.StartsWith(compareTo);
            }
        }

        [Serializable]
        private class EndsWithOperator : StringOperator
        {
             public override bool Evaluate(string value, string compareTo) {
                return value.EndsWith(compareTo);
            }
        }

        [Serializable]
        private class ContainsOperator : StringOperator
        {
            public override bool Evaluate(string value, string compareTo) {
                return value.Contains(compareTo);
            }
        }

        [Serializable]
        private class AnythingOperator : StringOperator
        {
            public override bool Evaluate(string value, string compareTo)
            {
                return true;
            }
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as StringOperator);
        }

        public bool Equals(StringOperator other)
        {
            if (other == null)
            {
                return false;
            }

            // just check by type, equality based on behavior
            return GetType().Equals(other.GetType());
        }

        public override int GetHashCode()
        {
            return GetType().GetHashCode();
        }
    }
}