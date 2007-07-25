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
using System.Data;
using System.Threading;

using Common.Logging;

using Quartz.Collection;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary> 
    /// An interface for providing thread/resource locking in order to protect
    /// resources from being altered by multiple threads at the same time.
    /// </summary>
    /// <author>James House</author>
    public class StdRowLockSemaphore : StdAdoConstants, ISemaphore
    {
        private static readonly ILog log = LogManager.GetLogger(typeof (StdRowLockSemaphore));

        /*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constants.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

        public static readonly string SELECT_FOR_LOCK =
            string.Format("SELECT * FROM {0}{1} WHERE {2} = {3} FOR UPDATE", TABLE_PREFIX_SUBST, AdoConstants.TABLE_LOCKS,
                          AdoConstants.COL_LOCK_NAME, "{0}");

        /*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

        //  java.util.HashMap threadLocksOb = new java.util.HashMap();
        private readonly string selectWithLockSQL = SELECT_FOR_LOCK;

        private readonly string tablePrefix;
        internal LocalDataStoreSlot lockOwners = Thread.AllocateDataSlot();

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
        public StdRowLockSemaphore(string tablePrefix, string selectWithLockSQL)
        {
            this.tablePrefix = tablePrefix;

            if (selectWithLockSQL != null && selectWithLockSQL.Trim().Length != 0)
            {
                this.selectWithLockSQL = selectWithLockSQL;
            }

            this.selectWithLockSQL = Util.ReplaceTablePrefix(this.selectWithLockSQL, tablePrefix).Replace("?", "{0}");
        }

        private HashSet ThreadLocks
        {
            get
            {
                HashSet threadLocks = (HashSet) Thread.GetData(lockOwners);
                if (threadLocks == null)
                {
                    threadLocks = new HashSet();
                    Thread.SetData(lockOwners, threadLocks);
                }
                return threadLocks;
            }
        }

        #region ISemaphore Members

        /// <summary> 
        /// Grants a lock on the identified resource to the calling thread (blocking
        /// until it is available).
        /// </summary>
        /// <returns> true if the lock was obtained.</returns>
        public virtual bool ObtainLock(DbMetadata metadata, ConnectionAndTransactionHolder conn, string lockName)
        {
            lockName = String.Intern(lockName);

            if (log.IsDebugEnabled)
            {
                log.Debug(string.Format("Lock '{0}' is desired by: {1}", lockName, Thread.CurrentThread.Name));
            }
            if (!IsLockOwner(conn, lockName))
            {
                
                using (IDbCommand ps = conn.Connection.CreateCommand())
                {
                    ps.CommandText = string.Format(selectWithLockSQL, metadata.GetParameterName("lockName"));
                    ps.CommandType = CommandType.Text;
                    IDbDataParameter param =  ps.CreateParameter();
                    param.Value = lockName;
                    param.ParameterName = metadata.GetParameterName("lockName");
                    ps.Parameters.Add(param);
                    if (conn.Transaction != null)
                    {
                        ps.Transaction = conn.Transaction;
                    }

                    try
                    {
                        if (log.IsDebugEnabled)
                        {
                            log.Debug(
                                string.Format("Lock '{0}' is being obtained: {1}", lockName, Thread.CurrentThread.Name));
                        }
                        using (IDataReader rs = ps.ExecuteReader())
                        {
                            if (!rs.Read())
                            {
                                throw new JobPersistenceException(
                                    Util.ReplaceTablePrefix(
                                        "No row exists in table " + TABLE_PREFIX_SUBST + AdoConstants.TABLE_LOCKS +
                                        " for lock named: " + lockName,
                                        tablePrefix));
                            }
                        }
                    }
                    catch (Exception sqle)
                    {
                        if (log.IsDebugEnabled)
                        {
                            log.Debug(
                                string.Format("Lock '{0}' was not obtained by: {1}", lockName, Thread.CurrentThread.Name));
                        }
                        throw new LockException("Failure obtaining db row lock: " + sqle.Message, sqle);
                    }
                }
                if (log.IsDebugEnabled)
                {
                    log.Debug(string.Format("Lock '{0}' given to: {1}", lockName, Thread.CurrentThread.Name));
                }
                ThreadLocks.Add(lockName);
                //getThreadLocksObtainer().put(lockName, new
                // Exception("Obtainer..."));
            }
            else if (log.IsDebugEnabled)
            {
                log.Debug(string.Format("Lock '{0}' Is already owned by: {1}", lockName, Thread.CurrentThread.Name));
            }

            return true;
        }

        /// <summary> Release the lock on the identified resource if it is held by the calling
        /// thread.
        /// </summary>
        public virtual void ReleaseLock(ConnectionAndTransactionHolder conn, string lockName)
        {
            lockName = String.Intern(lockName);

            if (IsLockOwner(conn, lockName))
            {
                if (log.IsDebugEnabled)
                {
                    log.Debug("Lock '" + lockName + "' returned by: " + Thread.CurrentThread.Name);
                }
                ThreadLocks.Remove(lockName);
                //getThreadLocksObtainer().remove(lockName);
            }
            else if (log.IsDebugEnabled)
            {
                log.Warn(
                    string.Format("Lock '{0}' attempt to retun by: {1} -- but not owner!", lockName,
                                  Thread.CurrentThread.Name),
                    new Exception("stack-trace of wrongful returner"));
            }
        }

        /// <summary> 
        /// Determine whether the calling thread owns a lock on the identified
        /// resource.
        /// </summary>
        public virtual bool IsLockOwner(ConnectionAndTransactionHolder conn, string lockName)
        {
            lockName = String.Intern(lockName);

            return ThreadLocks.Contains(lockName);
        }

        #endregion
    }
}