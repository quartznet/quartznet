#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// The driver descriptions Quartz ships, written in code.
/// </summary>
/// <remarks>
/// <para>
/// These used to live in an embedded <c>.properties</c> resource, parsed at run time and poured into a
/// <see cref="DbMetadata" /> by reflection over property names. Every description said the same eleven
/// things, so the parsing bought nothing but a chance to misspell a key, and the reflective assignment
/// is exactly the shape a trimmed or ahead-of-time-compiled application cannot see through. A
/// description of a driver Quartz does not ship still arrives as text — through
/// <c>quartz.dbprovider.*</c> keys or a <c>UseGenericDatabase</c> callback — and those paths are
/// unchanged.
/// </para>
/// <para>
/// Driver types are still resolved by name: none of these assemblies is referenced by Quartz, and an
/// application brings exactly the one it uses. Only the description asked for resolves its types, so
/// naming a driver here costs nothing until somebody configures it. The two parameter type enums that
/// live in the shared framework are named with <c>typeof</c>.
/// </para>
/// </remarks>
internal sealed class BuiltInDbMetadataFactory : DbMetadataFactory
{
    /// <summary>
    /// What <see cref="DbProvider" /> does with a driver type: construct a connection or a command, and
    /// read the properties a <see cref="DbMetadata" /> names on them.
    /// </summary>
    private const DynamicallyAccessedMemberTypes DriverTypeMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties;

    /// <summary>
    /// Provider names in the order the old resource listed them, which is the order they are reported
    /// back to somebody who named one that does not exist.
    /// </summary>
    private static readonly string[] providerNames =
    [
        "SqlServer",
        "SystemDataSqlClient",
        "MicrosoftDataSqlClient",
        "Npgsql",
        "MySql",
        "MySqlConnector",
        "SQLite",
        "SQLite-Microsoft",
        "Firebird",
        "OracleODPManaged"
    ];

    public override List<string> GetProviderNames() => [.. providerNames];

    public override DbMetadata GetDbMetadata(string providerName)
    {
        try
        {
            DbMetadata? metadata = Describe(providerName);
            if (metadata is null)
            {
                Throw.ArgumentOutOfRangeException(nameof(providerName), "No built-in description for provider '" + providerName + "'");
            }

            metadata.Validate();
            return metadata;
        }
        catch (Exception ex)
        {
            // The failure that reaches here is a driver assembly the application did not bring along.
            // Reported as it always was, naming the provider rather than the type that would not load.
            Throw.ArgumentException("Error while reading metadata information for provider '" + providerName + "'", nameof(providerName), ex);
            return default!;
        }
    }

