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
using System.Text;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Table, column and index inventory for one table prefix, with the prefix normalized away so two
/// schemas built different ways in one database compare directly.
/// </summary>
/// <remarks>
/// Shared by the two tests that ask the same question of different routes to a schema:
/// <see cref="MigrationScriptTest" /> compares a migrated schema with a fresh one, and
/// <see cref="SchemaProvisioningTest" /> compares a provisioned one with a fresh one. Both are
/// really asking whether the route matters, and neither answer means much unless the two use the
/// same introspection.
/// </remarks>
internal sealed record SchemaSnapshot(
    IReadOnlyCollection<string> Tables,
    IReadOnlyCollection<string> Columns,
    IReadOnlyCollection<string> Indexes)
{
    public static async Task<SchemaSnapshot> ReadAsync(DbConnection connection, string dialect, string prefix)
    {
        (string tableSql, string columnSql, string indexSql) = Queries(dialect, prefix);

        List<string> tables = await QueryAsync(connection, tableSql, prefix);
        List<string> columns = await QueryAsync(connection, columnSql, prefix);
        List<string> indexes = await QueryAsync(connection, indexSql, prefix);

        return new SchemaSnapshot(tables, columns, indexes);
    }

    private static async Task<List<string>> QueryAsync(DbConnection connection, string sql, string prefix)
    {
        List<string> rows = [];

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            StringBuilder row = new StringBuilder();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0)
                {
                    row.Append('|');
                }

                row.Append(reader.IsDBNull(i) ? "" : Cell(reader.GetValue(i).ToString()));
            }

            // Strip the prefix so QRTZ_TRIGGERS and QRTZM_TRIGGERS compare equal.
            rows.Add(row.ToString().Replace(prefix.ToUpperInvariant(), "", StringComparison.Ordinal));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>
    /// One catalog value, in a form two schemas of the same dialect can be compared in.
    /// </summary>
    /// <remarks>
    /// Whitespace is removed rather than trimmed, because SQLite reports a column's type as the text it
    /// was declared with and <c>tables_sqlite.sql</c> declares some of them <c>NVARCHAR (512)</c> and
    /// the rest <c>NVARCHAR(512)</c>. SQLite reads the two identically — affinity is decided by
    /// substring, not by parsing — so that space is a fact about the file rather than about the schema.
    /// Nothing is lost on the other five: their catalogs report a resolved type name, and no two type
    /// names differ only in their spaces.
    /// </remarks>
    private static string Cell(string value)
    {
        StringBuilder cell = new(value.Length);
        foreach (char c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                cell.Append(char.ToUpperInvariant(c));
            }
        }

        return cell.ToString();
    }

    /// <summary>
    /// Per-dialect introspection. Index queries deliberately exclude primary keys and unique
    /// constraints: those come from the table definition rather than from what is under test.
    /// </summary>
    private static (string Tables, string Columns, string Indexes) Queries(string dialect, string prefix)
    {
        // '_' is a single-character wildcard in LIKE, so an unescaped 'QRTZ_%' also matches
        // QRTZM_TRIGGERS. Escape it, or the fresh snapshot silently swallows the other one.
        string p = prefix.ToUpperInvariant().Replace("_", "!_", StringComparison.Ordinal);

        return dialect switch
        {
            "sqlite" => (
                $"SELECT UPPER(name) FROM sqlite_master WHERE type = 'table' AND UPPER(name) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(m.name), UPPER(c.name), UPPER(c.type), c.\"notnull\" FROM sqlite_master m JOIN pragma_table_info(m.name) c WHERE m.type = 'table' AND UPPER(m.name) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(i.tbl_name), UPPER(i.name), UPPER(c.name), c.seqno FROM sqlite_master i JOIN pragma_index_info(i.name) c WHERE i.type = 'index' AND i.sql IS NOT NULL AND UPPER(i.tbl_name) LIKE '{p}%' ESCAPE '!'"),

            "sqlServer" => (
                $"SELECT UPPER(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(TABLE_NAME), UPPER(COLUMN_NAME), UPPER(DATA_TYPE), IS_NULLABLE, ISNULL(CHARACTER_MAXIMUM_LENGTH, -1) FROM INFORMATION_SCHEMA.COLUMNS WHERE UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                $"""
                 SELECT UPPER(t.name), UPPER(i.name), UPPER(c.name), ic.key_ordinal
                 FROM sys.indexes i
                 JOIN sys.tables t ON t.object_id = i.object_id
                 JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                 JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
                 WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND UPPER(t.name) LIKE '{p}%' ESCAPE '!'
                 """),

            "postgres" => (
                $"SELECT UPPER(table_name) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(table_name), UPPER(column_name), UPPER(data_type), is_nullable, COALESCE(character_maximum_length, -1) FROM information_schema.columns WHERE table_schema = 'public' AND UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                $"""
                 SELECT UPPER(t.relname), UPPER(i.relname), UPPER(a.attname), k.ord
                 FROM pg_class t
                 JOIN pg_index ix ON t.oid = ix.indrelid
                 JOIN pg_class i ON i.oid = ix.indexrelid
                 JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON TRUE
                 JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                 WHERE ix.indisprimary = FALSE AND ix.indisunique = FALSE AND UPPER(t.relname) LIKE '{p}%' ESCAPE '!'
                 """),

            "mysql_innodb" => (
                $"SELECT UPPER(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(TABLE_NAME), UPPER(COLUMN_NAME), UPPER(DATA_TYPE), IS_NULLABLE, IFNULL(CHARACTER_MAXIMUM_LENGTH, -1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(TABLE_NAME), UPPER(INDEX_NAME), UPPER(COLUMN_NAME), SEQ_IN_INDEX FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME <> 'PRIMARY' AND NON_UNIQUE = 1 AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'"),

            "oracle" => (
                $"SELECT UPPER(table_name) FROM user_tables WHERE UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(table_name), UPPER(column_name), UPPER(data_type), nullable, NVL(data_length, -1) FROM user_tab_columns WHERE UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                $"""
                 SELECT UPPER(ic.table_name), UPPER(ic.index_name), UPPER(ic.column_name), ic.column_position
                 FROM user_ind_columns ic
                 JOIN user_indexes i ON i.index_name = ic.index_name
                 WHERE i.uniqueness = 'NONUNIQUE' AND UPPER(ic.table_name) LIKE '{p}%' ESCAPE '!'
                 """),

            "firebird" => (
                $"SELECT TRIM(UPPER(rdb$relation_name)) FROM rdb$relations WHERE rdb$view_blr IS NULL AND rdb$system_flag = 0 AND TRIM(UPPER(rdb$relation_name)) LIKE '{p}%' ESCAPE '!'",
                $"""
                 SELECT TRIM(UPPER(rf.rdb$relation_name)), TRIM(UPPER(rf.rdb$field_name)), TRIM(UPPER(f.rdb$field_type)), COALESCE(rf.rdb$null_flag, 0), COALESCE(f.rdb$character_length, -1)
                 FROM rdb$relation_fields rf
                 JOIN rdb$fields f ON f.rdb$field_name = rf.rdb$field_source
                 WHERE TRIM(UPPER(rf.rdb$relation_name)) LIKE '{p}%' ESCAPE '!'
                 """,
                $"""
                 SELECT TRIM(UPPER(i.rdb$relation_name)), TRIM(UPPER(i.rdb$index_name)), TRIM(UPPER(s.rdb$field_name)), s.rdb$field_position
                 FROM rdb$indices i
                 JOIN rdb$index_segments s ON s.rdb$index_name = i.rdb$index_name
                 WHERE COALESCE(i.rdb$unique_flag, 0) = 0 AND i.rdb$system_flag = 0 AND TRIM(UPPER(i.rdb$relation_name)) LIKE '{p}%' ESCAPE '!'
                 """),

            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "no introspection queries for this dialect")
        };
    }
}
