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

using System.Data.Common;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is a driver delegate for the MySQL ADO.NET driver.
/// </summary>
/// <author>Marko Lahma</author>
public class MySQLDelegate : StdAdoDelegate
{
    /// <summary>
    /// MySQL pages with LIMIT/OFFSET rather than the ANSI clause.
    /// </summary>
    /// <remarks>
    /// MySQL has no OFFSET-only form: skipping without limiting is written as a LIMIT of the largest
    /// BIGINT UNSIGNED value, which is what the manual prescribes.
    /// </remarks>
    protected override string ApplyPaging(string sql, bool takeLimited)
    {
        return takeLimited
            ? sql + " LIMIT @pageTake OFFSET @pageSkip"
            : sql + " LIMIT 18446744073709551615 OFFSET @pageSkip";
    }

    /// <summary>
    /// Binds the LIMIT/OFFSET parameters in the order the clause names them, which is the reverse of
    /// the ANSI clause's order and matters to providers that bind positionally.
    /// </summary>
    protected override void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)
    {
        if (takeLimited)
        {
            AddCommandParameter(cmd, "pageTake", take);
        }

        AddCommandParameter(cmd, "pageSkip", skip);
    }

    /// <summary>
    /// Gets the select next trigger to acquire SQL clause.
    /// MySQL version with LIMIT support and a FORCE INDEX hint pointing at IDX_*_T_NFT_ST.
    /// </summary>
    protected override string GetSelectNextTriggerToAcquireSql(int maxCount)
    {
        return StdAdoConstants.SqlSelectNextTriggerToAcquire
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST)")
            + " LIMIT " + maxCount;
    }

    /// <summary>
    /// MySQL version with LIMIT support and a FORCE INDEX hint pointing at IDX_*_T_NFT_ST_MISFIRE.
    /// The hint attaches to the aliased TRIGGERS table, since this statement joins the type tables onto
    /// it rather than selecting from TRIGGERS alone.
    /// </summary>
    protected override string GetSelectMisfiredTriggersToRecoverSql(int count)
    {
        if (count != -1)
        {
            return StdAdoConstants.SqlSelectMisfiredTriggersToRecover
                .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST_MISFIRE)")
                + " LIMIT " + count;
        }
        return base.GetSelectMisfiredTriggersToRecoverSql(count);
    }

    protected override string GetCountMisfiredTriggersInStateSql()
    {
        return StdAdoConstants.SqlCountMisfiredTriggersInStates
            .Replace("{0}TRIGGERS WHERE", "{0}TRIGGERS FORCE INDEX (IDX_{1}T_NFT_ST_MISFIRE) WHERE");
    }
}