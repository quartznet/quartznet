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
	/// An exception that is thrown to indicate that there is a misconfiguration of
	/// the <see cref="ISchedulerFactory" />- or one of the components it
	/// configures.
	/// </summary>
	/// <author>James House</author>
	[Serializable]
	public class SchedulerConfigException : SchedulerException
	{
		/// <summary>
		/// Create a <see cref="JobPersistenceException" /> with the given message.
		/// </summary>
		public SchedulerConfigException(string msg) : base(msg, ErrorBadConfiguration)
		{
		}

		/// <summary>
		/// Create a <see cref="JobPersistenceException" /> with the given message
		/// and cause.
		/// </summary>
		public SchedulerConfigException(string msg, Exception cause) : base(msg, cause)
		{
			ErrorCode = ErrorBadConfiguration;
		}
	}
}