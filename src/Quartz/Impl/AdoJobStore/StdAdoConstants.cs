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

using System.Globalization;
using System.Text;

using Quartz.Extensibility;

using static System.FormattableString;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The SQL statement templates <see cref="StdAdoDelegate" /> and the dialect delegates issue.
/// </summary>
/// <remarks>
/// Internal on purpose: the exact text of a statement is not a contract. The schema it addresses is,
/// and that lives in <see cref="AdoConstants" />, which stays public.
/// </remarks>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>Marko Lahma (.NET)</author>
internal static class StdAdoConstants
{
    public const string TablePrefixSubst = "{0}";

    /// <summary>
    /// Escape character for the group-name patterns <c>StdAdoDelegate.ToSqlLikeClause</c> produces, so
    /// that a group literally named <c>50%</c> or <c>a_b</c> matches itself instead of acting as a
    /// wildcard.
    /// </summary>
    /// <remarks>
    /// Deliberately not the conventional backslash: MySQL applies C-style escaping inside string
    /// literals, where <c>ESCAPE '\'</c> is a syntax error (it would need <c>ESCAPE '\\'</c>, which in
    /// turn is wrong everywhere else). <c>!</c> is an ordinary character in a string literal on every
    /// supported dialect, so one statement text serves them all.
    /// </remarks>
    public const char SqlLikeEscapeCharacter = '!';

    /// <summary>
    /// The ANSI <c>ESCAPE</c> clause naming <see cref="SqlLikeEscapeCharacter" />. Every statement that
    /// binds a pattern from <c>StdAdoDelegate.ToSqlLikeClause</c> ends its LIKE with this.
    /// </summary>
    /// <remarks>
    /// <c>ESCAPE</c> is ANSI SQL and is accepted by SQL Server, PostgreSQL, MySQL, SQLite, Oracle and
    /// Firebird alike.
    /// </remarks>
    public static readonly string SqlLikeEscapeClause = Invariant($" ESCAPE '{SqlLikeEscapeCharacter}'");

