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
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

using Quartz.EntityFrameworkCore;

namespace Quartz.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// What the tables are called, and what <c>AddQuartzTables</c> refuses.
/// </summary>
public sealed class QuartzTableNamingTest
{
    private static IEnumerable<string> Dialects() => EntityFrameworkCoreContext.Dialects;

    [Test]
    public async Task APrefixAndASchemaNameEveryTableAndIndex()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("sqlServer", t =>
        {
            t.TablePrefix = "APP_";
            t.Schema = "jobs";
        });

        IRelationalModel model = context.DesignModel.GetRelationalModel();

        model.Tables.Should().HaveCount(15).And.AllSatisfy(t =>
        {
            t.Schema.Should().Be("jobs");
            t.Name.Should().StartWith("APP_");
        });
        model.Tables.SelectMany(t => t.Indexes).Select(i => i.Name).Should().Contain("IDX_APP_T_NFT_ST")
            .And.AllSatisfy(n => n.Should().StartWith("IDX_APP_"), "an index is named IDX_, the prefix and its suffix, as the scripts name it");
    }

    [Test]
    public async Task PostgreSqlFoldsEveryNameToLowerCaseAsItFoldsTheStoresUnquotedOnes()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("postgres", t =>
        {
            t.TablePrefix = "MyApp_";
            t.Schema = "Quartz";
        });

        IRelationalModel model = context.DesignModel.GetRelationalModel();

        ITable triggers = model.FindTable("myapp_triggers", "quartz")!;
        triggers.Should().NotBeNull("PostgreSQL reads the store's unquoted MyApp_TRIGGERS as myapp_triggers, and EF Core quotes what it is given");
        triggers.Columns.Select(c => c.Name).Should().Contain("sched_name").And.OnlyContain(n => n == n.ToLowerInvariant());
        triggers.Indexes.Select(i => i.Name).Should().Contain("idx_myapp_t_nft_st");
    }

    [Test]
    public async Task TwoPrefixesShareOneModel()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.For("sqlite", (model, database) =>
        {
            model.AddQuartzTables(database.ProviderName);
            model.AddQuartzTables(database.ProviderName, t => t.TablePrefix = "QRTZ2_");
        });

        context.DesignModel.GetRelationalModel().Tables.Should().HaveCount(30,
            "two schedulers' stores can keep their tables in one database under two prefixes, and so in one model");
    }

    private static IEnumerable<string> AppAnyDialects() => EntityFrameworkCoreContext.AppAnyDialects;

    /// <summary>
    /// The name <see cref="QuartzTableNaming.ForeignKeyName" /> says EF Core gives a foreign key is the one EF Core
    /// gives it, cut where the provider cuts it, which is what the move from AppAny drops a key by.
    /// </summary>
    [TestCaseSource(nameof(AppAnyDialects))]
    public async Task AForeignKeyIsNamedAsEntityFrameworkCoreNamesIt(string dialect)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect);

        QuartzDialect quartz = QuartzDialect.ForProvider(context.Database.ProviderName);
        QuartzTableNaming naming = QuartzTableNaming.Create(quartz, null);
        IRelationalModel model = context.DesignModel.GetRelationalModel();

        foreach (QuartzTableDefinition table in quartz.Tables.Where(t => t.ForeignKey is not null))
        {
            model.FindTable(naming.Table(table.Name), null)!.ForeignKeyConstraints.Select(f => f.Name)
                .Should().Equal([naming.ForeignKeyName(table.Name, table.ForeignKey!)]);
        }
    }

    /// <summary>
    /// On Oracle and Firebird every primary and foreign key is named as <c>create_&lt;dialect&gt;.sql</c> names it:
    /// Oracle's abbreviated stems, Firebird's numbered foreign keys.
    /// </summary>
    [TestCase("oracle")]
    [TestCase("firebird")]
    public async Task OracleAndFirebirdKeysAreNamedAsTheScriptsNameThem(string dialect)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect);

        IRelationalModel model = context.DesignModel.GetRelationalModel();
        IEnumerable<string> ours = model.Tables.Select(t => t.PrimaryKey!.Name)
            .Concat(model.Tables.SelectMany(t => t.ForeignKeyConstraints.Select(f => f.Name)));

        IEnumerable<string> script = ScriptConstraint.Matches(Impl.AdoJobStore.SchemaScriptTest.GeneratedScript(dialect))
            .Select(m => m.Groups["name"].Value.Replace("{1}", "QRTZ_", StringComparison.Ordinal));

        ours.Should().BeEquivalentTo(script);
    }

    private static readonly System.Text.RegularExpressions.Regex ScriptConstraint = new(
        @"CONSTRAINT (?<name>\{1\}\w+|\w+_\{1\}\w+) (?:PRIMARY|FOREIGN) KEY",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Oracle and Firebird fold the store's unquoted identifiers to upper case, and their providers quote every
    /// name, so a lower-case prefix still names the tables the store finds.
    /// </summary>
    [TestCase("oracle")]
    [TestCase("firebird")]
    public async Task OracleAndFirebirdFoldEveryNameToUpperCase(string dialect)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect, t => t.TablePrefix = "qrtz_");

        IRelationalModel model = context.DesignModel.GetRelationalModel();

        model.Tables.Select(t => t.Name).Should().Contain("QRTZ_TRIGGERS").And.OnlyContain(n => n == n.ToUpperInvariant());
        model.Tables.SelectMany(t => t.Indexes).Select(i => i.Name).Should().Contain("IDX_QRTZ_T_NFT_ST");
        context.Database.GenerateCreateScript().Should().Contain("\"QRTZ_TRIGGERS\"", "both providers quote every identifier they write");
    }

    /// <summary>
    /// Every name the model gives on Firebird fits Firebird 3's 31 bytes under the default prefix, and the
    /// longest, <c>IDX_QRTZ_FT_INST_JOB_REQ_RCVRY</c>, leaves room for a prefix one character longer.
    /// </summary>
    [Test]
    public async Task EveryFirebirdNameFits31BytesUnderTheDefaultPrefix()
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("firebird");

        IRelationalModel model = context.DesignModel.GetRelationalModel();
        IEnumerable<string> names = model.Tables.Select(t => t.Name)
            .Concat(model.Tables.SelectMany(t => t.Columns.Select(c => c.Name)))
            .Concat(model.Tables.SelectMany(t => t.Indexes.Select(i => i.Name!)))
            .Concat(model.Tables.Select(t => t.PrimaryKey!.Name))
            .Concat(model.Tables.SelectMany(t => t.ForeignKeyConstraints.Select(f => f.Name)));

        names.Should().AllSatisfy(n => System.Text.Encoding.UTF8.GetByteCount(n).Should().BeLessThanOrEqualTo(31, n));
        names.Max(n => n.Length).Should().Be(30, "IDX_QRTZ_FT_INST_JOB_REQ_RCVRY is the longest");
    }

    /// <summary>
    /// A prefix that makes a name longer than the database takes is refused before anything runs, naming the name
    /// and the longest prefix that fits, in bytes, and in characters where the two differ: EF Core passes a name it
    /// is given through, PostgreSQL would cut it silently, and Firebird refuse it half-way through a migration.
    /// </summary>
    [TestCase("FirebirdSql.EntityFrameworkCore.Firebird", "QRTZ_X_", "*IDX_QRTZ_X_FT_INST_JOB_REQ_RCVRY 32 bytes long, and Firebird takes 31 bytes (Firebird 3's limit*at most 6 bytes of UTF-8, which is 6 characters of ASCII.*")]
    [TestCase("FirebirdSql.EntityFrameworkCore.Firebird", "QRTZÄ_", "*IDX_QRTZÄ_FT_INST_JOB_REQ_RCVRY 32 bytes long*at most 6 bytes*('QRTZÄ_' is 6 characters and 7 bytes).*")]
    [TestCase("Npgsql.EntityFrameworkCore.PostgreSQL", "P234567890123456789012345678901234567890_", "*66 bytes long, and PostgreSQL takes 63 bytes. Use a prefix of at most 38 bytes of UTF-8*")]
    public void APrefixThatMakesANameTooLongIsRefused(string provider, string prefix, string message)
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables(provider, t => t.TablePrefix = prefix);

        add.Should().Throw<ArgumentException>().WithParameterName("configure").WithMessage(message);
    }

    [Test]
    public void AProviderQuartzHasNoModelForIsRefusedWithTheOnesItHas()
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables("Microsoft.EntityFrameworkCore.InMemory");

        add.Should().Throw<NotSupportedException>()
            .WithMessage("*'Microsoft.EntityFrameworkCore.InMemory'*Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.SqlServer, "
                         + "MySql.EntityFrameworkCore, Pomelo.EntityFrameworkCore.MySql, Microsoft.EntityFrameworkCore.Sqlite, "
                         + "Oracle.EntityFrameworkCore, FirebirdSql.EntityFrameworkCore.Firebird*");
    }

    [Test]
    public void NoProviderIsRefused()
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables(null);

        add.Should().Throw<ArgumentNullException>().WithParameterName("providerName").WithMessage("*Database.ProviderName*");
    }

    /// <summary>
    /// A prefix is what an unquoted identifier can hold: the store writes it into its SQL unquoted, and so does
    /// <c>CompleteQuartzSchema</c> into SQLite's triggers, so a schema, a quote or a statement separator in it is
    /// refused rather than written there.
    /// </summary>
    [TestCase("quartz.QRTZ_")]
    [TestCase("[quartz].QRTZ_")]
    [TestCase("\"QRTZ_\"")]
    [TestCase("QRTZ_;")]
    [TestCase("QRTZ-")]
    [TestCase("QRTZ/*")]
    [TestCase("QRTZ ")]
    [TestCase(null)]
    public void APrefixAnUnquotedIdentifierCannotHoldIsRefused(string? prefix)
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables("Microsoft.EntityFrameworkCore.Sqlite", t => t.TablePrefix = prefix!);
        Action complete = () => new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite").CompleteQuartzSchema(t => t.TablePrefix = prefix!);

        add.Should().Throw<ArgumentException>().WithParameterName("configure")
            .WithMessage("*letters, digits, '_', '$' and '#'*put the schema in QuartzTablesOptions.Schema*");
        complete.Should().Throw<ArgumentException>().WithParameterName("configure");
    }

    [TestCase("")]
    [TestCase("QRTZ_")]
    [TestCase("App2$#_")]
    [TestCase("Quarz_Ä_")]
    public async Task APrefixOfIdentifierCharactersNamesTheTables(string prefix)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("sqlite", t => t.TablePrefix = prefix);

        context.DesignModel.GetRelationalModel().Tables.Select(t => t.Name).Should().Contain(prefix + "TRIGGERS");
    }

    [TestCase("MySql.EntityFrameworkCore")]
    [TestCase("Microsoft.EntityFrameworkCore.Sqlite")]
    [TestCase("Oracle.EntityFrameworkCore")]
    [TestCase("FirebirdSql.EntityFrameworkCore.Firebird")]
    public void ASchemaIsRefusedWhereTheDatabaseHasNone(string provider)
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables(provider, t => t.Schema = "quartz");

        add.Should().Throw<ArgumentException>().WithParameterName("configure").WithMessage("*has no schema*");
    }

    [Test]
    public void PomeloSpeaksMySql()
    {
        QuartzDialect.ForProvider("Pomelo.EntityFrameworkCore.MySql").Should().BeSameAs(QuartzDialect.MySql);
    }
}
