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

    /// <summary>
    /// The name <see cref="QuartzTableNaming.ForeignKeyName" /> says EF Core gives a foreign key is the one EF Core
    /// gives it, cut where the provider cuts it, which is what the move from AppAny drops a key by.
    /// </summary>
    [TestCaseSource(nameof(Dialects))]
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

    [Test]
    public void AProviderQuartzHasNoModelForIsRefusedWithTheOnesItHas()
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables("Oracle.EntityFrameworkCore");

        add.Should().Throw<NotSupportedException>()
            .WithMessage("*'Oracle.EntityFrameworkCore'*Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.SqlServer, "
                         + "MySql.EntityFrameworkCore, Pomelo.EntityFrameworkCore.MySql, Microsoft.EntityFrameworkCore.Sqlite*");
    }

    [Test]
    public void NoProviderIsRefused()
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables(null);

        add.Should().Throw<ArgumentNullException>().WithParameterName("providerName").WithMessage("*Database.ProviderName*");
    }

    [TestCase("quartz.QRTZ_")]
    [TestCase("[quartz].QRTZ_")]
    [TestCase("\"QRTZ_\"")]
    public void APrefixWithASchemaInItIsRefused(string prefix)
    {
        ModelBuilder modelBuilder = new();

        Action add = () => modelBuilder.AddQuartzTables("Microsoft.EntityFrameworkCore.SqlServer", t => t.TablePrefix = prefix);

        add.Should().Throw<ArgumentException>().WithParameterName("configure")
            .WithMessage("*put the schema in QuartzTablesOptions.Schema*");
    }

    [TestCase("MySql.EntityFrameworkCore")]
    [TestCase("Microsoft.EntityFrameworkCore.Sqlite")]
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
