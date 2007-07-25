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
	public class SimpleSemaphore : ISemaphore
	{
		private ILog log = LogManager.GetLogger(typeof(SimpleSemaphore));
		internal LocalDataStoreSlot lockOwners = Thread.AllocateDataSlot();
		internal HashSet locks = new HashSet();
		
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

		/// <summary> 
		/// Grants a lock on the identified resource to the calling thread (blocking
		/// until it is available).
		/// </summary>
		/// <returns>True if the lock was obtained.</returns>
		public virtual bool ObtainLock(DbMetadata metadata, ConnectionAndTransactionHolder conn, string lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				if (log.IsDebugEnabled)
					log.Debug("Lock '" + lockName + "' is desired by: " + Thread.CurrentThread.Name);

				if (!IsLockOwner(conn, lockName))
				{
					if (log.IsDebugEnabled)
					{
						log.Debug("Lock '" + lockName + "' is being obtained: " + Thread.CurrentThread.Name);
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
								log.Debug("Lock '" + lockName + "' was not obtained by: " + Thread.CurrentThread.Name);
							}
						}
					}

					if (log.IsDebugEnabled)
					{
						log.Debug(string.Format("Lock '{0}' given to: {1}", lockName, Thread.CurrentThread.Name));
					}
					ThreadLocks.Add(lockName);
					locks.Add(lockName);
				}
				else if (log.IsDebugEnabled)
				{
					log.Debug(string.Format("Lock '{0}' already owned by: {1} -- but not owner!", lockName, Thread.CurrentThread.Name), new Exception("stack-trace of wrongful returner"));
				}

				return true;
			}
		}

		/// <summary> Release the lock on the identified resource if it is held by the calling
		/// thread.
		/// </summary>
		public virtual void ReleaseLock(ConnectionAndTransactionHolder conn, string lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				if (IsLockOwner(conn, lockName))
				{
					if (log.IsDebugEnabled)
					{
						log.Debug(string.Format("Lock '{0}' retuned by: {1}", lockName, Thread.CurrentThread.Name));
					}
					ThreadLocks.Remove(lockName);
					locks.Remove(lockName);
					Monitor.Pulse(this);
				}
				else if (log.IsDebugEnabled)
				{
					log.Debug(string.Format("Lock '{0}' attempt to retun by: {1} -- but not owner!", lockName, Thread.CurrentThread.Name), new Exception("stack-trace of wrongful returner"));
				}
			}
		}

		/// <summary> 
		/// Determine whether the calling thread owns a lock on the identified
		/// resource.
		/// </summary>
		public virtual bool IsLockOwner(ConnectionAndTransactionHolder conn, String lockName)
		{
			lock (this)
			{
				lockName = String.Intern(lockName);

				return ThreadLocks.Contains(lockName);
			}
		}

		
		public virtual void Init(IDbConnection conn, IList listOfLocks)
		{
			// nothing to do...
		}
	}
}