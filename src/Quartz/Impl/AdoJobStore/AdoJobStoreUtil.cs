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
using System.Globalization;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary> 
	/// This class contains utility functions for use in all delegate classes.
	/// </summary>
	/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
	public sealed class AdoJobStoreUtil
	{
	    private AdoJobStoreUtil()
	    {
	    }

	    /// <summary>
		/// Replace the table prefix in a query by replacing any occurrences of
		/// "{0}" with the table prefix.
		/// </summary>
		/// <param name="query">The unsubstitued query</param>
		/// <param name="tablePrefix">The table prefix</param>
		/// <returns>The query, with proper table prefix substituted</returns>
		public static string ReplaceTablePrefix(string query, string tablePrefix)
		{
			return string.Format(CultureInfo.InvariantCulture, query, tablePrefix);
		}

		/// <summary>
		/// Obtain a unique key for a given job.
		/// </summary>
		/// <param name="jobName">The job name</param>
		/// <param name="groupName">The group containing the job
		/// </param>
		/// <returns>A unique <code>string</code> key </returns>
		internal static string GetJobNameKey(string jobName, string groupName)
		{
			return String.Intern(string.Format(CultureInfo.InvariantCulture, "{0}_$x$x$_{1}", groupName, jobName));
		}

		/// <summary>
		/// Obtain a unique key for a given trigger.
		/// </summary>
		/// <param name="triggerName">The trigger name</param>
		/// <param name="groupName">The group containing the trigger</param>
		/// <returns>A unique <code>string</code> key</returns>
		internal static string GetTriggerNameKey(string triggerName, string groupName)
		{
			return String.Intern(string.Format(CultureInfo.InvariantCulture, "{0}_$x$x$_{1}", groupName, triggerName));
		}
	}
}