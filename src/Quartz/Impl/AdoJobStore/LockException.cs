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

namespace Quartz.Impl.AdoJobStore
{
	/// <summary>
	/// Exception class for when there is a failure obtaining or releasing a
	/// resource lock.
	/// </summary>
	/// <seealso cref="ISemaphore" />
	/// <author>James House</author>
	[Serializable]
	public class LockException : JobPersistenceException
	{
		public LockException(string msg) : base(msg)
		{
		}

		public LockException(string msg, System.Exception cause) : base(msg, cause)
		{
		}
	}
}