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

namespace Quartz.EntityFrameworkCore;

/// <summary>
/// What a schema <c>AppAny.Quartz.EntityFrameworkCore.Migrations</c> 0.6.3 created needs to become the one this
/// package models, as far as it is worth changing.
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of difference, and the move treats each its own way. <b>Wrong</b>: what makes Quartz fail or read
/// slowly — the missing history indexes, and on SQL Server the <c>NOT NULL</c> string properties and the narrow
/// instance name. Repaired. <b>Different and harmless</b>: a nullability, a column type or a delete rule the
/// scripts declare otherwise. Converged where the change is cheap and never narrows a column, left otherwise.
/// <b>Spelling only</b>: <c>bigint(19)</c> for <c>bigint</c> on MySQL, <c>text</c> for <c>NVARCHAR(n)</c> on
/// SQLite. Left, because the database is the same either way and respelling would rebuild tables.
/// </para>
/// <para>
/// Only 0.6.3 is described. An earlier AppAny model has different indexes, and its users upgrade to 0.6.3 and
/// migrate before they move.
/// </para>
/// </remarks>
internal static class AppAnyMove
{
    /// <summary>The execution-history indexes AppAny 0.6.3 does not create, on every dialect.</summary>
    internal static readonly string[] MissingIndexes = ["EH_JOB_TIME", "EH_FIRE_INST", "EH_TRIG_TIME"];

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
                // Harmless, and cheap to converge: a metadata change, and a widening.
                new("FIRED_TRIGGERS", "IS_NONCONCURRENT", "bit", QuartzColumnKind.Boolean, Nullable: false),
                new("TRIGGERS", "MISFIRE_INSTR", "smallint", QuartzColumnKind.Int16, Nullable: true),
            ];
        }

        if (dialect == QuartzDialect.MySql)
        {
            return [new("FIRED_TRIGGERS", "IS_NONCONCURRENT", "tinyint(1)", QuartzColumnKind.Boolean, Nullable: false)];
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
            QuartzColumnKind.Int32 => migrationBuilder.AlterColumn<int>(name, table, ours.StoreType, schema: naming.Schema,
                nullable: ours.Nullable, oldClrType: oldClrType, oldType: appAny.StoreType, oldNullable: appAny.Nullable),
            _ => throw new InvalidOperationException($"{appAny.Table}.{appAny.Column} is a {ours.Kind} column, which the move has no change for"),
        };
    }

    private static Type ClrType(QuartzColumnKind kind) => kind switch
    {
        QuartzColumnKind.String => typeof(string),
        QuartzColumnKind.Boolean => typeof(bool),
        QuartzColumnKind.Int16 => typeof(short),
        QuartzColumnKind.Int32 => typeof(int),
        _ => throw new InvalidOperationException($"the move describes no AppAny column of kind {kind}"),
    };
}
