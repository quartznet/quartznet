 /* 
 * Copyright 2004-2006 OpenSymphony 
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

using System;
using System.Threading;

using Common.Logging;

using Quartz.Collection;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{

/// <summary>
/// Base class for database based lock handlers for providing thread/resource locking 
/// in order to protect resources from being altered by multiple threads at the 
/// same time.
/// </summary>
public abstract class DBSemaphore : StdAdoConstants, ISemaphore, ITablePrefixAware {

    private readonly ILog log;
    private const string ThreadContextKeyLockOwners = "qrtz_dbs_lck_owners";
    private string sql;
    private string tablePrefix;
    private string expandedSQL;
    private AdoUtil adoUtil;

    /// <summary>
    /// Initializes a new instance of the <see cref="DBSemaphore"/> class.
    /// </summary>
    /// <param name="tablePrefix">The table prefix.</param>
    /// <param name="sql">The SQL.</param>
    /// <param name="defaultSQL">The default SQL.</param>
    /// <param name="dbProvider">The db provider.</param>
    public DBSemaphore(string tablePrefix, string sql, string defaultSQL, IDbProvider dbProvider) {
        log = LogManager.GetLogger(GetType());
        this.sql = defaultSQL;
        this.tablePrefix = tablePrefix;
        SQL = sql;
        adoUtil = new AdoUtil(dbProvider);
    }

    /// <summary>
    /// Gets or sets the lock owners.
    /// </summary>
    /// <value>The lock owners.</value>
    private static HashSet LockOwners
    {
        get { return (HashSet) LogicalThreadContext.GetData(ThreadContextKeyLockOwners); }
        set { LogicalThreadContext.SetData(ThreadContextKeyLockOwners, value); }
    }


    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    protected ILog Log
    {
        get { return log; }
    }

    private static HashSet ThreadLocks
    {
        get
        {
            if (LockOwners == null)
            {
                LockOwners = new HashSet();
            }
            return LockOwners;
        }
    }

    /// <summary>
    /// Execute the SQL that will lock the proper database row.
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="lockName"></param>
    /// <param name="expandedSQL"></param>
    protected abstract void ExecuteSQL(ConnectionAndTransactionHolder conn, string lockName, string expandedSQL);


    /// <summary>
    /// Grants a lock on the identified resource to the calling thread (blocking
    /// until it is available).
    /// </summary>
    /// <param name="metadata"></param>
    /// <param name="conn"></param>
    /// <param name="lockName"></param>
    /// <returns>true if the lock was obtained.</returns>
    public bool ObtainLock(DbMetadata metadata, ConnectionAndTransactionHolder conn, string lockName)
    {
        lockName = string.Intern(lockName);

        if (Log.IsDebugEnabled)
        {
            Log.Debug(
                "Lock '" + lockName + "' is desired by: "
                        + Thread.CurrentThread.Name);
        }
        if (!IsLockOwner(conn, lockName)) {

            ExecuteSQL(conn, lockName, expandedSQL);

            if (Log.IsDebugEnabled)
            {
                Log.Debug(
                    "Lock '" + lockName + "' given to: "
                            + Thread.CurrentThread.Name);
            }
            ThreadLocks.Add(lockName);
            //getThreadLocksObtainer().put(lockName, new
            // Exception("Obtainer..."));
        } else if(log.IsDebugEnabled) {
            Log.Debug(
                "Lock '" + lockName + "' Is already owned by: "
                        + Thread.CurrentThread.Name);
        }

        return true;
    }


    /// <summary>
    /// Release the lock on the identified resource if it is held by the calling
    /// thread.
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="lockName"></param>
    public void ReleaseLock(ConnectionAndTransactionHolder conn, string lockName) {

        lockName = string.Intern(lockName);

        if (IsLockOwner(conn, lockName)) {
            if(Log.IsDebugEnabled) {
                Log.Debug(
                    "Lock '" + lockName + "' returned by: "
                            + Thread.CurrentThread.Name);
            }
            ThreadLocks.Remove(lockName);
            //getThreadLocksObtainer().remove(lockName);
        } else if (Log.IsDebugEnabled) {
            Log.Warn(
                "Lock '" + lockName + "' attempt to return by: "
                        + Thread.CurrentThread.Name
                        + " -- but not owner!",
                new Exception("stack-trace of wrongful returner"));
        }
    }

   /// <summary>
   /// Determine whether the calling thread owns a lock on the identified
   /// resource.
   /// </summary>
   /// <param name="conn"></param>
   /// <param name="lockName"></param>
   /// <returns></returns>
    public bool IsLockOwner(ConnectionAndTransactionHolder conn, string lockName) {
        lockName = string.Intern(lockName);

        return ThreadLocks.Contains(lockName);
    }

    /// <summary>
    /// This Semaphore implementation does use the database.
    /// </summary>
    public bool RequiresConnection
    {
        get { return true; }
    }

    protected string SQL
    {
        get { return sql; }
        set
        {
            if ((value != null) && (value.Trim().Length != 0))
            {
                sql = value;
            }
            SetExpandedSQL();
        }
    }

    private void SetExpandedSQL() {
        if (TablePrefix != null) {
            expandedSQL = AdoJobStoreUtil.ReplaceTablePrefix(sql, TablePrefix);
        }
    }

    /// <summary>
    /// Gets or sets the table prefix.
    /// </summary>
    /// <value>The table prefix.</value>
    public string TablePrefix
    {
        get { return tablePrefix; }
        set
        {
            tablePrefix = value;
            SetExpandedSQL();
        }
    }


    protected AdoUtil AdoUtil
    {
        get { return adoUtil; }
    }
}

}
