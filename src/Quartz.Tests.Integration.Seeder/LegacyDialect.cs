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

using System.Data.Common;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// The three things a dialect name has to be turned into: the provider name 3.20's
/// <c>DbProvider</c> knows it by, the driver delegate that speaks it, and a connection the seeder can
/// read the tables back through.
/// </summary>
/// <remarks>
/// The dialect names are the repository's, the ones <c>database/tables/tables_&lt;dialect&gt;.sql</c>
/// is spelled with, so that a caller passes the same word to the seeder that names the script.
/// </remarks>
internal static class LegacyDialect
{
    /// <summary>
    /// The provider names in 3.20's <c>dbproviders.netstandard.properties</c>. Each is the assembly
    /// this project references for that database, which is what makes the reflective load 3.20's
    /// <c>DbProvider</c> performs succeed.
    /// </summary>
    public static string ProviderName(string dialect) => dialect switch
    {
        "sqlite" => "SQLite-Microsoft",
        "sqlServer" => "SqlServer",
        "postgres" => "Npgsql",
        "mysql_innodb" => "MySqlConnector",
        "oracle" => "OracleODPManaged",
        "firebird" => "Firebird",
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "no 3.20 provider name for this dialect")
    };

    /// <summary>
    /// The seeder's own driver delegate for the dialect. Each is the 3.20 delegate with one method
    /// overridden — see <see cref="BlobStorageOverride" /> — so the SQL is the released version's.
    /// </summary>
    public static string DriverDelegateType(string dialect) => dialect switch
    {
        "sqlite" => TypeName<BlobForcingSQLiteDelegate>(),
        "sqlServer" => TypeName<BlobForcingSqlServerDelegate>(),
        "postgres" => TypeName<BlobForcingPostgreSQLDelegate>(),
        "mysql_innodb" => TypeName<BlobForcingMySQLDelegate>(),
        "oracle" => TypeName<BlobForcingOracleDelegate>(),
        "firebird" => TypeName<BlobForcingFirebirdDelegate>(),
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "no driver delegate for this dialect")
    };

    public static DbConnection OpenConnection(string dialect, string connectionString)
    {
        DbConnection connection = dialect switch
        {
            "sqlite" => new SqliteConnection(connectionString),
            "sqlServer" => new SqlConnection(connectionString),
            "postgres" => new NpgsqlConnection(connectionString),
            "mysql_innodb" => new MySqlConnection(connectionString),
            "oracle" => new OracleConnection(connectionString),
            "firebird" => new FbConnection(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "no connection type for this dialect")
        };

        connection.Open();
        return connection;
    }

    /// <summary>
    /// The literals the boolean-ish columns take. Every dialect stores them differently, and the
    /// seeder writes such a column by hand only where it has to.
    /// </summary>
    public static (string Yes, string No) BooleanLiterals(string dialect) => dialect switch
    {
        "postgres" => ("TRUE", "FALSE"),
        "oracle" => ("'1'", "'0'"),
        _ => ("1", "0")
    };

    private static string TypeName<T>() => typeof(T).FullName + ", " + typeof(T).Assembly.GetName().Name;
}
