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

namespace Quartz.impl.jdbcjobstore
{
	/// <summary> An interface for providing thread/resource locking in order to protect
	/// resources from being altered by multiple threads at the same time.
	/// 
	/// </summary>
	/// <author>  jhouse
	/// </author>
	public interface Semaphore
	{
		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary> Grants a lock on the identified resource to the calling thread (blocking
		/// until it is available).
		/// 
		/// </summary>
		/// <returns> true if the lock was obtained.
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		bool obtainLock(System.Data.OleDb.OleDbConnection conn, string lockName);

		/// <summary> Release the lock on the identified resource if it is held by the calling
		/// thread.
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		void releaseLock(System.Data.OleDb.OleDbConnection conn, string lockName);

		/// <summary> Determine whether the calling thread owns a lock on the identified
		/// resource.
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		bool isLockOwner(System.Data.OleDb.OleDbConnection conn, string lockName);
	}
}