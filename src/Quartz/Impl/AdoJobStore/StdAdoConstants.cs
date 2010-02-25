#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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

        // DELETE
        public static readonly string SqlDeleteBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableBlobTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlDeleteCalendar =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @calendarName", TablePrefixSubst, TableCalendars,
                          ColumnCalendarName);

        public static readonly string SqlDeleteCronTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableCronTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlDeleteFiredTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @triggerEntryId", TablePrefixSubst, TableFiredTriggers,
                          ColumnEntryId);

        public static readonly string SqlDeleteFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1}", TablePrefixSubst, TableFiredTriggers);

        public static readonly string SqlDeleteInstancesFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @instanceName", TablePrefixSubst, TableFiredTriggers,
                          ColumnInstanceName);

        public static readonly string SqlDeleteJobDetail =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @jobName AND {3} = @jobGroup", TablePrefixSubst,
                          TableJobDetails, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlDeleteJobListeners =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @jobName AND {3} = @jobGroup", TablePrefixSubst,
                          TableJobListeners, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlDeleteNoRecoveryFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @instanceName AND {3} = @requestsRecovery", TablePrefixSubst,
                          TableFiredTriggers, ColumnInstanceName,
                          ColumnRequestsRecovery);

        public static readonly string SqlDeletePausedTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @triggerGroup", TablePrefixSubst, TablePausedTriggers,
                          ColumnTriggerGroup);

        public static readonly string SqlDeletePausedTriggerGroups =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1}", TablePrefixSubst, TablePausedTriggers);

        public static readonly string SqlDeleteSchedulerState =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @instanceName", TablePrefixSubst, TableSchedulerState,
                          ColumnInstanceName);

        public static readonly string SqlDeleteSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableSimpleTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlDeleteTrigger =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableTriggers, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlDeleteTriggerListeners =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableTriggerListeners, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlDeleteVolatileFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "DELETE FROM {0}{1} WHERE {2} = @volatile", TablePrefixSubst, TableFiredTriggers,
                          ColumnIsVolatile);

        // INSERT

        public static readonly string SqlInsertBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}, {3}, {4})  VALUES(@triggerName, @triggerGroup, @blob)",
                          TablePrefixSubst,
                          TableBlobTriggers, ColumnTriggerName,
                          ColumnTriggerGroup, ColumnBlob);

        public static readonly string SqlInsertCalendar =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}, {3})  VALUES(@calendarName, @calendar)", TablePrefixSubst,
                          TableCalendars, ColumnCalendarName, ColumnCalendar);

        public static readonly string SqlInsertCronTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5})  VALUES(@triggerName, @triggerGroup, @triggerCronExpression, @triggerTimeZone)",
                TablePrefixSubst,
                TableCronTriggers, ColumnTriggerName,
                ColumnTriggerGroup, ColumnCronExpression,
                ColumnTimeZoneId);

        public static readonly string SqlInsertFiredTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}) VALUES(@triggerEntryId, @triggerName, @triggerGroup, @triggerVolatile, @triggerInstanceName, @triggerFireTime, @triggerState, @triggerJobName, @triggerJobGroup, @triggerJobStateful, @triggerJobRequestsRecovery, @triggerPriority)",
                TablePrefixSubst, TableFiredTriggers, ColumnEntryId,
                ColumnTriggerName, ColumnTriggerGroup, ColumnIsVolatile,
                ColumnInstanceName, ColumnFiredTime, ColumnEntryState,
                ColumnJobName, ColumnJobGroup, ColumnIsStateful,
                ColumnRequestsRecovery, ColumnPriority);

        public static readonly string SqlInsertJobDetail =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})  VALUES(@jobName, @jobGroup, @jobDescription, @jobType, @jobDurable, @jobVolatile, @jobStateful, @jobRequestsRecovery, @jobDataMap)",
                TablePrefixSubst, TableJobDetails, ColumnJobName,
                ColumnJobGroup, ColumnDescription, ColumnJobClass,
                ColumnIsDurable, ColumnIsVolatile, ColumnIsStateful,
                ColumnRequestsRecovery, ColumnJobDataMap);

        public static readonly string SqlInsertJobListener =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}, {3}, {4}) VALUES(@jobName, @jobGroup, @listener)",
                          TablePrefixSubst,
                          TableJobListeners, ColumnJobName, ColumnJobGroup,
                          ColumnJobListener);

        public static readonly string SqlInsertPausedTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}) VALUES (@triggerGroup)", TablePrefixSubst,
                          TablePausedTriggers, ColumnTriggerGroup);

        public static readonly string SqlInsertSchedulerState =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}) VALUES(@instanceName, @lastCheckinTime, @checkinInterval)",
                TablePrefixSubst,
                TableSchedulerState, ColumnInstanceName,
                ColumnLastCheckinTime, ColumnCheckinInterval);

        public static readonly string SqlInsertSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6})  VALUES(@triggerName, @triggerGroup, @triggerRepeatCount, @triggerRepeatInterval, @triggerTimesTriggered)",
                TablePrefixSubst,
                TableSimpleTriggers, ColumnTriggerName,
                ColumnTriggerGroup, ColumnRepeatCount,
                ColumnRepeatInterval, ColumnTimesTriggered);

        public static readonly string SqlInsertTrigger =
            string.Format(CultureInfo.InvariantCulture,
                @"INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17})  
                        VALUES(@triggerName, @triggerGroup, @triggerJobName, @triggerJobGroup, @triggerVolatile, @triggerDescription, @triggerNextFireTime, @triggerPreviousFireTime, @triggerState, @triggerType, @triggerStartTime, @triggerEndTime, @triggerCalendarName, @triggerMisfireInstruction, @triggerJobJobDataMap, @triggerPriority)",
                TablePrefixSubst, TableTriggers, ColumnTriggerName,
                ColumnTriggerGroup, ColumnJobName, ColumnJobGroup,
                ColumnIsVolatile, ColumnDescription, ColumnNextFireTime,
                ColumnPreviousFireTime, ColumnTriggerState, ColumnTriggerType,
                ColumnStartTime, ColumnEndTime, ColumnCalendarName,
                ColumnMifireInstruction, ColumnJobDataMap, ColumnPriority);

        public static readonly string SqlInsertTriggerListener =
            string.Format(CultureInfo.InvariantCulture, "INSERT INTO {0}{1} ({2}, {3}, {4}) VALUES(@triggerName, @triggerGroup, @listener)",
                          TablePrefixSubst,
                          TableTriggerListeners, ColumnTriggerName,
                          ColumnTriggerGroup, ColumnTriggerListener);

        // SELECT

        public static readonly string SqlSelectBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableBlobTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectCalendar =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @calendarName", TablePrefixSubst, TableCalendars,
                          ColumnCalendarName);

        public static readonly string SqlSelectCalendarExistence =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @calendarName", ColumnCalendarName, TablePrefixSubst,
                          TableCalendars, ColumnCalendarName);

        public static readonly string SqlSelectCalendars =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2}", ColumnCalendarName, TablePrefixSubst,
                          TableCalendars);

        public static readonly string SqlSelectCronTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableCronTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectFiredTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableFiredTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectFiredTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @triggerGroup", TablePrefixSubst,
                          TableFiredTriggers, ColumnTriggerGroup);

        public static readonly string SqlSelectFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1}", TablePrefixSubst, TableFiredTriggers);

        public static readonly string SqlSelectFiredTriggerInstanceNames =
        string.Format(CultureInfo.InvariantCulture, "SELECT DISTINCT {0} FROM {1}{2}", ColumnInstanceName, TablePrefixSubst, TableFiredTriggers);

        public static readonly string SqlSelectFiredTriggersOfJob =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @jobName AND {3} = @jobGroup", TablePrefixSubst,
                          TableFiredTriggers, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectFiredTriggersOfJobGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @jobGroup", TablePrefixSubst,
                          TableFiredTriggers, ColumnJobGroup);

        public static readonly string SqlSelectInstancesFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @instanceName", TablePrefixSubst,
                          TableFiredTriggers, ColumnInstanceName);

        public static readonly string SqlSelectInstancesRecoverableFiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @instanceName AND {3} = @requestsRecovery",
                          TablePrefixSubst,
                          TableFiredTriggers, ColumnInstanceName,
                          ColumnRequestsRecovery);

        public static readonly string SqlSelectJobDetail =
            string.Format(CultureInfo.InvariantCulture,
                "SELECT {0},{1},{2},{3},{4},{5},{6},{7},{8} FROM {9}{10} WHERE {11} = @jobName AND {12} = @jobGroup",
                ColumnJobName, ColumnJobGroup, ColumnDescription, ColumnJobClass,
                ColumnIsDurable, ColumnIsVolatile, ColumnIsStateful, ColumnRequestsRecovery,
                ColumnJobDataMap, TablePrefixSubst, TableJobDetails, ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectJobExecutionCount =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0}) FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", ColumnTriggerName,
                          TablePrefixSubst, TableFiredTriggers, ColumnJobName,
                          ColumnJobGroup);

        public static readonly string SqlSelectJobExistence =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", ColumnJobName,
                          TablePrefixSubst, TableJobDetails, ColumnJobName,
                          ColumnJobGroup);

        public static readonly string SqlSelectJobForTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "SELECT J.{0}, J.{1}, J.{2}, J.{3}, J.{4} FROM {5}{6} T, {7}{8} J WHERE T.{9} = @triggerName AND T.{10} = @triggerGroup AND T.{11} = J.{12} AND T.{13} = J.{14}",
                ColumnJobName, ColumnJobGroup, ColumnIsDurable,
                ColumnJobClass, ColumnRequestsRecovery, TablePrefixSubst,
                TableTriggers, TablePrefixSubst, TableJobDetails,
                ColumnTriggerName, ColumnTriggerGroup, ColumnJobName,
                ColumnJobName, ColumnJobGroup, ColumnJobGroup);

        public static readonly string SqlSelectJobGroups =
            string.Format(CultureInfo.InvariantCulture, "SELECT DISTINCT({0}) FROM {1}{2}", ColumnJobGroup, TablePrefixSubst,
                          TableJobDetails);

        public static readonly string SqlSelectJobListeners =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", ColumnJobListener,
                          TablePrefixSubst, TableJobListeners, ColumnJobName,
                          ColumnJobGroup);

        public static readonly string SqlSelectJobStateful =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", ColumnIsStateful,
                          TablePrefixSubst, TableJobDetails, ColumnJobName,
                          ColumnJobGroup);

        public static readonly string SqlSelectJobsInGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @jobGroup", ColumnJobName, TablePrefixSubst,
                          TableJobDetails, ColumnJobGroup);

        public static readonly string SqlSelectMisfiredTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} < @nextFireTime ORDER BY {3} ASC", TablePrefixSubst,
                          TableTriggers, ColumnNextFireTime, ColumnNextFireTime);

        public static readonly string SqlSelectMisfiredTriggersInGroupInState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} < @nextFireTime AND {4} = @triggerGroup AND {5} = @state ORDER BY {6} ASC",
                          ColumnTriggerName, TablePrefixSubst, TableTriggers,
                          ColumnNextFireTime, ColumnTriggerGroup,
                          ColumnTriggerState, ColumnNextFireTime);

        public static readonly string SqlSelectMisfiredTriggersInState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} < @nextFireTime AND {5} = @state ORDER BY {6} ASC", ColumnTriggerName,
                          ColumnTriggerGroup, TablePrefixSubst, TableTriggers,
                          ColumnNextFireTime, ColumnTriggerState, ColumnNextFireTime);

        public static readonly string SqlCountMisfiredTriggersInStates = 
            string.Format("SELECT COUNT({0}) FROM {1}{2} WHERE {3} < @nextFireTime AND (({4} = @state1) OR ({5} = @state2))", 
            ColumnTriggerName, TablePrefixSubst, TableTriggers, ColumnNextFireTime, ColumnTriggerState, ColumnTriggerState);

        public static readonly string SqlSelectMisfiredTriggersInStates =
            string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} < @nextFireTime AND (({5} = @state1) OR ({6} = @state2)) ORDER BY {7} ASC",
            ColumnTriggerName, ColumnTriggerGroup, TablePrefixSubst, TableTriggers, ColumnNextFireTime, ColumnTriggerState, ColumnTriggerState, ColumnNextFireTime);

        public static readonly string SqlSelectNextFireTime =
            string.Format(CultureInfo.InvariantCulture, "SELECT MIN({0}) AS {1} FROM {2}{3} WHERE {4} = @state AND {5} >= 0",
                          ColumnNextFireTime, AliasColumnNextFireTime, TablePrefixSubst,
                          TableTriggers, ColumnTriggerState,
                          ColumnNextFireTime);

        public static readonly string SqlSelectNextTriggerToAcquire =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1}, {2}, {9} FROM {3}{4} WHERE {5} = @state AND {6} < @noLaterThan AND ({7} >= @noEarlierThan) ORDER BY {8} ASC, {9} DESC", 
            ColumnTriggerName, ColumnTriggerGroup, ColumnNextFireTime, 
            TablePrefixSubst, TableTriggers, 
            ColumnTriggerState, ColumnNextFireTime, 
            ColumnNextFireTime, ColumnNextFireTime, 
            ColumnPriority);

        public static readonly string SqlSelectNumCalendars =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2}", ColumnCalendarName, TablePrefixSubst,
                          TableCalendars);

        public static readonly string SqlSelectNumJobs =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2}", ColumnJobName, TablePrefixSubst,
                          TableJobDetails);

        public static readonly string SqlSelectNumTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2}", ColumnTriggerName, TablePrefixSubst,
                          TableTriggers);

        public static readonly string SqlSelectNumTriggersForJob =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0}) FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", ColumnTriggerName,
                          TablePrefixSubst, TableTriggers, ColumnJobName,
                          ColumnJobGroup);

        public static readonly string SqlSelectNumTriggersInGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT COUNT({0})  FROM {1}{2} WHERE {3} = @triggerGroup", ColumnTriggerName,
                          TablePrefixSubst, TableTriggers, ColumnTriggerGroup);

        public static readonly string SqlSelectPausedTriggerGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @triggerGroup", ColumnTriggerGroup, TablePrefixSubst,
                          TablePausedTriggers, ColumnTriggerGroup);

        public static readonly string SqlSelectPausedTriggerGroups =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2}", ColumnTriggerGroup, TablePrefixSubst,
                          TablePausedTriggers);

        public static readonly string SqlSelectReferencedCalendar =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @calendarName", ColumnCalendarName, TablePrefixSubst,
                          TableTriggers, ColumnCalendarName);

        public static readonly string SqlSelectSchedulerState =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @instanceName", TablePrefixSubst,
                          TableSchedulerState, ColumnInstanceName);

        public static readonly string SqlSelectSchedulerStates =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1}", TablePrefixSubst, TableSchedulerState);

        public static readonly string SqlSelectSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableSimpleTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectStatefulJobsOfTriggerGroup =
            string.Format(
                CultureInfo.InvariantCulture,
                "SELECT DISTINCT J.{0}, J.{1} FROM {2}{3} T, {4}{5} J WHERE T.{6} = @triggerGroup AND T.{7} = J.{8} AND T.{9} = J.{10} AND J.{11} = @stateful",
                ColumnJobName, ColumnJobGroup, TablePrefixSubst,
                TableTriggers, TablePrefixSubst, TableJobDetails,
                ColumnTriggerGroup, ColumnJobName, ColumnJobName,
                ColumnJobGroup, ColumnJobGroup, ColumnIsStateful);

        public static readonly string SqlSelectTrigger =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TablePrefixSubst,
                          TableTriggers, ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerData =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup", ColumnJobDataMap,
                          TablePrefixSubst, TableTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerExistence =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup", ColumnTriggerName,
                          TablePrefixSubst, TableTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerForFireTime =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = @state AND {5} = @nextFireTime", ColumnTriggerName,
                          ColumnTriggerGroup, TablePrefixSubst, TableTriggers,
                          ColumnTriggerState, ColumnNextFireTime);

        public static readonly string SqlSelectTriggerGroups =
            string.Format(CultureInfo.InvariantCulture, "SELECT DISTINCT({0}) FROM {1}{2}", ColumnTriggerGroup, TablePrefixSubst,
                          TableTriggers);

        public static readonly string SqlSelectTriggerListeners =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup",
                          ColumnTriggerListener,
                          TablePrefixSubst, TableTriggerListeners, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup", ColumnTriggerState,
                          TablePrefixSubst, TableTriggers, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlSelectTriggerStatus =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1}, {2}, {3} FROM {4}{5} WHERE {6} = @triggerName AND {7} = @triggerGroup",
                          ColumnTriggerState, ColumnNextFireTime, ColumnJobName,
                          ColumnJobGroup, TablePrefixSubst, TableTriggers,
                          ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggersForCalendar =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = @calendarName", ColumnTriggerName,
                          ColumnTriggerGroup, TablePrefixSubst, TableTriggers,
                          ColumnCalendarName);

        public static readonly string SqlSelectTriggersForJob =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = @jobName AND {5} = @jobGroup", ColumnTriggerName,
                          ColumnTriggerGroup, TablePrefixSubst, TableTriggers,
                          ColumnJobName, ColumnJobGroup);

        public static readonly string SqlSelectTriggersInGroup =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0} FROM {1}{2} WHERE {3} = @triggerGroup", ColumnTriggerName, TablePrefixSubst,
                          TableTriggers, ColumnTriggerGroup);

        public static readonly string SqlSelectTriggersInState =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = @state", ColumnTriggerName,
                          ColumnTriggerGroup, TablePrefixSubst, TableTriggers,
                          ColumnTriggerState);

        public static readonly string SqlSelectVolatileJobs =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = @volatile", ColumnJobName,
                          ColumnJobGroup, TablePrefixSubst, TableJobDetails,
                          ColumnIsVolatile);

        public static readonly string SqlSelectVolatileTriggers =
            string.Format(CultureInfo.InvariantCulture, "SELECT {0}, {1} FROM {2}{3} WHERE {4} = @volatile", ColumnTriggerName,
                          ColumnTriggerGroup, TablePrefixSubst, TableTriggers,
                          ColumnIsVolatile);

        // UPDATE 

        public static readonly string SqlUpdateBlobTrigger =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @blob WHERE {3} = @triggerName AND {4} = @triggerGroup",
                          TablePrefixSubst,
                          TableBlobTriggers, ColumnBlob, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlUpdateCalendar =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @calendar  WHERE {3} = @calendarName", TablePrefixSubst,
                          TableCalendars, ColumnCalendar, ColumnCalendarName);

        public static readonly string SqlUpdateCronTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @triggerCronExpression WHERE {3} = @triggerName AND {4} = @triggerGroup",
                TablePrefixSubst,
                TableCronTriggers, ColumnCronExpression,
                ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlUpdateInstancesFiredTriggerState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @triggerEntryState AND {3} = @firedTime AND {4} = @priority WHERE {5} = @instanceName",
                          TablePrefixSubst,
                          TableFiredTriggers, ColumnEntryState,
                          ColumnFiredTime, ColumnPriority, ColumnInstanceName);

        public static readonly string SqlUpdateJobData =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @jobDataMap  WHERE {3} = @jobName AND {4} = @jobGroup",
                          TablePrefixSubst,
                          TableJobDetails, ColumnJobDataMap, ColumnJobName,
                          ColumnJobGroup);

        public static readonly string SqlUpdateJobDetail =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @jobDescription, {3} = @jobType, {4} = @jobDurable, {5} = @jobVolatile, {6} = @jobStateful, {7} = @jobRequestsRecovery, {8} = @jobDataMap  WHERE {9} = @jobName AND {10} = @jobGroup",
                TablePrefixSubst, TableJobDetails, ColumnDescription,
                ColumnJobClass, ColumnIsDurable, ColumnIsVolatile,
                ColumnIsStateful, ColumnRequestsRecovery, ColumnJobDataMap,
                ColumnJobName, ColumnJobGroup);

        public static readonly string SqlUpdateJobTriggerStates =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @state WHERE {3} = @jobName AND {4} = @jobGroup", TablePrefixSubst,
                          TableTriggers, ColumnTriggerState, ColumnJobName,
                          ColumnJobGroup);

        public static readonly string SqlUpdateJobTriggerStatesFromOtherState =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @state WHERE {3} = @jobName AND {4} = @jobGroup AND {5} = @oldState",
                TablePrefixSubst,
                TableTriggers, ColumnTriggerState, ColumnJobName,
                ColumnJobGroup, ColumnTriggerState);

        public static readonly string SqlUpdateSchedulerState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @lastCheckinTime WHERE {3} = @instanceName",
                          TablePrefixSubst,
                          TableSchedulerState, ColumnLastCheckinTime,
                          ColumnInstanceName);

        public static readonly string SqlUpdateSimpleTrigger =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @triggerRepeatCount, {3} = @triggerRepeatInterval, {4} = @triggerTimesTriggered WHERE {5} = @triggerName AND {6} = @triggerGroup",
                TablePrefixSubst, TableSimpleTriggers, ColumnRepeatCount,
                ColumnRepeatInterval, ColumnTimesTriggered,
                ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlUpdateTrigger =
            string.Format(CultureInfo.InvariantCulture,
                @"UPDATE {0}{1} SET {2} = @triggerJobName, {3} = @triggerJobGroup, {4} = @triggerVolatile, {5} = @triggerDescription, {6} = @triggerNextFireTime, {7} = @triggerPreviousFireTime,
                        {8} = @triggerState, {9} = @triggerType, {10} = @triggerStartTime, {11} = @triggerEndTime, {12} = @triggerCalendarName, {13} = @triggerMisfireInstruction, {14} = @triggerPriority, {15} = @triggerJobJobDataMap
                        WHERE {16} = @triggerName AND {17} = @triggerGroup",
                TablePrefixSubst, TableTriggers, ColumnJobName,
                ColumnJobGroup, ColumnIsVolatile, ColumnDescription,
                ColumnNextFireTime, ColumnPreviousFireTime, ColumnTriggerState,
                ColumnTriggerType, ColumnStartTime, ColumnEndTime,
                ColumnCalendarName, ColumnMifireInstruction, ColumnPriority, ColumnJobDataMap,
                ColumnTriggerName, ColumnTriggerGroup);

        public static readonly string SqlUpdateTriggerGroupState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @state", TablePrefixSubst, TableTriggers,
                          ColumnTriggerState);

        public static readonly string SqlTriggerGroupStateFromState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @newState WHERE {3} = @triggerGroup AND {4} = @oldState",
                          TablePrefixSubst,
                          TableTriggers, ColumnTriggerState,
                          ColumnTriggerGroup, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerGroupStateFromStates =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @newState WHERE {3} = @groupName AND ({4} = @oldState1 OR {5} = @oldState2 OR {6} = @oldState3)",
                TablePrefixSubst, TableTriggers, ColumnTriggerState,
                ColumnTriggerGroup, ColumnTriggerState,
                ColumnTriggerState, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerSkipData =
            string.Format(CultureInfo.InvariantCulture,
                @"UPDATE {0}{1} SET {2} = @triggerJobName, {3} = @triggerJobGroup, {4} = @triggerVolatile, {5} = @triggerDescription, {6} = @triggerNextFireTime, {7} = @triggerPreviousFireTime, 
                        {8} = @triggerState, {9} = @triggerType, {10} = @triggerStartTime, {11} = @triggerEndTime, {12} = @triggerCalendarName, {13} = @triggerMisfireInstruction, {14} = @triggerPriority
                    WHERE {15} = @triggerName AND {16} = @triggerGroup",
                TablePrefixSubst, TableTriggers, ColumnJobName,
                ColumnJobGroup, ColumnIsVolatile, ColumnDescription,
                ColumnNextFireTime, ColumnPreviousFireTime, ColumnTriggerState,
                ColumnTriggerType, ColumnStartTime, ColumnEndTime,
                ColumnCalendarName, ColumnMifireInstruction, ColumnPriority, ColumnTriggerName,
                ColumnTriggerGroup);

        public static readonly string SqlUpdateTriggerState =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @state WHERE {3} = @triggerName AND {4} = @triggerGroup",
                          TablePrefixSubst,
                          TableTriggers, ColumnTriggerState, ColumnTriggerName,
                          ColumnTriggerGroup);

        public static readonly string SqlUpdateTriggerStateFromOtherStatesBeforeTime =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @newState WHERE ({3} = @oldState1 OR {4} = @oldState2) AND {5} < @time",
                TablePrefixSubst,
                TableTriggers, ColumnTriggerState,
                ColumnTriggerState, ColumnTriggerState,
                ColumnNextFireTime);

        public static readonly string SqlUpdateTriggerStateFromState =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @newState WHERE {3} = @triggerName AND {4} = @triggerGroup AND {5} = @oldState",
                TablePrefixSubst,
                TableTriggers, ColumnTriggerState, ColumnTriggerName,
                ColumnTriggerGroup, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerStateFromStates =
            string.Format(CultureInfo.InvariantCulture,
                "UPDATE {0}{1} SET {2} = @newState WHERE {3} = @triggerName AND {4} = @triggerGroup AND ({5} = @oldState1 OR {6} = @oldState2 OR {7} = @oldState3)",
                TablePrefixSubst, TableTriggers, ColumnTriggerState,
                ColumnTriggerName, ColumnTriggerGroup, ColumnTriggerState,
                ColumnTriggerState, ColumnTriggerState);

        public static readonly string SqlUpdateTriggerStatesFromOtherStates =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = @newState WHERE {3} = @oldState1 OR {4} = @oldState2",
                          TablePrefixSubst,
                          TableTriggers, ColumnTriggerState,
                          ColumnTriggerState, ColumnTriggerState);
    }
}