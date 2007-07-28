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

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
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
        private string fireInstanceId;
        private long fireTimestamp;
        private string schedulerInstanceId;
        private Key triggerKey;
        private string fireInstanceState;
        private bool triggerIsVolatile;
        private Key jobKey;
        private bool jobIsStateful;
        private bool jobRequestsRecovery;
        private int priority;

        /// <summary>
        /// Gets or sets the fire instance id.
        /// </summary>
        /// <value>The fire instance id.</value>
		public virtual string FireInstanceId
		{
			get { return fireInstanceId; }
			set { fireInstanceId = value; }
		}

        /// <summary>
        /// Gets or sets the fire timestamp.
        /// </summary>
        /// <value>The fire timestamp.</value>
		public virtual long FireTimestamp
		{
			get { return fireTimestamp; }
			set { fireTimestamp = value; }
		}

        /// <summary>
        /// Gets or sets a value indicating whether [job is stateful].
        /// </summary>
        /// <value><c>true</c> if [job is stateful]; otherwise, <c>false</c>.</value>
		public virtual bool JobIsStateful
		{
			get { return jobIsStateful; }
			set { jobIsStateful = value; }
		}

        /// <summary>
        /// Gets or sets the job key.
        /// </summary>
        /// <value>The job key.</value>
		public virtual Key JobKey
		{
			get { return jobKey; }
			set { jobKey = value; }

		}

        /// <summary>
        /// Gets or sets the scheduler instance id.
        /// </summary>
        /// <value>The scheduler instance id.</value>
		public virtual string SchedulerInstanceId
		{
			get { return schedulerInstanceId; }
			set { schedulerInstanceId = value; }

		}

        /// <summary>
        /// Gets or sets the trigger key.
        /// </summary>
        /// <value>The trigger key.</value>
		public virtual Key TriggerKey
		{
			get { return triggerKey; }
			set { triggerKey = value; }

		}

        /// <summary>
        /// Gets or sets the state of the fire instance.
        /// </summary>
        /// <value>The state of the fire instance.</value>
		public virtual string FireInstanceState
		{
			get { return fireInstanceState; }
			set { fireInstanceState = value; }

		}

        /// <summary>
        /// Gets or sets a value indicating whether [job requests recovery].
        /// </summary>
        /// <value><c>true</c> if [job requests recovery]; otherwise, <c>false</c>.</value>
		public virtual bool JobRequestsRecovery
		{
			get { return jobRequestsRecovery; }
			set { jobRequestsRecovery = value; }

		}

        /// <summary>
        /// Gets or sets a value indicating whether [trigger is volatile].
        /// </summary>
        /// <value><c>true</c> if [trigger is volatile]; otherwise, <c>false</c>.</value>
		public virtual bool TriggerIsVolatile
		{
			get { return triggerIsVolatile; }
			set { triggerIsVolatile = value; }

		}

        /// <summary>
        /// Gets or sets the priority.
        /// </summary>
        /// <value>The priority.</value>
	    public int Priority
	    {
	        get { return priority; }
	        set { priority = value; }
	    }

	}

}
