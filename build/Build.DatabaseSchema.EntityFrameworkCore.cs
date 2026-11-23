using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// Renders the schema model as the table definitions <c>Quartz.EntityFrameworkCore</c> adds to an
/// application's own <c>DbContext</c>, so the tables <c>dotnet ef migrations add</c> creates are the ones
/// <c>create_&lt;dialect&gt;.sql</c> creates.
/// </summary>
/// <remarks>
/// <para>
/// What is rendered is data, one array of tables per dialect: each column's store type, nullability,
/// default and the CLR type the model gives it; the primary key; the foreign key and whether it cascades;
/// each index with its directions; and the release that added each of them. A hand-written interpreter
/// beside the generated file turns it into EF Core's model, and the package's two <c>MigrationBuilder</c>
/// extensions read the same arrays, so nothing about a table is written twice.
/// </para>
/// <para>
/// Every column has an explicit store type, because a provider left to choose picks types the scripts do
/// not use (PostgreSQL <c>character varying(n)</c>, MySQL <c>longblob</c>, Oracle <c>BOOLEAN</c> and
/// <c>NVARCHAR2</c>, Firebird <c>BLOB SUB_TYPE BINARY</c>). The spelling is the script's,
/// folded the way the package's predecessor spelled it where that changes nothing in the database:
/// PostgreSQL in lower case, as its script is generated; MySQL in lower case, with <c>BOOLEAN</c> as
/// <c>tinyint(1)</c>, because Oracle's MySQL provider writes any other boolean type with a <c>(1)</c> after
/// it. EF Core compares a store type as written, so a spelling that changes is a migration.
/// </para>
/// <para>
/// The names that depend on the table prefix and the schema are the package's at run time: they are a rule
/// rather than data, as on the Weasel models. On Oracle and Firebird the primary- and foreign-key names are the
/// scripts' own, rendered with the prefix as <c>{1}</c>; on the other four they are left to EF Core. A new
/// dialect is one more <see cref="EntityFrameworkCoreDialect" />.
/// </para>
/// </remarks>
partial class Build
{
    /// <summary>
    /// One dialect the Entity Framework Core model is rendered for.
    /// </summary>
    /// <param name="Dialect">The dialect, as <see cref="SchemaColumn.Definition" /> is keyed.</param>
    /// <param name="Member">The name of the generated array, and of the file it is written to.</param>
    /// <param name="StoreType">A column type as the model declares it, spelled the way the EF model says it.</param>
    /// <param name="Cascades">Whether the dialect's script honours <see cref="SchemaForeignKey.Cascade" />.</param>
    /// <param name="IntegerIs64Bits">
    /// Whether the dialect's <c>INTEGER</c> holds 64 bits, as SQLite's does, so that the CLR type matches what
    /// a column of it can hold.
    /// </param>
    /// <param name="DeleteTriggers">Whether the dialect carries <see cref="SqliteDeleteTriggers" />.</param>
    /// <param name="NamesConstraints">
    /// Whether the model names the primary and foreign keys as the dialect's script names them. Oracle's and
    /// Firebird's scripts name every key, and no earlier EF Core model of these two databases left the names to
    /// EF Core, so the scripts' names cost nobody a migration. The other four leave them to EF Core, because
    /// the AppAny models a database may have been built with did.
    /// </param>
    sealed record EntityFrameworkCoreDialect(
        string Dialect,
        string Member,
        Func<string, string> StoreType,
        bool Cascades,
        bool IntegerIs64Bits = false,
        bool DeleteTriggers = false,
        bool NamesConstraints = false);

    const string EntityFrameworkCoreProject = "Quartz.EntityFrameworkCore";

    /// <summary>The dialects <c>Quartz.EntityFrameworkCore</c> has a model for, in the order they are written.</summary>
    static readonly EntityFrameworkCoreDialect[] EntityFrameworkCoreDialects =
    [
        new("postgres", "PostgreSql", Lower, Cascades: true),
        new("sqlServer", "SqlServer", AsDeclared, Cascades: true),
        new("mysql_innodb", "MySql", MySqlEntityFrameworkCoreType, Cascades: false),
        new("sqlite", "Sqlite", AsDeclared, Cascades: true, IntegerIs64Bits: true, DeleteTriggers: true),
        new("oracle", "Oracle", AsDeclared, Cascades: false, NamesConstraints: true),
        new("firebird", "Firebird", AsDeclared, Cascades: false, NamesConstraints: true),
    ];

    /// <summary>Every generated model, as a path under <c>src/</c> and its content.</summary>
    static List<(string Path, string Content)> BuildEntityFrameworkCoreModels() =>
        EntityFrameworkCoreDialects
            .Select(x => ($"{EntityFrameworkCoreProject}/Generated/QuartzSchema.{x.Member}.g.cs", RenderEntityFrameworkCoreModel(x)))
            .ToList();

