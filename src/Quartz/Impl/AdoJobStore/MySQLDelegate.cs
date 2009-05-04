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

using Common.Logging;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is a driver delegate for the MySQL ADO.NET driver.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class MySQLDelegate : StdAdoDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MySQLDelegate"/> class.
        /// </summary>
        /// <param name="logger">the logger to use during execution</param>
        /// <param name="tablePrefix">the prefix of all table names</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="dbProvider">The db provider.</param>
        public MySQLDelegate(ILog logger, string tablePrefix, string instanceId, IDbProvider dbProvider) : base(logger, tablePrefix, instanceId, dbProvider)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MySQLDelegate"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="dbProvider">The db provider.</param>
        /// <param name="useProperties">if set to <c>true</c> [use properties].</param>
        public MySQLDelegate(ILog logger, string tablePrefix, string instanceId, IDbProvider dbProvider, bool useProperties) : base(logger, tablePrefix, instanceId, dbProvider, useProperties)
        {
        }

        /// <summary>
        /// Gets the select next trigger to acquire SQL clause.
        /// MySQL version with LIMIT support.
        /// </summary>
        /// <returns></returns>
        protected override string GetSelectNextTriggerToAcquireSql()
        {
            return SqlSelectNextTriggerToAcquire + " LIMIT " + TriggersToAcquireLimit;
        }
    }
}