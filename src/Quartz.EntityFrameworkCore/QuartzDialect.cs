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
    internal static readonly QuartzDialect PostgreSql = new(
        "PostgreSQL",
        QuartzSchema.PostgreSql,
        QuartzIdentifierCase.Lower,
        supportsSchemas: true,
        maxIdentifierLength: 63,
        rebuildsDescendingIndexes: false,
        deleteTriggers: [],
        hasAppAnyModel: true);

    internal static readonly QuartzDialect SqlServer = new(
        "SQL Server",
        QuartzSchema.SqlServer,
        QuartzIdentifierCase.AsWritten,
        supportsSchemas: true,
        maxIdentifierLength: 128,
        rebuildsDescendingIndexes: false,
        deleteTriggers: [],
        hasAppAnyModel: true);

    // Oracle's MySql.EntityFrameworkCore drops an index column's direction when it creates the index, so the
    // schema is completed by an index rebuild that says it.
    internal static readonly QuartzDialect MySql = new(
        "MySQL",
        QuartzSchema.MySql,
        QuartzIdentifierCase.AsWritten,
        supportsSchemas: false,
        maxIdentifierLength: 64,
        rebuildsDescendingIndexes: true,
        deleteTriggers: [],
        hasAppAnyModel: true);

    internal static readonly QuartzDialect Sqlite = new(
        "SQLite",
        QuartzSchema.Sqlite,
        QuartzIdentifierCase.AsWritten,
        supportsSchemas: false,
        maxIdentifierLength: int.MaxValue,
        rebuildsDescendingIndexes: false,
        deleteTriggers: QuartzSchema.SqliteDeleteTriggers,
        hasAppAnyModel: true);

    // The tables go in the session's own schema, as the scripts and the Weasel package put them.
    internal static readonly QuartzDialect Oracle = new(
        "Oracle",
        QuartzSchema.Oracle,
        QuartzIdentifierCase.Upper,
        supportsSchemas: false,
        maxIdentifierLength: 128,
        rebuildsDescendingIndexes: false,
        deleteTriggers: [],
        hasAppAnyModel: false);

    // 31 bytes is Firebird 3's limit. Firebird 4 and 5 take 63, but a model cannot know which server opens the
    // database, so every Firebird database is held to the lower one.
    internal static readonly QuartzDialect Firebird = new(
        "Firebird",
        QuartzSchema.Firebird,
        QuartzIdentifierCase.Upper,
        supportsSchemas: false,
        maxIdentifierLength: 31,
        rebuildsDescendingIndexes: false,
        deleteTriggers: [],
        hasAppAnyModel: false,
        plainDelete: DeleteBehavior.Restrict);

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

    private QuartzDialect(
        string label,
        QuartzTableDefinition[] tables,
        QuartzIdentifierCase identifierCase,
        bool supportsSchemas,
        int maxIdentifierLength,
        bool rebuildsDescendingIndexes,
        QuartzTriggerDefinition[] deleteTriggers,
        bool hasAppAnyModel,
        DeleteBehavior plainDelete = DeleteBehavior.NoAction)
    {
        PlainDelete = plainDelete;
        Label = label;
        Tables = tables;
        IdentifierCase = identifierCase;
        SupportsSchemas = supportsSchemas;
        MaxIdentifierLength = maxIdentifierLength;
        RebuildsDescendingIndexes = rebuildsDescendingIndexes;
        DeleteTriggers = deleteTriggers;
        HasAppAnyModel = hasAppAnyModel;
    }

    /// <summary>The database's name, for messages.</summary>
    internal string Label { get; }

    /// <summary>Every table, tables before the ones that reference them.</summary>
    internal QuartzTableDefinition[] Tables { get; }

    /// <summary>
    /// What the database does to the case of the store's unquoted identifiers. EF Core quotes what it creates, so
    /// the model has to say the folded names itself.
    /// </summary>
    internal QuartzIdentifierCase IdentifierCase { get; }

    /// <summary>Whether the tables may go in a schema of their own.</summary>
    internal bool SupportsSchemas { get; }

    /// <summary>
    /// The longest identifier the database takes, in bytes: where EF Core cuts the names it makes up, and past
    /// which a name the model gives is refused.
    /// </summary>
    internal int MaxIdentifierLength { get; }

    /// <summary>Whether an index with a descending column has to be created again after EF Core created it.</summary>
    internal bool RebuildsDescendingIndexes { get; }

    /// <summary>The triggers the model cannot express, which <c>CompleteQuartzSchema</c> creates.</summary>
    internal QuartzTriggerDefinition[] DeleteTriggers { get; }

    /// <summary>Whether AppAny.Quartz.EntityFrameworkCore.Migrations has a model of this database to move from.</summary>
    internal bool HasAppAnyModel { get; }

    /// <summary>
    /// The delete rule of a foreign key the script declares without one, as the database records it: NO ACTION,
    /// except on Firebird, which records the script's keys as RESTRICT and EF Core's NO ACTION as NO ACTION.
    /// </summary>
    internal DeleteBehavior PlainDelete { get; }

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