    static string RenderEntityFrameworkCoreModel(EntityFrameworkCoreDialect ef)
    {
        StringBuilder o = new();
        string dialect = ef.Dialect;
        string label = DialectLabel[dialect];

        o.Append($$"""
            // <auto-generated>
            //   GENERATED FILE. Describe the schema in build/Build.DatabaseSchema.cs and run
            //   'dotnet fallout GenerateSchema'; edits made here are overwritten.
            //
            //   The {{label}} job store schema as Entity Framework Core table definitions, from the same model
            //   as src/Quartz/Impl/AdoJobStore/Schema/create_{{dialect}}.sql. Names are unprefixed and as the
            //   script writes them; QuartzTableNaming, beside this file, turns them into the database's.
            // </auto-generated>

            #nullable enable

            namespace {{EntityFrameworkCoreProject}};

            internal static partial class QuartzSchema
            {
                /// <summary>
                /// Every table of the {{label}} schema, tables before the ones that reference them.
                /// </summary>
                internal static readonly QuartzTableDefinition[] {{ef.Member}} =
                [

            """);

        IndexDef[] indexes = AllSchemaIndexes(dialect);

        foreach (SchemaTable table in SchemaTables)
        {
            o.AppendLine($"        new(\"{table.Name}\",");
            o.AppendLine("            [");

            foreach (SchemaColumn column in table.Columns)
            {
                WeaselColumn parsed = ParseWeaselColumn(column.Definition[dialect]);
                string kind = EntityFrameworkCoreKind(ef, parsed.Type);
                StringBuilder line = new($"                new(\"{column.Name}\", QuartzColumnKind.{kind}, \"{ef.StoreType(parsed.Type)}\", Nullable: {(parsed.NotNull ? "false" : "true")}");

                // Firebird writes a nullable column's absent default as DEFAULT NULL, which is no default at all.
                if (parsed.Default is { } defaultValue && !defaultValue.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    line.Append($", DefaultValue: {EntityFrameworkCoreDefault(kind, defaultValue)}");
                }

                if (column.AddedBy is { } columnAddedBy)
                {
                    line.Append($", AddedBy: \"{columnAddedBy}\"");
                }

                o.AppendLine(line.Append("),").ToString());
            }

            o.AppendLine("            ],");
            o.AppendLine($"            PrimaryKey: {StringArray(table.PrimaryKey)},");

            if (ef.NamesConstraints)
            {
                o.AppendLine($"            PrimaryKeyName: \"{ConstraintName(PrimaryKeyName(dialect, table))}\",");
            }

            if (table.ForeignKey is not null && ForeignKeyOn(dialect, table) is null)
            {
                o.AppendLine($"            // No foreign key: tables_{dialect}.sql creates none on this table. See SchemaForeignKey.LeftOutOn.");
                o.AppendLine("            ForeignKey: null,");
            }
            else if (ForeignKeyOn(dialect, table) is { } foreignKey)
            {
                bool cascade = foreignKey.Cascade && ef.Cascades;
                o.AppendLine(
                    $"            ForeignKey: new({StringArray(foreignKey.Columns)}, \"{foreignKey.ReferencedTable}\", "
                    + $"{StringArray(foreignKey.ReferencedColumns)}, Cascade: {(cascade ? "true" : "false")}"
                    + (ef.NamesConstraints ? $", Name: \"{ConstraintName(ForeignKeyName(dialect, table, foreignKey))}\"" : "")
                    + "),");
            }
            else
            {
                o.AppendLine("            ForeignKey: null,");
            }

            IndexDef[] tableIndexes = indexes.Where(i => IndexTable(i) == table.Name).ToArray();
            if (tableIndexes.Length == 0)
            {
                o.Append("            Indexes: []");
            }
            else
            {
                o.AppendLine("            Indexes:");
                o.AppendLine("            [");
                foreach (IndexDef index in tableIndexes)
                {
                    string[][] parts = IndexColumnParts(index);
                    StringBuilder line = new($"                new(\"{IndexSuffix(index)}\", {StringArray(parts.Select(p => p[0]))}");

                    if (parts.Any(p => p.Length > 1 && p[1].Equals("DESC", StringComparison.OrdinalIgnoreCase)))
                    {
                        line.Append(", Descending: [" + string.Join(", ", parts.Select(p =>
                            p.Length > 1 && p[1].Equals("DESC", StringComparison.OrdinalIgnoreCase) ? "true" : "false")) + "]");
                    }

                    if (index.AddedBy is { } indexAddedBy)
                    {
                        line.Append($", AddedBy: \"{indexAddedBy}\"");
                    }

                    o.AppendLine(line.Append("),").ToString());
                }

                o.Append("            ]");
            }

            o.AppendLine(table.AddedBy is { } tableAddedBy ? $",\n            AddedBy: \"{tableAddedBy}\")," : "),");
            o.AppendLine();
        }

        o.AppendLine("    ];");

        if (ef.DeleteTriggers)
        {
            o.AppendLine();
            o.AppendLine("    /// <summary>");
            o.AppendLine("    /// The delete triggers that stand in for the cascades on a connection that never turns foreign keys on,");
            o.AppendLine("    /// as statements with the table prefix as <c>{0}</c>. EF Core cannot model a trigger, so");
            o.AppendLine("    /// <c>CompleteQuartzSchema</c> creates them.");
            o.AppendLine("    /// </summary>");
            o.AppendLine($"    internal static readonly QuartzTriggerDefinition[] {ef.Member}DeleteTriggers =");
            o.AppendLine("    [");
            foreach ((string trigger, string child) in SqliteDeleteTriggers)
            {
                o.AppendLine($"        new(\"{trigger}\", {CSharpString(CreateSqliteDeleteTrigger(trigger, child))}),");
            }

            o.AppendLine("    ];");
        }

        o.Append('}');
        return o.ToString();
    }

