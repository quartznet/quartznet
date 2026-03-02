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

using static System.FormattableString;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is a driver delegate for the MySQL ADO.NET driver.
/// </summary>
/// <author>Marko Lahma</author>
public class MySQLDelegate : StdAdoDelegate
{
    /// <summary>
    /// Gets the select next trigger to acquire SQL clause.
    /// MySQL version with LIMIT support and optimized for performance with large datasets.
    /// Uses FORCE INDEX to ensure the query uses the most efficient index for filtering and sorting.
    /// </summary>
    /// <returns></returns>
    protected override string GetSelectNextTriggerToAcquireSql(int maxCount)
    {
        // Optimized query that forces MySQL to use the index IDX_QRTZ_T_NFT_ST
        // which contains (SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)
        // This significantly improves performance when there are hundreds of thousands of triggers
        return Invariant($@"SELECT
                t.{ColumnTriggerName}, t.{ColumnTriggerGroup}, jd.{ColumnJobClass}
              FROM
                {TablePrefixSubst}{TableTriggers} t FORCE INDEX (IDX_QRTZ_T_NFT_ST)
              JOIN
                {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName}) 
              WHERE
                t.{ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state AND {ColumnNextFireTime} <= @noLaterThan AND ({ColumnMifireInstruction} = -1 OR ({ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} >= @noEarlierThan))
              ORDER BY 
                {ColumnNextFireTime} ASC, {ColumnPriority} DESC
              LIMIT {maxCount}");
    }

    protected override string GetSelectNextMisfiredTriggersInStateToAcquireSql(int count)
    {
        if (count != -1)
        {
            // Optimized query that forces MySQL to use the index IDX_QRTZ_T_NFT_ST_MISFIRE
            // which contains (SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)
            // This significantly improves performance when there are hundreds of thousands of triggers
            return Invariant($@"SELECT
                {ColumnTriggerName}, {ColumnTriggerGroup}
              FROM
                {TablePrefixSubst}{TableTriggers} FORCE INDEX (IDX_QRTZ_T_NFT_ST_MISFIRE)
              WHERE
                {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerState} = @state1
              ORDER BY
                {ColumnNextFireTime} ASC, {ColumnPriority} DESC
              LIMIT {count}");
        }
        return base.GetSelectNextMisfiredTriggersInStateToAcquireSql(count);
    }
}