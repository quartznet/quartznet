using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// Renders the schema model as the Weasel tables the <c>Quartz.Weasel.*</c> dialect packages register,
/// so the model behind <c>create_&lt;dialect&gt;.sql</c> is also the model Weasel migrates towards.
/// </summary>
/// <remarks>
/// <para>
/// Weasel compares a model with what the database's catalog reads back, so the model has to be spelled
/// the way each catalog stores it or a database the script created reads as changed. PostgreSQL folds
/// unquoted identifiers to lower case and names the constraints the script leaves unnamed itself
/// (<c>&lt;table&gt;_pkey</c>, <c>&lt;table&gt;_&lt;columns&gt;_fkey</c>, truncated to 63 bytes), so the
/// model says those names. SQLite reports the columns in lower case to Weasel and has no names for the
/// script's unnamed foreign keys, which Weasel reads back as <c>fk_&lt;table&gt;_&lt;referenced&gt;_&lt;n&gt;</c>.
/// SQL Server keeps the script's upper-case names and its <c>PK_</c> / <c>FK_</c> constraint names, and
/// reads a column's direction back per index column. MySQL rewrites a type as it stores it
/// (<c>BOOLEAN</c> is <c>TINYINT(1)</c>, <c>NUMERIC</c> is <c>DECIMAL</c>) and names an unnamed foreign key
/// <c>&lt;table&gt;_ibfk_1</c>; Oracle stores <c>NUMERIC</c> as <c>NUMBER</c> and keeps the constraint
/// names its script gives. Firebird folds the script's names to upper case and keeps its <c>PK_</c>,
/// <c>FK_&lt;table&gt;_1</c> and <c>IDX_</c> names and its <c>DEFAULT NULL</c>; its indexes have one
/// direction each. The names that depend on the table prefix are computed at run time by the
/// naming class beside the generated file, which is hand-written because it is the one part that is a
/// rule rather than data.
/// </para>
/// <para>
/// What is rendered: every table in <see cref="SchemaTables" /> with its columns, primary key, foreign
/// key and indexes; the SQLite delete triggers; and the index names Quartz has retired
/// (<see cref="AllLegacyIndexes" /> less the 4.x set), which the model drops because the tables are
/// add-only and Weasel would otherwise keep them for ever. Only the dialects Weasel can read back
/// without drift have a package, and <see cref="WeaselDialects" /> lists them.
/// </para>
/// </remarks>
partial class Build
{
    /// <summary>
    /// One dialect with a <c>Quartz.Weasel.*</c> package: where its model goes, and how its catalog spells
    /// what the model says. A new dialect is one more of these.
    /// </summary>
    /// <param name="Dialect">The dialect, as <see cref="SchemaColumn.Definition" /> is keyed.</param>
    /// <param name="Project">The package the model is generated into.</param>
    /// <param name="TablesNamespace">The namespace of the dialect's Weasel <c>Table</c>.</param>
    /// <param name="Type">A column type as the model declares it, spelled the way the catalog reads it back.</param>
    /// <param name="Default">A column default as the model declares it, spelled the way the catalog reads it back.</param>
    /// <param name="ColumnName">A column name as the catalog reads it back.</param>
    /// <param name="KeyColumn">A key column as the catalog spells it in a foreign key.</param>
    /// <param name="Cascades">Whether the dialect's script honours <see cref="SchemaForeignKey.Cascade" />.</param>
    /// <param name="IndexColumns">An index's columns, as the argument list of the naming class's <c>Index</c>.</param>
    /// <param name="PrimaryKeyArguments">What the naming class's <c>PrimaryKey</c> takes after the table.</param>
    /// <param name="ForeignKeyArguments">What the naming class's <c>ForeignKey</c> takes after the cascade.</param>
    /// <param name="DeleteTriggers">Whether the model carries <see cref="SqliteDeleteTriggers" />.</param>
    sealed record WeaselDialect(
        string Dialect,
        string Project,
        string TablesNamespace,
        Func<string, string> Type,
        Func<string, string> Default,
        Func<string, string> ColumnName,
        Func<string, string> KeyColumn,
        bool Cascades,
        Func<IndexDef, string> IndexColumns,
        Func<SchemaTable, string> PrimaryKeyArguments,
        Func<SchemaForeignKey, string> ForeignKeyArguments,
        bool DeleteTriggers = false);

