/* 
* Copyright 2004-2009 James House 
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
using System.Data;
using System.Globalization;
using System.Threading;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary> 
    /// Internal database based lock handler for providing thread/resource locking 
    /// in order to protect resources from being altered by multiple threads at the 
    /// same time.
    /// </summary>
    /// <author>James House</author>
    public class StdRowLockSemaphore : DBSemaphore
    {
        /*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constants.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

        public static readonly string SelectForLock =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = @lockName FOR UPDATE", TablePrefixSubst, TableLocks,
                          ColumnLockName);

        /*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

        /// <summary>
        /// Initializes a new instance of the <see cref="StdRowLockSemaphore"/> class.
        /// </summary>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="selectWithLockSQL">The select with lock SQL.</param>
        /// <param name="dbProvider"></param>
        public StdRowLockSemaphore(string tablePrefix, string selectWithLockSQL, IDbProvider dbProvider) : base(tablePrefix, selectWithLockSQL, SelectForLock, dbProvider)
        {
        }
    /**
     * Execute the SQL select for update that will lock the proper database row.
     */
    protected override void ExecuteSQL(ConnectionAndTransactionHolder conn, string lockName, string expandedSQL)
    {
        try {
            using (IDbCommand cmd = AdoUtil.PrepareCommand(conn, expandedSQL))
            {
                AdoUtil.AddCommandParameter(cmd, 1, "lockName", lockName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (Log.IsDebugEnabled)
                    {
                        Log.Debug("Lock '" + lockName + "' is being obtained: " + Thread.CurrentThread.Name);
                    }
                    
                    if (!rs.Read())
                    {
                        throw new Exception(AdoJobStoreUtil.ReplaceTablePrefix("No row exists in table " + TablePrefixSubst + TableLocks + " for lock named: " + lockName, TablePrefix));
                    }
                }
            }
        } catch (Exception sqle) {

            if (Log.IsDebugEnabled) {
                Log.Debug(
                    "Lock '" + lockName + "' was not obtained by: " + 
                    Thread.CurrentThread.Name);
            }
            
            throw new LockException("Failure obtaining db row lock: "
                    + sqle.Message, sqle);
        } 
    
    }
    }
}