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

using Microsoft.EntityFrameworkCore;

namespace Quartz.EntityFrameworkCore;

/// <summary>What a database does to the case of an identifier the store writes unquoted.</summary>
internal enum QuartzIdentifierCase
{
    /// <summary>Keeps it as written, or compares it without regard to case: SQL Server, MySQL, SQLite.</summary>
    AsWritten,

    /// <summary>Folds it to lower case: PostgreSQL.</summary>
    Lower,

    /// <summary>Folds it to upper case: Oracle and Firebird.</summary>
    Upper,
}

/// <summary>
/// One database Quartz has a model for, and the EF Core providers that speak to it.
/// </summary>
/// <remarks>
/// The dialect is chosen by the provider's name — <c>DbContext.Database.ProviderName</c> in a model,
/// <c>MigrationBuilder.ActiveProvider</c> in a migration — rather than by a reference to the provider, so the
/// package needs no provider at all and the application's own choice of version decides.
/// </remarks>
internal sealed class QuartzDialect
{
    internal static readonly QuartzDialect PostgreSql = new()
    {
        Label = "PostgreSQL",
        Tables = QuartzSchema.PostgreSql,
        IdentifierCase = QuartzIdentifierCase.Lower,
        SupportsSchemas = true,
        MaxIdentifierLength = 63,
        HasAppAnyModel = true,
    };

    internal static readonly QuartzDialect SqlServer = new()
    {
        Label = "SQL Server",
        Tables = QuartzSchema.SqlServer,
        IdentifierCase = QuartzIdentifierCase.AsWritten,
        SupportsSchemas = true,
        MaxIdentifierLength = 128,
        HasAppAnyModel = true,
    };

    // Oracle's MySql.EntityFrameworkCore drops an index column's direction when it creates the index, so the
    // schema is completed by an index rebuild that says it.
    internal static readonly QuartzDialect MySql = new()
    {
        Label = "MySQL",
        Tables = QuartzSchema.MySql,
        IdentifierCase = QuartzIdentifierCase.AsWritten,
        SupportsSchemas = false,
        MaxIdentifierLength = 64,
        HasAppAnyModel = true,
        RebuildsDescendingIndexes = true,
    };

    internal static readonly QuartzDialect Sqlite = new()
    {
        Label = "SQLite",
        Tables = QuartzSchema.Sqlite,
        IdentifierCase = QuartzIdentifierCase.AsWritten,
        SupportsSchemas = false,
        MaxIdentifierLength = int.MaxValue,
        HasAppAnyModel = true,
        DeleteTriggers = QuartzSchema.SqliteDeleteTriggers,
    };

    // The tables, and EF Core's __EFMigrationsHistory, are created unqualified, so they go in the schema of the user
    // the migration logs in as; the provider looks for the history table in USER_TABLES. The scripts put them in
    // the session's schema too. Quartz.Weasel.Oracle also takes an owner from a prefix such as OWNER.QRTZ_; here
    // the store's tables in another owner's schema are migrated connected as that owner.
    internal static readonly QuartzDialect Oracle = new()
    {
        Label = "Oracle",
        Tables = QuartzSchema.Oracle,
        IdentifierCase = QuartzIdentifierCase.Upper,
        SupportsSchemas = false,
        MaxIdentifierLength = 128,
        HasAppAnyModel = false,
    };

    // 31 bytes is Firebird 3's limit. Firebird 4 and 5 take 63 characters, but a model cannot know which server
    // opens the database, so a Firebird database is held to the lower one until the application says the higher.
    internal static readonly QuartzDialect Firebird = new()
    {
        Label = "Firebird",
        Tables = QuartzSchema.Firebird,
        IdentifierCase = QuartzIdentifierCase.Upper,
        SupportsSchemas = false,
        MaxIdentifierLength = 31,
        RaisedMaxIdentifierLength = 63,
        HasAppAnyModel = false,
        PlainDelete = DeleteBehavior.Restrict,
    };

    /// <summary>Every provider name Quartz recognizes, and the dialect it speaks.</summary>
    internal static readonly IReadOnlyList<(string ProviderName, QuartzDialect Dialect)> Providers =
    [
        ("Npgsql.EntityFrameworkCore.PostgreSQL", PostgreSql),
        ("Microsoft.EntityFrameworkCore.SqlServer", SqlServer),
        ("MySql.EntityFrameworkCore", MySql),
        ("Pomelo.EntityFrameworkCore.MySql", MySql),
        ("Microsoft.EntityFrameworkCore.Sqlite", Sqlite),
        ("Oracle.EntityFrameworkCore", Oracle),
        ("FirebirdSql.EntityFrameworkCore.Firebird", Firebird),
    ];

