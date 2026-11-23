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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Quartz.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// What Oracle's and Firebird's EF Core providers do that the model and the documentation rely on, shown on the
/// providers themselves rather than taken on trust.
/// </summary>
public sealed class OracleAndFirebirdProviderTest
{
    /// <summary>
    /// Oracle's provider takes no lock around <c>Migrate()</c>: its lock is handed back without the database being
    /// asked, so a server that is not there is never noticed. SQL Server's, the control, goes to the server for
    /// its application lock.
    /// </summary>
    [Test]
    public async Task OraclesMigrationLockTakesNoLock()
    {
        await using EntityFrameworkCoreContext oracle = EntityFrameworkCoreContext.For("oracle", (_, _) => { },
            "User Id=nobody;Password=nothing;Data Source=127.0.0.1:1/NOWHERE;Connection Timeout=1");
        await using EntityFrameworkCoreContext sqlServer = EntityFrameworkCoreContext.For("sqlServer", (_, _) => { },
            "Server=127.0.0.1,1;Database=nowhere;Connect Timeout=1;TrustServerCertificate=True");

        Func<Task> lockOracle = async () => await (await oracle.GetService<IHistoryRepository>().AcquireDatabaseLockAsync()).DisposeAsync();
        Func<Task> lockSqlServer = async () => await (await sqlServer.GetService<IHistoryRepository>().AcquireDatabaseLockAsync()).DisposeAsync();

        await lockOracle.Should().NotThrowAsync("Oracle's lock does nothing, so it never reaches the server");
        await lockSqlServer.Should().ThrowAsync<Exception>("the control: SQL Server's lock is sp_getapplock, on the server");
    }

    /// <summary>
    /// Firebird's provider cannot write an idempotent script: <c>dotnet ef migrations script --idempotent</c> fails.
    /// </summary>
    [Test]
    public async Task FirebirdWritesNoIdempotentScript()
    {
        await using EntityFrameworkCoreContext firebird = EntityFrameworkCoreContext.Quartz("firebird");

        Action script = () => firebird.GetService<IHistoryRepository>().GetBeginIfNotExistsScript("20261123000000_AddQuartz");

        script.Should().Throw<NotSupportedException>();
    }

    /// <summary>
    /// Firebird's provider refuses an index whose columns go in different directions, which is why the Firebird
    /// model keeps the acquisition index without them, as Firebird's script does.
    /// </summary>
    [Test]
    public async Task FirebirdRefusesAnIndexOfMixedDirections()
    {
        await using EntityFrameworkCoreContext mixed = EntityFrameworkCoreContext.For("firebird", (model, _) =>
            model.SharedTypeEntity<Dictionary<string, object>>("Mixed", b =>
            {
                b.ToTable("MIXED");
                b.IndexerProperty<int>("A").HasColumnType("INTEGER");
                b.IndexerProperty<int>("B").HasColumnType("INTEGER");
                b.HasKey("A");
                b.HasIndex("A", "B").HasDatabaseName("IDX_MIXED").IsDescending(false, true);
            }));

        Action script = () => mixed.Database.GenerateCreateScript();

        script.Should().Throw<NotSupportedException>().WithMessage("*Mixed order indices are not supported by Firebird*");

        await using EntityFrameworkCoreContext quartz = EntityFrameworkCoreContext.Quartz("firebird");
        quartz.Invoking(c => c.Database.GenerateCreateScript()).Should().NotThrow("Quartz's Firebird indexes have one direction each");
    }

    /// <summary>
    /// Firebird's provider cuts only the names it makes up at 31 characters: one it is given goes into the DDL as
    /// it is, however long, which is why the package checks the names it gives.
    /// </summary>
    [Test]
    public async Task FirebirdPassesAGivenNameThroughUnchecked()
    {
        const string longName = "IDX_A_NAME_OF_FORTY_CHARACTERS_ON_PURPOSE";
        longName.Length.Should().Be(41, "the premise");

        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.For("firebird", (model, _) =>
            model.SharedTypeEntity<Dictionary<string, object>>("Long", b =>
            {
                b.ToTable("LONG_NAMES");
                b.IndexerProperty<int>("A").HasColumnType("INTEGER");
                b.IndexerProperty<int>("B").HasColumnType("INTEGER");
                b.HasKey("A");
                b.HasIndex("B").HasDatabaseName(longName);
            }));

        context.Database.GenerateCreateScript().Should().Contain($"\"{longName}\"");
    }

    /// <summary>
    /// Oracle's provider gives a <c>bool</c> the 23ai <c>BOOLEAN</c> type unless it is told one, which Oracle 19c
    /// and 21c do not have; the model therefore says <c>VARCHAR2(1)</c>, as the script does.
    /// </summary>
    [Test]
    public async Task OracleGivesABoolBooleanUnlessToldOtherwise()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.For("oracle", (model, _) =>
            model.SharedTypeEntity<Dictionary<string, object>>("Flags", b =>
            {
                b.ToTable("FLAGS");
                b.IndexerProperty<int>("ID").HasColumnType("NUMBER(10)");
                b.IndexerProperty<bool>("UNTOLD");
                b.IndexerProperty<bool>("TOLD").HasColumnType("VARCHAR2(1)");
                b.HasKey("ID");
            }));

        string script = context.Database.GenerateCreateScript();

        script.Should().Contain("\"UNTOLD\" BOOLEAN").And.Contain("\"TOLD\" VARCHAR2(1)");

        await using EntityFrameworkCoreContext quartz = EntityFrameworkCoreContext.Quartz("oracle");
        quartz.Database.GenerateCreateScript().Should().NotContain("BOOLEAN");
    }
}
