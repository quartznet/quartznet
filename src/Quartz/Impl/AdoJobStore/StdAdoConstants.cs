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

using System;

namespace Quartz.Impl.AdoJobStore
{
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

        // table prefix substitution string
        [Obsolete("Scheduler name injection has been replaced with sql parameter @schedulerName")]
        public const string SchedulerNameSubst = "{1}";

        // DELETE
        public static readonly string SqlDeleteBlobTrigger =
            $"DELETE FROM {TablePrefixSubst}{TableBlobTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlDeleteCalendar =
            $"DELETE FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName";

        public static readonly string SqlDeleteCronTrigger =
            $"DELETE FROM {TablePrefixSubst}{TableCronTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlDeleteFiredTrigger =
            $"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnEntryId} = @triggerEntryId";

        public static readonly string SqlDeleteFiredTriggers =
            $"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlDeleteInstancesFiredTriggers =
            $"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName";

        public static readonly string SqlDeleteJobDetail =
            $"DELETE FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlDeleteNoRecoveryFiredTriggers =
            $"DELETE FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName AND {ColumnRequestsRecovery} = @requestsRecovery";

        public static readonly string SqlDeletePausedTriggerGroup =
            $"DELETE FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @triggerGroup";

        public static readonly string SqlDeletePausedTriggerGroups =
            $"DELETE FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlDeleteSchedulerState =
            $"DELETE FROM {TablePrefixSubst}{TableSchedulerState} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName";

        public static readonly string SqlDeleteSimpleTrigger =
            $"DELETE FROM {TablePrefixSubst}{TableSimpleTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlDeleteTrigger =
            $"DELETE FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlDeleteAllSimpleTriggers = $"DELETE FROM {TablePrefixSubst}SIMPLE_TRIGGERS WHERE {ColumnSchedulerName} = @schedulerName";
        public static readonly string SqlDeleteAllSimpropTriggers = $"DELETE FROM {TablePrefixSubst}SIMPROP_TRIGGERS WHERE {ColumnSchedulerName} = @schedulerName";
        public static readonly string SqlDeleteAllCronTriggers = $"DELETE FROM {TablePrefixSubst}CRON_TRIGGERS WHERE {ColumnSchedulerName} = @schedulerName";
        public static readonly string SqlDeleteAllBlobTriggers = $"DELETE FROM {TablePrefixSubst}BLOB_TRIGGERS WHERE {ColumnSchedulerName} = @schedulerName";
        public static readonly string SqlDeleteAllTriggers = $"DELETE FROM {TablePrefixSubst}TRIGGERS WHERE {ColumnSchedulerName} = @schedulerName";
        public static readonly string SqlDeleteAllJobDetails = $"DELETE FROM {TablePrefixSubst}JOB_DETAILS WHERE {ColumnSchedulerName} = @schedulerName";
        public static readonly string SqlDeleteAllCalendars = $"DELETE FROM {TablePrefixSubst}CALENDARS WHERE {ColumnSchedulerName} = @schedulerName";
        public static readonly string SqlDeleteAllPausedTriggerGrps = $"DELETE FROM {TablePrefixSubst}PAUSED_TRIGGER_GRPS WHERE {ColumnSchedulerName} = @schedulerName";

        // INSERT

        public static readonly string SqlInsertBlobTrigger =
            $"INSERT INTO {TablePrefixSubst}{TableBlobTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnBlob})  VALUES(@schedulerName, @triggerName, @triggerGroup, @blob)";

        public static readonly string SqlInsertCalendar =
            $"INSERT INTO {TablePrefixSubst}{TableCalendars} ({ColumnSchedulerName}, {ColumnCalendarName}, {ColumnCalendar})  VALUES(@schedulerName, @calendarName, @calendar)";

        public static readonly string SqlInsertCronTrigger =
            $"INSERT INTO {TablePrefixSubst}{TableCronTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnCronExpression}, {ColumnTimeZoneId}) VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerCronExpression, @triggerTimeZone)";

