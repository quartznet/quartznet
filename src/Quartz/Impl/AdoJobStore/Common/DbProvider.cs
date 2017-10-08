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

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common
{
    /// <summary>
    /// Concrete implementation of <see cref="IDbProvider" />.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class DbProvider : IDbProvider
    {
		protected const string PropertyDbProvider = StdSchedulerFactory.PropertyDbProvider;
        protected const string DbProviderSectionName = StdSchedulerFactory.ConfigurationSectionName;
        protected const string DbProviderResourceName =
#if NETSTANDARD_DBPROVIDERS
            "Quartz.Impl.AdoJobStore.Common.dbproviders.netstandard.properties";
#else // NETSTANDARD_DBPROVIDERS
            "Quartz.Impl.AdoJobStore.Common.dbproviders.properties";
#endif // NETSTANDARD_DBPROVIDERS

        private readonly MethodInfo commandBindByNamePropertySetter;

        private static readonly IList<DbMetadataFactory> dbMetadataFactories;
        private static readonly Dictionary<string, DbMetadata> dbMetadataLookup = new Dictionary<string, DbMetadata>();

        /// <summary>
        /// Parse metadata once in static constructor.
        /// </summary>
        static DbProvider()
        {
            dbMetadataFactories = new List<DbMetadataFactory>
            {
                new ConfigurationBasedDbMetadataFactory(DbProviderSectionName, PropertyDbProvider),
                new EmbeddedAssemblyResourceDbMetadataFactory(DbProviderResourceName, PropertyDbProvider)
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DbProvider"/> class.
        /// </summary>
        /// <param name="dbProviderName">Name of the db provider.</param>
        /// <param name="connectionString">The connection string.</param>
        public DbProvider(string dbProviderName, string connectionString)
        {
            ConnectionString = connectionString;
            Metadata = GetDbMetadata(dbProviderName);

            if (Metadata == null)
            {
                throw new ArgumentException($"Invalid DB provider name: {dbProviderName}{Environment.NewLine}{GenerateValidProviderNamesInfo()}");
            }

            // check if command supports direct setting of BindByName property, needed for Oracle Managed ODP diver at least
            var property = Metadata.CommandType.GetProperty("BindByName", BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.PropertyType == typeof (bool) && property.CanWrite)
            {
                commandBindByNamePropertySetter = property.GetSetMethod();
            }
        }

        public void Initialize()
        {
            // do nothing, initialized in static constructor
        }

        ///<summary>
        /// Registers DB metadata information for given provider name.
        ///</summary>
        ///<param name="dbProviderName"></param>
        ///<param name="metadata"></param>
        public static void RegisterDbMetadata(string dbProviderName, DbMetadata metadata)
        {
            dbMetadataLookup[dbProviderName] = metadata;
        }

        private DbMetadata GetDbMetadata(string providerName)
        {
            if (!dbMetadataLookup.TryGetValue(providerName, out var result))
            {
                foreach (var dbMetadataFactory in dbMetadataFactories)
                {
                    if (dbMetadataFactory.GetProviderNames().Contains(providerName))
                    {
                        result = dbMetadataFactory.GetDbMetadata(providerName);
                        RegisterDbMetadata(providerName, result);
                        return result;
                    }
                }
                throw new ArgumentOutOfRangeException(nameof(providerName), "There is no metadata information for provider '" + providerName + "'");
            }

            return result;
        }

        /// <summary>
        /// Generates the valid provider names information.
        /// </summary>
        /// <returns></returns>
        protected static string GenerateValidProviderNamesInfo()
        {
            var providerNames = dbMetadataFactories
                .SelectMany(factory => factory.GetProviderNames())
                .Distinct()
                .OrderBy(name => name);

            StringBuilder sb = new StringBuilder("Valid DB Provider names are:").Append(Environment.NewLine);
            foreach (string providerName in providerNames)
            {
                sb.Append("\t").Append(providerName).Append(Environment.NewLine);
            }
            return sb.ToString();
        }

        /// <inheritdoc />
        public virtual DbCommand CreateCommand()
        {
            var command = ObjectUtils.InstantiateType<DbCommand>(Metadata.CommandType);
            commandBindByNamePropertySetter?.Invoke(command, new object[] { Metadata.BindByName });
            return command;
        }

        /// <inheritdoc />
        public virtual DbConnection CreateConnection()
        {
            var conn = ObjectUtils.InstantiateType<DbConnection>(Metadata.ConnectionType);
            conn.ConnectionString = ConnectionString;
            return conn;
        }

        /// <summary>
        /// Returns a new parameter object for binding values to parameter
        /// placeholders in SQL statements or Stored Procedure variables.
        /// </summary>
        /// <returns>A new <see cref="IDbDataParameter"/></returns>
        public virtual DbParameter CreateParameter()
        {
            return ObjectUtils.InstantiateType<DbParameter>(Metadata.ParameterType);
        }

        /// <inheritdoc />
        public string ConnectionString { get; set; }

        /// <inheritdoc />
        public DbMetadata Metadata { get; }

        /// <inheritdoc />
        public virtual void Shutdown()
        {
        }
    }
}