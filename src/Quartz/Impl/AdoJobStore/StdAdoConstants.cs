/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This interface extends <code>{@link
    /// Constants}</code>
    /// to include the query string constants in use by the <code>{@link
    /// StdAdoDelegate}</code>
    /// class.
    /// </summary>
    /// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    public class StdAdoConstants
    {
        public const string TABLE_PREFIX_SUBST = "{0}";

        // DELETE
        public static readonly string DELETE_BLOB_TRIGGER =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string DELETE_CALENDAR =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @calendarName", TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS,
                              AdoConstants.COL_CALENDAR_NAME);

        public static readonly string DELETE_CRON_TRIGGER =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string DELETE_FIRED_TRIGGER =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @triggerEntryId", TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS,
                              AdoConstants.COL_ENTRY_ID);
        
        public static readonly string DELETE_FIRED_TRIGGERS =
                string.Format("DELETE FROM {0}{1}", TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS);

        public static readonly string DELETE_INSTANCES_FIRED_TRIGGERS =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @instanceName", TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS,
                              AdoConstants.COL_INSTANCE_NAME);
        
        public static readonly string DELETE_JOB_DETAIL =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @jobName AND {3} = @jobGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
        
        public static readonly string DELETE_JOB_LISTENERS =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @jobName AND {3} = @jobGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_JOB_LISTENERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);

        public static readonly string DELETE_NO_RECOVERY_FIRED_TRIGGERS =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @instanceName AND {3} = @requestsRecovery", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_INSTANCE_NAME,
                              AdoConstants.COL_REQUESTS_RECOVERY);
        
        public static readonly string DELETE_PAUSED_TRIGGER_GROUP =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @triggerGroup", TABLE_PREFIX_SUBST, AdoConstants.TABLE_PAUSED_TRIGGERS,
                              AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string DELETE_PAUSED_TRIGGER_GROUPS =
                string.Format("DELETE FROM {0}{1}", TABLE_PREFIX_SUBST, AdoConstants.TABLE_PAUSED_TRIGGERS);

        public static readonly string DELETE_SCHEDULER_STATE =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @instanceName", TABLE_PREFIX_SUBST, AdoConstants.TABLE_SCHEDULER_STATE,
                              AdoConstants.COL_INSTANCE_NAME);

        public static readonly string DELETE_SIMPLE_TRIGGER =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @trigggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string DELETE_TRIGGER =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string DELETE_TRIGGER_LISTENERS =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGER_LISTENERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string DELETE_VOLATILE_FIRED_TRIGGERS =
                string.Format("DELETE FROM {0}{1} WHERE {2} = @volatile", TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS,
                              AdoConstants.COL_IS_VOLATILE);

        // INSERT

        public static readonly string INSERT_BLOB_TRIGGER =
                string.Format("INSERT INTO {0}{1} ({2}, {3}, {4})  VALUES(@triggerName, @triggerGroup, @blob)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_BLOB);

        public static readonly string INSERT_CALENDAR =
                string.Format("INSERT INTO {0}{1} ({2}, {3})  VALUES(@calendarName, @calendar)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR_NAME, AdoConstants.COL_CALENDAR);

        public static readonly string INSERT_CRON_TRIGGER =
                string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5})  VALUES(@triggerName, @triggerGroup, @triggerCronExpression, @triggerTimeZone)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_CRON_EXPRESSION,
                              AdoConstants.COL_TIME_ZONE_ID);
        
        public static readonly string INSERT_FIRED_TRIGGER =
                string.Format(
                    "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}) VALUES(@triggerEntryId, @triggerName, @triggerGroup, @triggerVolatile, @triggerInstanceName, @triggerFiredTime, @triggerState, @triggerJobName, @triggerJobGroup, @triggerStateful, @triggerRequestsRecovery)",
                    TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_ENTRY_ID,
                    AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_IS_VOLATILE,
                    AdoConstants.COL_INSTANCE_NAME, AdoConstants.COL_FIRED_TIME, AdoConstants.COL_ENTRY_STATE,
                    AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_STATEFUL,
                    AdoConstants.COL_REQUESTS_RECOVERY);
        
        public static readonly string INSERT_JOB_DETAIL =
                string.Format(
                    "INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})  VALUES(@jobName, @jobGroup, @jobDescription, @jobType, @jobDurable, @jobVolatile, @jobStateful, @jobRequestsRecovery, @jobDataMap)",
                    TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME,
                    AdoConstants.COL_JOB_GROUP, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_JOB_CLASS,
                    AdoConstants.COL_IS_DURABLE, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_IS_STATEFUL,
                    AdoConstants.COL_REQUESTS_RECOVERY, AdoConstants.COL_JOB_DATAMAP);
        
        public static readonly string INSERT_JOB_LISTENER =
                string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}) VALUES(@jobName, @jobGroup, @listener)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_JOB_LISTENERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP,
                              AdoConstants.COL_JOB_LISTENER);
        
        public static readonly string INSERT_PAUSED_TRIGGER_GROUP =
                string.Format("INSERT INTO {0}{1} ({2}) VALUES (@triggerGroup)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_PAUSED_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string INSERT_SCHEDULER_STATE =
                string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5}) VALUES(@instanceName, @lastCheckinTime, @checkinInterval, @recoverer)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_SCHEDULER_STATE, AdoConstants.COL_INSTANCE_NAME,
                              AdoConstants.COL_LAST_CHECKIN_TIME, AdoConstants.COL_CHECKIN_INTERVAL,
                              AdoConstants.COL_RECOVERER);

        public static readonly string INSERT_SIMPLE_TRIGGER =
                string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6})  VALUES(@triggerName, @triggerGroup, @triggerRepeatCount, @triggerRepeatInterval, @triggerTimesTriggered)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_REPEAT_COUNT,
                              AdoConstants.COL_REPEAT_INTERVAL, AdoConstants.COL_TIMES_TRIGGERED);
        
        public static readonly string INSERT_TRIGGER =
                string.Format(
                    @"INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16})  
                        VALUES(@triggerName, @triggerGroup, @triggerJobName, @triggerJobGroup, @triggerVolatile, @triggerDescription, @triggerNextFireTime, @triggerPreviousFireTime, @triggerState, @triggerType, @triggerStartTime, @triggerEndTime, @triggerCalendarName, @triggerMisfireInstruction, @triggerJobJobDataMap)",
                    TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                    AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP,
                    AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_NEXT_FIRE_TIME,
                    AdoConstants.COL_PREV_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_TYPE,
                    AdoConstants.COL_START_TIME, AdoConstants.COL_END_TIME, AdoConstants.COL_CALENDAR_NAME,
                    AdoConstants.COL_MISFIRE_INSTRUCTION, AdoConstants.COL_JOB_DATAMAP);

        public static readonly string INSERT_TRIGGER_LISTENER =
                string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}) VALUES(@triggerName, @triggerGroup, @listener)", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGER_LISTENERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_LISTENER);

        // SELECT

        public static readonly string SELECT_BLOB_TRIGGER =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string SELECT_CALENDAR =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @calendarName", TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS,
                              AdoConstants.COL_CALENDAR_NAME);

        public static readonly string SELECT_CALENDAR_EXISTENCE =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @calendarName", AdoConstants.COL_CALENDAR_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR_NAME);

        public static readonly string SELECT_CALENDARS =
                string.Format("SELECT {0} FROM {1}{2}", AdoConstants.COL_CALENDAR_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CALENDARS);

        public static readonly string SELECT_CRON_TRIGGER =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string SELECT_FIRED_TRIGGER =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);

        
        public static readonly string SELECT_FIRED_TRIGGER_GROUP =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string SELECT_FIRED_TRIGGERS =
                string.Format("SELECT * FROM {0}{1}", TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS);
        
        public static readonly string SELECT_FIRED_TRIGGERS_OF_JOB =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @jobName AND {3} = @jobGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);

        public static readonly string SELECT_FIRED_TRIGGERS_OF_JOB_GROUP =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @jobGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_JOB_GROUP);

        public static readonly string SELECT_INSTANCES_FIRED_TRIGGERS =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @instanceName", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_INSTANCE_NAME);
        
        public static readonly string SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @instanceName AND {3} = @requestsRecovery", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_INSTANCE_NAME,
                              AdoConstants.COL_REQUESTS_RECOVERY);
        
        public static readonly string SELECT_JOB_DETAIL = 
            string.Format("SELECT {0},{1},{2},{3},{4},{5},{6},{7},{8} FROM {9}{10} WHERE {11} = @jobName AND {12} = @jobGroup", 
            AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_JOB_CLASS, 
            AdoConstants.COL_IS_DURABLE, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_IS_STATEFUL, AdoConstants.COL_REQUESTS_RECOVERY, 
            AdoConstants.COL_JOB_DATAMAP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);

        public static readonly string SELECT_JOB_EXECUTION_COUNT =
                string.Format("SELECT COUNT({0}) FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", AdoConstants.COL_TRIGGER_NAME,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP);
        
        public static readonly string SELECT_JOB_EXISTENCE =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", AdoConstants.COL_JOB_NAME,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP);

        public static readonly string SELECT_JOB_FOR_TRIGGER =
                string.Format(
                    "SELECT J.{0}, J.{1}, J.{2}, J.{3}, J.{4} FROM {5}{6} T, {7}{8} J WHERE T.{9} = @triggerName AND T.{10} = @triggerGroup AND T.{11} = J.{12} AND T.{13} = J.{14}",
                    AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_DURABLE,
                    AdoConstants.COL_JOB_CLASS, AdoConstants.COL_REQUESTS_RECOVERY, TABLE_PREFIX_SUBST,
                    AdoConstants.TABLE_TRIGGERS, TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS,
                    AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_JOB_NAME,
                    AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_JOB_GROUP);
        
        public static readonly string SELECT_JOB_GROUPS =
                string.Format("SELECT DISTINCT({0}) FROM {1}{2}", AdoConstants.COL_JOB_GROUP, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_JOB_DETAILS);
        
        public static readonly string SELECT_JOB_LISTENERS =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", AdoConstants.COL_JOB_LISTENER,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_LISTENERS, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP);
        
        public static readonly string SELECT_JOB_STATEFUL =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", AdoConstants.COL_IS_STATEFUL,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP);

        public static readonly string SELECT_JOBS_IN_GROUP =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @jobGroup", AdoConstants.COL_JOB_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_GROUP);
        
        public static readonly string SELECT_MISFIRED_TRIGGERS =
                string.Format("SELECT * FROM {0}{1} WHERE {2} < @nextFireTime ORDER BY START_TIME ASC", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_NEXT_FIRE_TIME);
        
        public static readonly string SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} < @nextFireTime AND {4} = @triggerGroup AND {5} = @state",
                              AdoConstants.COL_TRIGGER_NAME, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_TRIGGER_GROUP,
                              AdoConstants.COL_TRIGGER_STATE);
       
        public static readonly string SELECT_MISFIRED_TRIGGERS_IN_STATE =
                string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} < @nextFireTime AND {5} = @state", AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE);

        public static readonly string SELECT_NEXT_FIRE_TIME =
                string.Format("SELECT MIN({0}) AS {1} FROM {2}{3} WHERE {4} = @state AND {5} >= 0",
                              AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.ALIAS_COL_NEXT_FIRE_TIME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE,
                              AdoConstants.COL_NEXT_FIRE_TIME);

        public static readonly string SELECT_NUM_CALENDARS =
                string.Format("SELECT COUNT({0})  FROM {1}{2}", AdoConstants.COL_CALENDAR_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CALENDARS);
        
        public static readonly string SELECT_NUM_JOBS =
                string.Format("SELECT COUNT({0})  FROM {1}{2}", AdoConstants.COL_JOB_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_JOB_DETAILS);

        public static readonly string SELECT_NUM_TRIGGERS =
                string.Format("SELECT COUNT({0})  FROM {1}{2}", AdoConstants.COL_TRIGGER_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS);

        public static readonly string SELECT_NUM_TRIGGERS_FOR_JOB =
                string.Format("SELECT COUNT({0}) FROM {1}{2} WHERE {3} = @jobName AND {4} = @jobGroup", AdoConstants.COL_TRIGGER_NAME,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP);
        
        public static readonly string SELECT_NUM_TRIGGERS_IN_GROUP =
                string.Format("SELECT COUNT({0})  FROM {1}{2} WHERE {3} = @triggerGroup", AdoConstants.COL_TRIGGER_NAME,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string SELECT_PAUSED_TRIGGER_GROUP =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @triggerGroup", AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_PAUSED_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string SELECT_PAUSED_TRIGGER_GROUPS =
                string.Format("SELECT {0} FROM {1}{2}", AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_PAUSED_TRIGGERS);

        public static readonly string SELECT_REFERENCED_CALENDAR =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @calendarName", AdoConstants.COL_CALENDAR_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_CALENDAR_NAME);
        
        public static readonly string SELECT_SCHEDULER_STATE =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @instanceName", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_SCHEDULER_STATE, AdoConstants.COL_INSTANCE_NAME);
       
        public static readonly string SELECT_SCHEDULER_STATES =
                string.Format("SELECT * FROM {0}{1}", TABLE_PREFIX_SUBST, AdoConstants.TABLE_SCHEDULER_STATE);

        public static readonly string SELECT_SIMPLE_TRIGGER =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP =
                string.Format(
                    "SELECT DISTINCT J.{0}, J.{1} FROM {2}{3} T, {4}{5} J WHERE T.{6} = @triggerGroup AND T.{7} = J.{8} AND T.{9} = J.{10} AND J.{11} = @stateful",
                    AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, TABLE_PREFIX_SUBST,
                    AdoConstants.TABLE_TRIGGERS, TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS,
                    AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_NAME,
                    AdoConstants.COL_JOB_GROUP, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_STATEFUL);

        public static readonly string SELECT_TRIGGER =
                string.Format("SELECT * FROM {0}{1} WHERE {2} = @triggerName AND {3} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string SELECT_TRIGGER_DATA =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup", AdoConstants.COL_JOB_DATAMAP,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string SELECT_TRIGGER_EXISTENCE =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup", AdoConstants.COL_TRIGGER_NAME,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string SELECT_TRIGGER_FOR_FIRE_TIME =
                string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = @state AND {5} = @nextFireTime", AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_NEXT_FIRE_TIME);

        public static readonly string SELECT_TRIGGER_GROUPS =
                string.Format("SELECT DISTINCT({0}) FROM {1}{2}", AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS);

        public static readonly string SELECT_TRIGGER_LISTENERS =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup", AdoConstants.COL_TRIGGER_LISTENER,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGER_LISTENERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string SELECT_TRIGGER_STATE =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @triggerName AND {4} = @triggerGroup", AdoConstants.COL_TRIGGER_STATE,
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string SELECT_TRIGGER_STATUS =
                string.Format("SELECT {0}, {1}, {2}, {3} FROM {4}{5} WHERE {6} = @triggerName AND {7} = @triggerGroup",
                              AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string SELECT_TRIGGERS_FOR_CALENDAR =
                string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = @calendarName", AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_CALENDAR_NAME);
        
        public static readonly string SELECT_TRIGGERS_FOR_JOB =
                string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = @jobName AND {5} = @jobGroup", AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);

        public static readonly string SELECT_TRIGGERS_IN_GROUP =
                string.Format("SELECT {0} FROM {1}{2} WHERE {3} = @triggerGroup", AdoConstants.COL_TRIGGER_NAME, TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string SELECT_TRIGGERS_IN_STATE =
                string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = @state", AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_TRIGGER_STATE);

        public static readonly string SELECT_VOLATILE_JOBS =
                string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = @volatile", AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS,
                              AdoConstants.COL_IS_VOLATILE);
        
        public static readonly string SELECT_VOLATILE_TRIGGERS =
                string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = @volatile", AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_IS_VOLATILE);

        // UPDATE 

        public static readonly string UPDATE_BLOB_TRIGGER =
                string.Format("UPDATE {0}{1} SET {2} = @blob WHERE {3} = @triggerName AND {4} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_BLOB, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string UPDATE_CALENDAR =
                string.Format("UPDATE {0}{1} SET {2} = @calendar  WHERE {3} = @calendarName", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR, AdoConstants.COL_CALENDAR_NAME);

        public static readonly string UPDATE_CRON_TRIGGER =
                string.Format("UPDATE {0}{1} SET {2} = @triggerCronExpression WHERE {3} = @triggerName AND {4} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_CRON_EXPRESSION,
                              AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string UPDATE_INSTANCES_FIRED_TRIGGER_STATE =
                string.Format("UPDATE {0}{1} SET {2} = @triggerEntryState AND {3} = @firedTime WHERE {4} = @instanceName", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_ENTRY_STATE,
                              AdoConstants.COL_FIRED_TIME, AdoConstants.COL_INSTANCE_NAME);
        
        public static readonly string UPDATE_JOB_DATA =
                string.Format("UPDATE {0}{1} SET {2} = @jobDataMap  WHERE {3} = @jobName AND {4} = @jobGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_DATAMAP, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP);
        
        public static readonly string UPDATE_JOB_DETAIL =
                string.Format(
                    "UPDATE {0}{1} SET {2} = @jobDescription, {3} = @jobType, {4} = @durable, {5} = @volatile, {6} = @stateful, {7} = @requestsRecovery, {8} = @jobDataMap  WHERE {9} = @jobName AND {10} = @jobGroup",
                    TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_DESCRIPTION,
                    AdoConstants.COL_JOB_CLASS, AdoConstants.COL_IS_DURABLE, AdoConstants.COL_IS_VOLATILE,
                    AdoConstants.COL_IS_STATEFUL, AdoConstants.COL_REQUESTS_RECOVERY, AdoConstants.COL_JOB_DATAMAP,
                    AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);

        public static readonly string UPDATE_JOB_TRIGGER_STATES =
                string.Format("UPDATE {0}{1} SET {2} = @state WHERE {3} = @jobName AND {4} = @jobGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP);
        
        public static readonly string UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE =
                string.Format("UPDATE {0}{1} SET {2} = @state WHERE {3} = @jobName AND {4} = @jobGroup AND {5} = @oldState", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_JOB_NAME,
                              AdoConstants.COL_JOB_GROUP, AdoConstants.COL_TRIGGER_STATE);
        
        public static readonly string UPDATE_SCHEDULER_STATE =
                string.Format("UPDATE {0}{1} SET {2} = @lastCheckinTime, {3} = @recoverer WHERE {4} = @instranceName", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_SCHEDULER_STATE, AdoConstants.COL_LAST_CHECKIN_TIME,
                              AdoConstants.COL_RECOVERER, AdoConstants.COL_INSTANCE_NAME);

        public static readonly string UPDATE_SIMPLE_TRIGGER =
                string.Format("UPDATE {0}{1} SET {2} = @repeatCount, {3} = @triggerRepeatInterval, {4} = @triggerTimeTriggered WHERE {5} = @triggerName AND {6} = @triggerGroup",
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_REPEAT_COUNT,
                              AdoConstants.COL_REPEAT_INTERVAL, AdoConstants.COL_TIMES_TRIGGERED,
                              AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string UPDATE_TRIGGER =
                string.Format(
                    @"UPDATE {0}{1} SET {2} = @triggerJobName, {3} = @triggerJobGroup, {4} = @triggerVolatile, {5} = @triggerDescription, {6} = @triggerNextFireTime, {7} = @triggerPreviousFireTime,
                        {8} = @triggerState, {9} = @triggerType, {10} = @triggerStartTime, {11} = @triggerEndTime, {12} = @triggerCalendarName, {13} = triggerMisfireInstruction, {14} = triggerJobJobDataMap
                        WHERE {15} = @triggerName AND {16} = @triggerGroup",
                    TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_JOB_NAME,
                    AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_DESCRIPTION,
                    AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_PREV_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE,
                    AdoConstants.COL_TRIGGER_TYPE, AdoConstants.COL_START_TIME, AdoConstants.COL_END_TIME,
                    AdoConstants.COL_CALENDAR_NAME, AdoConstants.COL_MISFIRE_INSTRUCTION, AdoConstants.COL_JOB_DATAMAP,
                    AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string UPDATE_TRIGGER_GROUP_STATE =
                string.Format("UPDATE {0}{1} SET {2} = @state", TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS,
                              AdoConstants.COL_TRIGGER_STATE);

        public static readonly string UPDATE_TRIGGER_GROUP_STATE_FROM_STATE =
                string.Format("UPDATE {0}{1} SET {2} = @newState WHERE {3} = @triggerGroup AND {4} = @oldState", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE,
                              AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE);

        public static readonly string UPDATE_TRIGGER_GROUP_STATE_FROM_STATES =
                string.Format("UPDATE {0}{1} SET {2} = @newState WHERE {3} = @groupName AND ({4} = @oldState1 OR {5} = @oldState2 OR {6} = @oldState3)",
                              TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE,
                              AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE,
                              AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE);
        
        public static readonly string UPDATE_TRIGGER_SKIP_DATA =
                string.Format(
                    @"UPDATE {0}{1} SET {2} = @triggerJobName, {3} = @triggerJobGroup, {4} = @triggerVolatile, {5} = @triggerDescription, {6} = @triggerNextFireTime, {7} = @triggerPreviousFireTime, 
                        {8} = @triggerState, {9} = @triggerType, {10} = @triggerStartTime, {11} = @triggerEndTime, {12} = @triggerCalendarName, {13} = @triggerMisfireInstruction
                    WHERE {14} = @triggerName AND {15} = @triggerGroup",
                    TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_JOB_NAME,
                    AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_DESCRIPTION,
                    AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_PREV_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE,
                    AdoConstants.COL_TRIGGER_TYPE, AdoConstants.COL_START_TIME, AdoConstants.COL_END_TIME,
                    AdoConstants.COL_CALENDAR_NAME, AdoConstants.COL_MISFIRE_INSTRUCTION, AdoConstants.COL_TRIGGER_NAME,
                    AdoConstants.COL_TRIGGER_GROUP);

        public static readonly string UPDATE_TRIGGER_STATE =
                string.Format("UPDATE {0}{1} SET {2} = @state WHERE {3} = @triggerName AND {4} = @triggerGroup", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP);
        
        public static readonly string UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME =
                string.Format("UPDATE {0}{1} SET {2} = @newState WHERE ({3} = @oldState1 OR {4} = @oldState2) AND {5} < @time", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE,
                              AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE,
                              AdoConstants.COL_NEXT_FIRE_TIME);

        public static readonly string UPDATE_TRIGGER_STATE_FROM_STATE =
                string.Format("UPDATE {0}{1} SET {2} = @newState WHERE {3} = @triggerName AND {4} = @triggerGroup AND {5} = @oldState", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_NAME,
                              AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE);
        
        public static readonly string UPDATE_TRIGGER_STATE_FROM_STATES =
                string.Format(
                    "UPDATE {0}{1} SET {2} = @newState WHERE {3} = @triggerName AND {4} = @triggerGroup AND ({5} = @oldState1 OR {6} = @oldState2 OR {7} = @oldState3)",
                    TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE,
                    AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE,
                    AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE);
        
        public static readonly string UPDATE_TRIGGER_STATES_FROM_OTHER_STATES =
                string.Format("UPDATE {0}{1} SET {2} = @newState WHERE {3} = @oldState1 OR {4} = @oldState2", TABLE_PREFIX_SUBST,
                              AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE,
                              AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE);

    }
}