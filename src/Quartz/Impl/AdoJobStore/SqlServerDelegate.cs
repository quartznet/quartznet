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

using System;
using System.Data;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// A SQL Server specific driver delegate.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class SqlServerDelegate : StdAdoDelegate
    {
        /// <summary>
        /// Gets the select next trigger to acquire SQL clause.
        /// SQL Server specific version with TOP functionality
        /// </summary>
        /// <returns></returns>
        protected override string GetSelectNextTriggerToAcquireSql(int maxCount)
        {
            string sqlSelectNextTriggerToAcquire = SqlSelectNextTriggerToAcquire;

            // add limit clause to correct place
            sqlSelectNextTriggerToAcquire = "SELECT TOP " + maxCount + " " + sqlSelectNextTriggerToAcquire.Substring(6);

            return sqlSelectNextTriggerToAcquire;
        }

        protected override string GetSelectNextMisfiredTriggersInStateToAcquireSql(int count)
        {
            if (count != -1)
            {
                var sqlSelectHasMisfiredTriggersInState = SqlSelectHasMisfiredTriggersInState;

                // add limit clause to correct place
                sqlSelectHasMisfiredTriggersInState = "SELECT TOP " + count + " " + sqlSelectHasMisfiredTriggersInState.Substring(6);

                return sqlSelectHasMisfiredTriggersInState;
            }
            return base.GetSelectNextMisfiredTriggersInStateToAcquireSql(count);
        }

        public override void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue, Enum dataType)
        {
            // deeded for SQL Server CE
            if (paramValue is bool && dataType == default(Enum))
            {
                paramValue = (bool) paramValue ? 1 : 0;
            }

            base.AddCommandParameter(cmd, paramName, paramValue, dataType);
        }
    }
}