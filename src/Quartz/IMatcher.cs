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

using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// Matchers can be used in various <see cref="IScheduler" /> API methods to 
    /// select the entities that should be operated upon.
    /// </summary>
    /// <author>James House</author>
    /// <typeparam name="T"></typeparam>
    public interface IMatcher<T> where T : Key<T>
    {
        bool IsMatch(T key);

        int GetHashCode();

        bool Equals(object obj);
    }
}