    /// <summary>The dialects with a <c>Quartz.Weasel.*</c> package, in the order their models are written.</summary>
    static readonly WeaselDialect[] WeaselDialects =
    [
        new("postgres", "Quartz.Weasel.PostgreSQL", "Weasel.Postgresql.Tables",
            Type: Lower, Default: Lower, ColumnName: Lower, KeyColumn: Lower, Cascades: true,
            IndexColumns: DirectedColumnNames, PrimaryKeyArguments: NoArguments, ForeignKeyArguments: NoArguments),
        new("sqlServer", "Quartz.Weasel.SqlServer", "Weasel.SqlServer.Tables",
            Type: AsDeclared, Default: AsDeclared, ColumnName: AsDeclared, KeyColumn: AsDeclared, Cascades: true,
            IndexColumns: ColumnNamesAndDescending, PrimaryKeyArguments: NoArguments, ForeignKeyArguments: NoArguments),
        new("sqlite", "Quartz.Weasel.SQLite", "Weasel.Sqlite.Tables",
            Type: AsDeclared, Default: AsDeclared, ColumnName: Lower, KeyColumn: AsDeclared, Cascades: true,
            IndexColumns: ColumnNamesOrExpression, PrimaryKeyArguments: NoArguments, ForeignKeyArguments: NoArguments,
            DeleteTriggers: true),
        new("mysql_innodb", "Quartz.Weasel.MySQL", "Weasel.MySql.Tables",
            Type: MySqlCatalogType, Default: MySqlCatalogDefault, ColumnName: AsDeclared, KeyColumn: AsDeclared, Cascades: false,
            IndexColumns: ColumnNamesAndDescending, PrimaryKeyArguments: NoArguments, ForeignKeyArguments: NoArguments),
        new("oracle", "Quartz.Weasel.Oracle", "Weasel.Oracle.Tables",
            Type: OracleCatalogType, Default: AsDeclared, ColumnName: AsDeclared, KeyColumn: AsDeclared, Cascades: false,
            IndexColumns: ColumnNamesAndDescending,
            PrimaryKeyArguments: table => $", \"{table.OracleStem ?? table.Name}\"",
            ForeignKeyArguments: foreignKey => $", \"{foreignKey.OracleName}\""),
        new("firebird", "Quartz.Weasel.Firebird", "Weasel.Firebird.Tables",
            Type: AsDeclared, Default: AsDeclared, ColumnName: AsDeclared, KeyColumn: AsDeclared, Cascades: false,
            IndexColumns: ColumnNamesWithoutDirection, PrimaryKeyArguments: NoArguments, ForeignKeyArguments: NoArguments),
    ];

    /// <summary>Every generated model, as a path under <c>src/</c> and its content.</summary>
    static List<(string Path, string Content)> BuildWeaselModels() =>
        WeaselDialects
            .Select(x => ($"{x.Project}/Generated/QuartzTables.g.cs", RenderWeaselModel(x)))
            .ToList();

