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
/// The table, column and state names of the standard Quartz database schema, for
/// <see cref="IDriverDelegate" /> and <see cref="ITriggerPersistenceDelegate" /> implementations
/// that read and write it.
/// </summary>
/// <remarks>
/// These used to be inherited: a delegate declared <c>: AdoConstants</c> and referred to the names
/// unqualified. Inheritance is not how a constant container is consumed — it burns the single base
/// class of every type that wants to name a column — so this is a static class now, named explicitly
/// at the use site.
/// </remarks>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Marko Lahma(.NET)</author>
public static class AdoConstants
{
    /// <summary>
    /// Every table the store reads or writes, in the order the schema scripts create them.
    /// </summary>
    /// <remarks>
    /// This is what <see cref="IDriverDelegate.ValidateSchema" /> probes at startup, so a table
    /// missing from here is a table a database can be missing and still start — the failure moves to
    /// the first statement that names it, which is what validation exists to prevent. Every table
    /// name therefore belongs on this class, whichever delegate writes to it. <c>SchemaScriptTest</c>
    /// holds this list to what each dialect's fresh-install script creates, and
    /// <c>SchemaValidationTest</c> drops each of them from a real database in turn.
    /// </remarks>
    internal static readonly string[] AllTableNames =
    [
        TableJobDetails,
        TableTriggers,
        TableSimpleTriggers,
        TableSimplePropertiesTriggers,
        TableCronTriggers,
        TableBlobTriggers,
        TableFiredTriggers,
        TableCalendars,
        TablePausedTriggers,
        TablePausedJobs,
        TableLocks,
        TableSchedulerState
    ];

    /// <summary>
    /// Every column 4.x requires on a table 3.x already had — the columns
    /// <c>database/migrations/4.0/schema_30_to_40_upgrade_&lt;dialect&gt;.sql</c> adds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AllTableNames" /> is not enough on its own, and the gap is the one path where it
    /// matters most. A 3.x database is missing one <em>table</em> and six <em>columns</em>, so a
    /// table-level check passes everything but <c>PAUSED_JOB_GRPS</c> — and
    /// <see cref="SchemaProvisioning.CreateIfMissing" /> creates that one table, reports the schema
    /// validated, starts, and then fails every acquisition and every misfire pass for ever on a
    /// column that is not there. These are probed at startup too, so that database is refused before
    /// it can fire nothing.
    /// </para>
    /// <para>
    /// The list is the 4.0 migration's column additions and nothing else: a column 3.x also had is
    /// present on any database that can be upgraded at all, and a whole table that is missing is
    /// <see cref="AllTableNames" />' business. <c>MigratedColumnTest</c> derives the same list from
    /// the generated scripts and fails when the two disagree, so a column added to the migration
    /// without being added here is a failing test rather than a silent hole in the check.
    /// </para>
    /// </remarks>
    internal static readonly (string Table, string Column)[] MigratedColumnNames =
    [
        (TableTriggers, ColumnMisfireOriginalFireTime),
        (TableTriggers, ColumnExecutionGroup),
        (TableFiredTriggers, ColumnExecutionGroup),
        (TableTriggers, ColumnPreferredNode),
        (TableTriggers, ColumnPreferredNodeAuto),
        (TableTriggers, ColumnRetryPolicy),
        (TableTriggers, ColumnRetryAttempt)
    ];

    // Table names
    /// <summary>
    /// The <c>JOB_DETAILS</c> table, without the table prefix.
    /// </summary>
    public const string TableJobDetails = "JOB_DETAILS";

    /// <summary>
    /// The <c>TRIGGERS</c> table, without the table prefix.
    /// </summary>
    public const string TableTriggers = "TRIGGERS";

    /// <summary>
    /// The <c>SIMPLE_TRIGGERS</c> table, without the table prefix.
    /// </summary>
    public const string TableSimpleTriggers = "SIMPLE_TRIGGERS";

    /// <summary>
    /// The <c>SIMPROP_TRIGGERS</c> table, without the table prefix.
    /// </summary>
    public const string TableSimplePropertiesTriggers = "SIMPROP_TRIGGERS";

