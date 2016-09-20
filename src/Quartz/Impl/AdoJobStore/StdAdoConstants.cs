#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
        public const string SchedulerNameSubst = "{1}";

        // DELETE
        public static readonly string SqlDeleteBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup", TablePrefixSubst,
                          TableBlobTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, 
                          ColumnTriggerGroup);

        public static readonly string SqlDeleteCalendar =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @calendarName", 
            TablePrefixSubst, TableCalendars, ColumnSchedulerName, SchedulerNameSubst, ColumnCalendarName);

        public static readonly string SqlDeleteCronTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup",
                TablePrefixSubst, TableCronTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlDeleteFiredTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerEntryId", 
            TablePrefixSubst, TableFiredTriggers,ColumnSchedulerName, SchedulerNameSubst, ColumnEntryId);

        public static readonly string SqlDeleteFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3}", TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlDeleteInstancesFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @instanceName", TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst,
                          ColumnInstanceName);

        public static readonly string SqlDeleteJobDetail =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @jobName AND {5} = @jobGroup", TablePrefixSubst,
                          TableJobDetails, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlDeleteNoRecoveryFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @instanceName AND {5} = @requestsRecovery", 
                    TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnInstanceName, ColumnRequestsRecovery);

        public static readonly string SqlDeletePausedTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} LIKE @triggerGroup",
            TablePrefixSubst, TablePausedTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerGroup);

        public static readonly string SqlDeletePausedTriggerGroups =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3}", TablePrefixSubst, TablePausedTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlDeleteSchedulerState =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @instanceName", 
                TablePrefixSubst, TableSchedulerState, ColumnSchedulerName, SchedulerNameSubst, ColumnInstanceName);

        public static readonly string SqlDeleteSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup",
                TablePrefixSubst, TableSimpleTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlDeleteTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup",
                TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlDeleteAllSimpleTriggers = string.Format("DELETE FROM {0}SIMPLE_TRIGGERS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);
        public static readonly string SqlDeleteAllSimpropTriggers = string.Format("DELETE FROM {0}SIMPROP_TRIGGERS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);
        public static readonly string SqlDeleteAllCronTriggers = string.Format("DELETE FROM {0}CRON_TRIGGERS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);
        public static readonly string SqlDeleteAllBlobTriggers = string.Format("DELETE FROM {0}BLOB_TRIGGERS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);
        public static readonly string SqlDeleteAllTriggers = string.Format("DELETE FROM {0}TRIGGERS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);
        public static readonly string SqlDeleteAllJobDetails = string.Format("DELETE FROM {0}JOB_DETAILS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);
        public static readonly string SqlDeleteAllCalendars = string.Format("DELETE FROM {0}CALENDARS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);
        public static readonly string SqlDeleteAllPausedTriggerGrps = string.Format("DELETE FROM {0}PAUSED_TRIGGER_GRPS WHERE {1} = {2}", TablePrefixSubst, ColumnSchedulerName, SchedulerNameSubst);


        // INSERT

        public static readonly string SqlInsertBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}, {3}, {4}, {5})  VALUES({6}, @triggerName, @triggerGroup, @blob)",
                          TablePrefixSubst,
                          TableBlobTriggers, ColumnSchedulerName, ColumnTriggerName,
                          ColumnTriggerGroup, ColumnBlob, SchedulerNameSubst);

        public static readonly string SqlInsertCalendar =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}, {3}, {4})  VALUES({5}, @calendarName, @calendar)",
                TablePrefixSubst, TableCalendars, ColumnSchedulerName, ColumnCalendarName, ColumnCalendar, SchedulerNameSubst);

        public static readonly string SqlInsertCronTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}) VALUES({7}, @triggerName, @triggerGroup, @triggerCronExpression, @triggerTimeZone)",
                TablePrefixSubst,
                TableCronTriggers, ColumnSchedulerName, ColumnTriggerName,
                ColumnTriggerGroup, ColumnCronExpression, ColumnTimeZoneId, SchedulerNameSubst);

        public static readonly string SqlInsertFiredTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}) VALUES({15}, @triggerEntryId, @triggerName, @triggerGroup, @triggerInstanceName, @triggerFireTime, @triggerScheduledTime, @triggerState, @triggerJobName, @triggerJobGroup, @triggerJobStateful, @triggerJobRequestsRecovery, @triggerPriority)",
                TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, ColumnEntryId,
                ColumnTriggerName, ColumnTriggerGroup,
                ColumnInstanceName, ColumnFiredTime, ColumnScheduledTime, ColumnEntryState,
                ColumnJobName, ColumnJobGroup, ColumnIsNonConcurrent,
                ColumnRequestsRecovery, ColumnPriority, SchedulerNameSubst);

        public static readonly string SqlInsertJobDetail =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11})  VALUES({12}, @jobName, @jobGroup, @jobDescription, @jobType, @jobDurable, @jobVolatile, @jobStateful, @jobRequestsRecovery, @jobDataMap)",
                TablePrefixSubst, TableJobDetails, ColumnSchedulerName, ColumnJobName,
                ColumnJobGroup, ColumnDescription, ColumnJobClass,
                ColumnIsDurable, ColumnIsNonConcurrent, ColumnIsUpdateData,
                ColumnRequestsRecovery, ColumnJobDataMap, SchedulerNameSubst);

        public static readonly string SqlInsertPausedTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}, {3}) VALUES ({4}, @triggerGroup)", TablePrefixSubst,
                          TablePausedTriggers, ColumnSchedulerName, ColumnTriggerGroup, SchedulerNameSubst);

        public static readonly string SqlInsertSchedulerState =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}) VALUES({6}, @instanceName, @lastCheckinTime, @checkinInterval)",
                TablePrefixSubst,
                TableSchedulerState, ColumnSchedulerName, ColumnInstanceName, ColumnLastCheckinTime, ColumnCheckinInterval, SchedulerNameSubst);

        public static readonly string SqlInsertSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7})  VALUES({8}, @triggerName, @triggerGroup, @triggerRepeatCount, @triggerRepeatInterval, @triggerTimesTriggered)",
                TablePrefixSubst,
                TableSimpleTriggers, ColumnSchedulerName, ColumnTriggerName, ColumnTriggerGroup, ColumnRepeatCount, ColumnRepeatInterval, ColumnTimesTriggered, SchedulerNameSubst);

        public static readonly string SqlInsertTrigger =
            string.Format(CultureInfo.InvariantCulture,
                @"INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17})  
                        VALUES({18}, @triggerName, @triggerGroup, @triggerJobName, @triggerJobGroup, @triggerDescription, @triggerNextFireTime, @triggerPreviousFireTime, @triggerState, @triggerType, @triggerStartTime, @triggerEndTime, @triggerCalendarName, @triggerMisfireInstruction, @triggerJobJobDataMap, @triggerPriority)",
                TablePrefixSubst, TableTriggers, ColumnSchedulerName, ColumnTriggerName,
                ColumnTriggerGroup, ColumnJobName, ColumnJobGroup,
                ColumnDescription, ColumnNextFireTime,
                ColumnPreviousFireTime, ColumnTriggerState, ColumnTriggerType,
                ColumnStartTime, ColumnEndTime, ColumnCalendarName,
                ColumnMifireInstruction, ColumnJobDataMap, ColumnPriority, SchedulerNameSubst);

        // SELECT

        public static readonly string SqlSelectBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT {6} FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup", TablePrefixSubst,
                          TableBlobTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup,
                          ColumnBlob);

        public static readonly string SqlSelectCalendar =
            string.Format(CultureInfo.InvariantCulture, "SELECT {5} FROM {0}{1} WHERE {2} = {3} AND {4} = @calendarName", TablePrefixSubst, TableCalendars,
                          ColumnSchedulerName, SchedulerNameSubst, ColumnCalendarName, ColumnCalendar);

        public static readonly string SqlSelectCalendarExistence =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @calendarName",
            ColumnCalendarName, TablePrefixSubst, TableCalendars, ColumnSchedulerName, SchedulerNameSubst, ColumnCalendarName);

        public static readonly string SqlSelectCalendars =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4}", 
                ColumnCalendarName, TablePrefixSubst, TableCalendars, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectCronTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup", TablePrefixSubst,
                          TableCronTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectFiredTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup", 
                TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectFiredTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerGroup",
                TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerGroup);

        public static readonly string SqlSelectFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3}", 
                TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectFiredTriggerInstanceNames =
            string.Format(CultureInfo.InvariantCulture, "SELECT DISTINCT {0} FROM {1}{2} WHERE {3} = {4}",
                ColumnInstanceName, TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectFiredTriggersOfJob =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @jobName AND {5} = @jobGroup",
                TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectFiredTriggersOfJobGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @jobGroup",
                TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnJobGroup);

        public static readonly string SqlSelectInstancesFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @instanceName",
                TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnInstanceName);

        public static readonly string SqlSelectInstancesRecoverableFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @instanceName AND {5} = @requestsRecovery",
                 TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnInstanceName, ColumnRequestsRecovery);

        public static readonly string SqlSelectJobDetail =
            string.Format(CultureInfo.InvariantCulture,
                "SELECT {6},{7},{8},{9},{10},{11},{12} FROM {0}{1} WHERE {2} = {3} AND {4} = @jobName AND {5} = @jobGroup",
                TablePrefixSubst, TableJobDetails, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup,
                ColumnJobName, ColumnJobGroup, ColumnDescription, ColumnJobClass, ColumnIsDurable, ColumnRequestsRecovery, ColumnJobDataMap);

        public static readonly string SqlSelectJobExecutionCount =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0}) FROM {1}{2} WHERE {3} = {4} AND {5} = @jobName AND {6} = @jobGroup",
                ColumnTriggerName, TablePrefixSubst, TableFiredTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectJobExistence =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @jobName AND {6} = @jobGroup",
                ColumnJobName, TablePrefixSubst, TableJobDetails, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectJobForTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "SELECT J.{0}, J.{1}, J.{2}, J.{3}, J.{4} FROM {5}{6} T, {7}{8} J WHERE T.{9} = {10} AND T.{11} = J.{12} AND T.{13} = @triggerName AND T.{14} = @triggerGroup AND T.{15} = J.{16} AND T.{17} = J.{18}",
                ColumnJobName, ColumnJobGroup, ColumnIsDurable,
                ColumnJobClass, ColumnRequestsRecovery, TablePrefixSubst,
                TableTriggers, TablePrefixSubst, TableJobDetails, ColumnSchedulerName, SchedulerNameSubst, ColumnSchedulerName, ColumnSchedulerName,
                ColumnTriggerName, ColumnTriggerGroup, ColumnJobName,
                ColumnJobName, ColumnJobGroup, ColumnJobGroup);

        public static readonly string SqlSelectJobGroups =
            string.Format(CultureInfo.InvariantCulture, "SELECT DISTINCT({0}) FROM {1}{2} WHERE {3} = {4}", ColumnJobGroup, TablePrefixSubst,
                          TableJobDetails, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectJobNonConcurrent =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @jobName AND {6} = @jobGroup", 
                ColumnIsNonConcurrent, TablePrefixSubst, TableJobDetails, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectJobsInGroupLike =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} LIKE @jobGroup",
                ColumnJobName, ColumnJobGroup, TablePrefixSubst, TableJobDetails, ColumnSchedulerName, SchedulerNameSubst, ColumnJobGroup);

        public static readonly string SqlSelectJobsInGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} = @jobGroup",
                ColumnJobName, ColumnJobGroup, TablePrefixSubst, TableJobDetails, ColumnSchedulerName, SchedulerNameSubst, ColumnJobGroup);

        public static readonly string SqlSelectMisfiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} <> {5} AND {6} < @nextFireTime ORDER BY {7} ASC, {8} DESC", TablePrefixSubst,
                          TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnMifireInstruction, MisfireInstruction.IgnoreMisfirePolicy, ColumnNextFireTime, ColumnNextFireTime, ColumnPriority);

        public static readonly string SqlSelectMisfiredTriggersInGroupInState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} <> {6} AND {7} < @nextFireTime AND {8} = @triggerGroup AND {9} = @state ORDER BY {10} ASC, {11} DESC",
                          ColumnTriggerName, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst,
                          ColumnMifireInstruction, MisfireInstruction.IgnoreMisfirePolicy,
                          ColumnNextFireTime, ColumnTriggerGroup,
                          ColumnTriggerState, ColumnNextFireTime, ColumnPriority);

        public static readonly string SqlSelectMisfiredTriggersInState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} <> {7} AND {8} < @nextFireTime AND {9} = @state ORDER BY {10} ASC, {11} DESC", ColumnTriggerName,
                          ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnMifireInstruction, MisfireInstruction.IgnoreMisfirePolicy,
                          ColumnNextFireTime, ColumnTriggerState, ColumnNextFireTime, ColumnPriority);

        public static readonly string SqlCountMisfiredTriggersInStates =
            string.Format("SELECT COUNT({0}) FROM {1}{2} WHERE {3} = {4} AND {5} <> {6} AND {7} < @nextFireTime AND {8} = @state1",
            ColumnTriggerName, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnMifireInstruction, MisfireInstruction.IgnoreMisfirePolicy, ColumnNextFireTime, ColumnTriggerState);

        public static readonly string SqlSelectHasMisfiredTriggersInState =
            string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} <> {7} AND {8} < @nextFireTime AND {9} = @state1 ORDER BY {10} ASC, {11} DESC",
            ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst,
            ColumnMifireInstruction, MisfireInstruction.IgnoreMisfirePolicy, ColumnNextFireTime, ColumnTriggerState, ColumnNextFireTime, ColumnPriority);

        public static readonly string SqlSelectNextTriggerToAcquire =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1}, {2}, {3} FROM {4}{5} WHERE {6} = {7} AND {8} = @state AND {9} <= @noLaterThan AND ({10} = -1 OR ({10} <> -1 AND {9} >= @noEarlierThan)) ORDER BY {9} ASC, {11} DESC", 
            ColumnTriggerName, ColumnTriggerGroup, ColumnNextFireTime, ColumnPriority,
            TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst,
            ColumnTriggerState, ColumnNextFireTime, ColumnMifireInstruction, ColumnPriority);

        public static readonly string SqlSelectNumCalendars =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2} WHERE {3} = {4}",
                ColumnCalendarName, TablePrefixSubst, TableCalendars, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectNumJobs =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2} WHERE {3} = {4}",
                ColumnJobName, TablePrefixSubst, TableJobDetails, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectNumTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2} WHERE {3} = {4}",
                ColumnTriggerName, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectNumTriggersForJob =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0}) FROM {1}{2} WHERE {3} = {4} AND {5} = @jobName AND {6} = @jobGroup",
                ColumnTriggerName, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectNumTriggersInGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2} WHERE {3} = {4} AND {5} = @triggerGroup",
                ColumnTriggerName, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerGroup);

        public static readonly string SqlSelectPausedTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @triggerGroup", 
                ColumnTriggerGroup, TablePrefixSubst, TablePausedTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerGroup);

        public static readonly string SqlSelectPausedTriggerGroups =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4}", 
                ColumnTriggerGroup, TablePrefixSubst, TablePausedTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectReferencedCalendar =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @calendarName",
                ColumnCalendarName, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnCalendarName);

        public static readonly string SqlSelectSchedulerState =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @instanceName",
                TablePrefixSubst, TableSchedulerState, ColumnSchedulerName, SchedulerNameSubst, ColumnInstanceName);

        public static readonly string SqlSelectSchedulerStates =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3}", TablePrefixSubst, TableSchedulerState, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup",
                TablePrefixSubst, TableSimpleTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT {6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17} FROM {0}{1} WHERE {2} = {3} AND {4} = @triggerName AND {5} = @triggerGroup",
                TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup,
                ColumnJobName, ColumnJobGroup, ColumnDescription, ColumnNextFireTime, ColumnPreviousFireTime, ColumnTriggerType, ColumnStartTime, ColumnEndTime, ColumnCalendarName, ColumnMifireInstruction, ColumnPriority, ColumnJobDataMap);

        public static readonly string SqlSelectTriggerData =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @triggerName AND {6} = @triggerGroup",
                ColumnJobDataMap, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerExistence =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @triggerName AND {6} = @triggerGroup",
                ColumnTriggerName, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerForFireTime =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} = @state AND {7} = @nextFireTime",
                ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerState, ColumnNextFireTime);

        public static readonly string SqlSelectTriggerGroups =
            string.Format(CultureInfo.InvariantCulture, "SELECT DISTINCT({0}) FROM {1}{2} WHERE {3} = {4}",
                ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectTriggerGroupsFiltered =
            string.Format(CultureInfo.InvariantCulture, "SELECT DISTINCT({0}) FROM {1}{2} WHERE {3} = {4} AND {0} LIKE @triggerGroup",
            ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst);

        public static readonly string SqlSelectTriggerState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = {4} AND {5} = @triggerName AND {6} = @triggerGroup", 
                ColumnTriggerState, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerStatus =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1}, {2}, {3} FROM {4}{5} WHERE {6} = {7} AND {8} = @triggerName AND {9} = @triggerGroup",
                          ColumnTriggerState, ColumnNextFireTime, ColumnJobName, ColumnJobGroup, 
                          TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggersForCalendar =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} = @calendarName",
                ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnCalendarName);

        public static readonly string SqlSelectTriggersForJob =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} = @jobName AND {7} = @jobGroup",
                ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectTriggersInGroupLike =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} LIKE @triggerGroup",
                ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggersInGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} = @triggerGroup",
                ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggersInState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = {5} AND {6} = @state", 
                ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerState);

        // UPDATE 

        public static readonly string SqlUpdateBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @blob WHERE {3} = {4} AND {5} = @triggerName AND {6} = @triggerGroup",
                TablePrefixSubst, TableBlobTriggers, ColumnBlob, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlUpdateCalendar =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @calendar WHERE {3} = {4} AND {5} = @calendarName",
                TablePrefixSubst, TableCalendars, ColumnCalendar, ColumnSchedulerName, SchedulerNameSubst, ColumnCalendarName);

        public static readonly string SqlUpdateCronTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @triggerCronExpression, {3} = @timeZoneId WHERE {4} = {5} AND {6} = @triggerName AND {7} = @triggerGroup",
                TablePrefixSubst, TableCronTriggers, ColumnCronExpression, ColumnTimeZoneId, ColumnSchedulerName, SchedulerNameSubst, 
                ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlUpdateJobData =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @jobDataMap WHERE {3} = {4} AND {5} = @jobName AND {6} = @jobGroup",
                          TablePrefixSubst, TableJobDetails, ColumnJobDataMap, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlUpdateJobDetail =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @jobDescription, {3} = @jobType, {4} = @jobDurable, {5} = @jobVolatile, {6} = @jobStateful, {7} = @jobRequestsRecovery, {8} = @jobDataMap  WHERE {9} = {10} AND {11} = @jobName AND {12} = @jobGroup",
                TablePrefixSubst, TableJobDetails, ColumnDescription,
                ColumnJobClass, ColumnIsDurable, ColumnIsNonConcurrent,
                ColumnIsUpdateData, ColumnRequestsRecovery, ColumnJobDataMap,
                ColumnSchedulerName, SchedulerNameSubst,
                ColumnJobName, ColumnJobGroup);

        public static readonly string SqlUpdateJobTriggerStates =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @state WHERE {3} = {4} AND {5} = @jobName AND {6} = @jobGroup", TablePrefixSubst,
                          TableTriggers, ColumnTriggerState, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlUpdateJobTriggerStatesFromOtherState =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @state WHERE {3} = {4} AND {5} = @jobName AND {6} = @jobGroup AND {7} = @oldState",
                TablePrefixSubst,
                TableTriggers, ColumnTriggerState, ColumnSchedulerName, SchedulerNameSubst, ColumnJobName, ColumnJobGroup, ColumnTriggerState);

        public static readonly string SqlUpdateSchedulerState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @lastCheckinTime WHERE {3} = {4} AND {5} = @instanceName",
                          TablePrefixSubst, TableSchedulerState, ColumnLastCheckinTime, ColumnSchedulerName, SchedulerNameSubst, ColumnInstanceName);

        public static readonly string SqlUpdateSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @triggerRepeatCount, {3} = @triggerRepeatInterval, {4} = @triggerTimesTriggered WHERE {5} = {6} AND {7} = @triggerName AND {8} = @triggerGroup",
                TablePrefixSubst, TableSimpleTriggers, ColumnRepeatCount,
                ColumnRepeatInterval, ColumnTimesTriggered, ColumnSchedulerName, SchedulerNameSubst,
                ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlUpdateTrigger =
            string.Format(CultureInfo.InvariantCulture,
                @"UPDATE {0}{1} SET {2} = @triggerJobName, {3} = @triggerJobGroup, {4} = @triggerDescription, {5} = @triggerNextFireTime, {6} = @triggerPreviousFireTime,
                        {7} = @triggerState, {8} = @triggerType, {9} = @triggerStartTime, {10} = @triggerEndTime, {11} = @triggerCalendarName, {12} = @triggerMisfireInstruction, {13} = @triggerPriority, {14} = @triggerJobJobDataMap
                        WHERE {15} = {16} AND {17} = @triggerName AND {18} = @triggerGroup",
                TablePrefixSubst, TableTriggers, ColumnJobName,
                ColumnJobGroup, ColumnDescription,
                ColumnNextFireTime, ColumnPreviousFireTime, ColumnTriggerState,
                ColumnTriggerType, ColumnStartTime, ColumnEndTime,
                ColumnCalendarName, ColumnMifireInstruction, ColumnPriority, ColumnJobDataMap,
                ColumnSchedulerName, SchedulerNameSubst,
                ColumnTriggerName, ColumnTriggerGroup);


        public static readonly string SqlUpdateFiredTrigger = string.Format(
            CultureInfo.InvariantCulture,
            "UPDATE {0}{1} SET {2} = @instanceName, {3} = @firedTime, {12} = @scheduledTime, {4} = @entryState, {5} = @jobName, {6} = @jobGroup, {7} = @isNonConcurrent, {8} = @requestsRecover WHERE {9} = {10} AND {11} = @entryId", 
            TablePrefixSubst, TableFiredTriggers, ColumnInstanceName, ColumnFiredTime, ColumnEntryState, 
            ColumnJobName, ColumnJobGroup, ColumnIsNonConcurrent, ColumnRequestsRecovery, ColumnSchedulerName, SchedulerNameSubst, ColumnEntryId, ColumnScheduledTime);

        public static readonly string SqlUpdateTriggerGroupStateFromState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @newState WHERE {3} = {4} AND {5} LIKE @triggerGroup AND {6} = @oldState",
                          TablePrefixSubst,
                          TableTriggers, ColumnTriggerState, ColumnSchedulerName, SchedulerNameSubst,
                          ColumnTriggerGroup, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerGroupStateFromStates =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @newState WHERE {3} = {4} AND {5} LIKE @groupName AND ({6} = @oldState1 OR {7} = @oldState2 OR {8} = @oldState3)",
                TablePrefixSubst, TableTriggers, ColumnTriggerState, ColumnSchedulerName, SchedulerNameSubst,
                ColumnTriggerGroup, ColumnTriggerState,
                ColumnTriggerState, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerSkipData =
            string.Format(CultureInfo.InvariantCulture,
                @"UPDATE {0}{1} SET {2} = @triggerJobName, {3} = @triggerJobGroup, {4} = @triggerDescription, {5} = @triggerNextFireTime, {6} = @triggerPreviousFireTime, 
                        {7} = @triggerState, {8} = @triggerType, {9} = @triggerStartTime, {10} = @triggerEndTime, {11} = @triggerCalendarName, {12} = @triggerMisfireInstruction, {13} = @triggerPriority
                    WHERE {14} = {15} AND {16} = @triggerName AND {17} = @triggerGroup",
                TablePrefixSubst, TableTriggers, ColumnJobName,
                ColumnJobGroup, ColumnDescription,
                ColumnNextFireTime, ColumnPreviousFireTime, ColumnTriggerState,
                ColumnTriggerType, ColumnStartTime, ColumnEndTime,
                ColumnCalendarName, ColumnMifireInstruction, ColumnPriority, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName,
                ColumnTriggerGroup);

        public static readonly string SqlUpdateTriggerState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @state WHERE {3} = {4} AND {5} = @triggerName AND {6} = @triggerGroup",
                          TablePrefixSubst, TableTriggers, ColumnTriggerState, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlUpdateTriggerStateFromState =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @newState WHERE {3} = {4} AND {5} = @triggerName AND {6} = @triggerGroup AND {7} = @oldState",
                TablePrefixSubst,
                TableTriggers, ColumnTriggerState, ColumnSchedulerName, SchedulerNameSubst, ColumnTriggerName, ColumnTriggerGroup, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerStateFromStates =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @newState WHERE {3} = {4} AND {5} = @triggerName AND {6} = @triggerGroup AND ({7} = @oldState1 OR {8} = @oldState2 OR {9} = @oldState3)",
                TablePrefixSubst, TableTriggers, ColumnTriggerState, ColumnSchedulerName, SchedulerNameSubst,
                ColumnTriggerName, ColumnTriggerGroup, ColumnTriggerState,
                ColumnTriggerState, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerStatesFromOtherStates =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @newState WHERE {3} = {4} AND ({5} = @oldState1 OR {6} = @oldState2)",
                          TablePrefixSubst,
                          TableTriggers, ColumnTriggerState,
                          ColumnSchedulerName, SchedulerNameSubst,
                          ColumnTriggerState, ColumnTriggerState);
    }
}