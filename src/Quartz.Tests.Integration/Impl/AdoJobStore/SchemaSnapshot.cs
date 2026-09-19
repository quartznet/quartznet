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
using System.Text.RegularExpressions;

using Oracle.ManagedDataAccess.Client;

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
        List<string> indexes = dialect == "oracle"
            ? await ReadOracleIndexesAsync(connection, indexSql, prefix)
            : await QueryAsync(connection, indexSql, prefix);

        return new SchemaSnapshot(tables, columns, indexes);
    }

    private static async Task<List<string>> QueryAsync(DbConnection connection, string sql, string prefix)
    {
        List<string> rows = [];

        foreach (string[] cells in await QueryRowsAsync(connection, sql))
        {
            rows.Add(Compose(cells, prefix));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>
    /// One query's rows, each already normalized cell by cell but not yet joined — for the readers
    /// that have to look at a cell before deciding what the row says.
    /// </summary>
    private static async Task<List<string[]>> QueryRowsAsync(DbConnection connection, string sql)
    {
        List<string[]> rows = [];

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        // Oracle returns a LONG column as an empty string unless the command is told how much of it
        // to fetch, and the index expressions this reader needs are LONGs. -1 is "all of it".
        if (command is OracleCommand oracleCommand)
        {
            oracleCommand.InitialLONGFetchSize = -1;
        }

        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string[] cells = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                cells[i] = reader.IsDBNull(i) ? "" : Cell(reader.GetValue(i).ToString());
            }

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>
    /// Joins a row's cells and strips the table prefix, so QRTZ_TRIGGERS and QRTZM_TRIGGERS compare
    /// equal.
    /// </summary>
    private static string Compose(IReadOnlyList<string> cells, string prefix)
    {
        StringBuilder row = new StringBuilder();
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                row.Append('|');
            }

            row.Append(cells[i]);
        }

        return row.ToString().Replace(prefix.ToUpperInvariant(), "", StringComparison.Ordinal);
    }

    /// <summary>
    /// The hidden virtual column Oracle puts behind a descending or function-based index, whose
    /// generated name is <c>SYS_NC&lt;n&gt;$</c>.
    /// </summary>
    private static readonly Regex OracleHiddenColumn = new Regex(
        @"^SYS_NC\d+\$$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Oracle's index columns, with the hidden virtual column behind a descending index replaced by
    /// the expression it stands for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>n</c> in <c>SYS_NC&lt;n&gt;$</c> is the hidden column's position in the table, so the same
    /// index over the same expression gets a different name depending on how many columns the table
    /// had when the index was created. A fresh install creates every column and then the index; a
    /// migrated schema creates the index and then appends whatever later migrations add. Comparing
    /// the generated names therefore reports a difference between two schemas that are identical —
    /// which is what the Oracle legs hit once <c>4.2/add_continuations</c> landed after the 4.0 index
    /// script (<c>SYS_NC00023$</c> against <c>SYS_NC00026$</c>).
    /// </para>
    /// <para>
    /// So the snapshot says what the index indexes rather than what Oracle called it:
    /// <c>USER_IND_EXPRESSIONS</c> holds the expression, keyed by index and column position. The
    /// descending flag is folded into the name for every row, hidden or not, because it is part of
    /// what the index is and the generated name used to carry it implicitly.
    /// </para>
    /// <para>
    /// Two queries rather than a join: <c>COLUMN_EXPRESSION</c> is a <c>LONG</c>, and a LONG in the
    /// select list of an outer join is the kind of thing Oracle refuses in some versions and not
    /// others. On its own, in a single-table select, it is unambiguously allowed.
    /// </para>
    /// </remarks>
    private static async Task<List<string>> ReadOracleIndexesAsync(DbConnection connection, string indexSql, string prefix)
    {
        Dictionary<(string Index, string Position), string> expressions = new();

        foreach (string[] cells in await QueryRowsAsync(connection, OracleIndexExpressionSql(prefix)))
        {
            expressions[(cells[0], cells[1])] = cells[2];
        }

        List<string> rows = [];

        // Cells: table, index, column, descend, position.
        foreach (string[] cells in await QueryRowsAsync(connection, indexSql))
        {
            string name = OracleHiddenColumn.IsMatch(cells[2])
                          && expressions.TryGetValue((cells[1], cells[4]), out string expression)
                ? expression
                : cells[2];

            if (cells[3] == "DESC")
            {
                name += ":DESC";
            }

            rows.Add(Compose([cells[0], cells[1], name, cells[4]], prefix));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static string OracleIndexExpressionSql(string prefix)
    {
        string p = prefix.ToUpperInvariant().Replace("_", "!_", StringComparison.Ordinal);

        return $"SELECT UPPER(index_name), column_position, column_expression FROM user_ind_expressions WHERE UPPER(table_name) LIKE '{p}%' ESCAPE '!'";
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

            // The column list is table, index, column, descend, position — in that order, because
            // ReadOracleIndexesAsync reads it by position and swaps the third cell for the index
            // expression when Oracle put a hidden virtual column there.
            "oracle" => (
                $"SELECT UPPER(table_name) FROM user_tables WHERE UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                $"SELECT UPPER(table_name), UPPER(column_name), UPPER(data_type), nullable, NVL(data_length, -1) FROM user_tab_columns WHERE UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                $"""
                 SELECT UPPER(ic.table_name), UPPER(ic.index_name), UPPER(ic.column_name), UPPER(ic.descend), ic.column_position
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