    /// <summary>
    /// The <c>CRON_TRIGGERS</c> table, without the table prefix.
    /// </summary>
    public const string TableCronTriggers = "CRON_TRIGGERS";

    /// <summary>
    /// The <c>BLOB_TRIGGERS</c> table, without the table prefix.
    /// </summary>
    public const string TableBlobTriggers = "BLOB_TRIGGERS";

    /// <summary>
    /// The <c>FIRED_TRIGGERS</c> table, without the table prefix.
    /// </summary>
    public const string TableFiredTriggers = "FIRED_TRIGGERS";

    /// <summary>
    /// The <c>CALENDARS</c> table, without the table prefix.
    /// </summary>
    public const string TableCalendars = "CALENDARS";

    /// <summary>
    /// The <c>PAUSED_TRIGGER_GRPS</c> table, without the table prefix.
    /// </summary>
    public const string TablePausedTriggers = "PAUSED_TRIGGER_GRPS";

    /// <summary>
    /// The <c>PAUSED_JOB_GRPS</c> table, without the table prefix.
    /// </summary>
    public const string TablePausedJobs = "PAUSED_JOB_GRPS";

    /// <summary>
    /// The <c>LOCKS</c> table, without the table prefix.
    /// </summary>
    public const string TableLocks = "LOCKS";

    /// <summary>
    /// The <c>SCHEDULER_STATE</c> table, without the table prefix.
    /// </summary>
    public const string TableSchedulerState = "SCHEDULER_STATE";

    // TableJobDetails columns names
    /// <summary>
    /// The <c>SCHED_NAME</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnSchedulerName = "SCHED_NAME";

    /// <summary>
    /// The <c>JOB_NAME</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnJobName = "JOB_NAME";

    /// <summary>
    /// The <c>JOB_GROUP</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnJobGroup = "JOB_GROUP";

    /// <summary>
    /// The <c>IS_DURABLE</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnIsDurable = "IS_DURABLE";

    /// <summary>
    /// The <c>IS_NONCONCURRENT</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnIsNonConcurrent = "IS_NONCONCURRENT";

    /// <summary>
    /// The <c>IS_UPDATE_DATA</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnIsUpdateData = "IS_UPDATE_DATA";

    /// <summary>
    /// The <c>REQUESTS_RECOVERY</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnRequestsRecovery = "REQUESTS_RECOVERY";

    /// <summary>
    /// The <c>JOB_DATA</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnJobDataMap = "JOB_DATA";

    /// <summary>
    /// The <c>JOB_CLASS_NAME</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnJobClass = "JOB_CLASS_NAME";

    /// <summary>
    /// The <c>DESCRIPTION</c> column of <see cref="TableJobDetails" />.
    /// </summary>
    public const string ColumnDescription = "DESCRIPTION";

    // TableTriggers columns names
    /// <summary>
    /// The <c>TRIGGER_NAME</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnTriggerName = "TRIGGER_NAME";

    /// <summary>
    /// The <c>TRIGGER_GROUP</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnTriggerGroup = "TRIGGER_GROUP";

    /// <summary>
    /// The <c>NEXT_FIRE_TIME</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnNextFireTime = "NEXT_FIRE_TIME";

    /// <summary>
    /// The <c>PREV_FIRE_TIME</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnPreviousFireTime = "PREV_FIRE_TIME";

    /// <summary>
    /// The <c>TRIGGER_STATE</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnTriggerState = "TRIGGER_STATE";

    /// <summary>
    /// The <c>TRIGGER_TYPE</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnTriggerType = "TRIGGER_TYPE";

    /// <summary>
    /// The <c>START_TIME</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnStartTime = "START_TIME";

    /// <summary>
    /// The <c>END_TIME</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnEndTime = "END_TIME";

    /// <summary>
    /// The <c>MISFIRE_INSTR</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnMisfireInstruction = "MISFIRE_INSTR";

    /// <summary>
    /// The <c>PRIORITY</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnPriority = "PRIORITY";

    /// <summary>
    /// The <c>MISFIRE_ORIG_FIRE_TIME</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnMisfireOriginalFireTime = "MISFIRE_ORIG_FIRE_TIME";

