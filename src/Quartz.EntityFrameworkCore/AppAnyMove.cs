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

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Quartz.EntityFrameworkCore;

/// <summary>
/// What a schema <c>AppAny.Quartz.EntityFrameworkCore.Migrations</c> 0.6.3 created needs to become the one this
/// package models, as far as it is worth changing.
/// </summary>
/// <remarks>
/// <para>
/// Four kinds of difference, and the move treats each its own way. <b>Wrong</b>: what makes Quartz fail or read
/// slowly — the missing history indexes, and on SQL Server the <c>NOT NULL</c> string properties and the narrow
/// instance name. Repaired. <b>Different and harmless</b>: a nullability, a column type or a delete rule the
/// scripts declare otherwise. Converged where the database changes it in place, without rewriting or rebuilding
/// the table, and never by narrowing a column; left otherwise. <b>Spelling only</b>: <c>bigint(19)</c> for
/// <c>bigint</c> on MySQL, <c>text</c> for <c>NVARCHAR(n)</c> on SQLite. Left, because the database is the same
/// either way and respelling would rebuild tables. <b>Missing</b>: columns AppAny 0.6.3 never had, which 4.9
/// adds. Added, nullable, with the model's store type.
/// </para>
/// <para>
/// So the move rebuilds no table. Two convergences would, and are left: on SQL Server <c>MISFIRE_INSTR</c> from
/// <c>smallint</c> to <c>int</c> rewrites every row of <c>QRTZ_TRIGGERS</c> and rebuilds the acquisition index over
/// it, and on MySQL making <c>IS_NONCONCURRENT</c> nullable rebuilds <c>QRTZ_FIRED_TRIGGERS</c> in place (MySQL
/// refuses <c>ALGORITHM=INSTANT</c> for it). The store reads the first with a conversion and always writes the
/// second, so neither changes anything it does.
/// </para>
/// <para>
/// Only 0.6.3 is described. An earlier AppAny model has different indexes, and its users upgrade to 0.6.3 and
/// migrate before they move.
/// </para>
/// <para>
/// What 0.6.3 lacks is written out by name, here and in <see cref="MissingIndexes" />, never derived from what the
/// model marks as added by a release: see <see cref="MissingTables" />.
/// </para>
/// </remarks>
internal static class AppAnyMove
{
    /// <summary>The execution-history indexes AppAny 0.6.3 does not create, on every dialect.</summary>
    internal static readonly string[] MissingIndexes = ["EH_JOB_TIME", "EH_FIRE_INST", "EH_TRIG_TIME"];

    internal static readonly string[] MissingTables = ["TRIGGER_STATUS"];

    /// <summary>The columns AppAny 0.6.3 never had, on QRTZ_FIRED_TRIGGERS, which the move adds.</summary>
    internal static readonly string[] MissingColumns = ["OCCURRENCE_TIME", "RETRY_ATTEMPT"];

    /// <summary>A column AppAny declares otherwise, as AppAny declares it.</summary>
    internal sealed record AppAnyColumn(string Table, string Column, string StoreType, QuartzColumnKind Kind, bool Nullable);

    private static readonly string[] JobKey = ["SCHED_NAME", "JOB_NAME", "JOB_GROUP"];
    private static readonly string[] TriggerKey = ["SCHED_NAME", "TRIGGER_NAME", "TRIGGER_GROUP"];

    /// <summary>The foreign keys AppAny declares, every one of them cascading.</summary>
    internal static readonly (string Table, QuartzForeignKeyDefinition ForeignKey)[] AppAnyForeignKeys =
    [
        ("TRIGGERS", new(JobKey, "JOB_DETAILS", JobKey, Cascade: true)),
        ("SIMPLE_TRIGGERS", new(TriggerKey, "TRIGGERS", TriggerKey, Cascade: true)),
        ("SIMPROP_TRIGGERS", new(TriggerKey, "TRIGGERS", TriggerKey, Cascade: true)),
        ("CRON_TRIGGERS", new(TriggerKey, "TRIGGERS", TriggerKey, Cascade: true)),
        ("BLOB_TRIGGERS", new(TriggerKey, "TRIGGERS", TriggerKey, Cascade: true)),
    ];

