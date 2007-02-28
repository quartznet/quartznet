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
	/// StdJDBCDelegate}</code>
	/// class.
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
	public class StdJDBCConstants_Fields
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
		static StdJDBCConstants_Fields()
		{
			UPDATE_TRIGGER_STATES_FROM_OTHER_STATES = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE (" + Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + Constants_Fields.COL_TRIGGER_STATE + " = ?) AND " + Constants_Fields.COL_NEXT_FIRE_TIME + " < ?";
			SELECT_MISFIRED_TRIGGERS = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_NEXT_FIRE_TIME + " < ? ORDER BY START_TIME ASC";
			SELECT_TRIGGERS_IN_STATE = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			SELECT_MISFIRED_TRIGGERS_IN_STATE = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_NEXT_FIRE_TIME + " < ? AND " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_NEXT_FIRE_TIME + " < ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ? AND " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			SELECT_VOLATILE_TRIGGERS = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_IS_VOLATILE + " = ?";
			DELETE_FIRED_TRIGGERS = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS;
			INSERT_JOB_DETAIL = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " (" + Constants_Fields.COL_JOB_NAME + ", " + Constants_Fields.COL_JOB_GROUP + ", " + Constants_Fields.COL_DESCRIPTION + ", " + Constants_Fields.COL_JOB_CLASS + ", " + Constants_Fields.COL_IS_DURABLE + ", " + Constants_Fields.COL_IS_VOLATILE + ", " + Constants_Fields.COL_IS_STATEFUL + ", " + Constants_Fields.COL_REQUESTS_RECOVERY + ", " + Constants_Fields.COL_JOB_DATAMAP + ") " + " VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?)";
			UPDATE_JOB_DETAIL = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " SET " + Constants_Fields.COL_DESCRIPTION + " = ?, " + Constants_Fields.COL_JOB_CLASS + " = ?, " + Constants_Fields.COL_IS_DURABLE + " = ?, " + Constants_Fields.COL_IS_VOLATILE + " = ?, " + Constants_Fields.COL_IS_STATEFUL + " = ?, " + Constants_Fields.COL_REQUESTS_RECOVERY + " = ?, " + Constants_Fields.COL_JOB_DATAMAP + " = ? " + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_TRIGGERS_FOR_JOB = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_TRIGGERS_FOR_CALENDAR = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP = "SELECT DISTINCT J." + Constants_Fields.COL_JOB_NAME + ", J." + Constants_Fields.COL_JOB_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " T, " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " J WHERE T." + Constants_Fields.COL_TRIGGER_GROUP + " = ? AND T." + Constants_Fields.COL_JOB_NAME + " = J." + Constants_Fields.COL_JOB_NAME + " AND T." + Constants_Fields.COL_JOB_GROUP + " = J." + Constants_Fields.COL_JOB_GROUP + " AND J." + Constants_Fields.COL_IS_STATEFUL + " = ?";
			DELETE_JOB_LISTENERS = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_LISTENERS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			DELETE_JOB_DETAIL = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_STATEFUL = "SELECT " + Constants_Fields.COL_IS_STATEFUL + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_EXISTENCE = "SELECT " + Constants_Fields.COL_JOB_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			UPDATE_JOB_DATA = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " SET " + Constants_Fields.COL_JOB_DATAMAP + " = ? " + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			INSERT_JOB_LISTENER = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_LISTENERS + " (" + Constants_Fields.COL_JOB_NAME + ", " + Constants_Fields.COL_JOB_GROUP + ", " + Constants_Fields.COL_JOB_LISTENER + ") VALUES(?, ?, ?)";
			SELECT_JOB_LISTENERS = "SELECT " + Constants_Fields.COL_JOB_LISTENER + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_LISTENERS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_DETAIL = "SELECT *" + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_NUM_JOBS = "SELECT COUNT(" + Constants_Fields.COL_JOB_NAME + ") " + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS;
			SELECT_JOB_GROUPS = "SELECT DISTINCT(" + Constants_Fields.COL_JOB_GROUP + ") FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS;
			SELECT_JOBS_IN_GROUP = "SELECT " + Constants_Fields.COL_JOB_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_VOLATILE_JOBS = "SELECT " + Constants_Fields.COL_JOB_NAME + ", " + Constants_Fields.COL_JOB_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + Constants_Fields.COL_IS_VOLATILE + " = ?";
			INSERT_TRIGGER = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " (" + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + ", " + Constants_Fields.COL_JOB_NAME + ", " + Constants_Fields.COL_JOB_GROUP + ", " + Constants_Fields.COL_IS_VOLATILE + ", " + Constants_Fields.COL_DESCRIPTION + ", " + Constants_Fields.COL_NEXT_FIRE_TIME + ", " + Constants_Fields.COL_PREV_FIRE_TIME + ", " + Constants_Fields.COL_TRIGGER_STATE + ", " + Constants_Fields.COL_TRIGGER_TYPE + ", " + Constants_Fields.COL_START_TIME + ", " + Constants_Fields.COL_END_TIME + ", " + Constants_Fields.COL_CALENDAR_NAME + ", " + Constants_Fields.COL_MISFIRE_INSTRUCTION + ", " + Constants_Fields.COL_JOB_DATAMAP + ") " + " VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
			INSERT_SIMPLE_TRIGGER = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SIMPLE_TRIGGERS + " (" + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + ", " + Constants_Fields.COL_REPEAT_COUNT + ", " + Constants_Fields.COL_REPEAT_INTERVAL + ", " + Constants_Fields.COL_TIMES_TRIGGERED + ") " + " VALUES(?, ?, ?, ?, ?)";
			INSERT_CRON_TRIGGER = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CRON_TRIGGERS + " (" + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + ", " + Constants_Fields.COL_CRON_EXPRESSION + ", " + Constants_Fields.COL_TIME_ZONE_ID + ") " + " VALUES(?, ?, ?, ?)";
			INSERT_BLOB_TRIGGER = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_BLOB_TRIGGERS + " (" + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + ", " + Constants_Fields.COL_BLOB + ") " + " VALUES(?, ?, ?)";
			UPDATE_TRIGGER_SKIP_DATA = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_JOB_NAME + " = ?, " + Constants_Fields.COL_JOB_GROUP + " = ?, " + Constants_Fields.COL_IS_VOLATILE + " = ?, " + Constants_Fields.COL_DESCRIPTION + " = ?, " + Constants_Fields.COL_NEXT_FIRE_TIME + " = ?, " + Constants_Fields.COL_PREV_FIRE_TIME + " = ?, " + Constants_Fields.COL_TRIGGER_STATE + " = ?, " + Constants_Fields.COL_TRIGGER_TYPE + " = ?, " + Constants_Fields.COL_START_TIME + " = ?, " + Constants_Fields.COL_END_TIME + " = ?, " + Constants_Fields.COL_CALENDAR_NAME + " = ?, " + Constants_Fields.COL_MISFIRE_INSTRUCTION + " = ? WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_JOB_NAME + " = ?, " + Constants_Fields.COL_JOB_GROUP + " = ?, " + Constants_Fields.COL_IS_VOLATILE + " = ?, " + Constants_Fields.COL_DESCRIPTION + " = ?, " + Constants_Fields.COL_NEXT_FIRE_TIME + " = ?, " + Constants_Fields.COL_PREV_FIRE_TIME + " = ?, " + Constants_Fields.COL_TRIGGER_STATE + " = ?, " + Constants_Fields.COL_TRIGGER_TYPE + " = ?, " + Constants_Fields.COL_START_TIME + " = ?, " + Constants_Fields.COL_END_TIME + " = ?, " + Constants_Fields.COL_CALENDAR_NAME + " = ?, " + Constants_Fields.COL_MISFIRE_INSTRUCTION + " = ?, " + Constants_Fields.COL_JOB_DATAMAP + " = ? WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_SIMPLE_TRIGGER = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SIMPLE_TRIGGERS + " SET " + Constants_Fields.COL_REPEAT_COUNT + " = ?, " + Constants_Fields.COL_REPEAT_INTERVAL + " = ?, " + Constants_Fields.COL_TIMES_TRIGGERED + " = ? WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_CRON_TRIGGER = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CRON_TRIGGERS + " SET " + Constants_Fields.COL_CRON_EXPRESSION + " = ? WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_BLOB_TRIGGER = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_BLOB_TRIGGERS + " SET " + Constants_Fields.COL_BLOB + " = ? WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_EXISTENCE = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER_STATE = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER_STATE_FROM_STATE = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ? AND " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_GROUP_STATE = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATE = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + Constants_Fields.COL_TRIGGER_GROUP + " = ? AND " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_STATE_FROM_STATES = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ? AND (" + Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + Constants_Fields.COL_TRIGGER_STATE + " = ?)";
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATES = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + Constants_Fields.COL_TRIGGER_GROUP + " = ? AND (" + Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + Constants_Fields.COL_TRIGGER_STATE + " = ?)";
			UPDATE_JOB_TRIGGER_STATES = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ? WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " SET " + Constants_Fields.COL_TRIGGER_STATE + " = ? WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ? AND " + Constants_Fields.COL_TRIGGER_STATE + " = ?";
			DELETE_TRIGGER_LISTENERS = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGER_LISTENERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			INSERT_TRIGGER_LISTENER = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGER_LISTENERS + " (" + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + ", " + Constants_Fields.COL_TRIGGER_LISTENER + ") VALUES(?, ?, ?)";
			SELECT_TRIGGER_LISTENERS = "SELECT " + Constants_Fields.COL_TRIGGER_LISTENER + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGER_LISTENERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_SIMPLE_TRIGGER = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SIMPLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_CRON_TRIGGER = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CRON_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_BLOB_TRIGGER = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_BLOB_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_TRIGGER = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_NUM_TRIGGERS_FOR_JOB = "SELECT COUNT(" + Constants_Fields.COL_TRIGGER_NAME + ") FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_FOR_TRIGGER = "SELECT J." + Constants_Fields.COL_JOB_NAME + ", J." + Constants_Fields.COL_JOB_GROUP + ", J." + Constants_Fields.COL_IS_DURABLE + ", J." + Constants_Fields.COL_JOB_CLASS + ", J." + Constants_Fields.COL_REQUESTS_RECOVERY + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " T, " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_JOB_DETAILS + " J WHERE T." + Constants_Fields.COL_TRIGGER_NAME + " = ? AND T." + Constants_Fields.COL_TRIGGER_GROUP + " = ? AND T." + Constants_Fields.COL_JOB_NAME + " = J." + Constants_Fields.COL_JOB_NAME + " AND T." + Constants_Fields.COL_JOB_GROUP + " = J." + Constants_Fields.COL_JOB_GROUP;
			SELECT_TRIGGER = "SELECT *" + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_DATA = "SELECT " + Constants_Fields.COL_JOB_DATAMAP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_STATE = "SELECT " + Constants_Fields.COL_TRIGGER_STATE + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_STATUS = "SELECT " + Constants_Fields.COL_TRIGGER_STATE + ", " + Constants_Fields.COL_NEXT_FIRE_TIME + ", " + Constants_Fields.COL_JOB_NAME + ", " + Constants_Fields.COL_JOB_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_SIMPLE_TRIGGER = "SELECT *" + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SIMPLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_CRON_TRIGGER = "SELECT *" + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CRON_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_BLOB_TRIGGER = "SELECT *" + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_BLOB_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_NUM_TRIGGERS = "SELECT COUNT(" + Constants_Fields.COL_TRIGGER_NAME + ") " + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS;
			SELECT_NUM_TRIGGERS_IN_GROUP = "SELECT COUNT(" + Constants_Fields.COL_TRIGGER_NAME + ") " + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_GROUPS = "SELECT DISTINCT(" + Constants_Fields.COL_TRIGGER_GROUP + ") FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS;
			SELECT_TRIGGERS_IN_GROUP = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			INSERT_CALENDAR = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CALENDARS + " (" + Constants_Fields.COL_CALENDAR_NAME + ", " + Constants_Fields.COL_CALENDAR + ") " + " VALUES(?, ?)";
			UPDATE_CALENDAR = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CALENDARS + " SET " + Constants_Fields.COL_CALENDAR + " = ? " + " WHERE " + Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_CALENDAR_EXISTENCE = "SELECT " + Constants_Fields.COL_CALENDAR_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CALENDARS + " WHERE " + Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_CALENDAR = "SELECT *" + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CALENDARS + " WHERE " + Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_REFERENCED_CALENDAR = "SELECT " + Constants_Fields.COL_CALENDAR_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_CALENDAR_NAME + " = ?";
			DELETE_CALENDAR = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CALENDARS + " WHERE " + Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_NUM_CALENDARS = "SELECT COUNT(" + Constants_Fields.COL_CALENDAR_NAME + ") " + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CALENDARS;
			SELECT_CALENDARS = "SELECT " + Constants_Fields.COL_CALENDAR_NAME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_CALENDARS;
			SELECT_NEXT_FIRE_TIME = "SELECT MIN(" + Constants_Fields.COL_NEXT_FIRE_TIME + ") AS " + Constants_Fields.ALIAS_COL_NEXT_FIRE_TIME + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_STATE + " = ? AND " + Constants_Fields.COL_NEXT_FIRE_TIME + " >= 0";
			SELECT_TRIGGER_FOR_FIRE_TIME = "SELECT " + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_STATE + " = ? AND " + Constants_Fields.COL_NEXT_FIRE_TIME + " = ?";
			INSERT_FIRED_TRIGGER = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " (" + Constants_Fields.COL_ENTRY_ID + ", " + Constants_Fields.COL_TRIGGER_NAME + ", " + Constants_Fields.COL_TRIGGER_GROUP + ", " + Constants_Fields.COL_IS_VOLATILE + ", " + Constants_Fields.COL_INSTANCE_NAME + ", " + Constants_Fields.COL_FIRED_TIME + ", " + Constants_Fields.COL_ENTRY_STATE + ", " + Constants_Fields.COL_JOB_NAME + ", " + Constants_Fields.COL_JOB_GROUP + ", " + Constants_Fields.COL_IS_STATEFUL + ", " + Constants_Fields.COL_REQUESTS_RECOVERY + ") VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
			UPDATE_INSTANCES_FIRED_TRIGGER_STATE = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " SET " + Constants_Fields.COL_ENTRY_STATE + " = ? AND " + Constants_Fields.COL_FIRED_TIME + " = ? WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ?";
			SELECT_INSTANCES_FIRED_TRIGGERS = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ?";
			SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ? AND " + Constants_Fields.COL_REQUESTS_RECOVERY + " = ?";
			SELECT_JOB_EXECUTION_COUNT = "SELECT COUNT(" + Constants_Fields.COL_TRIGGER_NAME + ") FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS;
			SELECT_FIRED_TRIGGER = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_FIRED_TRIGGER_GROUP = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS_OF_JOB = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_JOB_NAME + " = ? AND " + Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS_OF_JOB_GROUP = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_JOB_GROUP + " = ?";
			DELETE_FIRED_TRIGGER = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_ENTRY_ID + " = ?";
			DELETE_INSTANCES_FIRED_TRIGGERS = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ?";
			DELETE_VOLATILE_FIRED_TRIGGERS = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_IS_VOLATILE + " = ?";
			DELETE_NO_RECOVERY_FIRED_TRIGGERS = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ?" + Constants_Fields.COL_REQUESTS_RECOVERY + " = ?";
			INSERT_SCHEDULER_STATE = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SCHEDULER_STATE + " (" + Constants_Fields.COL_INSTANCE_NAME + ", " + Constants_Fields.COL_LAST_CHECKIN_TIME + ", " + Constants_Fields.COL_CHECKIN_INTERVAL + ", " + Constants_Fields.COL_RECOVERER + ") VALUES(?, ?, ?, ?)";
			SELECT_SCHEDULER_STATE = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SCHEDULER_STATE + " WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ?";
			SELECT_SCHEDULER_STATES = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SCHEDULER_STATE;
			DELETE_SCHEDULER_STATE = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SCHEDULER_STATE + " WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ?";
			UPDATE_SCHEDULER_STATE = "UPDATE " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_SCHEDULER_STATE + " SET " + Constants_Fields.COL_LAST_CHECKIN_TIME + " = ? WHERE " + Constants_Fields.COL_INSTANCE_NAME + " = ?";
			INSERT_PAUSED_TRIGGER_GROUP = "INSERT INTO " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_PAUSED_TRIGGERS + " (" + Constants_Fields.COL_TRIGGER_GROUP + ") VALUES(?)";
			SELECT_PAUSED_TRIGGER_GROUP = "SELECT " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_PAUSED_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_PAUSED_TRIGGER_GROUPS = "SELECT " + Constants_Fields.COL_TRIGGER_GROUP + " FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_PAUSED_TRIGGERS;
			DELETE_PAUSED_TRIGGER_GROUP = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_PAUSED_TRIGGERS + " WHERE " + Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_PAUSED_TRIGGER_GROUPS = "DELETE FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_PAUSED_TRIGGERS;
		}
	}

	public interface StdJDBCConstants 
	{

	}

	// EOF
}