    /// <summary>
    /// The <c>EXECUTION_GROUP</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnExecutionGroup = "EXECUTION_GROUP";

    /// <summary>
    /// The <c>PREFERRED_NODE</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnPreferredNode = "PREFERRED_NODE";

    /// <summary>
    /// The <c>PREFERRED_NODE_AUTO</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnPreferredNodeAuto = "PREFERRED_NODE_AUTO";

    // Retry columns, added by the 4.0 schema so that no later release has to add a column to
    // QRTZ_TRIGGERS. No statement selects or writes them yet (#3520).
    /// <summary>
    /// The <c>RETRY_POLICY</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnRetryPolicy = "RETRY_POLICY";

    /// <summary>
    /// The <c>RETRY_ATTEMPT</c> column of <see cref="TableTriggers" />.
    /// </summary>
    public const string ColumnRetryAttempt = "RETRY_ATTEMPT";

    // TableSimpleTriggers columns names
    /// <summary>
    /// The <c>REPEAT_COUNT</c> column of <see cref="TableSimpleTriggers" />.
    /// </summary>
    public const string ColumnRepeatCount = "REPEAT_COUNT";

    /// <summary>
    /// The <c>REPEAT_INTERVAL</c> column of <see cref="TableSimpleTriggers" />.
    /// </summary>
    public const string ColumnRepeatInterval = "REPEAT_INTERVAL";

    /// <summary>
    /// The <c>TIMES_TRIGGERED</c> column of <see cref="TableSimpleTriggers" />.
    /// </summary>
    public const string ColumnTimesTriggered = "TIMES_TRIGGERED";

    // TableCronTriggers columns names
    /// <summary>
    /// The <c>CRON_EXPRESSION</c> column of <see cref="TableCronTriggers" />.
    /// </summary>
    public const string ColumnCronExpression = "CRON_EXPRESSION";

    // TableBlobTriggers columns names
    /// <summary>
    /// The <c>BLOB_DATA</c> column of <see cref="TableBlobTriggers" />.
    /// </summary>
    public const string ColumnBlob = "BLOB_DATA";

    /// <summary>
    /// The <c>TIME_ZONE_ID</c> column of <see cref="TableBlobTriggers" />.
    /// </summary>
    public const string ColumnTimeZoneId = "TIME_ZONE_ID";

    // TableFiredTriggers columns names
    /// <summary>
    /// The <c>INSTANCE_NAME</c> column of <see cref="TableFiredTriggers" />.
    /// </summary>
    public const string ColumnInstanceName = "INSTANCE_NAME";

    /// <summary>
    /// The <c>FIRED_TIME</c> column of <see cref="TableFiredTriggers" />.
    /// </summary>
    public const string ColumnFiredTime = "FIRED_TIME";

    /// <summary>
    /// The <c>SCHED_TIME</c> column of <see cref="TableFiredTriggers" />.
    /// </summary>
    public const string ColumnScheduledTime = "SCHED_TIME";

    /// <summary>
    /// The <c>ENTRY_ID</c> column of <see cref="TableFiredTriggers" />.
    /// </summary>
    public const string ColumnEntryId = "ENTRY_ID";

    /// <summary>
    /// The <c>STATE</c> column of <see cref="TableFiredTriggers" />.
    /// </summary>
    public const string ColumnEntryState = "STATE";

    // TableCalendars columns names
    /// <summary>
    /// The <c>CALENDAR_NAME</c> column of <see cref="TableCalendars" />.
    /// </summary>
    public const string ColumnCalendarName = "CALENDAR_NAME";

    /// <summary>
    /// The <c>CALENDAR</c> column of <see cref="TableCalendars" />.
    /// </summary>
    public const string ColumnCalendar = "CALENDAR";

    // TableLocks columns names
    /// <summary>
    /// The <c>LOCK_NAME</c> column of <see cref="TableLocks" />.
    /// </summary>
    public const string ColumnLockName = "LOCK_NAME";

    // TableSchedulerState columns names
    /// <summary>
    /// The <c>LAST_CHECKIN_TIME</c> column of <see cref="TableSchedulerState" />.
    /// </summary>
    public const string ColumnLastCheckinTime = "LAST_CHECKIN_TIME";

