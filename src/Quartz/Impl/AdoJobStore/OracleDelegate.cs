#region License
/* 
 * Copyright 2009- Marko Lahma
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

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is a driver delegate for the Oracle database.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class OracleDelegate : StdAdoDelegate
    {
        private string sqlSelectNextTriggerToAcquire;

        /// <summary>
        /// Initializes a new instance of the <see cref="OracleDelegate"/> class.
        /// </summary>
        /// <param name="logger">the logger to use during execution</param>
        /// <param name="tablePrefix">the prefix of all table names</param>
        /// <param name="schedName">the scheduler name</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="dbProvider">The db provider.</param>
        /// <param name="typeLoadHelper">the type loader helper</param>
        public OracleDelegate(ILog logger, string tablePrefix, string schedName, string instanceId, IDbProvider dbProvider, ITypeLoadHelper typeLoadHelper)
            : base(logger, tablePrefix, schedName, instanceId, dbProvider, typeLoadHelper)
        {
            CreateSqlForSelectNextTriggerToAcquire();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OracleDelegate"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="schedName">the scheduler name</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="dbProvider">The db provider.</param>
        /// <param name="typeLoadHelper">the type loader helper</param>
        /// <param name="useProperties">if set to <c>true</c> [use properties].</param>
        public OracleDelegate(ILog logger, string tablePrefix, string schedName, string instanceId, IDbProvider dbProvider, ITypeLoadHelper typeLoadHelper, bool useProperties)
            : base(logger, tablePrefix, schedName, instanceId, dbProvider, typeLoadHelper, useProperties)
        {
            CreateSqlForSelectNextTriggerToAcquire();
        }

        /// <summary>
        /// Creates the SQL for select next trigger to acquire.
        /// </summary>
        private void CreateSqlForSelectNextTriggerToAcquire()
        {
            sqlSelectNextTriggerToAcquire = SqlSelectNextTriggerToAcquire;

            int whereEndIdx = sqlSelectNextTriggerToAcquire.IndexOf("WHERE") + 6;
            string beginningAndWhere = sqlSelectNextTriggerToAcquire.Substring(0, whereEndIdx);
            string theRest = sqlSelectNextTriggerToAcquire.Substring(whereEndIdx);

            // add limit clause to correct place
            sqlSelectNextTriggerToAcquire = beginningAndWhere + " rownum <= " + TriggersToAcquireLimit + " AND " + theRest;
        }

        /// <summary>
        /// Gets the select next trigger to acquire SQL clause.
        /// Oracle version with rownum support.
        /// </summary>
        /// <returns></returns>
        protected override string GetSelectNextTriggerToAcquireSql()
        {
            return sqlSelectNextTriggerToAcquire;
        }

    }
}