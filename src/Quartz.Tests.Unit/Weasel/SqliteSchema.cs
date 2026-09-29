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

#nullable enable

using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// A SQLite database's schema as rows that compare directly, and the scripts that build the schemas the
/// Weasel tests compare against.
/// </summary>
/// <remarks>
/// Everything is upper-cased and type names lose their spaces, because SQLite compares identifiers without
/// regard to case and reads <c>NVARCHAR (512)</c> as <c>NVARCHAR(512)</c>, and Weasel writes column names
/// in lower case where the scripts write them in upper. Columns are compared as a set rather than by
/// position, because a migration appends the columns it adds.
/// </remarks>
internal sealed record SqliteSchema(
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Indexes,
    IReadOnlyList<string> ForeignKeys,
    IReadOnlyList<string> Triggers)
{
    public static async Task<SqliteSchema> ReadAsync(string connectionString)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        return new SqliteSchema(
            await RowsAsync(connection, "SELECT upper(name) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite!_%' ESCAPE '!'"),
            await RowsAsync(connection,
                "SELECT upper(m.name), upper(c.name), upper(replace(c.type, ' ', '')), c.\"notnull\", upper(ifnull(c.dflt_value, '')), c.pk"
                + " FROM sqlite_master m JOIN pragma_table_info(m.name) c WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite!_%' ESCAPE '!'"),
            await RowsAsync(connection,
                "SELECT upper(m.tbl_name), upper(m.name), c.seqno, upper(ifnull(c.name, '')), c.\"desc\""
                + " FROM sqlite_master m JOIN pragma_index_xinfo(m.name) c WHERE m.type = 'index' AND m.sql IS NOT NULL AND c.key = 1"),
            await RowsAsync(connection,
                "SELECT upper(m.name), f.seq, upper(f.\"table\"), upper(f.\"from\"), upper(f.\"to\"), f.on_delete"
                + " FROM sqlite_master m JOIN pragma_foreign_key_list(m.name) f WHERE m.type = 'table'"),
            await RowsAsync(connection, "SELECT upper(name) FROM sqlite_master WHERE type = 'trigger'"));
    }

    public static async Task<List<string>> RowsAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<string> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                .Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// Runs <c>create_sqlite.sql</c> the way the store runs it for <c>ProvisionSchema()</c>: statement by
    /// statement, with the table prefix substituted.
    /// </summary>
    public static async Task CreateWithProvisioningScriptAsync(string connectionString, string tablePrefix = "QRTZ_")
    {
        foreach (string statement in ProvisioningStatements(tablePrefix))
        {
            await ExecuteAsync(connectionString, statement);
        }
    }

    public static IEnumerable<string> ProvisioningStatements(string tablePrefix) =>
        Resource(typeof(IScheduler), "Quartz.Impl.AdoJobStore.Schema.create_sqlite.sql")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n--;;\n")
            .Skip(1)
            .Select(x => string.Format(CultureInfo.InvariantCulture, x, tablePrefix, tablePrefix).Trim());

    /// <summary><c>database/tables/tables_sqlite.sql</c>, the schema a fresh install creates.</summary>
    public static Task CreateWithFreshInstallScriptAsync(string connectionString) =>
        ExecuteAsync(connectionString, Resource(typeof(SqliteSchema), "tables_sqlite.sql"));

    /// <summary>A released version's <c>tables_sqlite.sql</c>, from <c>SchemaBaselines</c>.</summary>
    public static Task CreateWithBaselineAsync(string connectionString, string version) =>
        ExecuteAsync(connectionString, Resource(typeof(SqliteSchema), $"baseline_{version}_tables_sqlite.sql"));

    /// <summary>
    /// The provisioned schema, except that <c>QRTZ_TRIGGERS</c> has been rebuilt by hand without its
    /// foreign key — and, if asked, with columns and constraints of the application's own.
    /// </summary>
    /// <remarks>
    /// Weasel can put the key back only by rebuilding the table, so an apply on this schema is a rebuild.
    /// </remarks>
    public static async Task CreateWithTriggersTableMissingItsForeignKeyAsync(string connectionString, string? extraDefinitions)
    {
        foreach (string statement in ProvisioningStatements("QRTZ_"))
        {
            string sql = statement;
            if (sql.Contains("CREATE TABLE IF NOT EXISTS QRTZ_TRIGGERS (", StringComparison.Ordinal))
            {
                int primaryKey = sql.IndexOf(",\n  PRIMARY KEY", StringComparison.Ordinal);
                int foreignKey = sql.IndexOf(",\n  FOREIGN KEY", StringComparison.Ordinal);
                foreignKey.Should().BeGreaterThan(primaryKey,
                    "the premise: the provisioning script declares QRTZ_TRIGGERS' key, then its foreign key");

                // After the last column and before the key: SQLite takes columns before table constraints,
                // so the extra definitions can end with constraints of their own.
                sql = sql[..primaryKey]
                      + (extraDefinitions is null ? "" : ",\n  " + extraDefinitions)
                      + sql[primaryKey..foreignKey]
                      + "\n);";
            }

            await ExecuteAsync(connectionString, sql);
        }
    }

    private static string Resource(Type anchor, string name)
    {
        using Stream stream = anchor.Assembly.GetManifestResourceStream(name)
                              ?? throw new InvalidOperationException($"{name} is not embedded in {anchor.Assembly.GetName().Name}");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
