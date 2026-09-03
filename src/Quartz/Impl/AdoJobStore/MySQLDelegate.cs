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
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST)")
            + " LIMIT " + maxCount;
    }

    protected override string GetSelectNextTriggerToAcquireWithExecutionGroupSql(int maxCount)
    {
        return SqlSelectNextTriggerToAcquireWithExecutionGroup
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST)")
            + " LIMIT " + maxCount;
    }

    protected override string GetSelectNextTriggerToAcquireWithPreferredNodeSql(int maxCount)
    {
        return SqlSelectNextTriggerToAcquireWithPreferredNode
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST)")
            + " LIMIT " + maxCount;
    }

    protected override string GetSelectNextTriggerToAcquireWithPreferredNodeOnlySql(int maxCount)
    {
        return SqlSelectNextTriggerToAcquireWithPreferredNodeOnly
            .Replace("{0}TRIGGERS t", "{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST)")
            + " LIMIT " + maxCount;
    }

    /// <summary>
    /// The misfire sweep carries a FORCE INDEX hint pointing at IDX_*_T_NFT_ST — the acquisition
    /// index, not IDX_*_T_NFT_ST_MISFIRE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It named the misfire index until <see href="https://github.com/quartznet/quartznet/issues/3608">#3608</see>
    /// measured what that costs. The sweep filters on SCHED_NAME and TRIGGER_STATE by equality, ranges
    /// on NEXT_FIRE_TIME and keeps MISFIRE_INSTR as a residual — which is exactly the shape of
    /// <c>IDX_QRTZ_T_NFT_ST (SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)</c>, and nothing like
    /// <c>(SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)</c>, whose second column is compared
    /// with <c>&lt;&gt;</c> and so stops the seek dead. Every other dialect's optimizer works this out
    /// on its own; MySQL is the one that cannot, because this hint tells it not to. Measured on a
    /// 100,000-trigger table with a 5,000-row backlog: 15,561 buffer pool reads and 66 ms became
    /// 129 reads and 0.7 ms.
    /// </para>
    /// <para>
    /// Applied whatever the batch size, including an unlimited sweep. It used to be skipped for the
    /// unlimited one, which was an artefact of the row limit and the hint sharing an early return
    /// rather than a decision: the index a statement should read is not a function of how many rows
    /// it returns, and an unlimited sweep is the case that reads the most.
    /// </para>
    /// </remarks>
    protected override string GetSelectNextMisfiredTriggersInStateToAcquireSql(int count)
    {
        string sql = SqlSelectHasMisfiredTriggersInState
            .Replace("{0}TRIGGERS WHERE", "{0}TRIGGERS FORCE INDEX (IDX_{1}T_NFT_ST) WHERE");

        return count != -1 ? sql + " LIMIT " + count : sql;
    }

    /// <summary>
    /// The counting form of the misfire scan carries the same FORCE INDEX hint.
    /// </summary>
    /// <remarks>
    /// Its predicate is the sweep's without the ORDER BY, so it wants the same index for the same
    /// reason — and it is the cheap peek every misfire-handler pass starts with, so it runs far more
    /// often than the sweep it guards. Measured on the same table with nothing misfired at all:
    /// 4,594 buffer pool reads and 111 ms became 8 reads and 0.7 ms.
    /// </remarks>
    protected override string GetCountMisfiredTriggersInStateSql()
    {
        return SqlCountMisfiredTriggersInStates
            .Replace("{0}TRIGGERS WHERE", "{0}TRIGGERS FORCE INDEX (IDX_{1}T_NFT_ST) WHERE");
    }
}