    // Each row sets its properties by name: the six every database differs in are required, and the four most
    // databases share have defaults.
    private QuartzDialect()
    {
    }

    /// <summary>The database's name, for messages.</summary>
    internal required string Label { get; init; }

    /// <summary>Every table, tables before the ones that reference them.</summary>
    internal required QuartzTableDefinition[] Tables { get; init; }

    /// <summary>
    /// What the database does to the case of the store's unquoted identifiers. EF Core quotes what it creates, so
    /// the model has to say the folded names itself.
    /// </summary>
    internal required QuartzIdentifierCase IdentifierCase { get; init; }

    /// <summary>Whether the tables may go in a schema of their own.</summary>
    internal required bool SupportsSchemas { get; init; }

    /// <summary>
    /// The longest identifier the database takes, in bytes: where EF Core cuts the names it makes up, and past
    /// which a name the model gives is refused, unless <see cref="RaisedMaxIdentifierLength" /> lets the
    /// application say more.
    /// </summary>
    internal required int MaxIdentifierLength { get; init; }

    /// <summary>
    /// The most <c>QuartzTablesOptions.MaxIdentifierLength</c> may be, where the database's later versions take
    /// longer names than <see cref="MaxIdentifierLength" />: Firebird 4's and 5's 63. <see langword="null" /> where
    /// every version has one limit, which the option cannot change.
    /// </summary>
    internal int? RaisedMaxIdentifierLength { get; init; }

    /// <summary>Whether an index with a descending column has to be created again after EF Core created it.</summary>
    internal bool RebuildsDescendingIndexes { get; init; }

    /// <summary>The triggers the model cannot express, which <c>CompleteQuartzSchema</c> creates.</summary>
    internal QuartzTriggerDefinition[] DeleteTriggers { get; init; } = [];

    /// <summary>Whether AppAny.Quartz.EntityFrameworkCore.Migrations has a model of this database to move from.</summary>
    internal required bool HasAppAnyModel { get; init; }

    /// <summary>
    /// The delete rule of a foreign key the script declares without one, as the database records it: NO ACTION,
    /// except on Firebird, which records the script's keys as RESTRICT and EF Core's NO ACTION as NO ACTION.
    /// </summary>
    /// <remarks>
    /// With Restrict, Firebird's provider writes no delete rule, so the key's reads back as the script's RESTRICT;
    /// it still writes <c>ON UPDATE NO ACTION</c>, where the script's key reads back RESTRICT for updates too.
    /// Firebird enforces the two alike, Quartz never updates a key column, and the schema comparison the tests make
    /// reads the delete rule alone.
    /// </remarks>
    internal DeleteBehavior PlainDelete { get; init; } = DeleteBehavior.NoAction;

    /// <summary>The table of this name, which every caller names from the model itself.</summary>
    internal QuartzTableDefinition Table(string name) => Tables.Single(t => t.Name == name);

    /// <summary>The dialect a provider speaks.</summary>
    /// <param name="providerName">The provider's name, as EF Core reports it.</param>
    /// <exception cref="ArgumentNullException">No provider is configured.</exception>
    /// <exception cref="NotSupportedException">The provider is not one Quartz has a model for.</exception>
    internal static QuartzDialect ForProvider(string? providerName)
    {
        if (providerName is null)
        {
            throw new ArgumentNullException(
                nameof(providerName),
                "No database provider is configured, so Quartz cannot tell which database's tables to add. "
                + "Configure the provider first (UseNpgsql, UseSqlServer, UseMySQL, UseSqlite, UseOracle or UseFirebird) and pass "
                + "Database.ProviderName.");
        }

        foreach ((string name, QuartzDialect dialect) in Providers)
        {
            if (string.Equals(name, providerName, StringComparison.Ordinal))
            {
                return dialect;
            }
        }

        throw new NotSupportedException(
            $"Quartz.EntityFrameworkCore has no model for the EF Core provider '{providerName}'. "
            + $"It has one for {string.Join(", ", Providers.Select(p => p.ProviderName))}.");
    }
}