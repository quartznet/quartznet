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
using System.Collections.Generic;

#if NET_35
using TimeZone = System.TimeZoneInfo;
#endif


namespace Quartz.Xml
{
	/// <summary> 
	/// Wraps a <see cref="JobDetail" /> and <see cref="Trigger" />.
	/// </summary>
	/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
	/// <author>James House</author>
	public class JobSchedulingBundle
	{
		protected JobDetail jobDetail;
		protected IList<Trigger> triggers = new List<Trigger>();
		
		/// <summary>
		/// Gets or sets the job detail.
		/// </summary>
		/// <value>The job detail.</value>
		public virtual JobDetail JobDetail
		{
			get { return jobDetail; }
			set { jobDetail = value; }
		}

		/// <summary>
		/// Gets or sets the triggers associated with this bundle.
		/// </summary>
		/// <value>The triggers.</value>
		public virtual IList<Trigger> Triggers
		{
			get { return triggers; }
			set { triggers = value; }
		}

		/// <summary>
		/// Gets the name of the bundle.
		/// </summary>
		/// <value>The name.</value>
		public virtual string Name
		{
			get
			{
				if (JobDetail != null)
				{
					return JobDetail.Name;
				}
				else
				{
					return null;
				}
			}
		}

		/// <summary>
		/// Gets the full name.
		/// </summary>
		/// <value>The full name.</value>
		public virtual string FullName
		{
			get
			{
				if (JobDetail != null)
				{
					return JobDetail.FullName;
				}
				else
				{
					return null;
				}
			}
		}

		/// <summary>
		/// Gets a value indicating whether this <see cref="JobSchedulingBundle"/> is valid.
		/// </summary>
		/// <value><c>true</c> if valid; otherwise, <c>false</c>.</value>
		public virtual bool Valid
		{
			get { return ((JobDetail != null) && (Triggers != null)); }
		}


		/// <summary>
		/// Adds a trigger to this bundle.
		/// </summary>
		/// <param name="trigger">The trigger.</param>
		public virtual void AddTrigger(Trigger trigger)
		{
			if (trigger.StartTimeUtc == DateTime.MinValue)
			{
				trigger.StartTimeUtc = DateTime.UtcNow;
			}

			if (trigger is CronTrigger)
			{
				CronTrigger ct = (CronTrigger) trigger;
				if (ct.TimeZone == null)
				{
#if !NET_35
					ct.TimeZone = TimeZone.CurrentTimeZone;
#else
                    ct.TimeZone = TimeZoneInfo.Local;
#endif
				}
			}

			triggers.Add(trigger);
		}

		/// <summary>
		/// Removes the given trigger from this bundle.
		/// </summary>
		/// <param name="trigger">The trigger.</param>
		public virtual void RemoveTrigger(Trigger trigger)
		{
			triggers.Remove(trigger);
		}
	}
}