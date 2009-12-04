/* 
* Copyright 2004-2009 James House 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

namespace Quartz.Util
{
    /// <summary>
    /// Utility class for storing two pieces of information together.
    /// </summary>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    public class Pair
    {
        private object first;
        private object second;

        /// <summary> 
        /// Get or sets the first object in the pair.
        /// </summary>
        public virtual object First
        {
            get { return first; }
            set { first = value; }
        }

        /// <summary> 
        /// Get or sets the second object in the pair.
        /// </summary>
        public virtual object Second
        {
            get { return second; }
            set { second = value; }
        }

        /// <summary>
        /// Test equality of this object with that.
        /// </summary>
        /// <param name="that">object to compare </param>
        /// <returns> true if objects are equal, false otherwise</returns>
        public override bool Equals(object that)
        {
            if (this == that)
            {
                return true;
            }
            if (that is Pair)
            {
                Pair other = (Pair) that;
                if (first == null && second == null)
                {
                    return (other.first == null && other.second == null);
                }
                else if (first == null)
                {
                    return second.Equals(other.second);
                }
                else if (second == null)
                {
                    return first.Equals(other.first);
                }
                else
                {
                    return (first.Equals(other.first) && second.Equals(other.second));
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Serves as a hash function for a particular type. <see cref="M:System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"></see>.
        /// </returns>
        public override int GetHashCode()
        {
            return (17*first.GetHashCode()) + second.GetHashCode();
        }
    }
}