    /// <summary>
    /// A MySQL type as the EF model spells it: lower case, as the package's predecessor and MySQL's own
    /// catalog write it, and <c>BOOLEAN</c> as the <c>tinyint(1)</c> it is stored as. Oracle's
    /// <c>MySql.EntityFrameworkCore</c> appends <c>(1)</c> to whatever store type a <c>bool</c> property has,
    /// and only <c>tinyint(1)</c> comes out as written.
    /// </summary>
    static string MySqlEntityFrameworkCoreType(string type)
    {
        string lower = type.ToLowerInvariant();
        return lower == "boolean" ? "tinyint(1)" : lower;
    }

    /// <summary>
    /// A constraint's name as the script's clause gives it, <c>CONSTRAINT {1}JOB_DETAILS_PK </c>, without the
    /// keyword: the template the package fills the prefix into.
    /// </summary>
    static string ConstraintName(string clause) => clause.Trim()["CONSTRAINT ".Length..];

    /// <summary>
    /// The CLR type a column of this store type is given, as a <c>QuartzColumnKind</c> member name. A type
    /// the model does not use yet is refused rather than guessed at.
    /// </summary>
    /// <remarks>
    /// Oracle's <c>VARCHAR2(1)</c> flags are strings, <c>'0'</c> and <c>'1'</c>, which is what the store writes and
    /// what their defaults say. Its <c>NUMBER(p)</c> is the smallest integer type that holds <c>p</c> digits.
    /// </remarks>
    static string EntityFrameworkCoreKind(EntityFrameworkCoreDialect ef, string type)
    {
        string upper = type.ToUpperInvariant();

        return upper switch
        {
            _ when upper.StartsWith("NVARCHAR", StringComparison.Ordinal)
                   || upper.StartsWith("VARCHAR", StringComparison.Ordinal)
                   || upper is "TEXT" or "LONGTEXT" or "CLOB" or "BLOB SUB_TYPE TEXT" => "String",
            _ when upper.StartsWith("NUMBER(", StringComparison.Ordinal) =>
                int.Parse(upper["NUMBER(".Length..^1], CultureInfo.InvariantCulture) switch
                {
                    <= 4 => "Int16",
                    <= 9 => "Int32",
                    _ => "Int64",
                },
            "BIGINT" => "Int64",
            "INTEGER" => ef.IntegerIs64Bits ? "Int64" : "Int32",
            "INT" => "Int32",
            "SMALLINT" => "Int16",
            "BIT" or "BOOL" or "BOOLEAN" => "Boolean",
            "VARBINARY(MAX)" or "BYTEA" or "BLOB" => "Binary",
            _ when upper.StartsWith("NUMERIC", StringComparison.Ordinal) => "Decimal",
            _ => throw new InvalidOperationException($"{DialectLabel[ef.Dialect]} type '{type}' has no CLR type in the Entity Framework Core rendering yet"),
        };
    }

    /// <summary>
    /// A column default as the CLR value <c>HasDefaultValue</c> takes, typed as the column's property is: EF
    /// Core refuses a default whose type is not the property's.
    /// </summary>
    static string EntityFrameworkCoreDefault(string kind, string value) => (kind, value.ToUpperInvariant()) switch
    {
        ("String", _) when value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' => CSharpString(value[1..^1].Replace("''", "'")),
        ("Boolean", "0" or "FALSE") => "false",
        ("Boolean", "1" or "TRUE") => "true",
        ("Int16", _) => $"(short) {int.Parse(value, CultureInfo.InvariantCulture)}",
        ("Int32", _) => int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        ("Int64", _) => long.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L",
        _ => throw new InvalidOperationException($"default '{value}' of a {kind} column has no CLR value in the Entity Framework Core rendering yet"),
    };
}
