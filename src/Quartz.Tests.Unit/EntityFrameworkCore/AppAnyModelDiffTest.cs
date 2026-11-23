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

using System.Text;

using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.MySql;
using AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL;
using AppAny.Quartz.EntityFrameworkCore.Migrations.SQLite;
using AppAny.Quartz.EntityFrameworkCore.Migrations.SqlServer;

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Quartz.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// What an application's <c>dotnet ef migrations add</c> finds between AppAny.Quartz.EntityFrameworkCore.Migrations
/// 0.6.3's model and this package's, per provider, and what <c>MoveQuartzTablesFromAppAny</c> does about each
/// difference: the list the documentation quotes.
/// </summary>
/// <remarks>
/// <para>
/// Each difference is one of three. <b>Repaired</b>: AppAny's schema has it wrong, and the move changes it.
/// <b>Converged</b>: harmless, but the database changes it in place, rebuilding no table and narrowing no column,
/// and the move changes it. <b>Left</b>: harmless, a spelling of the same type, or a change that would narrow a
/// column or rewrite or rebuild a table, and the move leaves the database as it is; the snapshot holds this
/// package's model from the move on, so no later migration sees it again.
/// </para>
/// <para>
/// The move is written by hand, so this is also what keeps it honest: every operation it makes is one the differ
/// finds, and nothing it does is unlabelled.
/// </para>
/// </remarks>
public sealed class AppAnyModelDiffTest
{
    private static IEnumerable<string> Dialects() => EntityFrameworkCoreContext.Dialects;

    [TestCaseSource(nameof(Dialects))]
    public async Task AppAnysModelBecomesThisPackagesThus(string dialect)
    {
        // AppAny's defaults, which an application moving from it keeps: schema quartz on PostgreSQL and SQL Server,
        // and the default prefix, which AppAny spells qrtz_ on PostgreSQL and PostgreSQL folds the store's to.
        Action<QuartzTablesOptions>? configure = dialect is "postgres" or "sqlServer" ? t => t.Schema = "quartz" : null;

        await using EntityFrameworkCoreContext appAny = EntityFrameworkCoreContext.For(dialect, (model, database) => model.AddQuartz(q => _ = dialect switch
        {
            "postgres" => q.UsePostgreSql(),
            "sqlServer" => q.UseSqlServer(),
            "mysql_innodb" => q.UseMySql(),
            _ => q.UseSqlite(),
        }));
        await using EntityFrameworkCoreContext ours = EntityFrameworkCoreContext.Quartz(dialect, configure);

        List<string> differences = [.. ours.DifferencesFrom(appAny).Select(MigrationOperationText.Describe)];
        IReadOnlyList<MigrationOperation> move = ours.Migration(m => m.MoveQuartzTablesFromAppAny(configure));
        List<string> moved = [.. move.Where(o => o is not SqlOperation).Select(MigrationOperationText.Describe)];

        moved.Should().OnlyHaveUniqueItems().And.BeSubsetOf(differences,
            "the move changes only what the differ finds between AppAny's model and this one, so that after it the "
            + "database is what the snapshot then says, as far as the move goes");

        StringBuilder text = new();
        text.Append("AppAny.Quartz.EntityFrameworkCore.Migrations 0.6.3 to Quartz.EntityFrameworkCore, ").Append(dialect).Append("\n");
        Section(text, "repaired: AppAny's schema has it wrong, and the move changes it",
            differences.Where(d => moved.Contains(d) && IsRepair(d)));
        Section(text, "converged: harmless, changed in place with no table rebuilt and no column narrowed",
            differences.Where(d => moved.Contains(d) && !IsRepair(d)));
        Section(text, "left: harmless, a spelling of the same type, or a change that would narrow a column or rebuild a table",
            differences.Where(d => !moved.Contains(d)));
        Section(text, "completed: CompleteQuartzSchema, which the move ends with",
            move.OfType<SqlOperation>().Select(MigrationOperationText.Describe));

        await Verify(text.ToString(), extension: "txt")
            .UseDirectory("../Verify")
            .UseFileName($"AppAnyModelDiffTest_{dialect}");
    }

    /// <summary>
    /// What AppAny 0.6.3's schema has wrong: three history indexes it never creates, which the history's reads by
    /// job, by trigger and by firing scan without; and on SQL Server the NOT NULL string properties Quartz writes
    /// NULL to for a calendar-interval or recurrence trigger, and an instance name narrower than the one the
    /// scheduler state takes.
    /// </summary>
    private static bool IsRepair(string difference) =>
        difference.StartsWith("CreateIndex ", StringComparison.Ordinal)
        || difference.Contains(".STR_PROP_", StringComparison.Ordinal)
        || difference.Contains("QRTZ_FIRED_TRIGGERS.INSTANCE_NAME ", StringComparison.Ordinal);

    private static void Section(StringBuilder text, string heading, IEnumerable<string> lines)
    {
        text.Append('\n').Append(heading).Append('\n');
        List<string> sorted = [.. lines.Order(StringComparer.Ordinal)];
        foreach (string line in sorted.Count == 0 ? ["(none)"] : sorted)
        {
            text.Append("  ").Append(line).Append('\n');
        }
    }
}
