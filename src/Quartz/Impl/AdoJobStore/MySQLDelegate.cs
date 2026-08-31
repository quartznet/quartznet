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
    /// <inheritdoc />
    protected override string? SchemaResourceName => "Quartz.Impl.AdoJobStore.Schema.create_mysql_innodb.sql";

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
            ? sql + " LIMIT @" + SqlParameters.PageTake + " OFFSET @" + SqlParameters.PageSkip
            : sql + " LIMIT 18446744073709551615 OFFSET @" + SqlParameters.PageSkip;
    }

    /// <summary>
    /// Binds the LIMIT/OFFSET parameters in the order the clause names them, which is the reverse of
    /// the ANSI clause's order and matters to providers that bind positionally.
    /// </summary>
    protected override void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)
    {
        if (takeLimited)
        {
            AddCommandParameter(cmd, SqlParameters.PageTake, take);
        }

        AddCommandParameter(cmd, SqlParameters.PageSkip, skip);
    }

    /// <summary>
    /// MySQL limits rows with a trailing <c>LIMIT n</c>.
    /// </summary>
    protected override SqlRowLimit GetRowLimit(int count) => SqlRowLimit.AtStatementEnd("LIMIT", count);

    /// <summary>
    /// The acquisition statement carries a FORCE INDEX hint pointing at IDX_*_T_NFT_ST.
    /// </summary>
    protected override string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
    {
        return base.GetSelectNextTriggerToAcquireSql(shape)
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST)");
    }

    /// <summary>
    /// The misfire recovery statement carries a FORCE INDEX hint pointing at IDX_*_T_NFT_ST_MISFIRE.
    /// The hint attaches to the aliased TRIGGERS table, since this statement joins the type tables onto
    /// it rather than selecting from TRIGGERS alone.
    /// </summary>
    /// <remarks>
    /// Applied whatever the batch size, including an unlimited sweep. It used to be skipped for the
    /// unlimited one, which was an artefact of the row limit and the hint sharing an early return
    /// rather than a decision: the index a statement should read is not a function of how many rows
    /// it returns, and an unlimited sweep is the case that reads the most.
    /// </remarks>
    protected override string GetSelectMisfiredTriggersToRecoverSql(int count)
    {
        return base.GetSelectMisfiredTriggersToRecoverSql(count)
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST_MISFIRE)");
    }

    /// <summary>
    /// The counting form of the misfire scan carries the same FORCE INDEX hint, on the unaliased table.
    /// </summary>
    protected override string GetCountMisfiredTriggersInStateSql()
    {
        return base.GetCountMisfiredTriggersInStateSql()
            .Replace("{0}TRIGGERS WHERE", "{0}TRIGGERS FORCE INDEX (IDX_{1}T_NFT_ST_MISFIRE) WHERE");
    }
}
