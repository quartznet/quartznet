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
    /// <author>Marko Lahma (.NET)</author>
    public class StdRowLockSemaphore : DBSemaphore
    {
        public static readonly string SelectForLock =
            string.Format(CultureInfo.InvariantCulture, "SELECT * FROM {0}{1} WHERE {2} = {3} AND {4} = @lockName FOR UPDATE",
                          TablePrefixSubst, TableLocks, ColumnSchedulerName, SchedulerNameSubst, ColumnLockName);

        
        public static readonly string InsertLock = 
            string.Format(CultureInfo.InstalledUICulture, "INSERT INTO {0}{1}({2}, {3}) VALUES ({4}, @lockName)", 
                          TablePrefixSubst, TableLocks, ColumnSchedulerName, ColumnLockName, SchedulerNameSubst); 

        /// <summary>
        /// Initializes a new instance of the <see cref="StdRowLockSemaphore"/> class.
        /// </summary>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="schedName">the scheduler name</param>
        /// <param name="selectWithLockSQL">The select with lock SQL.</param>
        /// <param name="dbProvider"></param>
        public StdRowLockSemaphore(string tablePrefix, string schedName, string selectWithLockSQL, IDbProvider dbProvider)
            : base(tablePrefix, schedName, selectWithLockSQL ?? SelectForLock, InsertLock, dbProvider)
        {
        }

        /// <summary>
        /// Execute the SQL select for update that will lock the proper database row.
        /// </summary>
        protected override void ExecuteSQL(ConnectionAndTransactionHolder conn, string lockName, string expandedSQL, string expandedInsertSQL)
        {
            try
            {
                using (IDbCommand cmd = AdoUtil.PrepareCommand(conn, expandedSQL))
                {
                    AdoUtil.AddCommandParameter(cmd, "lockName", lockName);

                    bool found = false;
                    using (IDataReader rs = cmd.ExecuteReader())
                    {
                        if (Log.IsDebugEnabled)
                        {
                            Log.Debug("Lock '" + lockName + "' is being obtained: " + Thread.CurrentThread.Name);
                        }

                        found = rs.Read();
                    }
                    
                    if (!found)
                    {
                        Log.Debug(
                            "Inserting new lock row for lock: '" + lockName + "' being obtained by thread: " +
                            Thread.CurrentThread.Name);

                        using (IDbCommand cmd2 = AdoUtil.PrepareCommand(conn, expandedInsertSQL))
                        {
                            AdoUtil.AddCommandParameter(cmd2, "lockName", lockName);
                            int res = cmd2.ExecuteNonQuery();

                            if (res != 1)
                            {
                                throw new Exception(AdoJobStoreUtil.ReplaceTablePrefix(
                                    "No row exists, and one could not be inserted in table " + TablePrefixSubst + TableLocks +
                                    " for lock named: " + lockName, TablePrefix, SchedulerNameLiteral));
                            }
                        }
                    }
                }
            }
            catch (Exception sqle)
            {
                if (Log.IsDebugEnabled)
                {
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