    /// <summary>The columns the move changes, as AppAny declares them, per dialect.</summary>
    internal static AppAnyColumn[] ChangedColumns(QuartzDialect dialect)
    {
        if (dialect == QuartzDialect.SqlServer)
        {
            return
            [
                // Wrong: Quartz writes NULL to STR_PROP_2 and STR_PROP_3 for a calendar-interval trigger and for a
                // recurrence trigger, and the insert fails.
                new("SIMPROP_TRIGGERS", "STR_PROP_1", "nvarchar(512)", QuartzColumnKind.String, Nullable: false),
                new("SIMPROP_TRIGGERS", "STR_PROP_2", "nvarchar(512)", QuartzColumnKind.String, Nullable: false),
                new("SIMPROP_TRIGGERS", "STR_PROP_3", "nvarchar(512)", QuartzColumnKind.String, Nullable: false),
                // Wrong: an instance id of 151 to 200 characters, which QRTZ_SCHEDULER_STATE takes, fails here.
                new("FIRED_TRIGGERS", "INSTANCE_NAME", "nvarchar(150)", QuartzColumnKind.String, Nullable: false),
                // Harmless, and converged because SQL Server changes only the metadata to allow NULL.
                new("FIRED_TRIGGERS", "IS_NONCONCURRENT", "bit", QuartzColumnKind.Boolean, Nullable: false),
            ];
        }

        return [];
    }

    /// <summary>
    /// Whether the move gives the foreign keys the scripts' delete rules. A foreign key is dropped and added
    /// again to change one, which PostgreSQL and SQL Server do in place and MySQL and SQLite do by copying the
    /// table, so the latter two keep AppAny's cascades.
    /// </summary>
    internal static bool ConvergesForeignKeys(QuartzDialect dialect) =>
        dialect == QuartzDialect.PostgreSql || dialect == QuartzDialect.SqlServer;

    internal static void Add(MigrationBuilder migrationBuilder, QuartzDialect dialect, QuartzTableNaming naming)
    {
        foreach (AppAnyColumn appAny in ChangedColumns(dialect))
        {
            QuartzColumnDefinition ours = dialect.Table(appAny.Table).Columns.Single(c => c.Name == appAny.Column);
            AlterColumn(migrationBuilder, naming, appAny, ours);
        }

        if (ConvergesForeignKeys(dialect))
        {
            foreach ((string table, QuartzForeignKeyDefinition appAny) in AppAnyForeignKeys)
            {
                QuartzForeignKeyDefinition? ours = dialect.Table(table).ForeignKey;
                if (ours is not null && ours.Cascade == appAny.Cascade)
                {
                    continue;
                }

                migrationBuilder.DropForeignKey(naming.ForeignKeyName(table, appAny), naming.Table(table), naming.Schema);

                if (ours is not null)
                {
                    migrationBuilder.AddForeignKey(
                        name: naming.ForeignKeyName(table, ours),
                        table: naming.Table(table),
                        columns: [.. ours.Columns.Select(naming.Column)],
                        principalTable: naming.Table(ours.ReferencedTable),
                        schema: naming.Schema,
                        principalSchema: naming.Schema,
                        principalColumns: [.. ours.ReferencedColumns.Select(naming.Column)],
                        onDelete: ours.Cascade ? ReferentialAction.Cascade : ReferentialAction.NoAction);
                }
            }
        }

        foreach (string table in MissingTables)
        {
            CreateTable(migrationBuilder, naming, dialect.Table(table));
        }

        foreach (QuartzTableDefinition table in dialect.Tables)
        {
            foreach (QuartzIndexDefinition index in table.Indexes.Where(i => MissingIndexes.Contains(i.Suffix)))
            {
                migrationBuilder.CreateIndex(
                    name: naming.Index(index.Suffix),
                    table: naming.Table(table.Name),
                    columns: [.. index.Columns.Select(naming.Column)],
                    schema: naming.Schema,
                    descending: index.Descending);
            }
        }

        // AppAny 0.6.3 never had these columns, and 4.9 adds them. They are named here rather than
        // derived from AddedBy: the move runs again whenever a fresh database applies an application's
        // migrations from the start, and a derived list would also add a column a later release brings,
        // which that release's own migration then adds a second time.
        foreach (QuartzColumnDefinition column in dialect.Table("FIRED_TRIGGERS").Columns.Where(c => MissingColumns.Contains(c.Name)))
        {
            AddColumn(migrationBuilder, naming, column);
        }
    }

