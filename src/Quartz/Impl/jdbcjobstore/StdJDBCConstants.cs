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

namespace Quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// This interface extends <code>{@link
	/// org.quartz.impl.jdbcjobstore.Constants}</code>
	/// to include the query string constants in use by the <code>{@link
	/// org.quartz.impl.jdbcjobstore.StdJDBCDelegate}</code>
	/// class.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
	/// </author>
	public struct StdJDBCConstants_Fields
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
			UPDATE_TRIGGER_STATES_FROM_OTHER_STATES = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?) AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " < ?";
			SELECT_MISFIRED_TRIGGERS = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " < ? ORDER BY START_TIME ASC";
			SELECT_TRIGGERS_IN_STATE = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			SELECT_MISFIRED_TRIGGERS_IN_STATE = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " < ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " < ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			SELECT_VOLATILE_TRIGGERS = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + " = ?";
			DELETE_FIRED_TRIGGERS = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS;
			INSERT_JOB_DETAIL = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_DESCRIPTION + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_CLASS + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_DURABLE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_STATEFUL + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REQUESTS_RECOVERY + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_DATAMAP + ") " + " VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?)";
			UPDATE_JOB_DETAIL = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_DESCRIPTION + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_CLASS + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_DURABLE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_STATEFUL + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REQUESTS_RECOVERY + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_DATAMAP + " = ? " + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_TRIGGERS_FOR_JOB = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_TRIGGERS_FOR_CALENDAR = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP = "SELECT DISTINCT J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " T, " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " J WHERE T." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ? AND T." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " AND T." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " AND J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_STATEFUL + " = ?";
			DELETE_JOB_LISTENERS = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_LISTENERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			DELETE_JOB_DETAIL = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_STATEFUL = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_STATEFUL + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_EXISTENCE = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			UPDATE_JOB_DATA = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_DATAMAP + " = ? " + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			INSERT_JOB_LISTENER = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_LISTENERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_LISTENER + ") VALUES(?, ?, ?)";
			SELECT_JOB_LISTENERS = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_LISTENER + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_LISTENERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_DETAIL = "SELECT *" + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_NUM_JOBS = "SELECT COUNT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ") " + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS;
			SELECT_JOB_GROUPS = "SELECT DISTINCT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + ") FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS;
			SELECT_JOBS_IN_GROUP = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_VOLATILE_JOBS = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + " = ?";
			INSERT_TRIGGER = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_DESCRIPTION + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_PREV_FIRE_TIME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_TYPE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_START_TIME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_END_TIME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_MISFIRE_INSTRUCTION + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_DATAMAP + ") " + " VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
			INSERT_SIMPLE_TRIGGER = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SIMPLE_TRIGGERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REPEAT_COUNT + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REPEAT_INTERVAL + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TIMES_TRIGGERED + ") " + " VALUES(?, ?, ?, ?, ?)";
			INSERT_CRON_TRIGGER = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CRON_TRIGGERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CRON_EXPRESSION + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TIME_ZONE_ID + ") " + " VALUES(?, ?, ?, ?)";
			INSERT_BLOB_TRIGGER = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_BLOB_TRIGGERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_BLOB + ") " + " VALUES(?, ?, ?)";
			UPDATE_TRIGGER_SKIP_DATA = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_DESCRIPTION + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_PREV_FIRE_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_TYPE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_START_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_END_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_MISFIRE_INSTRUCTION + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_DESCRIPTION + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_PREV_FIRE_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_TYPE + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_START_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_END_TIME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_MISFIRE_INSTRUCTION + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_DATAMAP + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_SIMPLE_TRIGGER = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SIMPLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REPEAT_COUNT + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REPEAT_INTERVAL + " = ?, " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TIMES_TRIGGERED + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_CRON_TRIGGER = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CRON_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CRON_EXPRESSION + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_BLOB_TRIGGER = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_BLOB_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_BLOB + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_EXISTENCE = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER_STATE = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			UPDATE_TRIGGER_STATE_FROM_STATE = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_GROUP_STATE = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATE = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			UPDATE_TRIGGER_STATE_FROM_STATES = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ? AND (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?)";
			UPDATE_TRIGGER_GROUP_STATE_FROM_STATES = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?" + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ? AND (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? OR " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?)";
			UPDATE_JOB_TRIGGER_STATES = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ?";
			DELETE_TRIGGER_LISTENERS = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGER_LISTENERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			INSERT_TRIGGER_LISTENER = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGER_LISTENERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_LISTENER + ") VALUES(?, ?, ?)";
			SELECT_TRIGGER_LISTENERS = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_LISTENER + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGER_LISTENERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_SIMPLE_TRIGGER = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SIMPLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_CRON_TRIGGER = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CRON_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_BLOB_TRIGGER = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_BLOB_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_TRIGGER = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_NUM_TRIGGERS_FOR_JOB = "SELECT COUNT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ") FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_JOB_FOR_TRIGGER = "SELECT J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + ", J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_DURABLE + ", J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_CLASS + ", J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REQUESTS_RECOVERY + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " T, " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_JOB_DETAILS + " J WHERE T." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND T." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ? AND T." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " AND T." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = J." + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP;
			SELECT_TRIGGER = "SELECT *" + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_DATA = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_DATAMAP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_STATE = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_STATUS = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_SIMPLE_TRIGGER = "SELECT *" + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SIMPLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_CRON_TRIGGER = "SELECT *" + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CRON_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_BLOB_TRIGGER = "SELECT *" + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_BLOB_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_NUM_TRIGGERS = "SELECT COUNT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ") " + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS;
			SELECT_NUM_TRIGGERS_IN_GROUP = "SELECT COUNT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ") " + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_TRIGGER_GROUPS = "SELECT DISTINCT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ") FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS;
			SELECT_TRIGGERS_IN_GROUP = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			INSERT_CALENDAR = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CALENDARS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR + ") " + " VALUES(?, ?)";
			UPDATE_CALENDAR = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CALENDARS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR + " = ? " + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_CALENDAR_EXISTENCE = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CALENDARS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_CALENDAR = "SELECT *" + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CALENDARS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_REFERENCED_CALENDAR = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?";
			DELETE_CALENDAR = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CALENDARS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " = ?";
			SELECT_NUM_CALENDARS = "SELECT COUNT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + ") " + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CALENDARS;
			SELECT_CALENDARS = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CALENDAR_NAME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_CALENDARS;
			SELECT_NEXT_FIRE_TIME = "SELECT MIN(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + ") AS " + org.quartz.impl.jdbcjobstore.Constants_Fields.ALIAS_COL_NEXT_FIRE_TIME + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " >= 0";
			SELECT_TRIGGER_FOR_FIRE_TIME = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_STATE + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_NEXT_FIRE_TIME + " = ?";
			INSERT_FIRED_TRIGGER = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_ENTRY_ID + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_FIRED_TIME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_ENTRY_STATE + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_STATEFUL + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REQUESTS_RECOVERY + ") VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
			UPDATE_INSTANCES_FIRED_TRIGGER_STATE = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_ENTRY_STATE + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_FIRED_TIME + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ?";
			SELECT_INSTANCES_FIRED_TRIGGERS = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ?";
			SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REQUESTS_RECOVERY + " = ?";
			SELECT_JOB_EXECUTION_COUNT = "SELECT COUNT(" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + ") FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS;
			SELECT_FIRED_TRIGGER = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_FIRED_TRIGGER_GROUP = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS_OF_JOB = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_NAME + " = ? AND " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			SELECT_FIRED_TRIGGERS_OF_JOB_GROUP = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_JOB_GROUP + " = ?";
			DELETE_FIRED_TRIGGER = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_ENTRY_ID + " = ?";
			DELETE_INSTANCES_FIRED_TRIGGERS = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ?";
			DELETE_VOLATILE_FIRED_TRIGGERS = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_IS_VOLATILE + " = ?";
			DELETE_NO_RECOVERY_FIRED_TRIGGERS = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_FIRED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ?" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_REQUESTS_RECOVERY + " = ?";
			INSERT_SCHEDULER_STATE = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SCHEDULER_STATE + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_LAST_CHECKIN_TIME + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_CHECKIN_INTERVAL + ", " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_RECOVERER + ") VALUES(?, ?, ?, ?)";
			SELECT_SCHEDULER_STATE = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SCHEDULER_STATE + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ?";
			SELECT_SCHEDULER_STATES = "SELECT * FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SCHEDULER_STATE;
			DELETE_SCHEDULER_STATE = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SCHEDULER_STATE + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ?";
			UPDATE_SCHEDULER_STATE = "UPDATE " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_SCHEDULER_STATE + " SET " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_LAST_CHECKIN_TIME + " = ? WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_INSTANCE_NAME + " = ?";
			INSERT_PAUSED_TRIGGER_GROUP = "INSERT INTO " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_PAUSED_TRIGGERS + " (" + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + ") VALUES(?)";
			SELECT_PAUSED_TRIGGER_GROUP = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_PAUSED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			SELECT_PAUSED_TRIGGER_GROUPS = "SELECT " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_PAUSED_TRIGGERS;
			DELETE_PAUSED_TRIGGER_GROUP = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_PAUSED_TRIGGERS + " WHERE " + org.quartz.impl.jdbcjobstore.Constants_Fields.COL_TRIGGER_GROUP + " = ?";
			DELETE_PAUSED_TRIGGER_GROUPS = "DELETE FROM " + org.quartz.impl.jdbcjobstore.StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + org.quartz.impl.jdbcjobstore.Constants_Fields.TABLE_PAUSED_TRIGGERS;
		}
	}

	public interface StdJDBCConstants : Constants
	{
		//UPGRADE_NOTE: Members of interface 'StdJDBCConstants' were extracted into structure 'StdJDBCConstants_Fields'. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1045_3"'

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constants.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

	}

	// EOF
}