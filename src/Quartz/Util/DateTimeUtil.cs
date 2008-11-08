/* 
* Copyright 2004-2005 OpenSymphony 
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

using System;


namespace Quartz.Util
{
    /// <summary>
    /// DateTime related utility methods.
    /// </summary>
    public static class DateTimeUtil
    {
        /// <summary>
        /// Assumes that given input is in UTC and sets the kind to be UTC.
        /// Just a precaution if somebody does not set it explicitly.
        /// <strong>This only works in .NET Framework 2.0 onwards.</strong>
        /// </summary>
        /// <param name="dt">The datetime to check.</param>
        /// <returns>DateTime with kind set to UTC.</returns>
        public static DateTime AssumeUniversalTime(DateTime dt)
        {
            return new DateTime(dt.Ticks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Assumes that given input is in UTC and sets the kind to be UTC.
        /// Just a precaution if somebody does not set it explicitly.
        /// </summary>
        /// <param name="dt">The datetime to check.</param>
        /// <returns>DateTime with kind set to UTC.</returns>
        public static DateTime? AssumeUniversalTime(DateTime? dt)
        {
            if (dt.HasValue)
            {
                return AssumeUniversalTime(dt.Value);
            }
            else
            {
                return null;
            }
        }
    }
}
