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
using System.Collections;
using System.Data.OleDb;
using System.Threading;
using log4net;

namespace Quartz.impl.jdbcjobstore
{
	/// <summary> An interface for providing thread/resource locking in order to protect
	/// resources from being altered by multiple threads at the same time.
	/// 
	/// </summary>
	/// <author>  jhouse
	/// </author>
	public class SimpleSemaphore : Semaphore
	{
		internal virtual ILog Log
		{
			/*
			* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			* 
			* Interface.
			* 
			* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			*/


			get
			{
				return LogFactory.Log;
				//return LogFactory.getLog("LOCK:"+Thread.currentThread().getName());
			}

		}

		//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
		private SupportClass.HashSetSupport ThreadLocks
		{
			get
			{
				//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
				SupportClass.HashSetSupport threadLocks = (SupportClass.HashSetSupport) Thread.GetData(lockOwners);
				if (threadLocks == null)
				{
					//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
					threadLocks = new SupportClass.HashSetSupport();
					Thread.SetData(lockOwners, threadLocks);
				}
				return threadLocks;
			}

		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		internal LocalDataStoreSlot lockOwners = Thread.AllocateDataSlot();

		//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
		internal SupportClass.HashSetSupport locks = new SupportClass.HashSetSupport();

		/// <summary> Grants a lock on the identified resource to the calling thread (blocking
		/// until it is available).
		/// 
		/// </summary>
		/// <returns> true if the lock was obtained.
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		//UPGRADE_NOTE: Synchronized keyword was removed from method 'obtainLock'. Lock expression was added. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1027_3"'
		public virtual bool obtainLock(OleDbConnection conn, String lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				Log log = Log;

				if (log.isDebugEnabled())
					log.debug("Lock '" + lockName + "' is desired by: " + SupportClass.ThreadClass.Current().Name);

				if (!isLockOwner(conn, lockName))
				{
					if (log.isDebugEnabled())
						log.debug("Lock '" + lockName + "' is being obtained: " + SupportClass.ThreadClass.Current().Name);
					while (locks.Contains(lockName))
					{
						try
						{
							Monitor.Wait(this);
						}
						catch (ThreadInterruptedException ie)
						{
							if (log.isDebugEnabled())
								log.debug("Lock '" + lockName + "' was not obtained by: " + SupportClass.ThreadClass.Current().Name);
						}
					}

					if (log.isDebugEnabled())
						log.debug("Lock '" + lockName + "' given to: " + SupportClass.ThreadClass.Current().Name);
					ThreadLocks.Add(lockName);
					locks.Add(lockName);
				}
				else if (log.isDebugEnabled())
					log.debug("Lock '" + lockName + "' already owned by: " + SupportClass.ThreadClass.Current().Name + " -- but not owner!", new Exception("stack-trace of wrongful returner"));

				return true;
			}
		}

		/// <summary> Release the lock on the identified resource if it is held by the calling
		/// thread.
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		//UPGRADE_NOTE: Synchronized keyword was removed from method 'releaseLock'. Lock expression was added. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1027_3"'
		public virtual void releaseLock(OleDbConnection conn, String lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				if (isLockOwner(conn, lockName))
				{
					if (Log.isDebugEnabled())
						Log.debug("Lock '" + lockName + "' retuned by: " + SupportClass.ThreadClass.Current().Name);
					ThreadLocks.Remove(lockName);
					locks.Remove(lockName);
					Monitor.Pulse(this);
				}
				else if (Log.isDebugEnabled())
					Log.debug("Lock '" + lockName + "' attempt to retun by: " + SupportClass.ThreadClass.Current().Name + " -- but not owner!", new Exception("stack-trace of wrongful returner"));
			}
		}

		/// <summary> Determine whether the calling thread owns a lock on the identified
		/// resource.
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		//UPGRADE_NOTE: Synchronized keyword was removed from method 'isLockOwner'. Lock expression was added. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1027_3"'
		public virtual bool isLockOwner(OleDbConnection conn, String lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				return ThreadLocks.Contains(lockName);
			}
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual void init(OleDbConnection conn, IList listOfLocks)
		{
			// nothing to do...
		}
	}
}