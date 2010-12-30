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

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Interface for Quartz objects that need to know what the table prefix of
    /// the tables used by a ADO.NET JobStore is.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public interface ITablePrefixAware
    {
        /// <summary>
        /// Table prefix to use.
        /// </summary>
        string TablePrefix { set; }

        string SchedName { set; }
    }
}