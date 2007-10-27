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

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Util
{
	/// <summary>
	/// Manages a collection of IDbProviders, and provides transparent access
	/// to their database.
	/// </summary>
	/// <seealso cref="IDbProvider" />
	/// <author>James House</author>
	/// <author>Sharada Jambula</author>
	/// <author>Mohammad Rezaei</author>
	public class DBConnectionManager
	{
        public const string PropertyDbPrefix = "quartz.db.";
        private static readonly DBConnectionManager instance = new DBConnectionManager();

        private readonly IDictionary providers = new Hashtable();

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


		/// <summary> 
		/// Private constructor
		/// </summary>
		private DBConnectionManager()
		{
		}

        /// <summary>
        /// Adds the connection provider.
        /// </summary>
        /// <param name="dataSourceName">Name of the data source.</param>
        /// <param name="provider">The provider.</param>
        public virtual void AddConnectionProvider(string dataSourceName, IDbProvider provider)
		{
			providers[dataSourceName] = provider;
		}

		/// <summary>
		/// Get a database connection from the DataSource with the given name.
		/// </summary>
		/// <returns> a database connection </returns>
		public virtual IDbConnection GetConnection(string dsName)
		{
		    IDbProvider provider = GetDbProvider(dsName);

			return provider.CreateConnection();
		}

		/// <summary> 
		/// Shuts down database connections from the DataSource with the given name,
		/// if applicable for the underlying provider.
		/// </summary>
		/// <returns> a database connection </returns>
		public virtual void Shutdown(string dsName)
		{
		    IDbProvider provider = GetDbProvider(dsName);
			provider.Shutdown();
		}

	    public DbMetadata GetDbMetadata(string dsName)
	    {
            return GetDbProvider(dsName).Metadata;
        }

        /// <summary>
        /// Gets the db provider.
        /// </summary>
        /// <param name="dsName">Name of the ds.</param>
        /// <returns></returns>
	    public IDbProvider GetDbProvider(string dsName)
	    {
            IDbProvider provider = (IDbProvider)providers[dsName];
            if (provider == null)
            {
                throw new Exception(string.Format("There is no DataSource named '{0}'", dsName));
            }
	        return provider;
        }
	}
}
