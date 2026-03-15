#region License
/* 
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is a driver delegate for the MySQL ADO.NET driver.
/// </summary>
/// <author>Marko Lahma</author>
public class MySQLDelegate : StdAdoDelegate
{
    /// <summary>
    /// Gets the select next trigger to acquire SQL clause.
    /// MySQL version with LIMIT support.
    /// </summary>
    /// <returns></returns>
    protected override string GetSelectNextTriggerToAcquireSql(int maxCount)
    {
        return SqlSelectNextTriggerToAcquire
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{0}T_NFT_ST)")
            + " LIMIT " + maxCount;
    }

    protected override string GetSelectNextMisfiredTriggersInStateToAcquireSql(int count)
    {
        if (count != -1)
        {
            return SqlSelectHasMisfiredTriggersInState
                .Replace("{0}TRIGGERS WHERE", "{0}TRIGGERS FORCE INDEX (IDX_{0}T_NFT_ST_MISFIRE) WHERE")
                + " LIMIT " + count;
        }
        return base.GetSelectNextMisfiredTriggersInStateToAcquireSql(count);
    }

    protected override string GetCountMisfiredTriggersInStateSql()
    {
        return SqlCountMisfiredTriggersInStates
            .Replace("{0}TRIGGERS WHERE", "{0}TRIGGERS FORCE INDEX (IDX_{0}T_NFT_ST_MISFIRE) WHERE");
    }
}