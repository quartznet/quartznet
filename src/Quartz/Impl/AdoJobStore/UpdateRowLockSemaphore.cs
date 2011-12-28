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

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Provide thread/resource locking in order to protect
    /// resources from being altered by multiple threads at the same time using
    /// a db row update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Note:</b> This Semaphore implementation is useful for databases that do
    /// not support row locking via "SELECT FOR UPDATE" or SQL Server's type syntax.
    /// </para>
    /// <para>
    /// As of Quartz.NET 2.0 version there is no need to use this implementation for 
    /// SQL Server databases.
    /// </para>
    /// </remarks>
    /// <author>Marko Lahma (.NET)</author>
    public class UpdateLockRowSemaphore : DBSemaphore
    {
        public static readonly string SqlUpdateForLock =
            string.Format(CultureInfo.InvariantCulture, "UPDATE {0}{1} SET {2} = {3} WHERE {4} = {5} AND {6} = @lockName",
                          TablePrefixSubst, TableLocks, ColumnLockName, ColumnLockName, ColumnSchedulerName, SchedulerNameSubst, ColumnLockName);

        public static readonly string SqlInsertLock =
            string.Format("INSERT INTO {0}{1}({2}, {3}) VALUES ({4}, @lockName)",
                          TablePrefixSubst, TableLocks, ColumnSchedulerName, ColumnLockName, SchedulerNameSubst);

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateLockRowSemaphore"/> class.
        /// </summary>
        public UpdateLockRowSemaphore(IDbProvider provider)
            : base(DefaultTablePrefix, null, SqlUpdateForLock, SqlInsertLock, provider)
        {
        }

        /// <summary>
        /// Execute the SQL that will lock the proper database row.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="lockName"></param>
        /// <param name="expandedSQL"></param>
        /// <param name="expandedInsertSQL"></param>
        protected override void ExecuteSQL(ConnectionAndTransactionHolder conn, string lockName, string expandedSQL, string expandedInsertSQL)
        {
            // attempt lock two times (to work-around possible race conditions in inserting the lock row the first time running)
            int count = 0;
            do
            {
                count++;
                try
                {
                    using (IDbCommand cmd = AdoUtil.PrepareCommand(conn, expandedSQL))
                    {
                        AdoUtil.AddCommandParameter(cmd, "lockName", lockName);

                        if (Log.IsDebugEnabled)
                        {
                            Log.DebugFormat("Lock '{0}' is being obtained: {1}", lockName, Thread.CurrentThread.Name);
                        }

                        int numUpdate = cmd.ExecuteNonQuery();

                        if (numUpdate < 1)
                        {
                            if (Log.IsDebugEnabled)
                            {
                                Log.DebugFormat("Inserting new lock row for lock: '{0}' being obtained by thread: {1}", lockName, Thread.CurrentThread.Name);
                            }
                            using (IDbCommand cmd2 = AdoUtil.PrepareCommand(conn, expandedInsertSQL))
                            {
                                AdoUtil.AddCommandParameter(cmd2, "lockName", lockName);

                                int res = cmd2.ExecuteNonQuery();

                                if (res != 1)
                                {
                                    if (count < 3)
                                    {
                                        // pause a bit to give another thread some time to commit the insert of the new lock row
                                        try
                                        {
                                            Thread.Sleep(TimeSpan.FromSeconds(1));
                                        }
                                        catch (ThreadInterruptedException)
                                        {
                                            Thread.CurrentThread.Interrupt();
                                        }
                                        // try again ...
                                        continue;
                                    }
                                    throw new Exception(AdoJobStoreUtil.ReplaceTablePrefix(
                                        "No row exists, and one could not be inserted in table " + TablePrefixSubst + TableLocks +
                                        " for lock named: " + lockName, TablePrefix, SchedulerNameLiteral));
                                }
                            }

                            break; // obtained lock, no need to retry
                        }
                    }
                }
                catch (Exception sqle)
                {
                    if (Log.IsDebugEnabled)
                    {
                        Log.DebugFormat("Lock '{0}' was not obtained by: {1}{2}", lockName, Thread.CurrentThread.Name, (count < 3 ? " - will try again." : ""));
                    }

                    if (count < 3)
                    {
                        // pause a bit to give another thread some time to commit the insert of the new lock row
                        try
                        {
                            Thread.Sleep(TimeSpan.FromSeconds(1));
                        }
                        catch (ThreadInterruptedException)
                        {
                            Thread.CurrentThread.Interrupt();
                        }
                        // try again ...
                        continue;
                    }

                    throw new LockException("Failure obtaining db row lock: " + sqle.Message, sqle);
                }
            } while (count < 2);
        }

        protected string UpdateLockRowSQL
        {
            get { return SQL; }
            set { SQL = value; }
        }
    }
}