        public static readonly string SqlInsertFiredTrigger =
            $"INSERT INTO {TablePrefixSubst}{TableFiredTriggers} ({ColumnSchedulerName}, {ColumnEntryId}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnInstanceName}, {ColumnFiredTime}, {ColumnScheduledTime}, {ColumnEntryState}, {ColumnJobName}, {ColumnJobGroup}, {ColumnIsNonConcurrent}, {ColumnRequestsRecovery}, {ColumnPriority}) VALUES(@schedulerName, @triggerEntryId, @triggerName, @triggerGroup, @triggerInstanceName, @triggerFireTime, @triggerScheduledTime, @triggerState, @triggerJobName, @triggerJobGroup, @triggerJobStateful, @triggerJobRequestsRecovery, @triggerPriority)";

        public static readonly string SqlInsertJobDetail =
            $"INSERT INTO {TablePrefixSubst}{TableJobDetails} ({ColumnSchedulerName}, {ColumnJobName}, {ColumnJobGroup}, {ColumnDescription}, {ColumnJobClass}, {ColumnIsDurable}, {ColumnIsNonConcurrent}, {ColumnIsUpdateData}, {ColumnRequestsRecovery}, {ColumnJobDataMap})  VALUES(@schedulerName, @jobName, @jobGroup, @jobDescription, @jobType, @jobDurable, @jobVolatile, @jobStateful, @jobRequestsRecovery, @jobDataMap)";

        public static readonly string SqlInsertPausedTriggerGroup =
            $"INSERT INTO {TablePrefixSubst}{TablePausedTriggers} ({ColumnSchedulerName}, {ColumnTriggerGroup}) VALUES (@schedulerName, @triggerGroup)";

        public static readonly string SqlInsertSchedulerState =
            $"INSERT INTO {TablePrefixSubst}{TableSchedulerState} ({ColumnSchedulerName}, {ColumnInstanceName}, {ColumnLastCheckinTime}, {ColumnCheckinInterval}) VALUES(@schedulerName, @instanceName, @lastCheckinTime, @checkinInterval)";

        public static readonly string SqlInsertSimpleTrigger =
            $"INSERT INTO {TablePrefixSubst}{TableSimpleTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnRepeatCount}, {ColumnRepeatInterval}, {ColumnTimesTriggered})  VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerRepeatCount, @triggerRepeatInterval, @triggerTimesTriggered)";

        public static readonly string SqlInsertTrigger =
            $@"INSERT INTO {TablePrefixSubst}{TableTriggers} ({ColumnSchedulerName}, {ColumnTriggerName}, {ColumnTriggerGroup}, {ColumnJobName}, {ColumnJobGroup}, {ColumnDescription}, {ColumnNextFireTime}, {ColumnPreviousFireTime}, {ColumnTriggerState}, {ColumnTriggerType}, {ColumnStartTime}, {ColumnEndTime}, {ColumnCalendarName}, {ColumnMifireInstruction}, {ColumnJobDataMap}, {ColumnPriority})  
                        VALUES(@schedulerName, @triggerName, @triggerGroup, @triggerJobName, @triggerJobGroup, @triggerDescription, @triggerNextFireTime, @triggerPreviousFireTime, @triggerState, @triggerType, @triggerStartTime, @triggerEndTime, @triggerCalendarName, @triggerMisfireInstruction, @triggerJobJobDataMap, @triggerPriority)";

        // SELECT

        public static readonly string SqlSelectBlobTrigger =
            string.Format("SELECT {6} FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup", TablePrefixSubst,
                TableBlobTriggers, ColumnSchedulerName, "@schedulerName", ColumnTriggerName, ColumnTriggerGroup,
                ColumnBlob);

        public static readonly string SqlSelectCalendar =
            string.Format("SELECT {5} FROM {0}{1} WHERE {2} = {3} AND {4} = @calendarName", TablePrefixSubst, TableCalendars,
                ColumnSchedulerName, "@schedulerName", ColumnCalendarName, ColumnCalendar);

        public static readonly string SqlSelectCalendarExistence =
            $"SELECT {ColumnCalendarName} FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName";

        public static readonly string SqlSelectCalendars =
            $"SELECT {ColumnCalendarName} FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectCronTriggers =
            $"SELECT * FROM {TablePrefixSubst}{TableCronTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectFiredTrigger =
            $"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectFiredTriggerGroup =
            $"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectFiredTriggers =
            $"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectFiredTriggerInstanceNames =
            $"SELECT DISTINCT {ColumnInstanceName} FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectFiredTriggersOfJob =
            $"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectFiredTriggersOfJobGroup =
            $"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectInstancesFiredTriggers =
            $"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName";

