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
/// The names of the parameters the statements in <see cref="StdAdoConstants" /> carry.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these used to be written twice: spelled <c>@triggerName</c> inside the statement and
/// again as a bare <c>"triggerName"</c> where the value is bound, with nothing but care holding the
/// two together. A misspelling on either side compiled, and surfaced as a provider complaining about
/// an unbound parameter — or, on a provider that adapts named parameters positionally, as a value
/// bound to the wrong column.
/// </para>
/// <para>
/// One constant now goes into both: <c>@{SqlParameters.TriggerName}</c> in the statement,
/// <c>SqlParameters.TriggerName</c> in the binder. The two spellings cannot drift, and a name that
/// does not exist is a build error rather than a runtime one.
/// </para>
/// <para>
/// Names generated per index — the key-set predicates' <c>tkn000</c>, the state predicates'
/// <c>oldState00</c>, the acquisition exclusions' <c>excludedJobType0000</c> — are not here, because
/// they already come from one function that both the statement builder and the binder call.
/// </para>
/// <para>
/// The lock statements' names are not here either: those statements and their binding live in the
/// same lock handler, which is a seam of its own.
/// </para>
/// </remarks>
internal static class SqlParameters
{
    // The scheduler and its nodes

    public const string SchedulerName = "schedulerName";
    public const string InstanceName = "instanceName";
    public const string InstanceId = "instanceId";
    public const string LastCheckinTime = "lastCheckinTime";
    public const string CheckinInterval = "checkinInterval";

    /// <summary>Matches the sentinel that asks for an auto-pin nothing has claimed yet.</summary>
    public const string AutoPinSentinel = "autoPinSentinel";

    /// <summary>The instant before which a node's last check-in makes it stale.</summary>
    public const string LiveNodeCutoff = "liveNodeCutoff";

    // Group listings, which name a group without saying whether it is a job's or a trigger's

    public const string GroupName = "groupName";

    // JOB_DETAILS

    public const string JobName = "jobName";
    public const string JobGroup = "jobGroup";
    public const string JobDescription = "jobDescription";
    public const string JobType = "jobType";
    public const string JobDurable = "jobDurable";
    public const string JobVolatile = "jobVolatile";
    public const string JobStateful = "jobStateful";
    public const string JobRequestsRecovery = "jobRequestsRecovery";
    public const string JobDataMap = "jobDataMap";

    // TRIGGERS, addressed by key or by state

    public const string TriggerName = "triggerName";
    public const string TriggerGroup = "triggerGroup";
    public const string State = "state";
    public const string NewState = "newState";
    public const string OldState = "oldState";
    public const string NextFireTime = "nextFireTime";
    public const string NoLaterThan = "noLaterThan";
    public const string NoEarlierThan = "noEarlierThan";
    public const string MisfireOrigFireTime = "misfireOrigFireTime";
    public const string ExecutionGroup = "executionGroup";

    // TRIGGERS, written as a whole row. The trigger* family is the one the INSERT and the several
    // UPDATEs share, so a column added to the row adds a name here rather than to the family above.

    public const string TriggerJobName = "triggerJobName";
    public const string TriggerJobGroup = "triggerJobGroup";
    public const string TriggerDescription = "triggerDescription";
    public const string TriggerNextFireTime = "triggerNextFireTime";
    public const string TriggerPreviousFireTime = "triggerPreviousFireTime";
    public const string TriggerState = "triggerState";
    public const string TriggerType = "triggerType";
    public const string TriggerStartTime = "triggerStartTime";
    public const string TriggerEndTime = "triggerEndTime";
    public const string TriggerCalendarName = "triggerCalendarName";
    public const string TriggerMisfireInstruction = "triggerMisfireInstruction";
    public const string TriggerMisfireOrigFireTime = "triggerMisfireOrigFireTime";
    public const string TriggerPriority = "triggerPriority";
    public const string TriggerJobJobDataMap = "triggerJobJobDataMap";
    public const string TriggerExecutionGroup = "triggerExecutionGroup";
    public const string TriggerRetryPolicy = "triggerRetryPolicy";
    public const string TriggerRetryAttempt = "triggerRetryAttempt";
    public const string TriggerRepeatCount = "triggerRepeatCount";
    public const string TriggerRepeatInterval = "triggerRepeatInterval";
    public const string TriggerTimesTriggered = "triggerTimesTriggered";
    public const string TriggerCronExpression = "triggerCronExpression";
    public const string TriggerTimeZone = "triggerTimeZone";
    public const string TimeZoneId = "timeZoneId";

    // Node affinity, which is written by compare-and-swap and so names both the value to write and
    // the value it must still hold

    public const string TriggerPreferredNode = "triggerPreferredNode";
    public const string TriggerPreferredNodeAuto = "triggerPreferredNodeAuto";
    public const string ExpectedPreferredNode = "expectedPreferredNode";
    public const string ExpectedPreferredNodeAuto = "expectedPreferredNodeAuto";
    public const string NewPreferredNode = "newPreferredNode";
    public const string NewPreferredNodeAuto = "newPreferredNodeAuto";
    public const string OldPreferredNode = "oldPreferredNode";
    public const string OldPreferredNodeAuto = "oldPreferredNodeAuto";

    // FIRED_TRIGGERS

    public const string EntryId = "entryId";
    public const string EntryState = "entryState";
    public const string ExecutingState = "executingState";
    public const string FiredTime = "firedTime";
    public const string ScheduledTime = "scheduledTime";
    public const string IsNonConcurrent = "isNonConcurrent";
    public const string RequestsRecovery = "requestsRecovery";

    /// <summary>
    /// The fired-trigger UPDATE's spelling of <see cref="RequestsRecovery" />, a syllable short of it.
    /// Two names for one column is not worth a schema-compatible statement change to fix, but it is
    /// worth being able to see: both are here, next to each other, rather than a letter apart in two
    /// unrelated strings.
    /// </summary>
    public const string RequestsRecover = "requestsRecover";

    public const string TriggerEntryId = "triggerEntryId";
    public const string TriggerInstanceName = "triggerInstanceName";
    public const string TriggerFireTime = "triggerFireTime";
    public const string TriggerScheduledTime = "triggerScheduledTime";
    public const string TriggerJobStateful = "triggerJobStateful";
    public const string TriggerJobRequestsRecovery = "triggerJobRequestsRecovery";

    // CALENDARS and BLOB_TRIGGERS

    public const string CalendarName = "calendarName";
    public const string Calendar = "calendar";
    public const string Blob = "blob";

    // SIMPROP_TRIGGERS, whose columns are numbered rather than named: which property each holds is
    // decided by the trigger type's persistence delegate.

    public const string String1 = "string1";
    public const string String2 = "string2";
    public const string String3 = "string3";
    public const string Int1 = "int1";
    public const string Int2 = "int2";
    public const string Long1 = "long1";
    public const string Long2 = "long2";
    public const string Decimal1 = "decimal1";
    public const string Decimal2 = "decimal2";
    public const string Boolean1 = "boolean1";
    public const string Boolean2 = "boolean2";

    // Paging, for the dialects whose clause takes its bounds as parameters. These two are the one pair
    // a delegate outside this assembly has to agree with — it overrides ApplyPaging and binds the same
    // names in AddPagingParameters — so the values live on AdoConstants, which is public, and this
    // class names them from there rather than repeating the strings.

    public const string PageSkip = AdoConstants.ParameterPageSkip;
    public const string PageTake = AdoConstants.ParameterPageTake;
}
