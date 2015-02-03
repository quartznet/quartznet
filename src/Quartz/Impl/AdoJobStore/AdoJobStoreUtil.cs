#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
#endregion

using System;
using System.Globalization;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary> 
	/// This class contains utility functions for use in all delegate classes.
	/// </summary>
	/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
	/// <author>Marko Lahma (.NET)</author>
	public static class AdoJobStoreUtil
	{
	    /// <summary>
		/// Replace the table prefix in a query by replacing any occurrences of
		/// "{0}" with the table prefix.
		/// </summary>
		/// <param name="query">The unsubstituted query</param>
		/// <param name="tablePrefix">The table prefix</param>
		/// <param name="schedNameLiteral">the scheduler name</param>
		/// <returns>The query, with proper table prefix substituted</returns>
		public static string ReplaceTablePrefix(string query, string tablePrefix, string schedNameLiteral)
		{
			return String.Format(CultureInfo.InvariantCulture, query, tablePrefix, schedNameLiteral);
		}
	}
}