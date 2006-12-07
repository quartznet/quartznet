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

namespace Quartz
{
	/// <summary>
	/// An exception that is thrown to indicate that a call to 
	/// InterruptableJob.interrupt() failed without interrupting the Job.
	/// </summary>
	/// <seealso cref="IInterruptableJob" />
	/// <author>James House</author>
	[Serializable]
	public class UnableToInterruptJobException : SchedulerException
	{
		/// <summary>
		/// Create a <code>UnableToInterruptJobException</code> with the given message.
		/// </summary>
		public UnableToInterruptJobException(string msg) : base(msg)
		{
		}

		/// <summary>
		/// Create a <code>UnableToInterruptJobException</code> with the given cause.
		/// </summary>
		public UnableToInterruptJobException(Exception cause) : base(cause)
		{
		}
	}
}