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
/// This class extends <see cref="AdoConstants" />
/// to include the query string constants in use by the <see cref="StdAdoDelegate" />
/// class.
/// </summary>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>Marko Lahma (.NET)</author>
public class StdAdoConstants : AdoConstants
{
    public const string TablePrefixSubst = "{0}";

    // DELETE
    public static readonly string SqlDeleteBlobTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableBlobTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteCalendar =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName");

    public static readonly string SqlDeleteCronTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableCronTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteFiredTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnEntryId} = @triggerEntryId");

    public static readonly string SqlDeleteFiredTriggers =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlDeleteInstancesFiredTriggers =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName");

    public static readonly string SqlDeleteFiredTriggersForTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteFiredTriggersForJob =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlDeleteJobDetail =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlDeletePausedTriggerGroup =
        Invariant($"DELETE FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @triggerGroup");

    public static readonly string SqlDeletePausedTriggerGroups =
        Invariant($"DELETE FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlDeleteSchedulerState =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableSchedulerState} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName");

    public static readonly string SqlDeleteSimpleTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableSimpleTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteAllSimpleTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{TableSimpleTriggers} WHERE {ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllSimpropTriggers = Invariant($"DELETE FROM {TablePrefixSubst}SIMPROP_TRIGGERS WHERE {ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllCronTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{TableCronTriggers} WHERE {ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllBlobTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{TableBlobTriggers} WHERE {ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllJobDetails = Invariant($"DELETE FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllCalendars = Invariant($"DELETE FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllPausedTriggerGrps = Invariant($"DELETE FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName");

    // INSERT

    public static readonly string SqlInsertBlobTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{TableBlobTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnBlob})  VALUES(@schedulerName, @triggerName, @triggerGroup, @blob)");

    public static readonly string SqlInsertCalendar =
        Invariant($"INSERT INTO {TablePrefixSubst}{TableCalendars} ({ColumnSchedulerName}, {ColumnCalendarName}, {ColumnCalendar})  VALUES(@schedulerName, @calendarName, @calendar)");

    public static readonly string SqlInsertCronTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{TableCronTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnCronExpression}, {ColumnTimeZoneId}) VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerCronExpression, @triggerTimeZone)");

    public static readonly string SqlInsertFiredTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{TableFiredTriggers} ({ColumnSchedulerName}, {ColumnEntryId}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnInstanceName}, {ColumnFiredTime}, {ColumnScheduledTime}, {ColumnEntryState}, {ColumnJobName}, {ColumnJobGroup}, {ColumnIsNonConcurrent}, {ColumnRequestsRecovery}, {ColumnPriority}) VALUES(@schedulerName, @triggerEntryId, @triggerName, @triggerGroup, @triggerInstanceName, @triggerFireTime, @triggerScheduledTime, @triggerState, @triggerJobName, @triggerJobGroup, @triggerJobStateful, @triggerJobRequestsRecovery, @triggerPriority)");

    public static readonly string SqlInsertJobDetail =
        Invariant($"INSERT INTO {TablePrefixSubst}{TableJobDetails} ({ColumnSchedulerName}, {ColumnJobName}, {ColumnJobGroup}, {ColumnDescription}, {ColumnJobClass}, {ColumnIsDurable}, {ColumnIsNonConcurrent}, {ColumnIsUpdateData}, {ColumnRequestsRecovery}, {ColumnJobDataMap})  VALUES(@schedulerName, @jobName, @jobGroup, @jobDescription, @jobType, @jobDurable, @jobVolatile, @jobStateful, @jobRequestsRecovery, @jobDataMap)");

    public static readonly string SqlInsertPausedTriggerGroup =
        Invariant($"INSERT INTO {TablePrefixSubst}{TablePausedTriggers} ({ColumnSchedulerName}, {ColumnTriggerGroup}) VALUES (@schedulerName, @triggerGroup)");

    public static readonly string SqlInsertSchedulerState =
        Invariant($"INSERT INTO {TablePrefixSubst}{TableSchedulerState} ({ColumnSchedulerName}, {ColumnInstanceName}, {ColumnLastCheckinTime}, {ColumnCheckinInterval}) VALUES(@schedulerName, @instanceName, @lastCheckinTime, @checkinInterval)");

    public static readonly string SqlInsertSimpleTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{TableSimpleTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnRepeatCount}, {ColumnRepeatInterval}, {ColumnTimesTriggered})  VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerRepeatCount, @triggerRepeatInterval, @triggerTimesTriggered)");

    public static readonly string SqlInsertTrigger =
        Invariant($@"INSERT INTO {TablePrefixSubst}{TableTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnJobName}, {ColumnJobGroup}, {ColumnDescription}, {ColumnNextFireTime}, {ColumnPreviousFireTime}, {ColumnTriggerState}, {ColumnTriggerType}, {ColumnStartTime}, {ColumnEndTime}, {ColumnCalendarName}, {ColumnMifireInstruction}, {ColumnJobDataMap}, {ColumnPriority}, {ColumnExecutionGroup}, {ColumnPreferredNode}, {ColumnPreferredNodeAuto})
                        VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerJobName, @triggerJobGroup, @triggerDescription, @triggerNextFireTime, @triggerPreviousFireTime, @triggerState, @triggerType, @triggerStartTime, @triggerEndTime, @triggerCalendarName, @triggerMisfireInstruction, @triggerJobJobDataMap, @triggerPriority, @triggerExecutionGroup, @triggerPreferredNode, @triggerPreferredNodeAuto)");

    // SELECT

    public static readonly string SqlSelectBlobTrigger =
        Invariant($"SELECT {ColumnBlob} FROM {TablePrefixSubst}{TableBlobTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Prefix of the batch blob-trigger lookup; the caller appends a key-set predicate built by
    /// <c>AdoUtil.BuildTriggerKeyPredicate</c>. The key columns follow the blob so the reader
    /// can stay in sequential-access mode.
    /// </summary>
    public static readonly string SqlSelectBlobTriggersByKeysPrefix =
        Invariant($"SELECT {ColumnBlob}, {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableBlobTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND ");

    /// <summary>
    /// Prefix of the batch simple-properties trigger lookup; the caller appends a key-set predicate built
    /// by <c>AdoUtil.BuildTriggerKeyPredicate</c>. All simple-properties trigger types
    /// (calendar-interval, daily-time-interval, recurrence, and any custom ones) share this one table, so a
    /// single query covers them all — the per-row discriminator comes from TRIGGERS.TRIGGER_TYPE.
    /// </summary>
    public static readonly string SqlSelectSimpropTriggersByKeysPrefix =
        Invariant($"SELECT * FROM {TablePrefixSubst}SIMPROP_TRIGGERS WHERE {ColumnSchedulerName} = @schedulerName AND ");

    public static readonly string SqlSelectCalendar =
        Invariant($"SELECT {ColumnCalendar} FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectCalendarExistence =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectCalendars =
        Invariant($"SELECT {ColumnCalendarName} FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectCronTriggers =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableCronTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectFiredTrigger =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectFiredTriggerGroup =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectFiredTriggerInstanceNames =
        Invariant($"SELECT DISTINCT {ColumnInstanceName} FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectFiredTriggersOfJob =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectCountExecutingFiredTriggersOfJob =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup AND {ColumnEntryState} = @executingState");

    public static readonly string SqlSelectCountExecutingFiredTriggersOfTrigger =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnEntryState} = @executingState");

    public static readonly string SqlSelectFiredTriggersOfJobGroup =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectInstancesFiredTriggers =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName");

    public static readonly string SqlSelectInstancesRecoverableFiredTriggers =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName AND {ColumnRequestsRecovery} = @requestsRecovery");

    public static readonly string SqlSelectJobDetail =
        Invariant($"SELECT {ColumnJobName},{ColumnJobGroup},{ColumnDescription},{ColumnJobClass},{ColumnIsDurable},{ColumnRequestsRecovery},{ColumnJobDataMap},{ColumnIsNonConcurrent},{ColumnIsUpdateData} FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectJobExecutionCount =
        Invariant($"SELECT COUNT({ColumnTriggerName}) FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectJobExistence =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectJobForTrigger =
        Invariant($"SELECT J.{ColumnJobName}, J.{ColumnJobGroup}, J.{ColumnIsDurable}, J.{ColumnJobClass}, J.{ColumnRequestsRecovery} FROM {TablePrefixSubst}{TableTriggers} T, {TablePrefixSubst}{TableJobDetails} J WHERE T.{ColumnSchedulerName} = @schedulerName AND T.{ColumnSchedulerName} = J.{ColumnSchedulerName} AND T.{ColumnTriggerName} = @triggerName AND T.{ColumnTriggerGroup} = @triggerGroup AND T.{ColumnJobName} = J.{ColumnJobName} AND T.{ColumnJobGroup} = J.{ColumnJobGroup}");

    public static readonly string SqlSelectJobGroups =
        Invariant($"SELECT DISTINCT({ColumnJobGroup}) FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectJobsInGroupLike =
        Invariant($"SELECT {ColumnJobName}, {ColumnJobGroup} FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobGroup} LIKE @jobGroup");

    public static readonly string SqlSelectJobsInGroup =
        Invariant($"SELECT {ColumnJobName}, {ColumnJobGroup} FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectMisfiredTriggers =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC");

    public static readonly string SqlSelectMisfiredTriggersInGroupInState =
        Invariant($"SELECT {ColumnTriggerName} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnTriggerState} = @state ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC");

    public static readonly string SqlSelectMisfiredTriggersInState =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerState} = @state ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC");

    public static readonly string SqlCountMisfiredTriggersInStates =
        Invariant($"SELECT COUNT({ColumnTriggerName}) FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerState} = @state1");

    public static readonly string SqlSelectHasMisfiredTriggersInState =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerState} = @state1 ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC");

    /// <summary>
    /// Sentinel stored in PREFERRED_NODE to request auto-pin that has not yet been claimed by
    /// any node. Distinct from a node name, and never itself flagged as auto-claimed.
    /// </summary>
    public const string AutoPinSentinel = "*";

    // Preferred node (node affinity) acquisition filter.
    //
    // @instanceId matches pins to this node (explicit or auto-claimed — both store the bare name).
    // @autoPinSentinel matches the "*" sentinel (auto-pin requested but unclaimed).
    // The final disjunct releases a trigger whose owning node is no longer checking in, using a
    // checkin-time-aware subquery against SCHEDULER_STATE. LAST_CHECKIN_TIME is stored in ticks and
    // CHECKIN_INTERVAL in milliseconds (10000 ticks per ms); @liveNodeCutoff is
    // (now - ClusterCheckinMisfireThreshold).UtcTicks. The arithmetic assumes the default
    // GetDbDateTimeValue/GetDbTimeSpanValue storage formats, so a delegate overriding those must
    // also override GetSelectNextTriggerToAcquireSql.
    //
    // The IS NULL test comes first so the overwhelmingly common unpinned row short-circuits before
    // the correlated subquery is considered. The node name is stored verbatim (the auto-claim flag
    // lives in its own column), so no REPLACE() is needed and the comparison stays index-friendly.
    //
    // The subquery correlates on t.SCHED_NAME instead of reusing @schedulerName: each named
    // parameter must be referenced exactly once in the statement, because providers with
    // bindByName=false adapt named parameters positionally and a reused name would produce more
    // placeholders than bound parameters.
    private static readonly string PreferredNodeWhereClause =
        Invariant($@"AND (t.{ColumnPreferredNode} IS NULL OR t.{ColumnPreferredNode} = @instanceId OR t.{ColumnPreferredNode} = @autoPinSentinel
                     OR t.{ColumnPreferredNode} NOT IN (SELECT ss.{ColumnInstanceName} FROM {TablePrefixSubst}{TableSchedulerState} ss WHERE ss.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND ss.{ColumnLastCheckinTime} + ss.{ColumnCheckinInterval} * 10000 >= @liveNodeCutoff))");

    // PREFERRED_NODE is filtered entirely in PreferredNodeWhereClause and is not projected —
    // acquisition never reads it from the result (the trigger is reloaded via RetrieveTrigger).
    public static readonly string SqlSelectNextTriggerToAcquire =
        Invariant($@"SELECT
                t.{ColumnTriggerName}, t.{ColumnTriggerGroup}, jd.{ColumnJobClass}, t.{ColumnExecutionGroup}
              FROM
                {TablePrefixSubst}{TableTriggers} t
              JOIN
                {TablePrefixSubst}{TableJobDetails} jd ON (jd.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND  jd.{ColumnJobGroup} = t.{ColumnJobGroup} AND jd.{ColumnJobName} = t.{ColumnJobName})
              WHERE
                t.{ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state AND {ColumnNextFireTime} <= @noLaterThan AND ({ColumnMifireInstruction} = -1 OR ({ColumnMifireInstruction} <> -1 AND {ColumnNextFireTime} >= @noEarlierThan))
                {PreferredNodeWhereClause}
              ORDER BY
                {ColumnNextFireTime} ASC, {ColumnPriority} DESC");

    public static readonly string SqlSelectNumCalendars =
        Invariant($"SELECT COUNT({ColumnCalendarName})  FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectNumJobs =
        Invariant($"SELECT COUNT({ColumnJobName})  FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectNumTriggers =
        Invariant($"SELECT COUNT({ColumnTriggerName})  FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectNumTriggersForJob =
        Invariant($"SELECT COUNT({ColumnTriggerName}) FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectNumTriggersInGroup =
        Invariant($"SELECT COUNT({ColumnTriggerName})  FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectPausedTriggerGroup =
        Invariant($"SELECT {ColumnTriggerGroup} FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectPausedTriggerGroups =
        Invariant($"SELECT {ColumnTriggerGroup} FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectReferencedCalendar =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectSchedulerState =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableSchedulerState} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName");

    public static readonly string SqlSelectSchedulerStates =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableSchedulerState} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectSimpleTrigger =
        Invariant($"SELECT * FROM {TablePrefixSubst}{TableSimpleTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Column list shared by <see cref="SqlSelectTrigger" /> and <see cref="SqlSelectMisfiredTriggersToRecover" />,
    /// so the single-trigger and batch read paths cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Ordinals matter here: <c>ReadMapFromReader(rs, 11)</c> reads JOB_DATA positionally. Append new
    /// columns to the end of this list, never insert into the middle.
    /// </remarks>
    private const string TriggerSelectColumns = $@"
                {ColumnJobName},
                {ColumnJobGroup},
                {ColumnDescription},
                {ColumnNextFireTime},
                {ColumnPreviousFireTime},
                {ColumnTriggerType},
                {ColumnStartTime},
                {ColumnEndTime},
                {ColumnCalendarName},
                {ColumnMifireInstruction},
                {ColumnPriority},
                {ColumnJobDataMap},
                {ColumnCronExpression},
                {ColumnTimeZoneId},
                {ColumnRepeatCount},
                {ColumnRepeatInterval},
                {ColumnTimesTriggered},
                t.{ColumnMisfireOriginalFireTime},
                t.{ColumnExecutionGroup},
                t.{ColumnPreferredNode},
                t.{ColumnPreferredNodeAuto}";

    /// <summary>
    /// FROM clause that left-joins the SIMPLE and CRON type tables onto TRIGGERS, letting the two most
    /// common trigger types be materialized from a single row without a follow-up query.
    /// </summary>
    private const string TriggerSelectFastPathFrom = $@"
            FROM
                {TablePrefixSubst}{TableTriggers} t
            LEFT JOIN
                {TablePrefixSubst}{TableSimpleTriggers} st ON (st.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND st.{ColumnTriggerGroup} = t.{ColumnTriggerGroup} AND st.{ColumnTriggerName} = t.{ColumnTriggerName})
            LEFT JOIN
                {TablePrefixSubst}{TableCronTriggers} ct ON (ct.{ColumnSchedulerName} = t.{ColumnSchedulerName} AND ct.{ColumnTriggerGroup} = t.{ColumnTriggerGroup} AND ct.{ColumnTriggerName} = t.{ColumnTriggerName})";

    public static readonly string SqlSelectTrigger =
        Invariant($@"SELECT {TriggerSelectColumns}{TriggerSelectFastPathFrom}
            WHERE
                t.{ColumnSchedulerName} = @schedulerName AND t.{ColumnTriggerName} = @triggerName AND t.{ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Selects the misfired triggers to recover as fully populated rows, so a whole misfire recovery
    /// batch costs one round-trip instead of one per trigger. Same predicate and ordering as
    /// <see cref="SqlSelectHasMisfiredTriggersInState" />, same columns as <see cref="SqlSelectTrigger" />
    /// with the key columns appended (they are ambiguous across the joined tables, hence the alias).
    /// </summary>
    /// <remarks>
    /// Must start with the <c>SELECT</c> keyword — <see cref="SqlServerDelegate" /> splices its
    /// <c>TOP n</c> in at that offset.
    /// </remarks>
    public static readonly string SqlSelectMisfiredTriggersToRecover =
        Invariant($@"SELECT {TriggerSelectColumns},
                t.{ColumnTriggerName},
                t.{ColumnTriggerGroup}{TriggerSelectFastPathFrom}
            WHERE
                t.{ColumnSchedulerName} = @schedulerName AND t.{ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND t.{ColumnNextFireTime} < @nextFireTime AND t.{ColumnTriggerState} = @state1
            ORDER BY t.{ColumnNextFireTime} ASC, t.{ColumnPriority} DESC");

    public static readonly string SqlSelectTriggerData =
        Invariant($"SELECT {ColumnJobDataMap} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggerExistence =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggerForFireTime =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state AND {ColumnNextFireTime} = @nextFireTime");

    public static readonly string SqlSelectTriggerGroups =
        Invariant($"SELECT DISTINCT({ColumnTriggerGroup}) FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectTriggerGroupsFiltered =
        Invariant($"SELECT DISTINCT({ColumnTriggerGroup}) FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @triggerGroup");

    public static readonly string SqlSelectTriggerState =
        Invariant($"SELECT {ColumnTriggerState} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggerStatus =
        Invariant($"SELECT {ColumnTriggerState}, {ColumnNextFireTime}, {ColumnJobName}, {ColumnJobGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggersForCalendar =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectTriggersForJob =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectTriggersInGroupLike =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @triggerGroup");

    public static readonly string SqlSelectTriggersInGroup =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggersInState =
        Invariant($"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state");

    public static readonly string SqlSelectTriggerType =
        Invariant($"SELECT {ColumnTriggerType} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    // UPDATE

    public static readonly string SqlUpdateBlobTrigger =
        Invariant($"UPDATE {TablePrefixSubst}{TableBlobTriggers} SET {ColumnBlob} = @blob WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateCalendar =
        Invariant($"UPDATE {TablePrefixSubst}{TableCalendars} SET {ColumnCalendar} = @calendar WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName");

    public static readonly string SqlUpdateCronTrigger =
        Invariant($"UPDATE {TablePrefixSubst}{TableCronTriggers} SET {ColumnCronExpression} = @triggerCronExpression, {ColumnTimeZoneId} = @timeZoneId WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateJobData =
        Invariant($"UPDATE {TablePrefixSubst}{TableJobDetails} SET {ColumnJobDataMap} = @jobDataMap WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlUpdateJobDetail =
        Invariant($"UPDATE {TablePrefixSubst}{TableJobDetails} SET {ColumnDescription} = @jobDescription, {ColumnJobClass} = @jobType, {ColumnIsDurable} = @jobDurable, {ColumnIsNonConcurrent} = @jobVolatile, {ColumnIsUpdateData} = @jobStateful, {ColumnRequestsRecovery} = @jobRequestsRecovery, {ColumnJobDataMap} = @jobDataMap  WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlUpdateJobTriggerStates =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @state WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup");

    public static readonly string SqlUpdateJobTriggerStatesFromOtherState =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @state WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup AND {ColumnTriggerState} = @oldState");

    public static readonly string SqlUpdateSchedulerState =
        Invariant($"UPDATE {TablePrefixSubst}{TableSchedulerState} SET {ColumnLastCheckinTime} = @lastCheckinTime WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName");

    public static readonly string SqlUpdateSimpleTrigger =
        Invariant($"UPDATE {TablePrefixSubst}{TableSimpleTriggers} SET {ColumnRepeatCount} = @triggerRepeatCount, {ColumnRepeatInterval} = @triggerRepeatInterval, {ColumnTimesTriggered} = @triggerTimesTriggered WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    // The preferred node columns are only written when the pin was actually changed on the trigger
    // instance, so each UPDATE comes in two flavours. Writing the pin back unconditionally would
    // clobber concurrent changes (ClusterRecover's failover reset, an UpdateTriggerDetails re-pin)
    // with the value that happened to be loaded at acquire time. Folding the columns into the main
    // UPDATE — rather than issuing a second statement — keeps it to one round-trip either way.
    private const string PreferredNodeSetClause =
        $", {ColumnPreferredNode} = @triggerPreferredNode, {ColumnPreferredNodeAuto} = @triggerPreferredNodeAuto";

    public static readonly string SqlUpdateTrigger =
        Invariant($@"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnJobName} = @triggerJobName, {ColumnJobGroup} = @triggerJobGroup, {ColumnDescription} = @triggerDescription, {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {ColumnTriggerState} = @triggerState, {ColumnTriggerType} = @triggerType, {ColumnStartTime} = @triggerStartTime, {ColumnEndTime} = @triggerEndTime, {ColumnCalendarName} = @triggerCalendarName, {ColumnMifireInstruction} = @triggerMisfireInstruction, {ColumnPriority} = @triggerPriority, {ColumnJobDataMap} = @triggerJobJobDataMap, {ColumnExecutionGroup} = @triggerExecutionGroup
                        WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerWithPreferredNode =
        Invariant($@"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnJobName} = @triggerJobName, {ColumnJobGroup} = @triggerJobGroup, {ColumnDescription} = @triggerDescription, {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {ColumnTriggerState} = @triggerState, {ColumnTriggerType} = @triggerType, {ColumnStartTime} = @triggerStartTime, {ColumnEndTime} = @triggerEndTime, {ColumnCalendarName} = @triggerCalendarName, {ColumnMifireInstruction} = @triggerMisfireInstruction, {ColumnPriority} = @triggerPriority, {ColumnJobDataMap} = @triggerJobJobDataMap, {ColumnExecutionGroup} = @triggerExecutionGroup{PreferredNodeSetClause}
                        WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateFiredTrigger = Invariant($"UPDATE {TablePrefixSubst}{TableFiredTriggers} SET {ColumnInstanceName} = @instanceName, {ColumnFiredTime} = @firedTime, {ColumnScheduledTime} = @scheduledTime, {ColumnEntryState} = @entryState, {ColumnJobName} = @jobName, {ColumnJobGroup} = @jobGroup, {ColumnIsNonConcurrent} = @isNonConcurrent, {ColumnRequestsRecovery} = @requestsRecover WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnEntryId} = @entryId");

    public static readonly string SqlUpdateTriggerGroupStateFromState =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @triggerGroup AND {ColumnTriggerState} = @oldState");

    public static readonly string SqlUpdateTriggerGroupStateFromStates =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @groupName AND ({ColumnTriggerState} = @oldState1 OR {ColumnTriggerState} = @oldState2 OR {ColumnTriggerState} = @oldState3)");

    public static readonly string SqlUpdateTriggerSkipData =
        Invariant($@"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnJobName} = @triggerJobName, {ColumnJobGroup} = @triggerJobGroup, {ColumnDescription} = @triggerDescription, {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {ColumnTriggerState} = @triggerState, {ColumnTriggerType} = @triggerType, {ColumnStartTime} = @triggerStartTime, {ColumnEndTime} = @triggerEndTime, {ColumnCalendarName} = @triggerCalendarName, {ColumnMifireInstruction} = @triggerMisfireInstruction, {ColumnPriority} = @triggerPriority, {ColumnExecutionGroup} = @triggerExecutionGroup
                    WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerSkipDataWithPreferredNode =
        Invariant($@"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnJobName} = @triggerJobName, {ColumnJobGroup} = @triggerJobGroup, {ColumnDescription} = @triggerDescription, {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {ColumnTriggerState} = @triggerState, {ColumnTriggerType} = @triggerType, {ColumnStartTime} = @triggerStartTime, {ColumnEndTime} = @triggerEndTime, {ColumnCalendarName} = @triggerCalendarName, {ColumnMifireInstruction} = @triggerMisfireInstruction, {ColumnPriority} = @triggerPriority, {ColumnExecutionGroup} = @triggerExecutionGroup{PreferredNodeSetClause}
                    WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    // Compare-and-swap for the auto-pin claim/steal: only writes when the columns still hold the
    // values observed at acquisition time, so a concurrent re-pin or clear wins over the claim.
    public static readonly string SqlUpdateTriggerPreferredNodeConditional =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnPreferredNode} = @triggerPreferredNode, {ColumnPreferredNodeAuto} = @triggerPreferredNodeAuto WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnPreferredNode} = @expectedPreferredNode AND {ColumnPreferredNodeAuto} = @expectedPreferredNodeAuto");

    // Failover reset: only auto-claimed pins belonging to the dead node are released back to the
    // "*" sentinel. Explicit pins are left alone so the original node reclaims them.
    public static readonly string SqlRepinTriggersFromDeadNode =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnPreferredNode} = @newPreferredNode, {ColumnPreferredNodeAuto} = @newPreferredNodeAuto WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnPreferredNode} = @oldPreferredNode AND {ColumnPreferredNodeAuto} = @oldPreferredNodeAuto");

    public static readonly string SqlUpdateTriggerState =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @state WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerStateFromState =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnTriggerState} = @oldState");

    public static readonly string SqlUpdateTriggerStateFromStates =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND ({ColumnTriggerState} = @oldState1 OR {ColumnTriggerState} = @oldState2 OR {ColumnTriggerState} = @oldState3)");

    public static readonly string SqlUpdateTriggerStateFromStateWithNextFireTime =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnTriggerState} = @oldState AND {ColumnNextFireTime} = @nextFireTime");

    public static readonly string SqlUpdateTriggerStatesFromOtherStates =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND ({ColumnTriggerState} = @oldState1 OR {ColumnTriggerState} = @oldState2)");

    public static readonly string SqlUpdateMisfireOrigFireTime =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnMisfireOriginalFireTime} = @misfireOrigFireTime WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    // Targeted misfire recovery UPDATE — only touches columns that change during UpdateAfterMisfire.
    // START_TIME is included because SimpleTrigger's RescheduleNowWith* policies modify it.
    public static readonly string SqlUpdateTriggerMisfire =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime, {ColumnTriggerState} = @triggerState, {ColumnStartTime} = @triggerStartTime WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerMisfireWithOrigFireTime =
        Invariant($"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime, {ColumnTriggerState} = @triggerState, {ColumnStartTime} = @triggerStartTime, {ColumnMisfireOriginalFireTime} = @triggerMisfireOrigFireTime WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup");
}