    private static void AddColumn(MigrationBuilder migrationBuilder, QuartzTableNaming naming, QuartzColumnDefinition column)
    {
        string name = naming.Column(column.Name);
        string table = naming.Table("FIRED_TRIGGERS");
        _ = column.Kind switch
        {
            QuartzColumnKind.Int64 => migrationBuilder.AddColumn<long>(name, table, column.StoreType, schema: naming.Schema, nullable: true),
            QuartzColumnKind.Int32 => migrationBuilder.AddColumn<int>(name, table, column.StoreType, schema: naming.Schema, nullable: true),
            _ => throw new InvalidOperationException($"FIRED_TRIGGERS.{column.Name} is a {column.Kind} column, which the move has no add for"),
        };
    }

    private static void AlterColumn(MigrationBuilder migrationBuilder, QuartzTableNaming naming, AppAnyColumn appAny, QuartzColumnDefinition ours)
    {
        string name = naming.Column(ours.Name);
        string table = naming.Table(appAny.Table);
        Type oldClrType = ClrType(appAny.Kind);

        _ = ours.Kind switch
        {
            QuartzColumnKind.String => migrationBuilder.AlterColumn<string>(name, table, ours.StoreType, schema: naming.Schema,
                nullable: ours.Nullable, oldClrType: oldClrType, oldType: appAny.StoreType, oldNullable: appAny.Nullable),
            QuartzColumnKind.Boolean => migrationBuilder.AlterColumn<bool>(name, table, ours.StoreType, schema: naming.Schema,
                nullable: ours.Nullable, oldClrType: oldClrType, oldType: appAny.StoreType, oldNullable: appAny.Nullable),
            _ => throw new InvalidOperationException($"{appAny.Table}.{appAny.Column} is a {ours.Kind} column, which the move has no change for"),
        };
    }

    /// <summary>
    /// Creates a table AppAny lacks as the model declares it, its primary key named as EF Core names one it is not
    /// told the name of, or as the script names it where the model says.
    /// </summary>
    private static void CreateTable(MigrationBuilder migrationBuilder, QuartzTableNaming naming, QuartzTableDefinition table)
    {
        string name = naming.Table(table.Name);
        CreateTableOperation operation = new()
        {
            Name = name,
            Schema = naming.Schema,
            PrimaryKey = new AddPrimaryKeyOperation
            {
                Name = table.PrimaryKeyName is { } primaryKeyName ? naming.Constraint(primaryKeyName) : "PK_" + name,
                Table = name,
                Schema = naming.Schema,
                Columns = [.. table.PrimaryKey.Select(naming.Column)],
            },
        };

        foreach (QuartzColumnDefinition column in table.Columns)
        {
            operation.Columns.Add(new AddColumnOperation
            {
                Name = naming.Column(column.Name),
                Table = name,
                Schema = naming.Schema,
                ClrType = ClrType(column.Kind),
                ColumnType = column.StoreType,
                IsNullable = column.Nullable,
                DefaultValue = column.DefaultValue,
            });
        }

        migrationBuilder.Operations.Add(operation);
    }

    private static Type ClrType(QuartzColumnKind kind) => kind switch
    {
        QuartzColumnKind.String => typeof(string),
        QuartzColumnKind.Int16 => typeof(short),
        QuartzColumnKind.Int32 => typeof(int),
        QuartzColumnKind.Int64 => typeof(long),
        QuartzColumnKind.Boolean => typeof(bool),
        QuartzColumnKind.Decimal => typeof(decimal),
        QuartzColumnKind.Binary => typeof(byte[]),
        _ => throw new InvalidOperationException($"the move has no CLR type for a column of kind {kind}"),
    };
}
