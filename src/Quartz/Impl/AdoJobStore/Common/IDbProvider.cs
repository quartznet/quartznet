#region License

/*
 * Copyright 2009- Marko Lahma
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

#endregion

using System.Data;
using System.Data.Common;

namespace Quartz.Impl.AdoJobStore.Common
{
    /// <summary>
    /// Data access provider interface.
    /// </summary>
    /// <author>Marko Lahma</author>
    public interface IDbProvider
    {
        /// <summary>
        /// Initializes the db provider implementation.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Returns a new command object for executing SQL statements/Stored Procedures
        /// against the database.
        /// </summary>
        /// <returns>An new <see cref="IDbCommand"/></returns>
        DbCommand CreateCommand();

        /// <summary>
        /// Returns a new connection object to communicate with the database.
        /// </summary>
        /// <returns>A new <see cref="IDbConnection"/></returns>
        DbConnection CreateConnection();

        /// <summary>
        /// Connection string used to create connections.
        /// </summary>
        string ConnectionString { set; get; }

        DbMetadata Metadata { get; }

        /// <summary>
        /// Shutdowns this instance.
        /// </summary>
        void Shutdown();
    }
}