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
using System.Collections;
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
	public class SimpleSemaphore : ISemaphore
	{
		private ILog log = LogManager.GetLogger(typeof(SimpleSemaphore));
		
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
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		internal LocalDataStoreSlot lockOwners = Thread.AllocateDataSlot();

		internal HashSet locks = new HashSet();

		/// <summary> Grants a lock on the identified resource to the calling thread (blocking
		/// until it is available).
		/// 
		/// </summary>
		/// <returns> true if the lock was obtained.
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		//UPGRADE_NOTE: Synchronized keyword was removed from method 'ObtainLock'. Lock expression was added. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1027_3"'
		public virtual bool ObtainLock(OleDbConnection conn, String lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				if (log.IsDebugEnabled)
					log.Debug("Lock '" + lockName + "' is desired by: " + SupportClass.ThreadClass.Current().Name);

				if (!IsLockOwner(conn, lockName))
				{
					if (log.IsDebugEnabled)
					{
						log.Debug("Lock '" + lockName + "' is being obtained: " + SupportClass.ThreadClass.Current().Name);
					}
					
					while (locks.Contains(lockName))
					{
						try
						{
							Monitor.Wait(this);
						}
						catch (ThreadInterruptedException)
						{
							if (log.IsDebugEnabled)
							{
								log.Debug("Lock '" + lockName + "' was not obtained by: " + SupportClass.ThreadClass.Current().Name);
							}
						}
					}

					if (log.IsDebugEnabled)
						log.Debug("Lock '" + lockName + "' given to: " + SupportClass.ThreadClass.Current().Name);
					ThreadLocks.Add(lockName);
					locks.Add(lockName);
				}
				else if (log.IsDebugEnabled)
					log.Debug("Lock '" + lockName + "' already owned by: " + SupportClass.ThreadClass.Current().Name + " -- but not owner!", new Exception("stack-trace of wrongful returner"));

				return true;
			}
		}

		/// <summary> Release the lock on the identified resource if it is held by the calling
		/// thread.
		/// </summary>
		public virtual void ReleaseLock(OleDbConnection conn, String lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				if (IsLockOwner(conn, lockName))
				{
					if (log.IsDebugEnabled)
					{
						log.Debug("Lock '" + lockName + "' retuned by: " + SupportClass.ThreadClass.Current().Name);
					}
					ThreadLocks.Remove(lockName);
					locks.Remove(lockName);
					Monitor.Pulse(this);
				}
				else if (log.IsDebugEnabled)
				{
					log.Debug("Lock '" + lockName + "' attempt to retun by: " + SupportClass.ThreadClass.Current().Name + " -- but not owner!", new Exception("stack-trace of wrongful returner"));
				}
			}
		}

		/// <summary> Determine whether the calling thread owns a lock on the identified
		/// resource.
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		//UPGRADE_NOTE: Synchronized keyword was removed from method 'IsLockOwner'. Lock expression was added. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1027_3"'
		public virtual bool IsLockOwner(OleDbConnection conn, String lockName)
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