        public static readonly string SqlSelectInstancesRecoverableFiredTriggers =
            $"SELECT * FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName AND {ColumnRequestsRecovery} = @requestsRecovery";

        public static readonly string SqlSelectJobDetail =
            string.Format(
                "SELECT {6},{7},{8},{9},{10},{11},{12} FROM {0}{1} WHERE {2} = {3} AND {4} = @jobName AND {5} = @jobGroup",
                TablePrefixSubst, TableJobDetails, ColumnSchedulerName, "@schedulerName", ColumnJobName, ColumnJobGroup,
                ColumnJobName, ColumnJobGroup, ColumnDescription, ColumnJobClass, ColumnIsDurable, ColumnRequestsRecovery, ColumnJobDataMap);

        public static readonly string SqlSelectJobExecutionCount =
            $"SELECT COUNT({ColumnTriggerName}) FROM {TablePrefixSubst}{TableFiredTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectJobExistence =
            $"SELECT {ColumnJobName} FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectJobForTrigger =
            $"SELECT J.{ColumnJobName}, J.{ColumnJobGroup}, J.{ColumnIsDurable}, J.{ColumnJobClass}, J.{ColumnRequestsRecovery} FROM {TablePrefixSubst}{TableTriggers} T, {TablePrefixSubst}{TableJobDetails} J WHERE T.{ColumnSchedulerName} = @schedulerName AND T.{ColumnSchedulerName} = J.{ColumnSchedulerName} AND T.{ColumnTriggerName} = @triggerName AND T.{ColumnTriggerGroup} = @triggerGroup AND T.{ColumnJobName} = J.{ColumnJobName} AND T.{ColumnJobGroup} = J.{ColumnJobGroup}";

        public static readonly string SqlSelectJobGroups =
            $"SELECT DISTINCT({ColumnJobGroup}) FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectJobNonConcurrent =
            $"SELECT {ColumnIsNonConcurrent} FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectJobsInGroupLike =
            $"SELECT {ColumnJobName}, {ColumnJobGroup} FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobGroup} LIKE @jobGroup";

        public static readonly string SqlSelectJobsInGroup =
            $"SELECT {ColumnJobName}, {ColumnJobGroup} FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectMisfiredTriggers =
            $"SELECT * FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC";

        public static readonly string SqlSelectMisfiredTriggersInGroupInState =
            $"SELECT {ColumnTriggerName} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnTriggerState} = @state ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC";