    static string RenderWeaselModel(WeaselDialect weasel)
    {
        StringBuilder o = new();
        string dialect = weasel.Dialect;
        string label = DialectLabel[dialect];

        o.Append($$"""
            // <auto-generated>
            //   GENERATED FILE. Describe the schema in build/Build.DatabaseSchema.cs and run
            //   'dotnet fallout GenerateSchema'; edits made here are overwritten.
            //
            //   The {{label}} job store schema as Weasel tables, from the same model as
            //   src/Quartz/Impl/AdoJobStore/Schema/create_{{dialect}}.sql and spelled the way the catalog reads
            //   it back, so a database that script created compares as unchanged. The names that depend on
            //   the table prefix come from QuartzTableNaming, beside this file.
            // </auto-generated>

            #nullable enable

            using Weasel.Core;
            using {{weasel.TablesNamespace}};

            namespace {{weasel.Project}};

            internal static partial class QuartzTables
            {
                /// <summary>
                /// Every object of the schema, tables before the ones that reference them.
                /// </summary>
                internal static List<ISchemaObject> Build(QuartzTableNaming naming)
                {
                    List<ISchemaObject> objects = [];

            """);

        o.AppendLine();

        IndexDef[] indexes = AllSchemaIndexes(dialect);

        foreach (SchemaTable table in SchemaTables)
        {
            string variable = CamelCase(table.Name);

            o.AppendLine($"        Table {variable} = naming.Table(\"{table.Name}\");");

            if (!table.PrimaryKey.SequenceEqual(table.Columns.Take(table.PrimaryKey.Length).Select(c => c.Name)))
            {
                throw new InvalidOperationException(
                    $"{table.Name}'s primary key is not its leading columns in order, which is the only shape the Weasel rendering expresses");
            }

            foreach (SchemaColumn column in table.Columns)
            {
                WeaselColumn parsed = ParseWeaselColumn(column.Definition[dialect]);
                StringBuilder line = new($"        {variable}.AddColumn(\"{weasel.ColumnName(column.Name)}\", \"{weasel.Type(parsed.Type)}\")");

                if (parsed.NotNull)
                {
                    line.Append(".NotNull()");
                }

                if (parsed.Default is { } defaultValue)
                {
                    line.Append($".DefaultValueByExpression(\"{weasel.Default(defaultValue)}\")");
                }

                if (table.PrimaryKey.Contains(column.Name))
                {
                    line.Append(".AsPrimaryKey()");
                }

                o.AppendLine(line.Append(';').ToString());
            }

            o.AppendLine($"        naming.PrimaryKey({variable}{weasel.PrimaryKeyArguments(table)});");

            if (table.ForeignKey is not null && ForeignKeyOn(dialect, table) is null)
            {
                o.AppendLine($"        // No foreign key: tables_{dialect}.sql creates none on this table. See SchemaForeignKey.LeftOutOn.");
            }
            else if (ForeignKeyOn(dialect, table) is { } foreignKey)
            {
                bool cascade = foreignKey.Cascade && weasel.Cascades;
                o.AppendLine(
                    $"        naming.ForeignKey({variable}, \"{foreignKey.ReferencedTable}\", "
                    + $"{StringArray(foreignKey.Columns.Select(weasel.KeyColumn))}, "
                    + $"{StringArray(foreignKey.ReferencedColumns.Select(weasel.KeyColumn))}, "
                    + $"cascade: {(cascade ? "true" : "false")}{weasel.ForeignKeyArguments(foreignKey)});");
            }

            foreach (IndexDef index in indexes.Where(i => IndexTable(i) == table.Name))
            {
                o.AppendLine($"        naming.Index({variable}, \"{IndexSuffix(index)}\", {weasel.IndexColumns(index)});");
            }

            o.AppendLine($"        objects.Add({variable});");
            o.AppendLine();
        }

        if (weasel.DeleteTriggers)
        {
            o.AppendLine("        // The delete triggers stand in for the cascades on a connection that never turns foreign keys on.");
            foreach ((string trigger, string child) in SqliteDeleteTriggers)
            {
                o.AppendLine($"        objects.Add(naming.Trigger(\"{trigger}\", {CSharpString(CreateSqliteDeleteTrigger(trigger, child))}));");
            }

            o.AppendLine();
        }

        HashSet<string> kept = indexes.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

        o.AppendLine("        // Index names Quartz has created and 4.x no longer does. The tables are add-only, so without these");
        o.AppendLine("        // Weasel would keep them for ever; database/migrations/4.0 drops the same names.");
        foreach ((string name, string table) in AllLegacyIndexes.Where(l => !kept.Contains(l.Name)))
        {
            o.AppendLine($"        objects.Add(naming.RetiredIndex(\"{table["QRTZ_".Length..]}\", \"{name["IDX_QRTZ_".Length..]}\"));");
        }

        o.Append("""

                    return objects;
                }
            }
            """);

        return o.ToString();
    }

    /// <summary>One column as Weasel's <c>AddColumn</c> takes it, before the dialect spells it.</summary>
    sealed record WeaselColumn(string Type, bool NotNull, string Default);

