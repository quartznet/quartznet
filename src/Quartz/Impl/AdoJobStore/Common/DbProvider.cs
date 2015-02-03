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
using System.Globalization;
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
        protected const string DbProviderResourceName = "Quartz.Impl.AdoJobStore.Common.dbproviders.properties";

        private string connectionString;
        private readonly DbMetadata dbMetadata;
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
                new EmbeddedAssemblyResourceDbMetadataFactory(DbProviderResourceName, PropertyDbProvider),
            };
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="DbProvider"/> class.
        /// </summary>
        /// <param name="dbProviderName">Name of the db provider.</param>
        /// <param name="connectionString">The connection string.</param>
        public DbProvider(string dbProviderName, string connectionString)
        {
            this.connectionString = connectionString;
            dbMetadata = GetDbMetadata(dbProviderName);

            if (dbMetadata == null)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Invalid DB provider name: {0}{1}{2}", dbProviderName, Environment.NewLine, GenerateValidProviderNamesInfo()));
            }
            
            // check if command supports direct setting of BindByName property, needed for Oracle Managed ODP diver at least
            var property = dbMetadata.CommandType.GetProperty("BindByName", BindingFlags.Instance | BindingFlags.Public);
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

        protected virtual DbMetadata GetDbMetadata(string providerName)
        {
            DbMetadata result;
            if (!dbMetadataLookup.TryGetValue(providerName, out result))
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
                throw new ArgumentOutOfRangeException("providerName", "There is no metadata information for provider '" + providerName + "'");
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

        /// <summary>
        /// Returns a new command object for executing SQL statements/Stored Procedures
        /// against the database.
        /// </summary>
        /// <returns>An new <see cref="IDbCommand"/></returns>
        public virtual IDbCommand CreateCommand()
        {
            var command = ObjectUtils.InstantiateType<IDbCommand>(dbMetadata.CommandType);
            if (commandBindByNamePropertySetter != null)
            {
                commandBindByNamePropertySetter.Invoke(command, new object[] { Metadata.BindByName });
            }
            return command;
        }

        /// <summary>
        /// Returns a new instance of the providers CommandBuilder class.
        /// </summary>
        /// <returns>A new Command Builder</returns>
        /// <remarks>In .NET 1.1 there was no common base class or interface
        /// for command builders, hence the return signature is object to
        /// be portable (but more loosely typed) across .NET 1.1/2.0</remarks>
        public virtual object CreateCommandBuilder()
        {
            return ObjectUtils.InstantiateType<object>(dbMetadata.CommandBuilderType);
        }

        /// <summary>
        /// Returns a new connection object to communicate with the database.
        /// </summary>
        /// <returns>A new <see cref="IDbConnection"/></returns>
        public virtual IDbConnection CreateConnection()
        {
            IDbConnection conn = ObjectUtils.InstantiateType<IDbConnection>(dbMetadata.ConnectionType);
            conn.ConnectionString = ConnectionString;
            return conn;
        }

        /// <summary>
        /// Returns a new parameter object for binding values to parameter
        /// placeholders in SQL statements or Stored Procedure variables.
        /// </summary>
        /// <returns>A new <see cref="IDbDataParameter"/></returns>
        public virtual IDbDataParameter CreateParameter()
        {
            return ObjectUtils.InstantiateType<IDbDataParameter>(dbMetadata.ParameterType);
        }

        /// <summary>
        /// Connection string used to create connections.
        /// </summary>
        /// <value></value>
        public virtual string ConnectionString
        {
            get { return connectionString; }
            set { connectionString = value; }
        }

        /// <summary>
        /// Gets the metadata.
        /// </summary>
        /// <value>The metadata.</value>
        public virtual DbMetadata Metadata
        {
            get { return dbMetadata; }
        }

        /// <summary>
        /// Shutdowns this instance.
        /// </summary>
        public virtual void Shutdown()
        {
        }
    }
}