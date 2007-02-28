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
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1262_3"'
//UPGRADE_TODO: The type 'org.apache.commons.logging.LogFactory' could not be found. If it was not included in the conversion, there may be compiler issues. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1262_3"'
using System;
using System.Data.OleDb;
using System.Threading;
using Common.Logging;

using Quartz.Collection;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary> An interface for providing thread/resource locking in order to protect
	/// resources from being altered by multiple threads at the same time.
	/// 
	/// </summary>
	/// <author>  jhouse
	/// </author>
	public class StdRowLockSemaphore : ISemaphore, StdJDBCConstants
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(StdRowLockSemaphore));

		//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
		private HashSet ThreadLocks
		{
			get
			{
				//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
				HashSet threadLocks = (HashSet) Thread.GetData(lockOwners);
				if (threadLocks == null)
				{
					//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
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

		//UPGRADE_NOTE: Final was removed from the declaration of 'SELECT_FOR_LOCK '. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1003_3"'
		public static readonly String SELECT_FOR_LOCK = "SELECT * FROM " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_LOCKS + " WHERE " + Constants_Fields.COL_LOCK_NAME + " = ? FOR UPDATE";

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		internal LocalDataStoreSlot lockOwners = Thread.AllocateDataSlot();

		//  java.util.HashMap threadLocksOb = new java.util.HashMap();
		private String selectWithLockSQL = SELECT_FOR_LOCK;

		private String tablePrefix;

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

			this.selectWithLockSQL = Util.rtp(this.selectWithLockSQL, tablePrefix);
		}

		/// <summary> Grants a lock on the identified resource to the calling thread (blocking
		/// until it is available).
		/// 
		/// </summary>
		/// <returns> true if the lock was obtained.
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool ObtainLock(OleDbConnection conn, String lockName)
		{
			lockName = String.Intern(lockName);

			if (log.IsDebugEnabled)
				log.Debug("Lock '" + lockName + "' is desired by: " + SupportClass.ThreadClass.Current().Name);
			if (!IsLockOwner(conn, lockName))
			{
				OleDbCommand ps = null;
				//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
				OleDbDataReader rs = null;
				try
				{
					ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, selectWithLockSQL);
					SupportClass.TransactionManager.manager.SetValue(ps, 1, lockName);


					if (log.IsDebugEnabled)
						log.Debug("Lock '" + lockName + "' is being obtained: " + SupportClass.ThreadClass.Current().Name);
					rs = ps.ExecuteReader();
					if (!rs.Read())
					{
						//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
						throw new SQLException(Util.rtp("No row exists in table " + StdJDBCConstants_Fields.TABLE_PREFIX_SUBST + Constants_Fields.TABLE_LOCKS + " for lock named: " + lockName, tablePrefix));
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

					if (log.IsDebugEnabled())
						log.Debug("Lock '" + lockName + "' was not obtained by: " + SupportClass.ThreadClass.Current().Name);
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new LockException("Failure obtaining db row lock: " + sqle.Message, sqle);
				}
				finally
				{
					if (rs != null)
						try
						{
							rs.Close();
						}
						catch (Exception ignore)
						{
						}
					if (ps != null)
						try
						{
							//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
							ps.close();
						}
						catch (Exception ignore)
						{
						}
				}
				if (log.IsDebugEnabled())
					log.Debug("Lock '" + lockName + "' given to: " + SupportClass.ThreadClass.Current().Name);
				ThreadLocks.Add(lockName);
				//getThreadLocksObtainer().put(lockName, new
				// Exception("Obtainer..."));
			}
			else if (log.IsDebugEnabled())
				log.Debug("Lock '" + lockName + "' Is already owned by: " + SupportClass.ThreadClass.Current().Name);

			return true;
		}

		/// <summary> Release the lock on the identified resource if it is held by the calling
		/// thread.
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void ReleaseLock(OleDbConnection conn, String lockName)
		{
			lockName = String.Intern(lockName);

			if (IsLockOwner(conn, lockName))
			{
				if (log.IsDebugEnabled)
					log.Debug("Lock '" + lockName + "' returned by: " + SupportClass.ThreadClass.Current().Name);
				ThreadLocks.Remove(lockName);
				//getThreadLocksObtainer().remove(lockName);
			}
			else if (log.IsDebugEnabled)
				log.Warn("Lock '" + lockName + "' attempt to retun by: " + SupportClass.ThreadClass.Current().Name + " -- but not owner!", new Exception("stack-trace of wrongful returner"));
		}

		/// <summary> Determine whether the calling thread owns a lock on the identified
		/// resource.
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool IsLockOwner(OleDbConnection conn, String lockName)
		{
			lockName = String.Intern(lockName);

			return ThreadLocks.Contains(lockName);
		}
	}
}