        public static readonly string SqlSelectMisfiredTriggersInState =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerState} = @state ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC";

        public static readonly string SqlCountMisfiredTriggersInStates =
            $"SELECT COUNT({ColumnTriggerName}) FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerState} = @state1";

        public static readonly string SqlSelectHasMisfiredTriggersInState =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnMifireInstruction} <> {MisfireInstruction.IgnoreMisfirePolicy} AND {ColumnNextFireTime} < @nextFireTime AND {ColumnTriggerState} = @state1 ORDER BY {ColumnNextFireTime} ASC, {ColumnPriority} DESC";

        public static readonly string SqlSelectNextTriggerToAcquire =
            string.Format("SELECT {0}, {1}, {2}, {3} FROM {4}{5} WHERE {6} = {7} AND {8} = @state AND {9} <= @noLaterThan AND ({10} = -1 OR ({10} <> -1 AND {9} >= @noEarlierThan)) ORDER BY {9} ASC, {11} DESC",
                ColumnTriggerName, ColumnTriggerGroup, ColumnNextFireTime, ColumnPriority,
                TablePrefixSubst, TableTriggers, ColumnSchedulerName, "@schedulerName",
                ColumnTriggerState, ColumnNextFireTime, ColumnMifireInstruction, ColumnPriority);

        public static readonly string SqlSelectNumCalendars =
            $"SELECT COUNT({ColumnCalendarName})  FROM {TablePrefixSubst}{TableCalendars} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectNumJobs =
            $"SELECT COUNT({ColumnJobName})  FROM {TablePrefixSubst}{TableJobDetails} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectNumTriggers =
            $"SELECT COUNT({ColumnTriggerName})  FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectNumTriggersForJob =
            $"SELECT COUNT({ColumnTriggerName}) FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectNumTriggersInGroup =
            $"SELECT COUNT({ColumnTriggerName})  FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectPausedTriggerGroup =
            $"SELECT {ColumnTriggerGroup} FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectPausedTriggerGroups =
            $"SELECT {ColumnTriggerGroup} FROM {TablePrefixSubst}{TablePausedTriggers} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectReferencedCalendar =
            $"SELECT {ColumnCalendarName} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName";

        public static readonly string SqlSelectSchedulerState =
            $"SELECT * FROM {TablePrefixSubst}{TableSchedulerState} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName";

        public static readonly string SqlSelectSchedulerStates =
            $"SELECT * FROM {TablePrefixSubst}{TableSchedulerState} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectSimpleTrigger =
            $"SELECT * FROM {TablePrefixSubst}{TableSimpleTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectTrigger =
            string.Format("SELECT {6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17} FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup",
                TablePrefixSubst, TableTriggers, ColumnSchedulerName, "@schedulerName", ColumnTriggerName, ColumnTriggerGroup,
                ColumnJobName, ColumnJobGroup, ColumnDescription, ColumnNextFireTime, ColumnPreviousFireTime, ColumnTriggerType, ColumnStartTime, ColumnEndTime, ColumnCalendarName, ColumnMifireInstruction, ColumnPriority, ColumnJobDataMap);

        public static readonly string SqlSelectTriggerData =
            $"SELECT {ColumnJobDataMap} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectTriggerExistence =
            $"SELECT {ColumnTriggerName} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectTriggerForFireTime =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state AND {ColumnNextFireTime} = @nextFireTime";

        public static readonly string SqlSelectTriggerGroups =
            $"SELECT DISTINCT({ColumnTriggerGroup}) FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName";

        public static readonly string SqlSelectTriggerGroupsFiltered =
            string.Format("SELECT DISTINCT({0}) FROM {1}{2} WHERE {3} = {4} AND {0} LIKE @triggerGroup",
                ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, "@schedulerName");

        public static readonly string SqlSelectTriggerState =
            $"SELECT {ColumnTriggerState} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectTriggerStatus =
            $"SELECT {ColumnTriggerState}, {ColumnNextFireTime}, {ColumnJobName}, {ColumnJobGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectTriggersForCalendar =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName";

        public static readonly string SqlSelectTriggersForJob =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlSelectTriggersInGroupLike =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @triggerGroup";

        public static readonly string SqlSelectTriggersInGroup =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlSelectTriggersInState =
            $"SELECT {ColumnTriggerName}, {ColumnTriggerGroup} FROM {TablePrefixSubst}{TableTriggers} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerState} = @state";

        // UPDATE 

        public static readonly string SqlUpdateBlobTrigger =
            $"UPDATE {TablePrefixSubst}{TableBlobTriggers} SET {ColumnBlob} = @blob WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlUpdateCalendar =
            $"UPDATE {TablePrefixSubst}{TableCalendars} SET {ColumnCalendar} = @calendar WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnCalendarName} = @calendarName";

        public static readonly string SqlUpdateCronTrigger =
            $"UPDATE {TablePrefixSubst}{TableCronTriggers} SET {ColumnCronExpression} = @triggerCronExpression, {ColumnTimeZoneId} = @timeZoneId WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlUpdateJobData =
            $"UPDATE {TablePrefixSubst}{TableJobDetails} SET {ColumnJobDataMap} = @jobDataMap WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlUpdateJobDetail =
            $"UPDATE {TablePrefixSubst}{TableJobDetails} SET {ColumnDescription} = @jobDescription, {ColumnJobClass} = @jobType, {ColumnIsDurable} = @jobDurable, {ColumnIsNonConcurrent} = @jobVolatile, {ColumnIsUpdateData} = @jobStateful, {ColumnRequestsRecovery} = @jobRequestsRecovery, {ColumnJobDataMap} = @jobDataMap  WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlUpdateJobTriggerStates =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @state WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup";

        public static readonly string SqlUpdateJobTriggerStatesFromOtherState =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @state WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnJobName} = @jobName AND {ColumnJobGroup} = @jobGroup AND {ColumnTriggerState} = @oldState";

        public static readonly string SqlUpdateSchedulerState =
            $"UPDATE {TablePrefixSubst}{TableSchedulerState} SET {ColumnLastCheckinTime} = @lastCheckinTime WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnInstanceName} = @instanceName";

        public static readonly string SqlUpdateSimpleTrigger =
            $"UPDATE {TablePrefixSubst}{TableSimpleTriggers} SET {ColumnRepeatCount} = @triggerRepeatCount, {ColumnRepeatInterval} = @triggerRepeatInterval, {ColumnTimesTriggered} = @triggerTimesTriggered WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlUpdateTrigger =
            $@"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnJobName} = @triggerJobName, {ColumnJobGroup} = @triggerJobGroup, {ColumnDescription} = @triggerDescription, {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime,
                        {ColumnTriggerState} = @triggerState, {ColumnTriggerType} = @triggerType, {ColumnStartTime} = @triggerStartTime, {ColumnEndTime} = @triggerEndTime, {ColumnCalendarName} = @triggerCalendarName, {ColumnMifireInstruction} = @triggerMisfireInstruction, {ColumnPriority} = @triggerPriority, {ColumnJobDataMap} = @triggerJobJobDataMap
                        WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlUpdateFiredTrigger = string.Format(
            "UPDATE {0}{1} SET {2} = @instanceName, {3} = @firedTime, {12} = @scheduledTime, {4} = @entryState, {5} = @jobName, {6} = @jobGroup, {7} = @isNonConcurrent, {8} = @requestsRecover WHERE {9} = {10} AND {11} = @entryId",
            TablePrefixSubst, TableFiredTriggers, ColumnInstanceName, ColumnFiredTime, ColumnEntryState,
            ColumnJobName, ColumnJobGroup, ColumnIsNonConcurrent, ColumnRequestsRecovery, ColumnSchedulerName, "@schedulerName", ColumnEntryId, ColumnScheduledTime);

        public static readonly string SqlUpdateTriggerGroupStateFromState =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @triggerGroup AND {ColumnTriggerState} = @oldState";

        public static readonly string SqlUpdateTriggerGroupStateFromStates =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerGroup} LIKE @groupName AND ({ColumnTriggerState} = @oldState1 OR {ColumnTriggerState} = @oldState2 OR {ColumnTriggerState} = @oldState3)";

        public static readonly string SqlUpdateTriggerSkipData =
            $@"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnJobName} = @triggerJobName, {ColumnJobGroup} = @triggerJobGroup, {ColumnDescription} = @triggerDescription, {ColumnNextFireTime} = @triggerNextFireTime, {ColumnPreviousFireTime} = @triggerPreviousFireTime, 
                        {ColumnTriggerState} = @triggerState, {ColumnTriggerType} = @triggerType, {ColumnStartTime} = @triggerStartTime, {ColumnEndTime} = @triggerEndTime, {ColumnCalendarName} = @triggerCalendarName, {ColumnMifireInstruction} = @triggerMisfireInstruction, {ColumnPriority} = @triggerPriority
                    WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlUpdateTriggerState =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @state WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup";

        public static readonly string SqlUpdateTriggerStateFromState =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnTriggerState} = @oldState";

        public static readonly string SqlUpdateTriggerStateFromStates =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND ({ColumnTriggerState} = @oldState1 OR {ColumnTriggerState} = @oldState2 OR {ColumnTriggerState} = @oldState3)";

        public static readonly string SqlUpdateTriggerStateFromStateWithNextFireTime =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnTriggerName} = @triggerName AND {ColumnTriggerGroup} = @triggerGroup AND {ColumnTriggerState} = @oldState AND {ColumnNextFireTime} = @nextFireTime";

        public static readonly string SqlUpdateTriggerStatesFromOtherStates =
            $"UPDATE {TablePrefixSubst}{TableTriggers} SET {ColumnTriggerState} = @newState WHERE {ColumnSchedulerName} = @schedulerName AND ({ColumnTriggerState} = @oldState1 OR {ColumnTriggerState} = @oldState2)";
    }
}