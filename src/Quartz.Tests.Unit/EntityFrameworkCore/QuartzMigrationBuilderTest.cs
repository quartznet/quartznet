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
    /// prefix and the schema AppAny's users have on the two databases that have one. On Oracle and Firebird,
    /// where AppAny has no model, the move's refusal.
    /// </summary>
    [TestCaseSource(nameof(Dialects))]
    public async Task TheMigrationExtensionsWriteThisSql(string dialect)
    {
        Action<QuartzTablesOptions>? configure = dialect is "postgres" or "sqlServer" ? t => t.Schema = "quartz" : null;
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect, configure);

        List<string> complete = context.Sql(context.Migration(m => m.CompleteQuartzSchema(configure)));
        string move = EntityFrameworkCoreContext.AppAnyDialects.Contains(dialect)
            ? string.Join("\n\n", context.Sql(context.Migration(m => m.MoveQuartzTablesFromAppAny(configure))))
            : "-- refused: " + Refusal(() => context.Migration(m => m.MoveQuartzTablesFromAppAny(configure)));

        await Verify(
                "-- CompleteQuartzSchema\n\n" + (complete.Count == 0 ? "-- nothing" : string.Join("\n\n", complete))
                + "\n\n-- MoveQuartzTablesFromAppAny\n\n" + move,
                extension: "sql")
            .UseDirectory("../Verify")
            .UseFileName($"QuartzMigrationBuilderTest_{dialect}");
    }

    /// <summary>
    /// AppAny has no Oracle or Firebird model, so there is no schema of its to move there: the move says so rather
    /// than doing nothing in silence.
    /// </summary>
    [TestCase("Oracle.EntityFrameworkCore", "Oracle")]
    [TestCase("FirebirdSql.EntityFrameworkCore.Firebird", "Firebird")]
    public void TheMoveFromAppAnyIsRefusedWhereAppAnyHasNoModel(string provider, string database)
    {
        Action move = () => new MigrationBuilder(provider).MoveQuartzTablesFromAppAny();

        move.Should().Throw<NotSupportedException>()
            .WithMessage($"AppAny.Quartz.EntityFrameworkCore.Migrations has no {database} model*CompleteQuartzSchema()*");
    }

    private static string Refusal(Action action)
    {
        try
        {
            action();
        }
        catch (NotSupportedException exception)
        {
            return exception.Message;
        }

        throw new AssertionException("the move was expected to refuse");
    }

    [Test]
    public async Task SqliteDeleteTriggersCarryThePrefix()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("sqlite");

        List<string> sql = context.Sql(context.Migration(m => m.CompleteQuartzSchema(t => t.TablePrefix = "QRTZX_")));

        sql.Should().HaveCount(4).And.AllSatisfy(s => s.Should().StartWith("CREATE TRIGGER IF NOT EXISTS QRTZX_DELETE_")
            .And.Contain("DELETE ON QRTZX_TRIGGERS"));
    }

    /// <summary>
    /// Every operation of the move is on a table of the configured schema, and a foreign key it adds references
    /// one there too: folded on PostgreSQL, as written on SQL Server.
    /// </summary>
    [TestCase("postgres", "quartz")]
    [TestCase("sqlServer", "Quartz")]
    public async Task TheMoveNamesTheSchemaOnEveryOperation(string dialect, string schema)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect);

        IReadOnlyList<MigrationOperation> operations = context.Migration(m => m.MoveQuartzTablesFromAppAny(t => t.Schema = "Quartz"));

        operations.OfType<ITableMigrationOperation>().Should().NotBeEmpty()
            .And.HaveCount(operations.Count, "every operation the move makes on these two databases is on a table")
            .And.AllSatisfy(o => o.Schema.Should().Be(schema));
        operations.OfType<AddForeignKeyOperation>().Should().NotBeEmpty()
            .And.AllSatisfy(o => o.PrincipalSchema.Should().Be(schema));
    }

    [Test]
    public void AMigrationForAProviderQuartzHasNoModelForIsRefused()
    {
        MigrationBuilder migrationBuilder = new("Microsoft.EntityFrameworkCore.InMemory");

        Action complete = () => migrationBuilder.CompleteQuartzSchema();
        Action move = () => migrationBuilder.MoveQuartzTablesFromAppAny();

        complete.Should().Throw<NotSupportedException>().WithMessage("*'Microsoft.EntityFrameworkCore.InMemory'*");
        move.Should().Throw<NotSupportedException>().WithMessage("*'Microsoft.EntityFrameworkCore.InMemory'*");
    }
}
