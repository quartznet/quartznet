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
	/// This interface can be implemented by any <code>IDriverDelegate</code>
	/// class that needs to use the constants contained herein.
	/// </summary>
	/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
	/// <author>James House</author>
	public class AdoConstants
	{
		// Table names
		public static readonly string TABLE_JOB_DETAILS = "JOB_DETAILS";
		public static readonly string TABLE_TRIGGERS = "TRIGGERS";
		public static readonly string TABLE_SIMPLE_TRIGGERS = "SIMPLE_TRIGGERS";
		public static readonly string TABLE_CRON_TRIGGERS = "CRON_TRIGGERS";
		public static readonly string TABLE_BLOB_TRIGGERS = "BLOB_TRIGGERS";
		public static readonly string TABLE_FIRED_TRIGGERS = "FIRED_TRIGGERS";
		public static readonly string TABLE_JOB_LISTENERS = "JOB_LISTENERS";
		public static readonly string TABLE_TRIGGER_LISTENERS = "TRIGGER_LISTENERS";
		public static readonly string TABLE_CALENDARS = "CALENDARS";
		public static readonly string TABLE_PAUSED_TRIGGERS = "PAUSED_TRIGGER_GRPS";
		public static readonly string TABLE_LOCKS = "LOCKS";
		public static readonly string TABLE_SCHEDULER_STATE = "SCHEDULER_STATE";
		
		// TABLE_JOB_DETAILS columns names
		public static readonly string COL_JOB_NAME = "JOB_NAME";
		public static readonly string COL_JOB_GROUP = "JOB_GROUP";
		public static readonly string COL_IS_DURABLE = "IS_DURABLE";
		public static readonly string COL_IS_VOLATILE = "IS_VOLATILE";
		public static readonly string COL_IS_STATEFUL = "IS_STATEFUL";
		public static readonly string COL_REQUESTS_RECOVERY = "REQUESTS_RECOVERY";
		public static readonly string COL_JOB_DATAMAP = "JOB_DATA";
		public static readonly string COL_JOB_CLASS = "JOB_CLASS_NAME";
		public static readonly string COL_DESCRIPTION = "DESCRIPTION";
		
		// TABLE_JOB_LISTENERS columns names
		public static readonly string COL_JOB_LISTENER = "JOB_LISTENER";
		
		// TABLE_TRIGGERS columns names
		public static readonly string COL_TRIGGER_NAME = "TRIGGER_NAME";
		public static readonly string COL_TRIGGER_GROUP = "TRIGGER_GROUP";
		public static readonly string COL_NEXT_FIRE_TIME = "NEXT_FIRE_TIME";
		public static readonly string COL_PREV_FIRE_TIME = "PREV_FIRE_TIME";
		public static readonly string COL_TRIGGER_STATE = "TRIGGER_STATE";
		public static readonly string COL_TRIGGER_TYPE = "TRIGGER_TYPE";
		public static readonly string COL_START_TIME = "START_TIME";
		public static readonly string COL_END_TIME = "END_TIME";
		public static readonly string COL_MISFIRE_INSTRUCTION = "MISFIRE_INSTR";
		public static readonly string ALIAS_COL_NEXT_FIRE_TIME = "ALIAS_NXT_FR_TM";
		
		// TABLE_SIMPLE_TRIGGERS columns names
		public static readonly string COL_REPEAT_COUNT = "REPEAT_COUNT";
		public static readonly string COL_REPEAT_INTERVAL = "REPEAT_INTERVAL";
		public static readonly string COL_TIMES_TRIGGERED = "TIMES_TRIGGERED";
		
		// TABLE_CRON_TRIGGERS columns names
		public static readonly string COL_CRON_EXPRESSION = "CRON_EXPRESSION";
		
		// TABLE_BLOB_TRIGGERS columns names
		public static readonly string COL_BLOB = "BLOB_DATA";
		public static readonly string COL_TIME_ZONE_ID = "TIME_ZONE_ID";
		
		// TABLE_TRIGGER_LISTENERS
		public static readonly string COL_TRIGGER_LISTENER = "TRIGGER_LISTENER";
		
		// TABLE_FIRED_TRIGGERS columns names
		public static readonly string COL_INSTANCE_NAME = "INSTANCE_NAME";
		public static readonly string COL_FIRED_TIME = "FIRED_TIME";
		public static readonly string COL_ENTRY_ID = "ENTRY_ID";
		public static readonly string COL_ENTRY_STATE = "STATE";
		
		// TABLE_CALENDARS columns names
		public static readonly string COL_CALENDAR_NAME = "CALENDAR_NAME";
		public static readonly string COL_CALENDAR = "CALENDAR";
		
		// TABLE_LOCKS columns names
		public static readonly string COL_LOCK_NAME = "LOCK_NAME";
		
		// TABLE_LOCKS columns names
		public static readonly string COL_LAST_CHECKIN_TIME = "LAST_CHECKIN_TIME";
		public static readonly string COL_CHECKIN_INTERVAL = "CHECKIN_INTERVAL";
		public static readonly string COL_RECOVERER = "RECOVERER";
		
		// MISC CONSTANTS
		public static readonly string DEFAULT_TABLE_PREFIX = "QRTZ_";
		
		// STATES
		public static readonly string STATE_WAITING = "WAITING";
		public static readonly string STATE_ACQUIRED = "ACQUIRED";
		public static readonly string STATE_EXECUTING = "EXECUTING";
		public static readonly string STATE_COMPLETE = "COMPLETE";
		public static readonly string STATE_BLOCKED = "BLOCKED";
		public static readonly string STATE_ERROR = "ERROR";
		public static readonly string STATE_PAUSED = "PAUSED";
		public static readonly string STATE_PAUSED_BLOCKED = "PAUSED_BLOCKED";
		public static readonly string STATE_DELETED = "DELETED";
		public static readonly string STATE_MISFIRED = "MISFIRED";
		public static readonly string ALL_GROUPS_PAUSED = "_$_ALL_GROUPS_PAUSED_$_";
		
		// TRIGGER TYPES
		public static readonly string TTYPE_SIMPLE = "SIMPLE";
		public static readonly string TTYPE_CRON = "CRON";
		public static readonly string TTYPE_BLOB = "BLOB";
	}
}