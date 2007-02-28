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

namespace Quartz.Impl.AdoJobStore
{
	/// <summary>
	/// Conveys a scheduler-instance state record.
	/// </summary>
	/// <author>James House</author>
	[Serializable]
	public class SchedulerStateRecord
	{
		private string schedulerInstanceId;
		private long checkinTimestamp;
		private long checkinInterval;
		private string recoverer;


		/// <summary>
		/// Gets or sets the checkin interval.
		/// </summary>
		/// <value>The checkin interval.</value>
		public virtual long CheckinInterval
		{
			get { return checkinInterval; }
			set { checkinInterval = value; }
		}

		/// <summary>
		/// Gets or sets the checkin timestamp.
		/// </summary>
		/// <value>The checkin timestamp.</value>
		public virtual long CheckinTimestamp
		{
			get { return checkinTimestamp; }
			set { checkinTimestamp = value; }
		}

		/// <summary>
		/// Gets or sets the recoverer.
		/// </summary>
		/// <value>The recoverer.</value>
		public virtual string Recoverer
		{
			get { return recoverer; }
			set { recoverer = value; }
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
	}

}