    /// <summary>
    /// Splits a column definition from the model into its type, its nullability and its default.
    /// </summary>
    /// <remarks>
    /// Every dialect's definitions are <c>TYPE [NOT NULL | NULL] [DEFAULT value]</c>, or
    /// <c>TYPE [DEFAULT value] [NOT NULL | NULL]</c> on Oracle and Firebird. A type runs to the first
    /// <c>NOT</c>, <c>NULL</c> or <c>DEFAULT</c>, so Firebird's <c>BLOB SUB_TYPE TEXT</c> is taken whole. How
    /// the catalog spells the parts is the dialect's <see cref="WeaselDialect.Type" /> and
    /// <see cref="WeaselDialect.Default" />.
    /// </remarks>
    static WeaselColumn ParseWeaselColumn(string definition)
    {
        string[] tokens = definition.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int typeLength = Array.FindIndex(tokens, 1, t => t.ToUpperInvariant() is "NOT" or "NULL" or "DEFAULT");
        typeLength = typeLength < 0 ? tokens.Length : typeLength;
        string type = string.Join(' ', tokens.Take(typeLength));
        bool notNull = false;
        string defaultValue = null;

        for (int i = typeLength; i < tokens.Length; i++)
        {
            switch (tokens[i].ToUpperInvariant())
            {
                case "NOT" when i + 1 < tokens.Length && tokens[i + 1].Equals("NULL", StringComparison.OrdinalIgnoreCase):
                    notNull = true;
                    i++;
                    break;
                case "NULL":
                    break;
                case "DEFAULT" when i + 1 < tokens.Length:
                    defaultValue = tokens[++i];
                    break;
                default:
                    throw new InvalidOperationException($"'{definition}' has a token the Weasel rendering does not know: {tokens[i]}");
            }
        }

        return new WeaselColumn(type, notNull, defaultValue);
    }

    /// <summary>
    /// The script's spelling, unchanged: SQL Server's types are already written the way its catalog reads
    /// them (<c>nvarchar(120)</c>, <c>varbinary(max)</c>, <c>numeric(13,4)</c>), SQLite's catalog reports the
    /// declared text back, and neither folds a key column. Weasel compares a SQL Server type by its name and,
    /// for a character type, its length, so a <c>numeric</c> precision is not compared.
    /// </summary>
    static string AsDeclared(string value) => value;

    /// <summary>PostgreSQL's folding of an unquoted identifier, and how Weasel writes and reads its types.</summary>
    static string Lower(string value) => value.ToLowerInvariant();

    static string NoArguments<T>(T _) => "";

    /// <summary>
    /// A MySQL type as <c>information_schema.COLUMNS.COLUMN_TYPE</c> reports it, upper-cased as Weasel
    /// reads it: <c>BOOLEAN</c> is <c>TINYINT(1)</c>, <c>INTEGER</c> is <c>INT</c>, <c>NUMERIC</c> is
    /// <c>DECIMAL</c>, and 8.0 reports an integer without its display width.
    /// </summary>
    /// <remarks>
    /// A type the model does not use yet is refused rather than guessed at, so that adding one is a
    /// decision checked against a real catalog.
    /// </remarks>
    static string MySqlCatalogType(string type)
    {
        string upper = type.ToUpperInvariant();

        return upper switch
        {
            "BOOLEAN" => "TINYINT(1)",
            "INTEGER" or "INT" => "INT",
            "BIGINT" or "SMALLINT" or "BLOB" or "LONGTEXT" => upper,
            _ when upper.StartsWith("VARCHAR(", StringComparison.Ordinal) => upper,
            _ when upper.StartsWith("NUMERIC(", StringComparison.Ordinal) => "DECIMAL" + upper["NUMERIC".Length..],
            _ => throw new InvalidOperationException($"MySQL type '{type}' has no catalog spelling in the Weasel rendering yet"),
        };
    }

    /// <summary>A MySQL default as <c>COLUMN_DEFAULT</c> reports it: <c>FALSE</c> is stored as <c>0</c>.</summary>
    static string MySqlCatalogDefault(string value) => value.ToUpperInvariant() switch
    {
        "FALSE" => "0",
        "TRUE" => "1",
        _ => value,
    };

