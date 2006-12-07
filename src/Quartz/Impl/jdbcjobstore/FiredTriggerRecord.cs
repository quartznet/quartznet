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
using System;
using Quartz.utils;

namespace Quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// Conveys the state of a fired-trigger record.
	/// </p>
	/// 
	/// </summary>
	/// <author>  James House
	/// </author>
	[Serializable]
	public class FiredTriggerRecord
	{
		public virtual string FireInstanceId
		{
			get { return fireInstanceId; }
			set { fireInstanceId = value; }
		}

		public virtual long FireTimestamp
		{
			get { return fireTimestamp; }
			set { fireTimestamp = value; }
		}

		public virtual bool JobIsStateful
		{
			get { return jobIsStateful; }
			set { jobIsStateful = value; }
		}

		public virtual Key JobKey
		{
			get { return jobKey; }

			set { jobKey = value; }

		}

		public virtual string SchedulerInstanceId
		{
			get { return schedulerInstanceId; }

			set { schedulerInstanceId = value; }

		}

		public virtual Key TriggerKey
		{
			get { return triggerKey; }

			set { triggerKey = value; }

		}

		public virtual string FireInstanceState
		{
			get { return fireInstanceState; }

			set { fireInstanceState = value; }

		}

		public virtual bool JobRequestsRecovery
		{
			get { return jobRequestsRecovery; }

			set { jobRequestsRecovery = value; }

		}

		public virtual bool TriggerIsVolatile
		{
			get { return triggerIsVolatile; }

			set { triggerIsVolatile = value; }

		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		private string fireInstanceId;

		private long fireTimestamp;

		private string schedulerInstanceId;

		private Key triggerKey;

		private string fireInstanceState;

		private bool triggerIsVolatile;

		private Key jobKey;

		private bool jobIsStateful;

		private bool jobRequestsRecovery;
	}

	// EOF
}