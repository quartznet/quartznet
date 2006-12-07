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

namespace Quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// This class contains utility functions for use in all delegate classes.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
	/// </author>
	public sealed class Util
	{
		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary> <p>
		/// Replace the table prefix in a query by replacing any occurrences of
		/// "{0}" with the table prefix.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">query
		/// the unsubstitued query
		/// </param>
		/// <param name="">query
		/// the table prefix
		/// </param>
		/// <returns> the query, with proper table prefix substituted
		/// </returns>
		public static string rtp(string query, string tablePrefix)
		{
			//UPGRADE_TODO: Method 'java.text.MessageFormat.format' was converted to 'string.Format' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_3"'
			return string.Format(query, new System.Object[] {tablePrefix});
		}

		/// <summary> <p>
		/// Obtain a unique key for a given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">jobName
		/// the job name
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> a unique <code>String</code> key
		/// </returns>
		internal static string getJobNameKey(string jobName, string groupName)
		{
			return String.Intern((groupName + "_$x$x$_" + jobName));
		}

		/// <summary> <p>
		/// Obtain a unique key for a given trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">triggerName
		/// the trigger name
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> a unique <code>String</code> key
		/// </returns>
		internal static string getTriggerNameKey(string triggerName, string groupName)
		{
			return String.Intern((groupName + "_$x$x$_" + triggerName));
		}
	}

	// EOF
}