    /// <summary>
    /// An Oracle type as <c>ALL_TAB_COLUMNS</c> reports it: <c>NUMERIC(13,4)</c> is stored as
    /// <c>NUMBER(13,4)</c>, and the others are written the way they are stored.
    /// </summary>
    /// <remarks>
    /// A type the model does not use yet is refused rather than guessed at, as on MySQL.
    /// </remarks>
    static string OracleCatalogType(string type)
    {
        string upper = type.ToUpperInvariant();

        return upper switch
        {
            "BLOB" or "CLOB" => upper,
            _ when upper.StartsWith("VARCHAR2(", StringComparison.Ordinal) || upper.StartsWith("NUMBER(", StringComparison.Ordinal) => upper,
            _ when upper.StartsWith("NUMERIC(", StringComparison.Ordinal) => "NUMBER" + upper["NUMERIC".Length..],
            _ => throw new InvalidOperationException($"Oracle type '{type}' has no catalog spelling in the Weasel rendering yet"),
        };
    }

    /// <summary>
    /// An index's columns as an array of names, with the descending ones named again: the catalogs that
    /// read each column's direction back into <c>DescendingColumns</c> — SQL Server, MySQL and Oracle.
    /// </summary>
    static string ColumnNamesAndDescending(IndexDef index)
    {
        string[][] parts = IndexColumnParts(index);
        string[] descending = parts
            .Where(p => p.Length > 1 && p[1].Equals("DESC", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0])
            .ToArray();

        string names = StringArray(parts.Select(p => p[0]));
        return descending.Length == 0 ? names : $"{names}, descending: {StringArray(descending)}";
    }

    /// <summary>
    /// An array of column names, and a refusal for a column with a direction: a Firebird index has one
    /// direction for all its columns, and <see cref="Target4X" /> gives Firebird an acquisition index without
    /// the mixed directions it cannot express, so a direction here is a mistake in the model.
    /// </summary>
    static string ColumnNamesWithoutDirection(IndexDef index)
    {
        string[][] parts = IndexColumnParts(index);

        if (parts.Any(p => p.Length > 1))
        {
            throw new InvalidOperationException(
                $"{index.Name} gives a column a direction on Firebird, whose indexes have one direction for all their columns");
        }

        return StringArray(parts.Select(p => p[0]));
    }

    /// <summary>
    /// PostgreSQL's <c>IndexDefinition</c> has one sort order for the whole index, so a descending column
    /// is written as the column text <c>priority DESC</c>, which Weasel emits as is and which reads back
    /// the same; <c>ASC</c> is the default and the catalog does not report it.
    /// </summary>
    static string DirectedColumnNames(IndexDef index) =>
        StringArray(IndexColumnParts(index).Select(parts =>
        {
            string name = parts[0].ToLowerInvariant();
            return parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase) ? $"{name} DESC" : name;
        }));

    /// <summary>
    /// SQLite's <c>IndexDefinition</c> has no per-column direction either, and takes the whole list as an
    /// expression instead, in the tight form the script writes it, when a column carries a direction.
    /// </summary>
    static string ColumnNamesOrExpression(IndexDef index)
    {
        string[] columns = index.Columns.Split(',').Select(c => c.Trim()).ToArray();

        return columns.Any(c => c.Contains(' '))
            ? $"expression: \"{TightColumns(index.Columns)}\""
            : StringArray(columns);
    }

    /// <summary>Each of an index's columns, split into its name and its direction, if it has one.</summary>
    static string[][] IndexColumnParts(IndexDef index) =>
        index.Columns.Split(',')
            .Select(c => c.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

    static string StringArray(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(v => $"\"{v}\"")) + "]";

    static string CSharpString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    /// <summary><c>PAUSED_TRIGGER_GRPS</c> as <c>pausedTriggerGrps</c>.</summary>
    static string CamelCase(string name)
    {
        string[] words = name.ToLowerInvariant().Split('_');
        return words[0] + string.Concat(words.Skip(1).Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }
}
