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

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Quartz.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// The SQL the two <c>MigrationBuilder</c> extensions add, as each provider writes it.
/// </summary>
public sealed class QuartzMigrationBuilderTest
{
    private static IEnumerable<string> Dialects() => EntityFrameworkCoreContext.Dialects;

    /// <summary>
    /// <c>CompleteQuartzSchema</c> and <c>MoveQuartzTablesFromAppAny</c>, statement by statement, under the default
    /// prefix and the schema AppAny's users have on the two databases that have one.
    /// </summary>
    [TestCaseSource(nameof(Dialects))]
    public async Task TheMigrationExtensionsWriteThisSql(string dialect)
    {
        Action<QuartzTablesOptions>? configure = dialect is "postgres" or "sqlServer" ? t => t.Schema = "quartz" : null;
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect, configure);

        List<string> complete = context.Sql(context.Migration(m => m.CompleteQuartzSchema(configure)));
        List<string> move = context.Sql(context.Migration(m => m.MoveQuartzTablesFromAppAny(configure)));

        await Verify(
                "-- CompleteQuartzSchema\n\n" + string.Join("\n\n", complete)
                + "\n\n-- MoveQuartzTablesFromAppAny\n\n" + string.Join("\n\n", move),
                extension: "sql")
            .UseDirectory("../Verify")
            .UseFileName($"QuartzMigrationBuilderTest_{dialect}");
    }

    [Test]
    public async Task SqliteDeleteTriggersCarryThePrefix()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("sqlite");

        List<string> sql = context.Sql(context.Migration(m => m.CompleteQuartzSchema(t => t.TablePrefix = "QRTZX_")));

        sql.Should().HaveCount(4).And.AllSatisfy(s => s.Should().StartWith("CREATE TRIGGER IF NOT EXISTS QRTZX_DELETE_")
            .And.Contain("DELETE ON QRTZX_TRIGGERS"));
    }

    [Test]
    public async Task TheMoveNamesTheSchemaOnEveryOperation()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("postgres", t => t.Schema = "quartz");

        IReadOnlyList<MigrationOperation> operations = context.Migration(m => m.MoveQuartzTablesFromAppAny(t => t.Schema = "Quartz"));

        operations.Should().NotBeEmpty();
        operations.OfType<TableOperation>().Should().AllSatisfy(o => o.Schema.Should().Be("quartz"));
        operations.OfType<CreateIndexOperation>().Should().AllSatisfy(o => o.Schema.Should().Be("quartz"));
    }

    [Test]
    public void AMigrationForAProviderQuartzHasNoModelForIsRefused()
    {
        MigrationBuilder migrationBuilder = new("Oracle.EntityFrameworkCore");

        Action complete = () => migrationBuilder.CompleteQuartzSchema();
        Action move = () => migrationBuilder.MoveQuartzTablesFromAppAny();

        complete.Should().Throw<NotSupportedException>().WithMessage("*'Oracle.EntityFrameworkCore'*");
        move.Should().Throw<NotSupportedException>().WithMessage("*'Oracle.EntityFrameworkCore'*");
    }
}
