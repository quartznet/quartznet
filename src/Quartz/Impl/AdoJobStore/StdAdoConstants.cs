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
			UPDATE_TRIGGER_STATES_FROM_OTHER_STATES = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?" + " WHERE " + AdoConstants.COL_TRIGGER_STATE + " = ? OR " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?" + " WHERE (" + AdoConstants.COL_TRIGGER_STATE + " = ? OR " + AdoConstants.COL_TRIGGER_STATE + " = ?) AND " + AdoConstants.COL_NEXT_FIRE_TIME + " < ?";
			SELECT_MISFIRED_TRIGGERS = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_NEXT_FIRE_TIME + " < ? ORDER BY START_TIME ASC";
			SELECT_TRIGGERS_IN_STATE = "SELECT " + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			SELECT_MISFIRED_TRIGGERS_IN_STATE = "SELECT " + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_NEXT_FIRE_TIME + " < ? AND " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE = "SELECT " + AdoConstants.COL_TRIGGER_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_NEXT_FIRE_TIME + " < ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ? AND " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			SELECT_VOLATILE_TRIGGERS = "SELECT " + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_IS_VOLATILE + " = ?";
			DELETE_FIRED_TRIGGERS = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS;
			INSERT_JOB_DETAIL = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " (" + AdoConstants.COL_JOB_NAME + ", " + AdoConstants.COL_JOB_GROUP + ", " + AdoConstants.COL_DESCRIPTION + ", " + AdoConstants.COL_JOB_CLASS + ", " + AdoConstants.COL_IS_DURABLE + ", " + AdoConstants.COL_IS_VOLATILE + ", " + AdoConstants.COL_IS_STATEFUL + ", " + AdoConstants.COL_REQUESTS_RECOVERY + ", " + AdoConstants.COL_JOB_DATAMAP + ") " + " VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?)";
			UPDATE_JOB_DETAIL = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " SET " + AdoConstants.COL_DESCRIPTION + " = ?, " + AdoConstants.COL_JOB_CLASS + " = ?, " + AdoConstants.COL_IS_DURABLE + " = ?, " + AdoConstants.COL_IS_VOLATILE + " = ?, " + AdoConstants.COL_IS_STATEFUL + " = ?, " + AdoConstants.COL_REQUESTS_RECOVERY + " = ?, " + AdoConstants.COL_JOB_DATAMAP + " = ? " + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_TRIGGERS_FOR_JOB = "SELECT " + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_TRIGGERS_FOR_CALENDAR = "SELECT " + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_CALENDAR_NAME + " = ?";
			SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP = "SELECT DISTINCT J." + AdoConstants.COL_JOB_NAME + ", J." + AdoConstants.COL_JOB_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " T, " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " J WHERE T." + AdoConstants.COL_TRIGGER_GROUP + " = ? AND T." + AdoConstants.COL_JOB_NAME + " = J." + AdoConstants.COL_JOB_NAME + " AND T." + AdoConstants.COL_JOB_GROUP + " = J." + AdoConstants.COL_JOB_GROUP + " AND J." + AdoConstants.COL_IS_STATEFUL + " = ?";
			DELETE_JOB_LISTENERS = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_LISTENERS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			DELETE_JOB_DETAIL = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_JOB_STATEFUL = "SELECT " + AdoConstants.COL_IS_STATEFUL + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_JOB_EXISTENCE = "SELECT " + AdoConstants.COL_JOB_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			UPDATE_JOB_DATA = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " SET " + AdoConstants.COL_JOB_DATAMAP + " = ? " + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			INSERT_JOB_LISTENER = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_LISTENERS + " (" + AdoConstants.COL_JOB_NAME + ", " + AdoConstants.COL_JOB_GROUP + ", " + AdoConstants.COL_JOB_LISTENER + ") VALUES(?, ?, ?)";
			SELECT_JOB_LISTENERS = "SELECT " + AdoConstants.COL_JOB_LISTENER + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_LISTENERS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_JOB_DETAIL = "SELECT *" + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_NUM_JOBS = "SELECT COUNT(" + AdoConstants.COL_JOB_NAME + ") " + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS;
			SELECT_JOB_GROUPS = "SELECT DISTINCT(" + AdoConstants.COL_JOB_GROUP + ") FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS;
			SELECT_JOBS_IN_GROUP = "SELECT " + AdoConstants.COL_JOB_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " WHERE " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_VOLATILE_JOBS = "SELECT " + AdoConstants.COL_JOB_NAME + ", " + AdoConstants.COL_JOB_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " WHERE " + AdoConstants.COL_IS_VOLATILE + " = ?";
			INSERT_TRIGGER = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " (" + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + ", " + AdoConstants.COL_JOB_NAME + ", " + AdoConstants.COL_JOB_GROUP + ", " + AdoConstants.COL_IS_VOLATILE + ", " + AdoConstants.COL_DESCRIPTION + ", " + AdoConstants.COL_NEXT_FIRE_TIME + ", " + AdoConstants.COL_PREV_FIRE_TIME + ", " + AdoConstants.COL_TRIGGER_STATE + ", " + AdoConstants.COL_TRIGGER_TYPE + ", " + AdoConstants.COL_START_TIME + ", " + AdoConstants.COL_END_TIME + ", " + AdoConstants.COL_CALENDAR_NAME + ", " + AdoConstants.COL_MISFIRE_INSTRUCTION + ", " + AdoConstants.COL_JOB_DATAMAP + ") " + " VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
			INSERT_SIMPLE_TRIGGER = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SIMPLE_TRIGGERS + " (" + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + ", " + AdoConstants.COL_REPEAT_COUNT + ", " + AdoConstants.COL_REPEAT_INTERVAL + ", " + AdoConstants.COL_TIMES_TRIGGERED + ") " + " VALUES(?, ?, ?, ?, ?)";
			INSERT_CRON_TRIGGER = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CRON_TRIGGERS + " (" + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + ", " + AdoConstants.COL_CRON_EXPRESSION + ", " + AdoConstants.COL_TIME_ZONE_ID + ") " + " VALUES(?, ?, ?, ?)";
			INSERT_BLOB_TRIGGER = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_BLOB_TRIGGERS + " (" + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + ", " + AdoConstants.COL_BLOB + ") " + " VALUES(?, ?, ?)";
			UPDATE_TRIGGER_SKIP_DATA = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_JOB_NAME + " = ?, " + AdoConstants.COL_JOB_GROUP + " = ?, " + AdoConstants.COL_IS_VOLATILE + " = ?, " + AdoConstants.COL_DESCRIPTION + " = ?, " + AdoConstants.COL_NEXT_FIRE_TIME + " = ?, " + AdoConstants.COL_PREV_FIRE_TIME + " = ?, " + AdoConstants.COL_TRIGGER_STATE + " = ?, " + AdoConstants.COL_TRIGGER_TYPE + " = ?, " + AdoConstants.COL_START_TIME + " = ?, " + AdoConstants.COL_END_TIME + " = ?, " + AdoConstants.COL_CALENDAR_NAME + " = ?, " + AdoConstants.COL_MISFIRE_INSTRUCTION + " = ? WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_JOB_NAME + " = ?, " + AdoConstants.COL_JOB_GROUP + " = ?, " + AdoConstants.COL_IS_VOLATILE + " = ?, " + AdoConstants.COL_DESCRIPTION + " = ?, " + AdoConstants.COL_NEXT_FIRE_TIME + " = ?, " + AdoConstants.COL_PREV_FIRE_TIME + " = ?, " + AdoConstants.COL_TRIGGER_STATE + " = ?, " + AdoConstants.COL_TRIGGER_TYPE + " = ?, " + AdoConstants.COL_START_TIME + " = ?, " + AdoConstants.COL_END_TIME + " = ?, " + AdoConstants.COL_CALENDAR_NAME + " = ?, " + AdoConstants.COL_MISFIRE_INSTRUCTION + " = ?, " + AdoConstants.COL_JOB_DATAMAP + " = ? WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			UPDATE_SIMPLE_TRIGGER = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SIMPLE_TRIGGERS + " SET " + AdoConstants.COL_REPEAT_COUNT + " = ?, " + AdoConstants.COL_REPEAT_INTERVAL + " = ?, " + AdoConstants.COL_TIMES_TRIGGERED + " = ? WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			UPDATE_CRON_TRIGGER = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CRON_TRIGGERS + " SET " + AdoConstants.COL_CRON_EXPRESSION + " = ? WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			UPDATE_BLOB_TRIGGER = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_BLOB_TRIGGERS + " SET " + AdoConstants.COL_BLOB + " = ? WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_EXISTENCE = "SELECT " + AdoConstants.COL_TRIGGER_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER_STATE = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?" + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER_STATE_FROM_STATE = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?" + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ? AND " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_GROUP_STATE = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATE = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?" + " WHERE " + AdoConstants.COL_TRIGGER_GROUP + " = ? AND " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_STATE_FROM_STATES = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?" + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ? AND (" + AdoConstants.COL_TRIGGER_STATE + " = ? OR " + AdoConstants.COL_TRIGGER_STATE + " = ? OR " + AdoConstants.COL_TRIGGER_STATE + " = ?)";
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATES = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ?" + " WHERE " + AdoConstants.COL_TRIGGER_GROUP + " = ? AND (" + AdoConstants.COL_TRIGGER_STATE + " = ? OR " + AdoConstants.COL_TRIGGER_STATE + " = ? OR " + AdoConstants.COL_TRIGGER_STATE + " = ?)";
			UPDATE_JOB_TRIGGER_STATES = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ? WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " SET " + AdoConstants.COL_TRIGGER_STATE + " = ? WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ? AND " + AdoConstants.COL_TRIGGER_STATE + " = ?";
			DELETE_TRIGGER_LISTENERS = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGER_LISTENERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			INSERT_TRIGGER_LISTENER = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGER_LISTENERS + " (" + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + ", " + AdoConstants.COL_TRIGGER_LISTENER + ") VALUES(?, ?, ?)";
			SELECT_TRIGGER_LISTENERS = "SELECT " + AdoConstants.COL_TRIGGER_LISTENER + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGER_LISTENERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			DELETE_SIMPLE_TRIGGER = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SIMPLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			DELETE_CRON_TRIGGER = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CRON_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			DELETE_BLOB_TRIGGER = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_BLOB_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			DELETE_TRIGGER = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_NUM_TRIGGERS_FOR_JOB = "SELECT COUNT(" + AdoConstants.COL_TRIGGER_NAME + ") FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_JOB_FOR_TRIGGER = "SELECT J." + AdoConstants.COL_JOB_NAME + ", J." + AdoConstants.COL_JOB_GROUP + ", J." + AdoConstants.COL_IS_DURABLE + ", J." + AdoConstants.COL_JOB_CLASS + ", J." + AdoConstants.COL_REQUESTS_RECOVERY + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " T, " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_JOB_DETAILS + " J WHERE T." + AdoConstants.COL_TRIGGER_NAME + " = ? AND T." + AdoConstants.COL_TRIGGER_GROUP + " = ? AND T." + AdoConstants.COL_JOB_NAME + " = J." + AdoConstants.COL_JOB_NAME + " AND T." + AdoConstants.COL_JOB_GROUP + " = J." + AdoConstants.COL_JOB_GROUP;
			SELECT_TRIGGER = "SELECT *" + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_DATA = "SELECT " + AdoConstants.COL_JOB_DATAMAP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_STATE = "SELECT " + AdoConstants.COL_TRIGGER_STATE + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_STATUS = "SELECT " + AdoConstants.COL_TRIGGER_STATE + ", " + AdoConstants.COL_NEXT_FIRE_TIME + ", " + AdoConstants.COL_JOB_NAME + ", " + AdoConstants.COL_JOB_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_SIMPLE_TRIGGER = "SELECT *" + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SIMPLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_CRON_TRIGGER = "SELECT *" + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CRON_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_BLOB_TRIGGER = "SELECT *" + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_BLOB_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_NUM_TRIGGERS = "SELECT COUNT(" + AdoConstants.COL_TRIGGER_NAME + ") " + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS;
			SELECT_NUM_TRIGGERS_IN_GROUP = "SELECT COUNT(" + AdoConstants.COL_TRIGGER_NAME + ") " + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_GROUPS = "SELECT DISTINCT(" + AdoConstants.COL_TRIGGER_GROUP + ") FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS;
			SELECT_TRIGGERS_IN_GROUP = "SELECT " + AdoConstants.COL_TRIGGER_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			INSERT_CALENDAR = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CALENDARS + " (" + AdoConstants.COL_CALENDAR_NAME + ", " + AdoConstants.COL_CALENDAR + ") " + " VALUES(?, ?)";
			UPDATE_CALENDAR = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CALENDARS + " SET " + AdoConstants.COL_CALENDAR + " = ? " + " WHERE " + AdoConstants.COL_CALENDAR_NAME + " = ?";
			SELECT_CALENDAR_EXISTENCE = "SELECT " + AdoConstants.COL_CALENDAR_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CALENDARS + " WHERE " + AdoConstants.COL_CALENDAR_NAME + " = ?";
			SELECT_CALENDAR = "SELECT *" + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CALENDARS + " WHERE " + AdoConstants.COL_CALENDAR_NAME + " = ?";
			SELECT_REFERENCED_CALENDAR = "SELECT " + AdoConstants.COL_CALENDAR_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_CALENDAR_NAME + " = ?";
			DELETE_CALENDAR = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CALENDARS + " WHERE " + AdoConstants.COL_CALENDAR_NAME + " = ?";
			SELECT_NUM_CALENDARS = "SELECT COUNT(" + AdoConstants.COL_CALENDAR_NAME + ") " + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CALENDARS;
			SELECT_CALENDARS = "SELECT " + AdoConstants.COL_CALENDAR_NAME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_CALENDARS;
			SELECT_NEXT_FIRE_TIME = "SELECT MIN(" + AdoConstants.COL_NEXT_FIRE_TIME + ") AS " + AdoConstants.ALIAS_COL_NEXT_FIRE_TIME + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_STATE + " = ? AND " + AdoConstants.COL_NEXT_FIRE_TIME + " >= 0";
			SELECT_TRIGGER_FOR_FIRE_TIME = "SELECT " + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_STATE + " = ? AND " + AdoConstants.COL_NEXT_FIRE_TIME + " = ?";
			INSERT_FIRED_TRIGGER = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " (" + AdoConstants.COL_ENTRY_ID + ", " + AdoConstants.COL_TRIGGER_NAME + ", " + AdoConstants.COL_TRIGGER_GROUP + ", " + AdoConstants.COL_IS_VOLATILE + ", " + AdoConstants.COL_INSTANCE_NAME + ", " + AdoConstants.COL_FIRED_TIME + ", " + AdoConstants.COL_ENTRY_STATE + ", " + AdoConstants.COL_JOB_NAME + ", " + AdoConstants.COL_JOB_GROUP + ", " + AdoConstants.COL_IS_STATEFUL + ", " + AdoConstants.COL_REQUESTS_RECOVERY + ") VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
			UPDATE_INSTANCES_FIRED_TRIGGER_STATE = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " SET " + AdoConstants.COL_ENTRY_STATE + " = ? AND " + AdoConstants.COL_FIRED_TIME + " = ? WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ?";
			SELECT_INSTANCES_FIRED_TRIGGERS = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ?";
			SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ? AND " + AdoConstants.COL_REQUESTS_RECOVERY + " = ?";
			SELECT_JOB_EXECUTION_COUNT = "SELECT COUNT(" + AdoConstants.COL_TRIGGER_NAME + ") FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS;
			SELECT_FIRED_TRIGGER = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_NAME + " = ? AND " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_FIRED_TRIGGER_GROUP = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS_OF_JOB = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_JOB_NAME + " = ? AND " + AdoConstants.COL_JOB_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS_OF_JOB_GROUP = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_JOB_GROUP + " = ?";
			DELETE_FIRED_TRIGGER = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_ENTRY_ID + " = ?";
			DELETE_INSTANCES_FIRED_TRIGGERS = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ?";
			DELETE_VOLATILE_FIRED_TRIGGERS = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_IS_VOLATILE + " = ?";
			DELETE_NO_RECOVERY_FIRED_TRIGGERS = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_FIRED_TRIGGERS + " WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ?" + AdoConstants.COL_REQUESTS_RECOVERY + " = ?";
			INSERT_SCHEDULER_STATE = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SCHEDULER_STATE + " (" + AdoConstants.COL_INSTANCE_NAME + ", " + AdoConstants.COL_LAST_CHECKIN_TIME + ", " + AdoConstants.COL_CHECKIN_INTERVAL + ", " + AdoConstants.COL_RECOVERER + ") VALUES(?, ?, ?, ?)";
			SELECT_SCHEDULER_STATE = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SCHEDULER_STATE + " WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ?";
			SELECT_SCHEDULER_STATES = "SELECT * FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SCHEDULER_STATE;
			DELETE_SCHEDULER_STATE = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SCHEDULER_STATE + " WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ?";
			UPDATE_SCHEDULER_STATE = "UPDATE " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_SCHEDULER_STATE + " SET " + AdoConstants.COL_LAST_CHECKIN_TIME + " = ? WHERE " + AdoConstants.COL_INSTANCE_NAME + " = ?";
			INSERT_PAUSED_TRIGGER_GROUP = "INSERT INTO " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_PAUSED_TRIGGERS + " (" + AdoConstants.COL_TRIGGER_GROUP + ") VALUES(?)";
			SELECT_PAUSED_TRIGGER_GROUP = "SELECT " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_PAUSED_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			SELECT_PAUSED_TRIGGER_GROUPS = "SELECT " + AdoConstants.COL_TRIGGER_GROUP + " FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_PAUSED_TRIGGERS;
			DELETE_PAUSED_TRIGGER_GROUP = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_PAUSED_TRIGGERS + " WHERE " + AdoConstants.COL_TRIGGER_GROUP + " = ?";
			DELETE_PAUSED_TRIGGER_GROUPS = "DELETE FROM " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_PAUSED_TRIGGERS;
		}
	}
}