    private static DbMetadata? Describe(string providerName) => providerName switch
    {
        // The default is Microsoft.Data.SqlClient.
        "SqlServer" or "MicrosoftDataSqlClient" => new DbMetadata
        {
            ProductName = "Microsoft SQL Server Core",
            AssemblyName = "System.Data",
            ConnectionType = LoadType("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient"),
            CommandType = LoadType("Microsoft.Data.SqlClient.SqlCommand, Microsoft.Data.SqlClient"),
            ParameterType = LoadType("Microsoft.Data.SqlClient.SqlParameter, Microsoft.Data.SqlClient"),
            ParameterDbType = typeof(SqlDbType),
            ParameterDbTypePropertyName = "SqlDbType",
            ParameterNamePrefix = "@",
            ExceptionType = LoadType("Microsoft.Data.SqlClient.SqlException, Microsoft.Data.SqlClient"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true,
            DbBinaryTypeName = "VarBinary"
        },

        // The System.Data.SqlClient driver, superseded by Microsoft.Data.SqlClient but still shipped
        // against by applications that have not moved.
        "SystemDataSqlClient" => new DbMetadata
        {
            ProductName = "Microsoft SQL Server Core",
            AssemblyName = "System.Data, Version=4.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            ConnectionType = LoadType("System.Data.SqlClient.SqlConnection, System.Data.SqlClient, Version=4.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"),
            CommandType = LoadType("System.Data.SqlClient.SqlCommand, System.Data.SqlClient, Version=4.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"),
            ParameterType = LoadType("System.Data.SqlClient.SqlParameter, System.Data.SqlClient, Version=4.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"),
            ParameterDbType = typeof(SqlDbType),
            ParameterDbTypePropertyName = "SqlDbType",
            ParameterNamePrefix = "@",
            ExceptionType = LoadType("System.Data.SqlClient.SqlException, System.Data.SqlClient, Version=4.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true,
            DbBinaryTypeName = "VarBinary"
        },

        "Npgsql" => new DbMetadata
        {
            ProductName = "Npgsql",
            AssemblyName = "Npgsql",
            ConnectionType = LoadType("Npgsql.NpgsqlConnection, Npgsql"),
            CommandType = LoadType("Npgsql.NpgsqlCommand, Npgsql"),
            ParameterType = LoadType("Npgsql.NpgsqlParameter, Npgsql"),
            ParameterDbType = LoadType("NpgsqlTypes.NpgsqlDbType, Npgsql"),
            ParameterDbTypePropertyName = "NpgsqlDbType",
            ParameterNamePrefix = ":",
            ExceptionType = LoadType("Npgsql.NpgsqlException, Npgsql"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true
        },

        "MySql" => new DbMetadata
        {
            ProductName = "MySQL, Oracle MySQL Connector/NET provider",
            AssemblyName = "MySql.Data",
            ConnectionType = LoadType("MySql.Data.MySqlClient.MySqlConnection, MySql.Data"),
            CommandType = LoadType("MySql.Data.MySqlClient.MySqlCommand, MySql.Data"),
            ParameterType = LoadType("MySql.Data.MySqlClient.MySqlParameter, MySql.Data"),
            ParameterDbType = LoadType("MySql.Data.MySqlClient.MySqlDbType, MySql.Data"),
            ParameterDbTypePropertyName = "MySqlDbType",
            ParameterNamePrefix = "?",
            ExceptionType = LoadType("MySql.Data.MySqlClient.MySqlException, MySql.Data"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true,
            DbBinaryTypeName = "Blob"
        },

        "MySqlConnector" => new DbMetadata
        {
            ProductName = "MySQL, MySqlConnector provider",
            AssemblyName = "MySqlConnector",
            ConnectionType = LoadType("MySqlConnector.MySqlConnection, MySqlConnector"),
            CommandType = LoadType("MySqlConnector.MySqlCommand, MySqlConnector"),
            ParameterType = LoadType("MySqlConnector.MySqlParameter, MySqlConnector"),
            ParameterDbType = LoadType("MySqlConnector.MySqlDbType, MySqlConnector"),
            ParameterDbTypePropertyName = "MySqlDbType",
            ParameterNamePrefix = "?",
            ExceptionType = LoadType("MySqlConnector.MySqlException, MySqlConnector"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true,
            DbBinaryTypeName = "Blob"
        },

        "SQLite" => new DbMetadata
        {
            AssemblyName = "System.Data.SQLite",
            ConnectionType = LoadType("System.Data.SQLite.SQLiteConnection, System.Data.SQLite"),
            CommandType = LoadType("System.Data.SQLite.SQLiteCommand, System.Data.SQLite"),
            ParameterType = LoadType("System.Data.SQLite.SQLiteParameter, System.Data.SQLite"),
            ParameterDbType = LoadType("System.Data.SQLite.TypeAffinity, System.Data.SQLite"),
            ParameterDbTypePropertyName = "DbType",
            ParameterNamePrefix = "@",
            ExceptionType = LoadType("System.Data.SQLite.SQLiteException, System.Data.SQLite"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true
        },

        "SQLite-Microsoft" => new DbMetadata
        {
            AssemblyName = "Microsoft.Data.Sqlite",
            ConnectionType = LoadType("Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite"),
            CommandType = LoadType("Microsoft.Data.Sqlite.SqliteCommand, Microsoft.Data.Sqlite"),
            ParameterType = LoadType("Microsoft.Data.Sqlite.SqliteParameter, Microsoft.Data.Sqlite"),
            ParameterDbType = typeof(DbType),
            ParameterDbTypePropertyName = "DbType",
            ParameterNamePrefix = "@",
            ExceptionType = LoadType("Microsoft.Data.Sqlite.SqliteException, Microsoft.Data.Sqlite"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true
        },

        "Firebird" => new DbMetadata
        {
            AssemblyName = "FirebirdSql.Data.FirebirdClient",
            ConnectionType = LoadType("FirebirdSql.Data.FirebirdClient.FbConnection, FirebirdSql.Data.FirebirdClient"),
            CommandType = LoadType("FirebirdSql.Data.FirebirdClient.FbCommand, FirebirdSql.Data.FirebirdClient"),
            ParameterType = LoadType("FirebirdSql.Data.FirebirdClient.FbParameter, FirebirdSql.Data.FirebirdClient"),
            ParameterDbType = LoadType("FirebirdSql.Data.FirebirdClient.FbDbType, FirebirdSql.Data.FirebirdClient"),
            ParameterDbTypePropertyName = "DbType",
            ParameterNamePrefix = "@",
            ExceptionType = LoadType("FirebirdSql.Data.FirebirdClient.FbException, FirebirdSql.Data.FirebirdClient"),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true
        },

        "OracleODPManaged" => new DbMetadata
        {
            ProductName = "Oracle, Managed Oracle provider",
            AssemblyName = "Oracle.ManagedDataAccess",
            ConnectionType = LoadType("Oracle.ManagedDataAccess.Client.OracleConnection, Oracle.ManagedDataAccess"),
            CommandType = LoadType("Oracle.ManagedDataAccess.Client.OracleCommand, Oracle.ManagedDataAccess"),
            ParameterType = LoadType("Oracle.ManagedDataAccess.Client.OracleParameter, Oracle.ManagedDataAccess"),
            ParameterDbType = LoadType("Oracle.ManagedDataAccess.Client.OracleDbType, Oracle.ManagedDataAccess"),
            ParameterDbTypePropertyName = "OracleDbType",
            ParameterNamePrefix = ":",
            ExceptionType = LoadType("Oracle.ManagedDataAccess.Client.OracleException, Oracle.ManagedDataAccess"),
            UseParameterNamePrefixInParameterCollection = false,
            BindByName = true
        },

        _ => null
    };

    /// <summary>
    /// Resolves one of the driver types named above. Throwing rather than leaving the description half
    /// filled is what the reflective binder did, and it is the right answer: a driver assembly the
    /// application did not bring along is a configuration mistake, not a description that half works.
    /// </summary>
    [return: DynamicallyAccessedMembers(DriverTypeMembers)]
    private static Type LoadType(string typeName) => Type.GetType(typeName, throwOnError: true)!;
}
