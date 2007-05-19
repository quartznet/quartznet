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
		// table prefix substitution string
		public static readonly string TABLE_PREFIX_SUBST = "{0}";
		// QUERIES
		public static readonly string UPDATE_TRIGGER_STATES_FROM_OTHER_STATES;
		public static readonly string UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME;
		public static readonly string SELECT_MISFIRED_TRIGGERS;
		public static readonly string SELECT_TRIGGERS_IN_STATE;
		public static readonly string SELECT_MISFIRED_TRIGGERS_IN_STATE;
		public static readonly string SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE;
		public static readonly string SELECT_VOLATILE_TRIGGERS;
		public static readonly string DELETE_FIRED_TRIGGERS;
		public static readonly string INSERT_JOB_DETAIL;
		public static readonly string UPDATE_JOB_DETAIL;
		public static readonly string SELECT_TRIGGERS_FOR_JOB;
		public static readonly string SELECT_TRIGGERS_FOR_CALENDAR;
		public static readonly string SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP;
		public static readonly string DELETE_JOB_LISTENERS;
		public static readonly string DELETE_JOB_DETAIL;
		public static readonly string SELECT_JOB_STATEFUL;
		public static readonly string SELECT_JOB_EXISTENCE;
		public static readonly string UPDATE_JOB_DATA;
		public static readonly string INSERT_JOB_LISTENER;
		public static readonly string SELECT_JOB_LISTENERS;
		public static readonly string SELECT_JOB_DETAIL;
		public static readonly string SELECT_NUM_JOBS;
		public static readonly string SELECT_JOB_GROUPS;
		public static readonly string SELECT_JOBS_IN_GROUP;
		public static readonly string SELECT_VOLATILE_JOBS;
		public static readonly string INSERT_TRIGGER;
		public static readonly string INSERT_SIMPLE_TRIGGER;
		public static readonly string INSERT_CRON_TRIGGER;
		public static readonly string INSERT_BLOB_TRIGGER;
		public static readonly string UPDATE_TRIGGER_SKIP_DATA;
		public static readonly string UPDATE_TRIGGER;
		public static readonly string UPDATE_SIMPLE_TRIGGER;
		public static readonly string UPDATE_CRON_TRIGGER;
		public static readonly string UPDATE_BLOB_TRIGGER;
		public static readonly string SELECT_TRIGGER_EXISTENCE;
		public static readonly string UPDATE_TRIGGER_STATE;
		public static readonly string UPDATE_TRIGGER_STATE_FROM_STATE;
		public static readonly string UPDATE_TRIGGER_GROUP_STATE;
		public static readonly string UPDATE_TRIGGER_GROUP_STATE_FROM_STATE;
		public static readonly string UPDATE_TRIGGER_STATE_FROM_STATES;
		public static readonly string UPDATE_TRIGGER_GROUP_STATE_FROM_STATES;
		public static readonly string UPDATE_JOB_TRIGGER_STATES;
		public static readonly string UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE;
		public static readonly string DELETE_TRIGGER_LISTENERS;
		public static readonly string INSERT_TRIGGER_LISTENER;
		public static readonly string SELECT_TRIGGER_LISTENERS;
		public static readonly string DELETE_SIMPLE_TRIGGER;
		public static readonly string DELETE_CRON_TRIGGER;
		public static readonly string DELETE_BLOB_TRIGGER;
		public static readonly string DELETE_TRIGGER;
		public static readonly string SELECT_NUM_TRIGGERS_FOR_JOB;
		public static readonly string SELECT_JOB_FOR_TRIGGER;
		public static readonly string SELECT_TRIGGER;
		public static readonly string SELECT_TRIGGER_DATA;
		public static readonly string SELECT_TRIGGER_STATE;
		public static readonly string SELECT_TRIGGER_STATUS;
		public static readonly string SELECT_SIMPLE_TRIGGER;
		public static readonly string SELECT_CRON_TRIGGER;
		public static readonly string SELECT_BLOB_TRIGGER;
		public static readonly string SELECT_NUM_TRIGGERS;
		public static readonly string SELECT_NUM_TRIGGERS_IN_GROUP;
		public static readonly string SELECT_TRIGGER_GROUPS;
		public static readonly string SELECT_TRIGGERS_IN_GROUP;
		public static readonly string INSERT_CALENDAR;
		public static readonly string UPDATE_CALENDAR;
		public static readonly string SELECT_CALENDAR_EXISTENCE;
		public static readonly string SELECT_CALENDAR;
		public static readonly string SELECT_REFERENCED_CALENDAR;
		public static readonly string DELETE_CALENDAR;
		public static readonly string SELECT_NUM_CALENDARS;
		public static readonly string SELECT_CALENDARS;
		public static readonly string SELECT_NEXT_FIRE_TIME;
		public static readonly string SELECT_TRIGGER_FOR_FIRE_TIME;
		public static readonly string INSERT_FIRED_TRIGGER;
		public static readonly string UPDATE_INSTANCES_FIRED_TRIGGER_STATE;
		public static readonly string SELECT_INSTANCES_FIRED_TRIGGERS;
		public static readonly string SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS;
		public static readonly string SELECT_JOB_EXECUTION_COUNT;
		public static readonly string SELECT_FIRED_TRIGGERS;
		public static readonly string SELECT_FIRED_TRIGGER;
		public static readonly string SELECT_FIRED_TRIGGER_GROUP;
		public static readonly string SELECT_FIRED_TRIGGERS_OF_JOB;
		public static readonly string SELECT_FIRED_TRIGGERS_OF_JOB_GROUP;
		public static readonly string DELETE_FIRED_TRIGGER;
		public static readonly string DELETE_INSTANCES_FIRED_TRIGGERS;
		public static readonly string DELETE_VOLATILE_FIRED_TRIGGERS;
		public static readonly string DELETE_NO_RECOVERY_FIRED_TRIGGERS;
		public static readonly string INSERT_SCHEDULER_STATE;
		public static readonly string SELECT_SCHEDULER_STATE;
		public static readonly string SELECT_SCHEDULER_STATES;
		public static readonly string DELETE_SCHEDULER_STATE;
		public static readonly string UPDATE_SCHEDULER_STATE;
		public static readonly string INSERT_PAUSED_TRIGGER_GROUP;
		public static readonly string SELECT_PAUSED_TRIGGER_GROUP;
		public static readonly string SELECT_PAUSED_TRIGGER_GROUPS;
		public static readonly string DELETE_PAUSED_TRIGGER_GROUP;
		public static readonly string DELETE_PAUSED_TRIGGER_GROUPS;

		//  CREATE TABLE qrtz_scheduler_state(INSTANCE_NAME VARCHAR2(80) NOT NULL,
		// LAST_CHECKIN_TIME NUMBER(13) NOT NULL, CHECKIN_INTERVAL NUMBER(13) NOT
		// NULL, RECOVERER VARCHAR2(80) NOT NULL, PRIMARY KEY (INSTANCE_NAME));
		static StdAdoConstants()
		{
			UPDATE_TRIGGER_STATES_FROM_OTHER_STATES = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? OR {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE);
			UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME = string.Format("UPDATE {0}{1} SET {2} = ? WHERE ({3} = ? OR {4} = ?) AND {5} < ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_NEXT_FIRE_TIME);
			SELECT_MISFIRED_TRIGGERS = string.Format("SELECT * FROM {0}{1} WHERE {2} < ? ORDER BY START_TIME ASC", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_NEXT_FIRE_TIME);
			SELECT_TRIGGERS_IN_STATE = string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = ?", AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE);
			SELECT_MISFIRED_TRIGGERS_IN_STATE = string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} < ? AND {5} = ?", AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE);
			SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE = string.Format("SELECT {0} FROM {1}{2} WHERE {3} < ? AND {4} = ? AND {5} = ?", AdoConstants.COL_TRIGGER_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE);
			SELECT_VOLATILE_TRIGGERS = string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = ?", AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_IS_VOLATILE);
			DELETE_FIRED_TRIGGERS = string.Format("DELETE FROM {0}{1}", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS);
			INSERT_JOB_DETAIL = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})  VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_JOB_CLASS, AdoConstants.COL_IS_DURABLE, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_IS_STATEFUL, AdoConstants.COL_REQUESTS_RECOVERY, AdoConstants.COL_JOB_DATAMAP);
			UPDATE_JOB_DETAIL = string.Format("UPDATE {0}{1} SET {2} = ?, {3} = ?, {4} = ?, {5} = ?, {6} = ?, {7} = ?, {8} = ?  WHERE {9} = ? AND {10} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_JOB_CLASS, AdoConstants.COL_IS_DURABLE, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_IS_STATEFUL, AdoConstants.COL_REQUESTS_RECOVERY, AdoConstants.COL_JOB_DATAMAP, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_TRIGGERS_FOR_JOB = string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = ? AND {5} = ?", AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_TRIGGERS_FOR_CALENDAR = string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = ?", AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_CALENDAR_NAME);
			SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP = string.Format("SELECT DISTINCT J.{0}, J.{1} FROM {2}{3} T, {4}{5} J WHERE T.{6} = ? AND T.{7} = J.{8} AND T.{9} = J.{10} AND J.{11} = ?", AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_STATEFUL);
			DELETE_JOB_LISTENERS = string.Format("DELETE FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_LISTENERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			DELETE_JOB_DETAIL = string.Format("DELETE FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_JOB_STATEFUL = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_IS_STATEFUL, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_JOB_EXISTENCE = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_JOB_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			UPDATE_JOB_DATA = string.Format("UPDATE {0}{1} SET {2} = ?  WHERE {3} = ? AND {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_DATAMAP, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			INSERT_JOB_LISTENER = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}) VALUES(?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_LISTENERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_JOB_LISTENER);
			SELECT_JOB_LISTENERS = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_JOB_LISTENER, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_LISTENERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_JOB_DETAIL = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_NUM_JOBS = string.Format("SELECT COUNT({0})  FROM {1}{2}", AdoConstants.COL_JOB_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS);
			SELECT_JOB_GROUPS = string.Format("SELECT DISTINCT({0}) FROM {1}{2}", AdoConstants.COL_JOB_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS);
			SELECT_JOBS_IN_GROUP = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ?", AdoConstants.COL_JOB_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_JOB_GROUP);
			SELECT_VOLATILE_JOBS = string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = ?", AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_IS_VOLATILE);
			INSERT_TRIGGER = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16})  VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_PREV_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_TYPE, AdoConstants.COL_START_TIME, AdoConstants.COL_END_TIME, AdoConstants.COL_CALENDAR_NAME, AdoConstants.COL_MISFIRE_INSTRUCTION, AdoConstants.COL_JOB_DATAMAP);
			INSERT_SIMPLE_TRIGGER = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6})  VALUES(?, ?, ?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_REPEAT_COUNT, AdoConstants.COL_REPEAT_INTERVAL, AdoConstants.COL_TIMES_TRIGGERED);
			INSERT_CRON_TRIGGER = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5})  VALUES(?, ?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_CRON_EXPRESSION, AdoConstants.COL_TIME_ZONE_ID);
			INSERT_BLOB_TRIGGER = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4})  VALUES(?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_BLOB);
			UPDATE_TRIGGER_SKIP_DATA = string.Format("UPDATE {0}{1} SET {2} = ?, {3} = ?, {4} = ?, {5} = ?, {6} = ?, {7} = ?, {8} = ?, {9} = ?, {10} = ?, {11} = ?, {12} = ?, {13} = ? WHERE {14} = ? AND {15} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_PREV_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_TYPE, AdoConstants.COL_START_TIME, AdoConstants.COL_END_TIME, AdoConstants.COL_CALENDAR_NAME, AdoConstants.COL_MISFIRE_INSTRUCTION, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			UPDATE_TRIGGER = string.Format("UPDATE {0}{1} SET {2} = ?, {3} = ?, {4} = ?, {5} = ?, {6} = ?, {7} = ?, {8} = ?, {9} = ?, {10} = ?, {11} = ?, {12} = ?, {13} = ?, {14} = ? WHERE {15} = ? AND {16} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_DESCRIPTION, AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_PREV_FIRE_TIME, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_TYPE, AdoConstants.COL_START_TIME, AdoConstants.COL_END_TIME, AdoConstants.COL_CALENDAR_NAME, AdoConstants.COL_MISFIRE_INSTRUCTION, AdoConstants.COL_JOB_DATAMAP, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			UPDATE_SIMPLE_TRIGGER = string.Format("UPDATE {0}{1} SET {2} = ?, {3} = ?, {4} = ? WHERE {5} = ? AND {6} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_REPEAT_COUNT, AdoConstants.COL_REPEAT_INTERVAL, AdoConstants.COL_TIMES_TRIGGERED, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			UPDATE_CRON_TRIGGER = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_CRON_EXPRESSION, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			UPDATE_BLOB_TRIGGER = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_BLOB, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_TRIGGER_EXISTENCE = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_TRIGGER_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			UPDATE_TRIGGER_STATE = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			UPDATE_TRIGGER_STATE_FROM_STATE = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ? AND {5} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE);
			UPDATE_TRIGGER_GROUP_STATE = string.Format("UPDATE {0}{1} SET {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE);
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATE = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE);
			UPDATE_TRIGGER_STATE_FROM_STATES = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ? AND ({5} = ? OR {6} = ? OR {7} = ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE);
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATES = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND ({4} = ? OR {5} = ? OR {6} = ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_TRIGGER_STATE);
			UPDATE_JOB_TRIGGER_STATES = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE = string.Format("UPDATE {0}{1} SET {2} = ? WHERE {3} = ? AND {4} = ? AND {5} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_TRIGGER_STATE);
			DELETE_TRIGGER_LISTENERS = string.Format("DELETE FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGER_LISTENERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			INSERT_TRIGGER_LISTENER = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}) VALUES(?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGER_LISTENERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_TRIGGER_LISTENER);
			SELECT_TRIGGER_LISTENERS = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_TRIGGER_LISTENER, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGER_LISTENERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			DELETE_SIMPLE_TRIGGER = string.Format("DELETE FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			DELETE_CRON_TRIGGER = string.Format("DELETE FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			DELETE_BLOB_TRIGGER = string.Format("DELETE FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			DELETE_TRIGGER = string.Format("DELETE FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_NUM_TRIGGERS_FOR_JOB = string.Format("SELECT COUNT({0}) FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_TRIGGER_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_JOB_FOR_TRIGGER = string.Format("SELECT J.{0}, J.{1}, J.{2}, J.{3}, J.{4} FROM {5}{6} T, {7}{8} J WHERE T.{9} = ? AND T.{10} = ? AND T.{11} = J.{12} AND T.{13} = J.{14}", AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_DURABLE, AdoConstants.COL_JOB_CLASS, AdoConstants.COL_REQUESTS_RECOVERY, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_JOB_DETAILS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_JOB_GROUP);
			SELECT_TRIGGER = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_TRIGGER_DATA = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_JOB_DATAMAP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_TRIGGER_STATE = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_TRIGGER_STATE, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_TRIGGER_STATUS = string.Format("SELECT {0}, {1}, {2}, {3} FROM {4}{5} WHERE {6} = ? AND {7} = ?", AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_SIMPLE_TRIGGER = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SIMPLE_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_CRON_TRIGGER = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CRON_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_BLOB_TRIGGER = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_BLOB_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_NUM_TRIGGERS = string.Format("SELECT COUNT({0})  FROM {1}{2}", AdoConstants.COL_TRIGGER_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS);
			SELECT_NUM_TRIGGERS_IN_GROUP = string.Format("SELECT COUNT({0})  FROM {1}{2} WHERE {3} = ?", AdoConstants.COL_TRIGGER_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_TRIGGER_GROUPS = string.Format("SELECT DISTINCT({0}) FROM {1}{2}", AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS);
			SELECT_TRIGGERS_IN_GROUP = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ?", AdoConstants.COL_TRIGGER_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
			INSERT_CALENDAR = string.Format("INSERT INTO {0}{1} ({2}, {3})  VALUES(?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR_NAME, AdoConstants.COL_CALENDAR);
			UPDATE_CALENDAR = string.Format("UPDATE {0}{1} SET {2} = ?  WHERE {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR, AdoConstants.COL_CALENDAR_NAME);
			SELECT_CALENDAR_EXISTENCE = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ?", AdoConstants.COL_CALENDAR_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR_NAME);
			SELECT_CALENDAR = string.Format("SELECT * FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR_NAME);
			SELECT_REFERENCED_CALENDAR = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ?", AdoConstants.COL_CALENDAR_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_CALENDAR_NAME);
			DELETE_CALENDAR = string.Format("DELETE FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS, AdoConstants.COL_CALENDAR_NAME);
			SELECT_NUM_CALENDARS = string.Format("SELECT COUNT({0})  FROM {1}{2}", AdoConstants.COL_CALENDAR_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS);
			SELECT_CALENDARS = string.Format("SELECT {0} FROM {1}{2}", AdoConstants.COL_CALENDAR_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_CALENDARS);
			SELECT_NEXT_FIRE_TIME = string.Format("SELECT MIN({0}) AS {1} FROM {2}{3} WHERE {4} = ? AND {5} >= 0", AdoConstants.COL_NEXT_FIRE_TIME, AdoConstants.ALIAS_COL_NEXT_FIRE_TIME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_NEXT_FIRE_TIME);
			SELECT_TRIGGER_FOR_FIRE_TIME = string.Format("SELECT {0}, {1} FROM {2}{3} WHERE {4} = ? AND {5} = ?", AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_TRIGGERS, AdoConstants.COL_TRIGGER_STATE, AdoConstants.COL_NEXT_FIRE_TIME);
			INSERT_FIRED_TRIGGER = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_ENTRY_ID, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP, AdoConstants.COL_IS_VOLATILE, AdoConstants.COL_INSTANCE_NAME, AdoConstants.COL_FIRED_TIME, AdoConstants.COL_ENTRY_STATE, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP, AdoConstants.COL_IS_STATEFUL, AdoConstants.COL_REQUESTS_RECOVERY);
			UPDATE_INSTANCES_FIRED_TRIGGER_STATE = string.Format("UPDATE {0}{1} SET {2} = ? AND {3} = ? WHERE {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_ENTRY_STATE, AdoConstants.COL_FIRED_TIME, AdoConstants.COL_INSTANCE_NAME);
			SELECT_INSTANCES_FIRED_TRIGGERS = string.Format("SELECT * FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_INSTANCE_NAME);
			SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_INSTANCE_NAME, AdoConstants.COL_REQUESTS_RECOVERY);
			SELECT_JOB_EXECUTION_COUNT = string.Format("SELECT COUNT({0}) FROM {1}{2} WHERE {3} = ? AND {4} = ?", AdoConstants.COL_TRIGGER_NAME, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_FIRED_TRIGGERS = string.Format("SELECT * FROM {0}{1}", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS);
			SELECT_FIRED_TRIGGER = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_TRIGGER_NAME, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_FIRED_TRIGGER_GROUP = string.Format("SELECT * FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_FIRED_TRIGGERS_OF_JOB = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? AND {3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_JOB_NAME, AdoConstants.COL_JOB_GROUP);
			SELECT_FIRED_TRIGGERS_OF_JOB_GROUP = string.Format("SELECT * FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_JOB_GROUP);
			DELETE_FIRED_TRIGGER = string.Format("DELETE FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_ENTRY_ID);
			DELETE_INSTANCES_FIRED_TRIGGERS = string.Format("DELETE FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_INSTANCE_NAME);
			DELETE_VOLATILE_FIRED_TRIGGERS = string.Format("DELETE FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_IS_VOLATILE);
			DELETE_NO_RECOVERY_FIRED_TRIGGERS = string.Format("DELETE FROM {0}{1} WHERE {2} = ?{3} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_FIRED_TRIGGERS, AdoConstants.COL_INSTANCE_NAME, AdoConstants.COL_REQUESTS_RECOVERY);
			INSERT_SCHEDULER_STATE = string.Format("INSERT INTO {0}{1} ({2}, {3}, {4}, {5}) VALUES(?, ?, ?, ?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SCHEDULER_STATE, AdoConstants.COL_INSTANCE_NAME, AdoConstants.COL_LAST_CHECKIN_TIME, AdoConstants.COL_CHECKIN_INTERVAL, AdoConstants.COL_RECOVERER);
			SELECT_SCHEDULER_STATE = string.Format("SELECT * FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SCHEDULER_STATE, AdoConstants.COL_INSTANCE_NAME);
			SELECT_SCHEDULER_STATES = string.Format("SELECT * FROM {0}{1}", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SCHEDULER_STATE);
			DELETE_SCHEDULER_STATE = string.Format("DELETE FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SCHEDULER_STATE, AdoConstants.COL_INSTANCE_NAME);
			UPDATE_SCHEDULER_STATE = string.Format("UPDATE {0}{1} SET {2} = ?, {3} = ? WHERE {4} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_SCHEDULER_STATE, AdoConstants.COL_LAST_CHECKIN_TIME, AdoConstants.COL_RECOVERER, AdoConstants.COL_INSTANCE_NAME);
			INSERT_PAUSED_TRIGGER_GROUP = string.Format("INSERT INTO {0}{1} ({2}) VALUES(?)", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_PAUSED_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_PAUSED_TRIGGER_GROUP = string.Format("SELECT {0} FROM {1}{2} WHERE {3} = ?", AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_PAUSED_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
			SELECT_PAUSED_TRIGGER_GROUPS = string.Format("SELECT {0} FROM {1}{2}", AdoConstants.COL_TRIGGER_GROUP, StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_PAUSED_TRIGGERS);
			DELETE_PAUSED_TRIGGER_GROUP = string.Format("DELETE FROM {0}{1} WHERE {2} = ?", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_PAUSED_TRIGGERS, AdoConstants.COL_TRIGGER_GROUP);
			DELETE_PAUSED_TRIGGER_GROUPS = string.Format("DELETE FROM {0}{1}", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_PAUSED_TRIGGERS);
		}
	}
}