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

using System.Data;

namespace Quartz.Util
{
	/// <summary> 
	/// Implementations of this interface used by <code>DBConnectionManager</code>
	/// to provide connections from various sources.
	/// </summary>
	/// <seealso cref="DBConnectionManager">
	/// </seealso>
	/// <author>Mohammad Rezaei</author>
	public interface IConnectionProvider
	{
		/// <returns> 
		/// Connection managed by this provider
		/// </returns>
		/// <throws>  SQLException </throws>
		IDbConnection Connection { get; }


		void Shutdown();
	}
}