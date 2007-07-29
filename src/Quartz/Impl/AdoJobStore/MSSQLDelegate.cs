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

using Common.Logging;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary>
	/// This is a driver delegate for the MSSQL ADO.NET driver.
	/// </summary>
	/// <author>Marko Lahma</author>
	public class MSSQLDelegate : StdAdoDelegate
	{
        /// <summary>
        /// Create new MSSQLDelegate instance.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="instanceId">The instance id.</param>
        public MSSQLDelegate(ILog log, string tablePrefix, string instanceId, IDbProvider dbProvider)
            : base(log, tablePrefix, instanceId, dbProvider)
		{
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="MSSQLDelegate"/> class.
        /// </summary>
        /// <param name="log">The log.</param>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="dbProvider">The db provider.</param>
        /// <param name="useProperties">if set to <c>true</c> [use properties].</param>
        public MSSQLDelegate(ILog log, string tablePrefix, string instanceId, IDbProvider dbProvider, bool useProperties)
            : base(log, tablePrefix, instanceId, dbProvider, useProperties)
		{
		}

		//---------------------------------------------------------------------------
		// protected methods that can be overridden by subclasses
		//---------------------------------------------------------------------------


	    protected override bool GetBoolean(object columnValue)
	    {
            // SQL Server treats as ints (1 and 0)
	        if (columnValue != null)
	        {
	            return Convert.ToInt32(columnValue) == 1;
	        }
            else
	        {
	            throw new ArgumentException("Value must be non-null.", "columnValue");
	        }
	    }
	}

	// EOF
}