    /// <summary>
    /// The <c>CHECKIN_INTERVAL</c> column of <see cref="TableSchedulerState" />.
    /// </summary>
    public const string ColumnCheckinInterval = "CHECKIN_INTERVAL";

    // PARAMETER NAMES A DIALECT DELEGATE HAS TO AGREE WITH

    /// <summary>
    /// The name of the parameter carrying how many rows a page skips.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StdAdoDelegate.ApplyPaging" /> splices these two names into the statement and
    /// <see cref="StdAdoDelegate.AddPagingParameters" /> binds them, and a delegate that overrides one
    /// of those members has to spell the same names in the other — so they are a contract between two
    /// assemblies, not one delegate's private detail. They are here rather than only on the internal
    /// list Quartz's own statements use, because this class is where the names a delegate must agree
    /// with already live.
    /// </para>
    /// <para>
    /// The value is the bare name; the statement carries it prefixed, as <c>"@" + ParameterPageSkip</c>.
    /// </para>
    /// </remarks>
    public const string ParameterPageSkip = "pageSkip";

    /// <summary>
    /// The name of the parameter carrying how many rows a page reads.
    /// </summary>
    /// <remarks>
    /// The other half of the pair <see cref="ParameterPageSkip" /> describes. It is absent from the
    /// statement altogether when the caller asked for an unbounded page.
    /// </remarks>
    public const string ParameterPageTake = "pageTake";

    // MISC CONSTANTS
    /// <summary>
    /// The table prefix a store uses when the configuration names none.
    /// </summary>
    public const string DefaultTablePrefix = "QRTZ_";

    // STATES
    /// <summary>
    /// The stored state of a trigger waiting for its next fire time.
    /// </summary>
    public const string StateWaiting = "WAITING";

    /// <summary>
    /// The stored state of a trigger acquired by a node, which will fire it.
    /// </summary>
    public const string StateAcquired = "ACQUIRED";

    /// <summary>
    /// The stored state of a trigger firing now.
    /// </summary>
    public const string StateExecuting = "EXECUTING";

    /// <summary>
    /// The stored state of a trigger finished and will not fire again.
    /// </summary>
    public const string StateComplete = "COMPLETE";

    /// <summary>
    /// The stored state of a trigger held back because another firing of its non-concurrent job is running.
    /// </summary>
    public const string StateBlocked = "BLOCKED";

    /// <summary>
    /// The stored state of a trigger in error, and will not fire until it is reset.
    /// </summary>
    public const string StateError = "ERROR";

    /// <summary>
    /// The stored state of a trigger paused.
    /// </summary>
    public const string StatePaused = "PAUSED";

    /// <summary>
    /// The stored state of a trigger paused and blocked at once, so resuming it leaves it blocked.
    /// </summary>
    public const string StatePausedBlocked = "PAUSED_BLOCKED";

    /// <summary>
    /// The stored state of a trigger deleted, which is a transient marker rather than a resting state.
    /// </summary>
    public const string StateDeleted = "DELETED";

    /// <summary>
    /// The group name a store records in the paused-groups table to mean that every group is paused,
    /// including the ones added next.
    /// </summary>
    public const string AllGroupsPaused = "_$_ALL_GROUPS_PAUSED_$_";

    // TRIGGER TYPES
    /// <summary>
    /// Simple Trigger type.
    /// </summary>
    public const string TriggerTypeSimple = "SIMPLE";

    /// <summary>
    /// Cron Trigger type.
    /// </summary>
    public const string TriggerTypeCron = "CRON";

    /// <summary>
    /// Calendar Interval Trigger type.
    /// </summary>
    public const string TriggerTypeCalendarInterval = "CAL_INT";

    /// <summary>
    /// Daily Time Interval Trigger type.
    /// </summary>
    public const string TriggerTypeDailyTimeInterval = "DAILY_I";

    /// <summary>
    /// Recurrence (RRULE) Trigger type.
    /// </summary>
    public const string TriggerTypeRecurrence = "RECUR";

    /// <summary>
    /// A general blob Trigger type.
    /// </summary>
    public const string TriggerTypeBlob = "BLOB";
}