    // DELETE
    public static readonly string SqlDeleteBlobTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableBlobTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteCalendar =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableCalendars} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnCalendarName} = @calendarName");

    public static readonly string SqlDeleteCronTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableCronTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteFiredTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnEntryId} = @triggerEntryId");

    /// <summary>
    /// Deletes every fired trigger of the scheduler; the caller appends the predicates of a
    /// <see cref="FiredTriggerQuery" /> to narrow it.
    /// </summary>
    public static readonly string SqlDeleteFiredTriggers =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlDeleteJobDetail =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlDeletePausedTriggerGroupEquals =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TablePausedTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeletePausedTriggerGroupLike =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TablePausedTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} LIKE @triggerGroup{SqlLikeEscapeClause}");

    public static readonly string SqlDeletePausedJobGroupEquals =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TablePausedJobs} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlDeletePausedJobGroupLike =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TablePausedJobs} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobGroup} LIKE @jobGroup{SqlLikeEscapeClause}");

    public static readonly string SqlDeleteSchedulerState =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableSchedulerState} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnInstanceName} = @instanceName");

    public static readonly string SqlDeleteSimpleTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableSimpleTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteTrigger =
        Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlDeleteAllSimpleTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableSimpleTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllSimpropTriggers = Invariant($"DELETE FROM {TablePrefixSubst}SIMPROP_TRIGGERS WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllCronTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableCronTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllBlobTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableBlobTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllTriggers = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllJobDetails = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllCalendars = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TableCalendars} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllPausedTriggerGrps = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TablePausedTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");
    public static readonly string SqlDeleteAllPausedJobGrps = Invariant($"DELETE FROM {TablePrefixSubst}{AdoConstants.TablePausedJobs} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    // INSERT

    public static readonly string SqlInsertBlobTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TableBlobTriggers} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnBlob})  VALUES(@schedulerName, @triggerName, @triggerGroup, @blob)");

    public static readonly string SqlInsertCalendar =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TableCalendars} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnCalendarName}, {AdoConstants.ColumnCalendar})  VALUES(@schedulerName, @calendarName, @calendar)");

    public static readonly string SqlInsertCronTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TableCronTriggers} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnCronExpression}, {AdoConstants.ColumnTimeZoneId}) VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerCronExpression, @triggerTimeZone)");

    public static readonly string SqlInsertFiredTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TableFiredTriggers} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnEntryId}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnInstanceName}, {AdoConstants.ColumnFiredTime}, {AdoConstants.ColumnScheduledTime}, {AdoConstants.ColumnEntryState}, {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnIsNonConcurrent}, {AdoConstants.ColumnRequestsRecovery}, {AdoConstants.ColumnPriority}, {AdoConstants.ColumnExecutionGroup}) VALUES(@schedulerName, @triggerEntryId, @triggerName, @triggerGroup, @triggerInstanceName, @triggerFireTime, @triggerScheduledTime, @triggerState, @triggerJobName, @triggerJobGroup, @triggerJobStateful, @triggerJobRequestsRecovery, @triggerPriority, @triggerExecutionGroup)");

    public static readonly string SqlInsertJobDetail =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TableJobDetails} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnDescription}, {AdoConstants.ColumnJobClass}, {AdoConstants.ColumnIsDurable}, {AdoConstants.ColumnIsNonConcurrent}, {AdoConstants.ColumnIsUpdateData}, {AdoConstants.ColumnRequestsRecovery}, {AdoConstants.ColumnJobDataMap})  VALUES(@schedulerName, @jobName, @jobGroup, @jobDescription, @jobType, @jobDurable, @jobVolatile, @jobStateful, @jobRequestsRecovery, @jobDataMap)");

    public static readonly string SqlInsertPausedTriggerGroup =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TablePausedTriggers} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnTriggerGroup}) VALUES (@schedulerName, @triggerGroup)");

    public static readonly string SqlInsertPausedJobGroup =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TablePausedJobs} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnJobGroup}) VALUES (@schedulerName, @jobGroup)");

    public static readonly string SqlInsertSchedulerState =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TableSchedulerState} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnInstanceName}, {AdoConstants.ColumnLastCheckinTime}, {AdoConstants.ColumnCheckinInterval}) VALUES(@schedulerName, @instanceName, @lastCheckinTime, @checkinInterval)");

    public static readonly string SqlInsertSimpleTrigger =
        Invariant($"INSERT INTO {TablePrefixSubst}{AdoConstants.TableSimpleTriggers} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnRepeatCount}, {AdoConstants.ColumnRepeatInterval}, {AdoConstants.ColumnTimesTriggered})  VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerRepeatCount, @triggerRepeatInterval, @triggerTimesTriggered)");

    public static readonly string SqlInsertTrigger =
        Invariant($@"INSERT INTO {TablePrefixSubst}{AdoConstants.TableTriggers} ({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnDescription}, {AdoConstants.ColumnNextFireTime}, {AdoConstants.ColumnPreviousFireTime}, {AdoConstants.ColumnTriggerState}, {AdoConstants.ColumnTriggerType}, {AdoConstants.ColumnStartTime}, {AdoConstants.ColumnEndTime}, {AdoConstants.ColumnCalendarName}, {AdoConstants.ColumnMisfireInstruction}, {AdoConstants.ColumnJobDataMap}, {AdoConstants.ColumnPriority}, {AdoConstants.ColumnExecutionGroup}, {AdoConstants.ColumnPreferredNode}, {AdoConstants.ColumnPreferredNodeAuto})
                        VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerJobName, @triggerJobGroup, @triggerDescription, @triggerNextFireTime, @triggerPreviousFireTime, @triggerState, @triggerType, @triggerStartTime, @triggerEndTime, @triggerCalendarName, @triggerMisfireInstruction, @triggerJobJobDataMap, @triggerPriority, @triggerExecutionGroup, @triggerPreferredNode, @triggerPreferredNodeAuto)");

    // SELECT

    public static readonly string SqlSelectBlobTrigger =
        Invariant($"SELECT {AdoConstants.ColumnBlob} FROM {TablePrefixSubst}{AdoConstants.TableBlobTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Prefix of the batch blob-trigger lookup; the caller appends a key-set predicate built by
    /// <c>AdoUtil.BuildTriggerKeyPredicate</c>. The key columns follow the blob so the reader
    /// can stay in sequential-access mode.
    /// </summary>
    public static readonly string SqlSelectBlobTriggersByKeysPrefix =
        Invariant($"SELECT {AdoConstants.ColumnBlob}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TableBlobTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND ");

    /// <summary>
    /// Prefix of the batch simple-properties trigger lookup; the caller appends a key-set predicate built
    /// by <c>AdoUtil.BuildTriggerKeyPredicate</c>. All simple-properties trigger types
    /// (calendar-interval, daily-time-interval, recurrence, and any custom ones) share this one table, so a
    /// single query covers them all — the per-row discriminator comes from TRIGGERS.TRIGGER_TYPE.
    /// </summary>
    public static readonly string SqlSelectSimpropTriggersByKeysPrefix =
        Invariant($"SELECT * FROM {TablePrefixSubst}SIMPROP_TRIGGERS WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND ");

    public static readonly string SqlSelectCalendar =
        Invariant($"SELECT {AdoConstants.ColumnCalendar} FROM {TablePrefixSubst}{AdoConstants.TableCalendars} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectCalendarExistence =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{AdoConstants.TableCalendars} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectCronTriggers =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableCronTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Selects every fired trigger of the scheduler; the caller appends the predicates of a
    /// <see cref="FiredTriggerQuery" /> to narrow it.
    /// </summary>
    public static readonly string SqlSelectFiredTriggers =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    // FIRED_TRIGGERS predicates, shared by the select and the delete so the two cannot filter
    // differently. They are appended — and their parameters bound — in the order declared here,
    // because providers with bindByName = false adapt named parameters positionally.

    public static readonly string SqlFiredTriggerTriggerPredicate =
        Invariant($" AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlFiredTriggerJobPredicate =
        Invariant($" AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlFiredTriggerInstancePredicate =
        Invariant($" AND {AdoConstants.ColumnInstanceName} = @instanceName");

    public static readonly string SqlSelectFiredTriggerInstanceNames =
        Invariant($"SELECT DISTINCT {AdoConstants.ColumnInstanceName} FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectCountExecutingFiredTriggersOfJob =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup AND {AdoConstants.ColumnEntryState} = @executingState");

    public static readonly string SqlSelectInstancesRecoverableFiredTriggers =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnInstanceName} = @instanceName AND {AdoConstants.ColumnRequestsRecovery} = @requestsRecovery");

    /// <summary>
    /// Column list shared by <see cref="SqlSelectJobDetail" /> and <see cref="SqlSelectJobDetailsByKeysPrefix" />,
    /// so the single-job and batch read paths cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Ordinals matter here: the row reader reads JOB_DATA positionally at index 6, and the reader runs in
    /// sequential-access mode. Append new columns to the end of this list, never insert into the middle.
    /// </remarks>
    private const string JobDetailSelectColumns =
        $"{AdoConstants.ColumnJobName},{AdoConstants.ColumnJobGroup},{AdoConstants.ColumnDescription},{AdoConstants.ColumnJobClass},{AdoConstants.ColumnIsDurable},{AdoConstants.ColumnRequestsRecovery},{AdoConstants.ColumnJobDataMap},{AdoConstants.ColumnIsNonConcurrent},{AdoConstants.ColumnIsUpdateData}";

    public static readonly string SqlSelectJobDetail =
        Invariant($"SELECT {JobDetailSelectColumns} FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    /// <summary>
    /// Prefix of the batch job lookup; the caller appends a key-set predicate built by
    /// <c>AdoUtil.BuildJobKeyPredicate</c>.
    /// </summary>
    public static readonly string SqlSelectJobDetailsByKeysPrefix =
        Invariant($"SELECT {JobDetailSelectColumns} FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND ");

    public static readonly string SqlSelectJobExistence =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectJobForTrigger =
        Invariant($"SELECT J.{AdoConstants.ColumnJobName}, J.{AdoConstants.ColumnJobGroup}, J.{AdoConstants.ColumnIsDurable}, J.{AdoConstants.ColumnJobClass}, J.{AdoConstants.ColumnRequestsRecovery} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} T, {TablePrefixSubst}{AdoConstants.TableJobDetails} J WHERE T.{AdoConstants.ColumnSchedulerName} = @schedulerName AND T.{AdoConstants.ColumnSchedulerName} = J.{AdoConstants.ColumnSchedulerName} AND T.{AdoConstants.ColumnTriggerName} = @triggerName AND T.{AdoConstants.ColumnTriggerGroup} = @triggerGroup AND T.{AdoConstants.ColumnJobName} = J.{AdoConstants.ColumnJobName} AND T.{AdoConstants.ColumnJobGroup} = J.{AdoConstants.ColumnJobGroup}");

    public static readonly string SqlSelectJobsInGroupLike =
        Invariant($"SELECT {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup} FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobGroup} LIKE @jobGroup{SqlLikeEscapeClause}");

    public static readonly string SqlSelectJobsInGroup =
        Invariant($"SELECT {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup} FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    /// <summary>
    /// The cheap count the misfire handler peeks with before it takes the trigger lock. It has to
    /// select the same rows <see cref="SqlSelectMisfiredTriggersToRecover" /> does, or the sweep
    /// decides there is nothing to recover and skips work it would have found.
    /// </summary>
    /// <remarks>
    /// <c>NEXT_FIRE_TIME &lt;= @nextFireTime</c> is the one rule about a misfire, spelled here in SQL:
    /// a trigger is late once its fire time is <em>at or before</em> <c>now - MisfireThreshold</c>.
    /// <c>RAMJobStore.ApplyMisfireNoLock</c>, <c>AdoJobStoreBase.UpdateMisfiredTrigger</c> and
    /// <c>AdoJobStoreBase.RecoverUnblockedMisfires</c> all decline a trigger whose fire time is
    /// strictly greater, and <see cref="SqlSelectNextTriggerToAcquire" /> takes the complement of it.
    /// </remarks>
    public static readonly string SqlCountMisfiredTriggersInStates =
        Invariant($"SELECT COUNT({AdoConstants.ColumnTriggerName}) FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnMisfireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {AdoConstants.ColumnNextFireTime} <= @nextFireTime AND {AdoConstants.ColumnTriggerState} = @state");

    /// <summary>
    /// Sentinel stored in PREFERRED_NODE to request auto-pin that has not yet been claimed by
    /// any node. Distinct from a node name, and never itself flagged as auto-claimed.
    /// </summary>
    public const string AutoPinSentinel = PreferredNode.AutoSentinel;

    // Preferred node (node affinity) acquisition filter.
    //
    // @instanceId matches pins to this node (explicit or auto-claimed — both store the bare name).
    // @autoPinSentinel matches the "*" sentinel (auto-pin requested but unclaimed).
    // The final disjunct releases a trigger whose owning node is no longer checking in, using a
    // checkin-time-aware subquery against SCHEDULER_STATE. LAST_CHECKIN_TIME is stored in ticks and
    // CHECKIN_INTERVAL in milliseconds (10000 ticks per ms); @liveNodeCutoff is
    // (now - ClusterCheckinMisfireThreshold), bound through GetDbDateTimeValue. Ticks and
    // milliseconds are the schema contract - the converters are not overridable - so this
    // arithmetic is always right.
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
        Invariant($@"AND (t.{AdoConstants.ColumnPreferredNode} IS NULL OR t.{AdoConstants.ColumnPreferredNode} = @instanceId OR t.{AdoConstants.ColumnPreferredNode} = @autoPinSentinel
                     OR t.{AdoConstants.ColumnPreferredNode} NOT IN (SELECT ss.{AdoConstants.ColumnInstanceName} FROM {TablePrefixSubst}{AdoConstants.TableSchedulerState} ss WHERE ss.{AdoConstants.ColumnSchedulerName} = t.{AdoConstants.ColumnSchedulerName} AND ss.{AdoConstants.ColumnLastCheckinTime} + ss.{AdoConstants.ColumnCheckinInterval} * 10000 >= @liveNodeCutoff))");

    // The one acquisition statement. Everything a caller can vary about it is the job-type exclusion
    // clause, which is empty when nothing is excluded - so SqlSelectNextTriggerToAcquire below is
    // literally the no-exclusion case of this template rather than a second copy kept in step by eye,
    // and a change to the projection, the join, the predicates or the ORDER BY is a change in one
    // place. The clause carries its own leading newline and indent, so passing an empty one yields
    // the statement byte for byte as it was before there was a clause to pass. The dialect's row
    // limit is the same idea: two named slots and an enclosing SELECT, all three empty by default.
    //
    // PREFERRED_NODE is filtered entirely in PreferredNodeWhereClause and is not projected —
    // acquisition never reads it from the result (the trigger is reloaded via GetTrigger).
    //
    // NEXT_FIRE_TIME > @noEarlierThan is the exact complement of the misfire predicate in
    // SqlCountMisfiredTriggersInStates, and has to stay that way: @noEarlierThan is the very
    // now - MisfireThreshold the sweep asks about, so a waiting trigger belongs either to acquisition
    // or to the misfire handler and never to both. Acquiring one the store already counts as misfired
    // would fire it late without ever applying the policy it asked for.
    private static string SelectNextTriggerToAcquire(string exclusionClause, SqlRowLimit rowLimit) =>
        rowLimit.Enclose(Invariant($@"SELECT{rowLimit.AfterSelect}
                t.{AdoConstants.ColumnTriggerName}, t.{AdoConstants.ColumnTriggerGroup}, jd.{AdoConstants.ColumnJobClass}, t.{AdoConstants.ColumnExecutionGroup}
              FROM
                {TablePrefixSubst}{AdoConstants.TableTriggers} t
              JOIN
                {TablePrefixSubst}{AdoConstants.TableJobDetails} jd ON (jd.{AdoConstants.ColumnSchedulerName} = t.{AdoConstants.ColumnSchedulerName} AND  jd.{AdoConstants.ColumnJobGroup} = t.{AdoConstants.ColumnJobGroup} AND jd.{AdoConstants.ColumnJobName} = t.{AdoConstants.ColumnJobName})
              WHERE
                t.{AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerState} = @state AND {AdoConstants.ColumnNextFireTime} <= @noLaterThan AND ({AdoConstants.ColumnMisfireInstruction} = -1 OR ({AdoConstants.ColumnMisfireInstruction} <> -1 AND {AdoConstants.ColumnNextFireTime} > @noEarlierThan))
                {PreferredNodeWhereClause}{exclusionClause}
              ORDER BY
                {AdoConstants.ColumnNextFireTime} ASC, {AdoConstants.ColumnPriority} DESC{rowLimit.AtEnd}"));

    public static readonly string SqlSelectNextTriggerToAcquire = SelectNextTriggerToAcquire(exclusionClause: "", SqlRowLimit.Unlimited);

    /// <summary>
    /// Exclusion counts are rounded up and padded to one of these sizes, limiting the acquisition
    /// query shapes seen by the database plan cache.
    /// </summary>
    private static readonly int[] excludedJobTypeBuckets = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512, JobTypeExclusions.MaxNames];

    // Unsynchronized on purpose: two threads racing here just build the same string twice and one
    // reference assignment wins, which costs nothing and cannot produce a wrong value.
    private static readonly string?[] sqlSelectNextTriggerToAcquireByExcludedJobTypeBucket = new string?[excludedJobTypeBuckets.Length];

    /// <summary>
    /// Rounds an exclusion count up to the next query bucket. Zero means no exclusion.
    /// </summary>
    internal static int RoundUpExcludedJobTypeCount(int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        foreach (int bucket in excludedJobTypeBuckets)
        {
            if (count <= bucket)
            {
                return bucket;
            }
        }

        Throw.ArgumentOutOfRangeException(nameof(count), $"Excluded job type count must not exceed {JobTypeExclusions.MaxNames}");
        return default;
    }

    /// <summary>
    /// Builds the trigger-acquisition query for an already-rounded job-type exclusion bucket and the
    /// dialect's row limit.
    /// </summary>
    /// <remarks>
    /// Only the unlimited statements are cached here. A limited one is a pure function of the same
    /// two inputs, but <c>StdAdoDelegate</c> already remembers the finished statement against the
    /// acquisition shape it was built for, so caching it twice would buy nothing.
    /// </remarks>
    internal static string BuildSqlSelectNextTriggerToAcquire(int excludedJobTypeBucket, SqlRowLimit rowLimit)
    {
        if (excludedJobTypeBucket == 0)
        {
            return rowLimit == SqlRowLimit.Unlimited
                ? SqlSelectNextTriggerToAcquire
                : SelectNextTriggerToAcquire(exclusionClause: "", rowLimit);
        }

        int bucketIndex = Array.IndexOf(excludedJobTypeBuckets, excludedJobTypeBucket);
        if (bucketIndex < 0)
        {
            Throw.ArgumentOutOfRangeException(nameof(excludedJobTypeBucket), "Excluded job type count must be rounded to a query bucket first");
        }

        if (rowLimit != SqlRowLimit.Unlimited)
        {
            return SelectNextTriggerToAcquire(ExcludedJobTypeWhereClause(excludedJobTypeBucket), rowLimit);
        }

        string? cached = sqlSelectNextTriggerToAcquireByExcludedJobTypeBucket[bucketIndex];
        if (cached is not null)
        {
            return cached;
        }

        string predicateSql = SelectNextTriggerToAcquire(ExcludedJobTypeWhereClause(excludedJobTypeBucket), rowLimit);

        sqlSelectNextTriggerToAcquireByExcludedJobTypeBucket[bucketIndex] = predicateSql;
        return predicateSql;
    }

    /// <summary>
    /// The exclusion clause the acquisition template splices in, on a line of its own and indented to
    /// match the predicate above it. The parameter names are fixed-width so that no name is a prefix
    /// of another — see the remarks above <see cref="PreferredNodeWhereClause" />.
    /// </summary>
    private static string ExcludedJobTypeWhereClause(int excludedJobTypeBucket)
    {
        StringBuilder clause = new StringBuilder(ClauseSeparator)
            .Append("AND jd.")
            .Append(AdoConstants.ColumnJobClass)
            .Append(" NOT IN (");

        for (int i = 0; i < excludedJobTypeBucket; i++)
        {
            if (i > 0)
            {
                clause.Append(", ");
            }

            clause.Append('@').Append(ExcludedJobTypeParameter(i));
        }

        return clause.Append(')').ToString();
    }

    /// <summary>
    /// What separates one optional WHERE clause of the acquisition statement from the next: a line
    /// break and the indent the template's own predicates sit at.
    /// </summary>
    private const string ClauseSeparator = "\n                ";

    internal static string ExcludedJobTypeParameter(int index) =>
        "excludedJobType" + index.ToString("D4", CultureInfo.InvariantCulture);

    public static readonly string SqlSelectNumTriggersForJob =
        Invariant($"SELECT COUNT({AdoConstants.ColumnTriggerName}) FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectPausedTriggerGroup =
        Invariant($"SELECT {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TablePausedTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectPausedJobGroup =
        Invariant($"SELECT {AdoConstants.ColumnJobGroup} FROM {TablePrefixSubst}{AdoConstants.TablePausedJobs} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectReferencedCalendar =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectSchedulerState =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableSchedulerState} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnInstanceName} = @instanceName");

    public static readonly string SqlSelectSchedulerStates =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableSchedulerState} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectSimpleTrigger =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableSimpleTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Column list shared by <see cref="SqlSelectTrigger" /> and <see cref="SqlSelectMisfiredTriggersToRecover" />,
    /// so the single-trigger and batch read paths cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Ordinals matter here: <c>ReadMapFromReader(rs, 11)</c> reads JOB_DATA positionally. Append new
    /// columns to the end of this list, never insert into the middle.
    /// </remarks>
    private const string TriggerSelectColumns = $@"
                {AdoConstants.ColumnJobName},
                {AdoConstants.ColumnJobGroup},
                {AdoConstants.ColumnDescription},
                {AdoConstants.ColumnNextFireTime},
                {AdoConstants.ColumnPreviousFireTime},
                {AdoConstants.ColumnTriggerType},
                {AdoConstants.ColumnStartTime},
                {AdoConstants.ColumnEndTime},
                {AdoConstants.ColumnCalendarName},
                {AdoConstants.ColumnMisfireInstruction},
                {AdoConstants.ColumnPriority},
                {AdoConstants.ColumnJobDataMap},
                {AdoConstants.ColumnCronExpression},
                {AdoConstants.ColumnTimeZoneId},
                {AdoConstants.ColumnRepeatCount},
                {AdoConstants.ColumnRepeatInterval},
                {AdoConstants.ColumnTimesTriggered},
                t.{AdoConstants.ColumnMisfireOriginalFireTime},
                t.{AdoConstants.ColumnExecutionGroup},
                t.{AdoConstants.ColumnPreferredNode},
                t.{AdoConstants.ColumnPreferredNodeAuto}";

    /// <summary>
    /// FROM clause that left-joins the SIMPLE and CRON type tables onto TRIGGERS, letting the two most
    /// common trigger types be materialized from a single row without a follow-up query.
    /// </summary>
    private const string TriggerSelectFastPathFrom = $@"
            FROM
                {TablePrefixSubst}{AdoConstants.TableTriggers} t
            LEFT JOIN
                {TablePrefixSubst}{AdoConstants.TableSimpleTriggers} st ON (st.{AdoConstants.ColumnSchedulerName} = t.{AdoConstants.ColumnSchedulerName} AND st.{AdoConstants.ColumnTriggerGroup} = t.{AdoConstants.ColumnTriggerGroup} AND st.{AdoConstants.ColumnTriggerName} = t.{AdoConstants.ColumnTriggerName})
            LEFT JOIN
                {TablePrefixSubst}{AdoConstants.TableCronTriggers} ct ON (ct.{AdoConstants.ColumnSchedulerName} = t.{AdoConstants.ColumnSchedulerName} AND ct.{AdoConstants.ColumnTriggerGroup} = t.{AdoConstants.ColumnTriggerGroup} AND ct.{AdoConstants.ColumnTriggerName} = t.{AdoConstants.ColumnTriggerName})";

    public static readonly string SqlSelectTrigger =
        Invariant($@"SELECT {TriggerSelectColumns}{TriggerSelectFastPathFrom}
            WHERE
                t.{AdoConstants.ColumnSchedulerName} = @schedulerName AND t.{AdoConstants.ColumnTriggerName} = @triggerName AND t.{AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Selects the misfired triggers to recover as fully populated rows, so a whole misfire recovery
    /// batch costs one round-trip instead of one per trigger. Same predicate as
    /// <see cref="SqlCountMisfiredTriggersInStates" />, same columns as <see cref="SqlSelectTrigger" />
    /// with the key columns appended (they are ambiguous across the joined tables, hence the alias).
    /// </summary>
    /// <remarks>
    /// The dialect's row limit goes in one of the two slots, or around the whole statement; a dialect
    /// that has no way to limit rows leaves all three empty and gets the statement unchanged.
    /// </remarks>
    private static string SelectMisfiredTriggersToRecover(SqlRowLimit rowLimit) =>
        rowLimit.Enclose(Invariant($@"SELECT{rowLimit.AfterSelect} {TriggerSelectColumns},
                t.{AdoConstants.ColumnTriggerName},
                t.{AdoConstants.ColumnTriggerGroup}{TriggerSelectFastPathFrom}
            WHERE
                t.{AdoConstants.ColumnSchedulerName} = @schedulerName AND t.{AdoConstants.ColumnMisfireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND t.{AdoConstants.ColumnNextFireTime} <= @nextFireTime AND t.{AdoConstants.ColumnTriggerState} = @state
            ORDER BY t.{AdoConstants.ColumnNextFireTime} ASC, t.{AdoConstants.ColumnPriority} DESC{rowLimit.AtEnd}"));

    /// <inheritdoc cref="SelectMisfiredTriggersToRecover" />
    public static readonly string SqlSelectMisfiredTriggersToRecover = SelectMisfiredTriggersToRecover(SqlRowLimit.Unlimited);

    /// <summary>
    /// Builds the misfire recovery statement for the dialect's row limit, which is the only thing
    /// that varies about it. <c>StdAdoDelegate</c> remembers the result against the batch size it was
    /// built for.
    /// </summary>
    internal static string BuildSqlSelectMisfiredTriggersToRecover(SqlRowLimit rowLimit) =>
        rowLimit == SqlRowLimit.Unlimited
            ? SqlSelectMisfiredTriggersToRecover
            : SelectMisfiredTriggersToRecover(rowLimit);

    /// <summary>
    /// Prefix of the batch trigger lookup; the caller appends a key-set predicate built by
    /// <c>AdoUtil.BuildTriggerKeyPredicate</c> with qualified key columns. Same columns as
    /// <see cref="SqlSelectTrigger" /> with the key columns appended (they are ambiguous across the
    /// joined tables, hence the alias).
    /// </summary>
    public static readonly string SqlSelectTriggersByKeysPrefix =
        Invariant($@"SELECT {TriggerSelectColumns},
                t.{AdoConstants.ColumnTriggerName},
                t.{AdoConstants.ColumnTriggerGroup}{TriggerSelectFastPathFrom}
            WHERE
                t.{AdoConstants.ColumnSchedulerName} = @schedulerName AND ");

    /// <summary>
    /// Prefix of the batch simple-trigger lookup; the caller appends a key-set predicate built by
    /// <c>AdoUtil.BuildTriggerKeyPredicate</c>.
    /// </summary>
    public static readonly string SqlSelectSimpleTriggersByKeysPrefix =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableSimpleTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND ");

    /// <summary>
    /// Prefix of the batch cron-trigger lookup; the caller appends a key-set predicate built by
    /// <c>AdoUtil.BuildTriggerKeyPredicate</c>.
    /// </summary>
    public static readonly string SqlSelectCronTriggersByKeysPrefix =
        Invariant($"SELECT * FROM {TablePrefixSubst}{AdoConstants.TableCronTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND ");

    public static readonly string SqlSelectTriggerData =
        Invariant($"SELECT {AdoConstants.ColumnJobDataMap} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggerExistence =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggerGroupsEquals =
        Invariant($"SELECT DISTINCT({AdoConstants.ColumnTriggerGroup}) FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggerGroupsLike =
        Invariant($"SELECT DISTINCT({AdoConstants.ColumnTriggerGroup}) FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} LIKE @triggerGroup{SqlLikeEscapeClause}");

    public static readonly string SqlSelectTriggerState =
        Invariant($"SELECT {AdoConstants.ColumnTriggerState} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Whether the trigger of the surrounding TRIGGERS row has an execution in flight. EXECUTING is only
    /// ever a FIRED_TRIGGERS state, never a TRIGGER_STATE, so this is the only way to establish it.
    /// </summary>
    /// <remarks>
    /// Correlates on the trigger table's full name rather than an alias, so that statements embedding it
    /// keep the shape callers append their own predicates to. The state value is embedded rather than
    /// passed as a parameter because a listing mentions this fragment twice in one statement — once in
    /// the projection, once in the state filter — and <see cref="AdoUtil" /> rewrites parameters by plain
    /// substring replace, which cannot cope with the same name occurring twice. The embedded value is a
    /// compile-time constant, not input.
    /// </remarks>
    internal static readonly string SqlExecutingFiredTriggerExists =
        Invariant($"EXISTS (SELECT 1 FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} FT WHERE FT.{AdoConstants.ColumnSchedulerName} = {TablePrefixSubst}{AdoConstants.TableTriggers}.{AdoConstants.ColumnSchedulerName} AND FT.{AdoConstants.ColumnTriggerName} = {TablePrefixSubst}{AdoConstants.TableTriggers}.{AdoConstants.ColumnTriggerName} AND FT.{AdoConstants.ColumnTriggerGroup} = {TablePrefixSubst}{AdoConstants.TableTriggers}.{AdoConstants.ColumnTriggerGroup} AND FT.{AdoConstants.ColumnEntryState} = '{AdoConstants.StateExecuting}')");

    public static readonly string SqlSelectTriggerStateWithExecuting =
        Invariant($"SELECT {AdoConstants.ColumnTriggerState}, CASE WHEN {SqlExecutingFiredTriggerExists} THEN 1 ELSE 0 END FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    /// <summary>
    /// Carries TRIGGER_TYPE alongside the state so that the fire path learns in one read what it used to
    /// learn in three: whether the row exists, what state it is in, and which type table holds its
    /// schedule. All four columns come off the same row, so the extra ones are free.
    /// </summary>
    public static readonly string SqlSelectTriggerHeader =
        Invariant($"SELECT {AdoConstants.ColumnTriggerState}, {AdoConstants.ColumnNextFireTime}, {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnTriggerType} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggersForCalendar =
        Invariant($"SELECT {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnCalendarName} = @calendarName");

    public static readonly string SqlSelectTriggersForJob =
        Invariant($"SELECT {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlSelectTriggersForJobInState =
        Invariant($"{SqlSelectTriggersForJob} AND {AdoConstants.ColumnTriggerState} = @state");

    public static readonly string SqlSelectTriggersInGroupLike =
        Invariant($"SELECT {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} LIKE @triggerGroup{SqlLikeEscapeClause}");

    public static readonly string SqlSelectTriggersInGroup =
        Invariant($"SELECT {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlSelectTriggersInState =
        Invariant($"SELECT {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerState} = @state");

    public static readonly string SqlSelectTriggerType =
        Invariant($"SELECT {AdoConstants.ColumnTriggerType} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    // PAGED LISTINGS
    //
    // Each listing is composed as: statement + optional predicates + ORDER BY + the dialect's paging
    // clause. The ORDER BY is part of the generated statement rather than something the caller adds,
    // because a page without one is not deterministic (see QTZ-413). The optional predicates append
    // to the count statement too, so a total count sees exactly the same WHERE.

    public static readonly string SqlSelectJobHeaders =
        Invariant($"SELECT {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnDescription}, {AdoConstants.ColumnJobClass}, {AdoConstants.ColumnIsDurable}, {AdoConstants.ColumnIsNonConcurrent}, {AdoConstants.ColumnIsUpdateData}, {AdoConstants.ColumnRequestsRecovery} FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCountJobHeaders =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlJobGroupEqualsPredicate = Invariant($" AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlJobGroupLikePredicate = Invariant($" AND {AdoConstants.ColumnJobGroup} LIKE @jobGroup{SqlLikeEscapeClause}");

    public static readonly string SqlJobNameEqualsPredicate = Invariant($" AND {AdoConstants.ColumnJobName} = @jobName");

    public static readonly string SqlJobNameLikePredicate = Invariant($" AND {AdoConstants.ColumnJobName} LIKE @jobName{SqlLikeEscapeClause}");

    public static readonly string SqlOrderByJobGroupAndName = Invariant($" ORDER BY {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnJobName}");

    public static readonly string SqlSelectTriggerHeaders =
        Invariant($"SELECT {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnDescription}, {AdoConstants.ColumnTriggerType}, {AdoConstants.ColumnTriggerState}, {AdoConstants.ColumnStartTime}, {AdoConstants.ColumnEndTime}, {AdoConstants.ColumnNextFireTime}, {AdoConstants.ColumnPreviousFireTime}, {AdoConstants.ColumnCalendarName}, {AdoConstants.ColumnPriority}, {AdoConstants.ColumnExecutionGroup}, CASE WHEN {SqlExecutingFiredTriggerExists} THEN 1 ELSE 0 END FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCountTriggerHeaders =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TableTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlTriggerGroupEqualsPredicate = Invariant($" AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlTriggerGroupLikePredicate = Invariant($" AND {AdoConstants.ColumnTriggerGroup} LIKE @triggerGroup{SqlLikeEscapeClause}");

    public static readonly string SqlTriggerNameEqualsPredicate = Invariant($" AND {AdoConstants.ColumnTriggerName} = @triggerName");

    public static readonly string SqlTriggerNameLikePredicate = Invariant($" AND {AdoConstants.ColumnTriggerName} LIKE @triggerName{SqlLikeEscapeClause}");

    public static readonly string SqlTriggerJobPredicate = Invariant($" AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlTriggerCalendarPredicate = Invariant($" AND {AdoConstants.ColumnCalendarName} = @calendarName");

    /// <summary>
    /// Opening of the trigger state filter; the caller appends <c>@state0[, @state1...])</c> for the
    /// internal states the requested <see cref="TriggerState" /> maps to.
    /// </summary>
    public static readonly string SqlTriggerStateInPredicateStart = Invariant($" AND {AdoConstants.ColumnTriggerState} IN (");

    /// <summary>
    /// Opening of the negated trigger state filter, for the state an unrecognised stored value reports
    /// as: the values it has to cover cannot be listed, so the others are excluded instead.
    /// </summary>
    public static readonly string SqlTriggerStateNotInPredicateStart = Invariant($" AND {AdoConstants.ColumnTriggerState} NOT IN (");

    /// <summary>
    /// Narrows a state filter to triggers that are currently executing.
    /// </summary>
    public static readonly string SqlTriggerExecutingPredicate = Invariant($" AND {SqlExecutingFiredTriggerExists}");

    /// <summary>
    /// Narrows a state filter to triggers that are not currently executing, so that a listing filtered by
    /// a state which executing outranks does not return rows it would then report as executing.
    /// </summary>
    public static readonly string SqlTriggerNotExecutingPredicate = Invariant($" AND NOT {SqlExecutingFiredTriggerExists}");

    public static readonly string SqlOrderByTriggerGroupAndName = Invariant($" ORDER BY {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnTriggerName}");

    // Correlates on j.SCHED_NAME instead of reusing @schedulerName, for the same reason
    // PausedTriggerGroupExists does: a named parameter referenced twice produces more placeholders
    // than bound parameters on providers that adapt named parameters positionally.
    private static readonly string PausedJobGroupExists =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{AdoConstants.TablePausedJobs} pg WHERE pg.{AdoConstants.ColumnSchedulerName} = j.{AdoConstants.ColumnSchedulerName} AND pg.{AdoConstants.ColumnJobGroup} = j.{AdoConstants.ColumnJobGroup}");

    public static readonly string SqlSelectJobGroupsWithPausedFlag =
        Invariant($"SELECT DISTINCT j.{AdoConstants.ColumnJobGroup}, CASE WHEN EXISTS ({PausedJobGroupExists}) THEN 1 ELSE 0 END AS IS_PAUSED FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} j WHERE j.{AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCountJobGroups =
        Invariant($"SELECT COUNT(DISTINCT j.{AdoConstants.ColumnJobGroup}) FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} j WHERE j.{AdoConstants.ColumnSchedulerName} = @schedulerName");

    /// <summary>
    /// The paused job group listing, read straight from PAUSED_JOB_GRPS so that a group paused while
    /// it holds no jobs is still reported. Unlike the trigger listing it needs no exclusion for
    /// <see cref="AdoConstants.AllGroupsPaused" />: pause-all is a trigger operation and never writes
    /// a marker row here.
    /// </summary>
    public static readonly string SqlSelectPausedJobGroups =
        Invariant($"SELECT {AdoConstants.ColumnJobGroup} FROM {TablePrefixSubst}{AdoConstants.TablePausedJobs} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCountPausedJobGroups =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TablePausedJobs} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlSelectUnpausedJobGroups =
        Invariant($"SELECT DISTINCT j.{AdoConstants.ColumnJobGroup} FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} j WHERE j.{AdoConstants.ColumnSchedulerName} = @schedulerName AND NOT EXISTS ({PausedJobGroupExists})");

    public static readonly string SqlCountUnpausedJobGroups =
        Invariant($"SELECT COUNT(DISTINCT j.{AdoConstants.ColumnJobGroup}) FROM {TablePrefixSubst}{AdoConstants.TableJobDetails} j WHERE j.{AdoConstants.ColumnSchedulerName} = @schedulerName AND NOT EXISTS ({PausedJobGroupExists})");

    /// <summary>
    /// Exact-name filter for the job group listing read straight from PAUSED_JOB_GRPS.
    /// </summary>
    public static readonly string SqlJobGroupNamePredicate = Invariant($" AND {AdoConstants.ColumnJobGroup} = @groupName");

    /// <summary>
    /// Exact-name filter for the job group listings that read from JOB_DETAILS under the alias 'j'.
    /// </summary>
    public static readonly string SqlAliasedJobGroupNamePredicate = Invariant($" AND j.{AdoConstants.ColumnJobGroup} = @groupName");

    public static readonly string SqlOrderByJobGroup = Invariant($" ORDER BY {AdoConstants.ColumnJobGroup}");

    public static readonly string SqlOrderByAliasedJobGroup = Invariant($" ORDER BY j.{AdoConstants.ColumnJobGroup}");

    // Correlates on t.SCHED_NAME instead of reusing @schedulerName: each named parameter must be
    // referenced exactly once in the statement, because providers with bindByName=false adapt named
    // parameters positionally and a reused name would produce more placeholders than bound parameters.
    private static readonly string PausedTriggerGroupExists =
        Invariant($"SELECT 1 FROM {TablePrefixSubst}{AdoConstants.TablePausedTriggers} pg WHERE pg.{AdoConstants.ColumnSchedulerName} = t.{AdoConstants.ColumnSchedulerName} AND pg.{AdoConstants.ColumnTriggerGroup} = t.{AdoConstants.ColumnTriggerGroup}");

    public static readonly string SqlSelectTriggerGroupsWithPausedFlag =
        Invariant($"SELECT DISTINCT t.{AdoConstants.ColumnTriggerGroup}, CASE WHEN EXISTS ({PausedTriggerGroupExists}) THEN 1 ELSE 0 END AS IS_PAUSED FROM {TablePrefixSubst}{AdoConstants.TableTriggers} t WHERE t.{AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCountTriggerGroups =
        Invariant($"SELECT COUNT(DISTINCT t.{AdoConstants.ColumnTriggerGroup}) FROM {TablePrefixSubst}{AdoConstants.TableTriggers} t WHERE t.{AdoConstants.ColumnSchedulerName} = @schedulerName");

    /// <summary>
    /// Excludes the "everything is paused" marker <see cref="AdoConstants.AllGroupsPaused" /> from a
    /// listing. Only the listings that read PAUSED_TRIGGER_GRPS need it: the marker is a row in that
    /// table but no trigger belongs to it, so it is not a group and must not be reported as one. The
    /// other group listings read TRIGGERS, where it cannot appear.
    /// </summary>
    private static readonly string ExceptAllGroupsPausedMarker =
        Invariant($" AND {AdoConstants.ColumnTriggerGroup} <> '{AdoConstants.AllGroupsPaused}'");

    public static readonly string SqlSelectPausedTriggerGroups =
        Invariant($"SELECT {AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TablePausedTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName{ExceptAllGroupsPausedMarker}");

    public static readonly string SqlCountPausedTriggerGroups =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TablePausedTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName{ExceptAllGroupsPausedMarker}");

    public static readonly string SqlSelectUnpausedTriggerGroups =
        Invariant($"SELECT DISTINCT t.{AdoConstants.ColumnTriggerGroup} FROM {TablePrefixSubst}{AdoConstants.TableTriggers} t WHERE t.{AdoConstants.ColumnSchedulerName} = @schedulerName AND NOT EXISTS ({PausedTriggerGroupExists})");

    public static readonly string SqlCountUnpausedTriggerGroups =
        Invariant($"SELECT COUNT(DISTINCT t.{AdoConstants.ColumnTriggerGroup}) FROM {TablePrefixSubst}{AdoConstants.TableTriggers} t WHERE t.{AdoConstants.ColumnSchedulerName} = @schedulerName AND NOT EXISTS ({PausedTriggerGroupExists})");

    /// <summary>
    /// Exact-name filter for the trigger group listing read straight from PAUSED_TRIGGER_GRPS.
    /// </summary>
    public static readonly string SqlTriggerGroupNamePredicate = Invariant($" AND {AdoConstants.ColumnTriggerGroup} = @groupName");

    /// <summary>
    /// Exact-name filter for the trigger group listings that read from TRIGGERS under the alias 't'.
    /// </summary>
    public static readonly string SqlAliasedTriggerGroupNamePredicate = Invariant($" AND t.{AdoConstants.ColumnTriggerGroup} = @groupName");

    public static readonly string SqlOrderByTriggerGroup = Invariant($" ORDER BY {AdoConstants.ColumnTriggerGroup}");

    public static readonly string SqlOrderByAliasedTriggerGroup = Invariant($" ORDER BY t.{AdoConstants.ColumnTriggerGroup}");

    public static readonly string SqlSelectCalendarNames =
        Invariant($"SELECT {AdoConstants.ColumnCalendarName} FROM {TablePrefixSubst}{AdoConstants.TableCalendars} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCountCalendarNames =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TableCalendars} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCalendarNameEqualsPredicate = Invariant($" AND {AdoConstants.ColumnCalendarName} = @calendarName");

    public static readonly string SqlCalendarNameLikePredicate = Invariant($" AND {AdoConstants.ColumnCalendarName} LIKE @calendarName{SqlLikeEscapeClause}");

    public static readonly string SqlOrderByCalendarName = Invariant($" ORDER BY {AdoConstants.ColumnCalendarName}");

    /// <summary>
    /// The fire-instance listing: one page of FIRED_TRIGGERS rows, projected in the order
    /// <c>ReadFireInstance</c> reads them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SqlSelectFiredTriggers" />, which is deliberately unpaged and selects
    /// every column for the recovery passes. This one names its columns, because a page has to be read
    /// positionally, and carries the ORDER BY that paging requires.
    /// </remarks>
    public static readonly string SqlSelectFireInstances =
        Invariant($"SELECT {AdoConstants.ColumnEntryId}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnJobName}, {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnInstanceName}, {AdoConstants.ColumnEntryState}, {AdoConstants.ColumnFiredTime}, {AdoConstants.ColumnScheduledTime}, {AdoConstants.ColumnExecutionGroup} FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    public static readonly string SqlCountFireInstances =
        Invariant($"SELECT COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName");

    /// <summary>
    /// The cluster's in-flight work per execution group, which is what a cluster-scoped execution limit
    /// is counted against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fired-triggers table is already the cluster's reservation ledger — a row appears when a node
    /// acquires a trigger, turns into the execution, and is deleted on completion or by cluster
    /// recovery — so the ceiling needs no table, column or migration of its own. Every state counts:
    /// an ACQUIRED row is a reservation another node has taken and is a slot that is genuinely spoken
    /// for.
    /// </para>
    /// <para>
    /// The grouping is by both columns rather than by EXECUTION_GROUP alone because
    /// <see cref="ExecutionLimits.UsesTriggerGroupWhenUnset" /> lets the trigger group stand in for an
    /// absent execution group; folding the pair down to one key is done in C#, by the same
    /// <c>ResolveGroupKey</c> the acquisition filter uses, which is why this needs no dialect variants.
    /// Row count is the number of distinct pairs in flight — tens, not thousands.
    /// </para>
    /// <para>
    /// Deliberately not narrowed to nodes that are still checking in. A node that has missed a check-in
    /// but is still running jobs would stop counting, which would let the cluster exceed the ceiling;
    /// counting its rows until recovery deletes them under-serves the quota instead, and that is the
    /// direction a quota should err in.
    /// </para>
    /// <para>
    /// No index leads with EXECUTION_GROUP, and <c>ExecutionCeilingBenchmark</c> is why (#3364): up to
    /// about a thousand rows in flight this statement costs a round trip and almost nothing else, so a
    /// covering index buys nothing measurable, and it would be a write cost on a table every firing
    /// inserts into and deletes from. Past that the scan does show, and the tuning note in
    /// <c>execution-groups.md</c> carries the index for the deployments that get there.
    /// </para>
    /// </remarks>
    public static readonly string SqlSelectExecutionGroupsInFlight =
        Invariant($"SELECT {AdoConstants.ColumnExecutionGroup}, {AdoConstants.ColumnTriggerGroup}, COUNT(*) FROM {TablePrefixSubst}{AdoConstants.TableFiredTriggers} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName GROUP BY {AdoConstants.ColumnExecutionGroup}, {AdoConstants.ColumnTriggerGroup}");

    public static readonly string SqlFireInstanceTriggerGroupEqualsPredicate = Invariant($" AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlFireInstanceTriggerGroupLikePredicate = Invariant($" AND {AdoConstants.ColumnTriggerGroup} LIKE @triggerGroup{SqlLikeEscapeClause}");

    public static readonly string SqlFireInstanceTriggerNameEqualsPredicate = Invariant($" AND {AdoConstants.ColumnTriggerName} = @triggerName");

    public static readonly string SqlFireInstanceTriggerNameLikePredicate = Invariant($" AND {AdoConstants.ColumnTriggerName} LIKE @triggerName{SqlLikeEscapeClause}");

    public static readonly string SqlFireInstanceJobPredicate = Invariant($" AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlFireInstanceInstancePredicate = Invariant($" AND {AdoConstants.ColumnInstanceName} = @instanceName");

    /// <summary>
    /// The state filter. A fire instance is either reserved or running, so the two
    /// <see cref="Quartz.FireInstanceState" /> members map onto stored states rather than onto one
    /// column value each: everything that is not ACQUIRED is an execution the store has started.
    /// </summary>
    public static readonly string SqlFireInstanceStateEqualsPredicate = Invariant($" AND {AdoConstants.ColumnEntryState} = @entryState");

    /// <inheritdoc cref="SqlFireInstanceStateEqualsPredicate" />
    public static readonly string SqlFireInstanceStateNotEqualsPredicate = Invariant($" AND {AdoConstants.ColumnEntryState} <> @entryState");

    /// <summary>
    /// Trigger group and name are not unique across fire instances — one trigger can have several
    /// firings at once — so the entry id is the tiebreaker that makes a page deterministic.
    /// </summary>
    public static readonly string SqlOrderByFireInstance =
        Invariant($" ORDER BY {AdoConstants.ColumnTriggerGroup}, {AdoConstants.ColumnTriggerName}, {AdoConstants.ColumnEntryId}");

    // UPDATE

    public static readonly string SqlUpdateBlobTrigger =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableBlobTriggers} SET {AdoConstants.ColumnBlob} = @blob WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateCalendar =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableCalendars} SET {AdoConstants.ColumnCalendar} = @calendar WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnCalendarName} = @calendarName");

    public static readonly string SqlUpdateCronTrigger =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableCronTriggers} SET {AdoConstants.ColumnCronExpression} = @triggerCronExpression, {AdoConstants.ColumnTimeZoneId} = @timeZoneId WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateJobData =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableJobDetails} SET {AdoConstants.ColumnJobDataMap} = @jobDataMap WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlUpdateJobDetail =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableJobDetails} SET {AdoConstants.ColumnDescription} = @jobDescription, {AdoConstants.ColumnJobClass} = @jobType, {AdoConstants.ColumnIsDurable} = @jobDurable, {AdoConstants.ColumnIsNonConcurrent} = @jobVolatile, {AdoConstants.ColumnIsUpdateData} = @jobStateful, {AdoConstants.ColumnRequestsRecovery} = @jobRequestsRecovery, {AdoConstants.ColumnJobDataMap} = @jobDataMap  WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlUpdateJobTriggerStates =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @state WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup");

    public static readonly string SqlUpdateJobTriggerStatesFromOtherState =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @state WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnJobName} = @jobName AND {AdoConstants.ColumnJobGroup} = @jobGroup AND {AdoConstants.ColumnTriggerState} = @oldState");

    public static readonly string SqlUpdateSchedulerState =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableSchedulerState} SET {AdoConstants.ColumnLastCheckinTime} = @lastCheckinTime WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnInstanceName} = @instanceName");

    public static readonly string SqlUpdateSimpleTrigger =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableSimpleTriggers} SET {AdoConstants.ColumnRepeatCount} = @triggerRepeatCount, {AdoConstants.ColumnRepeatInterval} = @triggerRepeatInterval, {AdoConstants.ColumnTimesTriggered} = @triggerTimesTriggered WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    // The preferred node columns are only written when the pin was actually changed on the trigger
    // instance, so each UPDATE comes in two flavours. Writing the pin back unconditionally would
    // clobber concurrent changes (ClusterRecover's failover reset, an UpdateTriggerDetails re-pin)
    // with the value that happened to be loaded at acquire time. Folding the columns into the main
    // UPDATE — rather than issuing a second statement — keeps it to one round-trip either way.
    private const string PreferredNodeSetClause =
        $", {AdoConstants.ColumnPreferredNode} = @triggerPreferredNode, {AdoConstants.ColumnPreferredNodeAuto} = @triggerPreferredNodeAuto";

    public static readonly string SqlUpdateTrigger =
        Invariant($@"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnJobName} = @triggerJobName, {AdoConstants.ColumnJobGroup} = @triggerJobGroup, {AdoConstants.ColumnDescription} = @triggerDescription, {AdoConstants.ColumnNextFireTime} = @triggerNextFireTime, {AdoConstants.ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {AdoConstants.ColumnTriggerState} = @triggerState, {AdoConstants.ColumnTriggerType} = @triggerType, {AdoConstants.ColumnStartTime} = @triggerStartTime, {AdoConstants.ColumnEndTime} = @triggerEndTime, {AdoConstants.ColumnCalendarName} = @triggerCalendarName, {AdoConstants.ColumnMisfireInstruction} = @triggerMisfireInstruction, {AdoConstants.ColumnPriority} = @triggerPriority, {AdoConstants.ColumnJobDataMap} = @triggerJobJobDataMap, {AdoConstants.ColumnExecutionGroup} = @triggerExecutionGroup
                        WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerWithPreferredNode =
        Invariant($@"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnJobName} = @triggerJobName, {AdoConstants.ColumnJobGroup} = @triggerJobGroup, {AdoConstants.ColumnDescription} = @triggerDescription, {AdoConstants.ColumnNextFireTime} = @triggerNextFireTime, {AdoConstants.ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {AdoConstants.ColumnTriggerState} = @triggerState, {AdoConstants.ColumnTriggerType} = @triggerType, {AdoConstants.ColumnStartTime} = @triggerStartTime, {AdoConstants.ColumnEndTime} = @triggerEndTime, {AdoConstants.ColumnCalendarName} = @triggerCalendarName, {AdoConstants.ColumnMisfireInstruction} = @triggerMisfireInstruction, {AdoConstants.ColumnPriority} = @triggerPriority, {AdoConstants.ColumnJobDataMap} = @triggerJobJobDataMap, {AdoConstants.ColumnExecutionGroup} = @triggerExecutionGroup{PreferredNodeSetClause}
                        WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateFiredTrigger = Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableFiredTriggers} SET {AdoConstants.ColumnInstanceName} = @instanceName, {AdoConstants.ColumnFiredTime} = @firedTime, {AdoConstants.ColumnScheduledTime} = @scheduledTime, {AdoConstants.ColumnEntryState} = @entryState, {AdoConstants.ColumnJobName} = @jobName, {AdoConstants.ColumnJobGroup} = @jobGroup, {AdoConstants.ColumnIsNonConcurrent} = @isNonConcurrent, {AdoConstants.ColumnRequestsRecovery} = @requestsRecover, {AdoConstants.ColumnExecutionGroup} = @executionGroup WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnEntryId} = @entryId");

    public static readonly string SqlUpdateTriggerGroupStateFromStateEquals =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup AND {AdoConstants.ColumnTriggerState} = @oldState");

    public static readonly string SqlUpdateTriggerGroupStateFromStateLike =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} LIKE @triggerGroup{SqlLikeEscapeClause} AND {AdoConstants.ColumnTriggerState} = @oldState");

    /// <summary>
    /// Prefix of the group state transition; the caller appends an old-state predicate built by
    /// <c>AdoUtil.BuildTriggerStatePredicate</c>.
    /// </summary>
    public static readonly string SqlUpdateTriggerGroupStateFromStatesEqualsPrefix =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} = @groupName AND ");

    /// <inheritdoc cref="SqlUpdateTriggerGroupStateFromStatesEqualsPrefix" />
    public static readonly string SqlUpdateTriggerGroupStateFromStatesLikePrefix =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerGroup} LIKE @groupName{SqlLikeEscapeClause} AND ");

    public static readonly string SqlUpdateTriggerSkipData =
        Invariant($@"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnJobName} = @triggerJobName, {AdoConstants.ColumnJobGroup} = @triggerJobGroup, {AdoConstants.ColumnDescription} = @triggerDescription, {AdoConstants.ColumnNextFireTime} = @triggerNextFireTime, {AdoConstants.ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {AdoConstants.ColumnTriggerState} = @triggerState, {AdoConstants.ColumnTriggerType} = @triggerType, {AdoConstants.ColumnStartTime} = @triggerStartTime, {AdoConstants.ColumnEndTime} = @triggerEndTime, {AdoConstants.ColumnCalendarName} = @triggerCalendarName, {AdoConstants.ColumnMisfireInstruction} = @triggerMisfireInstruction, {AdoConstants.ColumnPriority} = @triggerPriority, {AdoConstants.ColumnExecutionGroup} = @triggerExecutionGroup
                    WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerSkipDataWithPreferredNode =
        Invariant($@"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnJobName} = @triggerJobName, {AdoConstants.ColumnJobGroup} = @triggerJobGroup, {AdoConstants.ColumnDescription} = @triggerDescription, {AdoConstants.ColumnNextFireTime} = @triggerNextFireTime, {AdoConstants.ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {AdoConstants.ColumnTriggerState} = @triggerState, {AdoConstants.ColumnTriggerType} = @triggerType, {AdoConstants.ColumnStartTime} = @triggerStartTime, {AdoConstants.ColumnEndTime} = @triggerEndTime, {AdoConstants.ColumnCalendarName} = @triggerCalendarName, {AdoConstants.ColumnMisfireInstruction} = @triggerMisfireInstruction, {AdoConstants.ColumnPriority} = @triggerPriority, {AdoConstants.ColumnExecutionGroup} = @triggerExecutionGroup{PreferredNodeSetClause}
                    WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    // Compare-and-swap for the auto-pin claim/steal: only writes when the columns still hold the
    // values observed at acquisition time, so a concurrent re-pin or clear wins over the claim.
    public static readonly string SqlUpdateTriggerPreferredNodeConditional =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnPreferredNode} = @triggerPreferredNode, {AdoConstants.ColumnPreferredNodeAuto} = @triggerPreferredNodeAuto WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup AND {AdoConstants.ColumnPreferredNode} = @expectedPreferredNode AND {AdoConstants.ColumnPreferredNodeAuto} = @expectedPreferredNodeAuto");

    // Failover reset: only auto-claimed pins belonging to the dead node are released back to the
    // "*" sentinel. Explicit pins are left alone so the original node reclaims them.
    public static readonly string SqlRepinTriggersFromDeadNode =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnPreferredNode} = @newPreferredNode, {AdoConstants.ColumnPreferredNodeAuto} = @newPreferredNodeAuto WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnPreferredNode} = @oldPreferredNode AND {AdoConstants.ColumnPreferredNodeAuto} = @oldPreferredNodeAuto");

    public static readonly string SqlUpdateTriggerState =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @state WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerStateFromState =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup AND {AdoConstants.ColumnTriggerState} = @oldState");

    /// <summary>
    /// Prefix of the single-trigger state transition; the caller appends an old-state predicate built
    /// by <c>AdoUtil.BuildTriggerStatePredicate</c>.
    /// </summary>
    public static readonly string SqlUpdateTriggerStateFromStatesPrefix =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup AND ");

    public static readonly string SqlUpdateTriggerStateFromStateWithNextFireTime =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup AND {AdoConstants.ColumnTriggerState} = @oldState AND {AdoConstants.ColumnNextFireTime} = @nextFireTime");

    /// <summary>
    /// Prefix of the store-wide state transition; the caller appends an old-state predicate built by
    /// <c>AdoUtil.BuildTriggerStatePredicate</c>.
    /// </summary>
    public static readonly string SqlUpdateTriggerStatesFromOtherStatesPrefix =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = @newState WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND ");

    public static readonly string SqlUpdateMisfireOrigFireTime =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnMisfireOriginalFireTime} = @misfireOrigFireTime WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    // Targeted misfire recovery UPDATE — only touches columns that change during UpdateAfterMisfire.
    // START_TIME is included because SimpleTrigger's RescheduleNowWith* policies modify it.
    public static readonly string SqlUpdateTriggerMisfire =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnNextFireTime} = @triggerNextFireTime, {AdoConstants.ColumnPreviousFireTime} = @triggerPreviousFireTime, {AdoConstants.ColumnTriggerState} = @triggerState, {AdoConstants.ColumnStartTime} = @triggerStartTime WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

    public static readonly string SqlUpdateTriggerMisfireWithOrigFireTime =
        Invariant($"UPDATE {TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnNextFireTime} = @triggerNextFireTime, {AdoConstants.ColumnPreviousFireTime} = @triggerPreviousFireTime, {AdoConstants.ColumnTriggerState} = @triggerState, {AdoConstants.ColumnStartTime} = @triggerStartTime, {AdoConstants.ColumnMisfireOriginalFireTime} = @triggerMisfireOrigFireTime WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");
}
