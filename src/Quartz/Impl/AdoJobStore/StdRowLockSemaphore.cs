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
using System.Data.OleDb;
using System.Threading;
using Common.Logging;

using Quartz.Collection;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary> 
	/// An interface for providing thread/resource locking in order to protect
	/// resources from being altered by multiple threads at the same time.
	/// </summary>
	/// <author>James House</author>
	public class StdRowLockSemaphore : StdAdoConstants, ISemaphore
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(StdRowLockSemaphore));

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

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constants.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		public static readonly string SELECT_FOR_LOCK = string.Format("SELECT * FROM {0}{1} WHERE {2} = ? FOR UPDATE", StdAdoConstants.TABLE_PREFIX_SUBST, AdoConstants.TABLE_LOCKS, AdoConstants.COL_LOCK_NAME);

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		internal LocalDataStoreSlot lockOwners = Thread.AllocateDataSlot();

		//  java.util.HashMap threadLocksOb = new java.util.HashMap();
		private string selectWithLockSQL = SELECT_FOR_LOCK;

		private string tablePrefix;

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		public StdRowLockSemaphore(String tablePrefix, String selectWithLockSQL)
		{
			this.tablePrefix = tablePrefix;

			if (selectWithLockSQL != null && selectWithLockSQL.Trim().Length != 0)
				this.selectWithLockSQL = selectWithLockSQL;

			this.selectWithLockSQL = Util.ReplaceTablePrefix(this.selectWithLockSQL, tablePrefix);
		}

		/// <summary> Grants a lock on the identified resource to the calling thread (blocking
		/// until it is available).
		/// 
		/// </summary>
		/// <returns> true if the lock was obtained.
		/// </returns>
		
		public virtual bool ObtainLock(IDbConnection conn, String lockName)
		{
			lockName = String.Intern(lockName);

			if (log.IsDebugEnabled)
				log.Debug("Lock '" + lockName + "' is desired by: " + Thread.CurrentThread.Name);
			if (!IsLockOwner(conn, lockName))
			{
				OleDbCommand ps = null;

				OleDbDataReader rs = null;
				try
				{
					ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, selectWithLockSQL);
					SupportClass.TransactionManager.manager.SetValue(ps, 1, lockName);


					if (log.IsDebugEnabled)
						log.Debug("Lock '" + lockName + "' is being obtained: " + Thread.CurrentThread.Name);
					rs = ps.ExecuteReader();
					if (!rs.Read())
					{
						//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
						throw new JobPersistenceException(Util.ReplaceTablePrefix("No row exists in table " + StdAdoConstants.TABLE_PREFIX_SUBST + AdoConstants.TABLE_LOCKS + " for lock named: " + lockName, tablePrefix));
					}
				}
				catch (OleDbException sqle)
				{
					//Exception src =
					// (Exception)getThreadLocksObtainer().get(lockName);
					//if(src != null)
					//  src.printStackTrace();
					//else
					//  System.err.println("--- ***************** NO OBTAINER!");

					if (log.IsDebugEnabled)
					{
						log.Debug("Lock '" + lockName + "' was not obtained by: " + Thread.CurrentThread.Name);
					}
					throw new LockException("Failure obtaining db row lock: " + sqle.Message, sqle);
				}
				finally
				{
					if (rs != null)
						try
						{
							rs.Close();
						}
						catch (Exception)
						{
						}
					if (ps != null)
						try
						{
							ps.close();
						}
						catch (Exception)
						{
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
				log.Debug("Lock '" + lockName + "' Is already owned by: " + Thread.CurrentThread.Name);
			}

			return true;
		}

		/// <summary> Release the lock on the identified resource if it is held by the calling
		/// thread.
		/// </summary>
		
		public virtual void ReleaseLock(IDbConnection conn, String lockName)
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
				log.Warn(string.Format("Lock '{0}' attempt to retun by: {1} -- but not owner!", lockName, Thread.CurrentThread.Name), new Exception("stack-trace of wrongful returner"));
			}
		}

		/// <summary> 
		/// Determine whether the calling thread owns a lock on the identified
		/// resource.
		/// </summary>
		public virtual bool IsLockOwner(IDbConnection conn, String lockName)
		{
			lockName = String.Intern(lockName);

			return ThreadLocks.Contains(lockName);
		}
	}
}