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

namespace Quartz.Util
{
	/// <summary>
	/// Manages a collection of ConnectionProviders, and provides transparent access
	/// to their connections.
	/// 
	/// </summary>
	/// <seealso cref="IConnectionProvider">
	/// </seealso>
	/// <author>James House</author>
	/// <author>Sharada Jambula</author>
	/// <author>Mohammad Rezaei</author>
	public class DBConnectionManager
	{
		/// <summary> 
		/// Get the class instance.
		/// </summary>
		/// <returns> an instance of this class
		/// </returns>
		public static DBConnectionManager Instance
		{
			get
			{
				// since the instance variable is initialized at class loading time,
				// it's not necessary to synchronize this method */
				return instance;
			}
		}

		public const string DB_PROPS_PREFIX = "org.quartz.db.";
		private static readonly DBConnectionManager instance = new DBConnectionManager();

		private Hashtable providers = new Hashtable();


		/// <summary> 
		/// Private constructor
		/// </summary>
		private DBConnectionManager()
		{
		}

		public virtual void AddConnectionProvider(string dataSourceName, IConnectionProvider provider)
		{
			providers[dataSourceName] = provider;
		}

		/// <summary> Get a database connection from the DataSource with the given name.
		/// 
		/// </summary>
		/// <returns> a database connection
		/// </returns>
		/// <exception cref="Exception"> 
		/// if an error occurs, or there is no DataSource with the
		/// given name.
		/// </exception>
		public virtual IDbConnection GetConnection(string dsName)
		{
			IConnectionProvider provider = (IConnectionProvider) providers[dsName];
			if (provider == null)
			{
				throw new Exception("There is no DataSource named '" + dsName + "'");
			}

			return provider.Connection;
		}

		/// <summary> Shuts down database connections from the DataSource with the given name,
		/// if applicable for the underlying provider.
		/// 
		/// </summary>
		/// <returns> a database connection
		/// </returns>
		/// <exception cref="Exception"> 
		/// if an error occurs, or there is no DataSource with the
		/// given name.
		/// </exception>
		public virtual void Shutdown(string dsName)
		{
			IConnectionProvider provider = (IConnectionProvider) providers[dsName];
			if (provider == null)
			{
				throw new Exception("There is no DataSource named '" + dsName + "'");
			}
			provider.Shutdown();
		}
	}
}