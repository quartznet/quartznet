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

using System.Data.Common;

using JasperFx;
using JasperFx.Descriptors;

using Microsoft.Data.Sqlite;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Sqlite;

namespace Quartz.Weasel.SQLite;

/// <summary>
/// One scheduler's SQLite schema as a Weasel database, reached through the store's own connections.
/// </summary>
/// <remarks>
/// <para>
/// There is no global lock, because SQLite needs none for what this does. The database file serializes
/// writers; every <c>CREATE</c> Weasel issues here is guarded with <c>IF NOT EXISTS</c>, so two appliers
/// creating the schema at once both succeed; and the one statement that is not guarded,
/// <c>ALTER TABLE … ADD COLUMN</c>, fails for whichever applier loses the race. That failure is answered
/// the way <c>ProvisionSchema()</c> answers a lost race — by reading the schema again, and finding that
/// nothing is left to do. Any other failure is not retried: see <see cref="IsLostRace" />.
/// </para>
/// <para>
/// Every apply goes through <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" />, which is
/// re-implemented here for that retry.
/// </para>
/// <para>
/// A change SQLite cannot make with <c>ALTER TABLE</c> rebuilds the table. The tables are add-only, so the
/// rebuild keeps the columns, indexes and foreign keys an application added, and their rows.
/// </para>
/// </remarks>
internal sealed class SqliteQuartzDatabase : DatabaseBase<SqliteConnection>, IQuartzWeaselDatabase
{
    internal static readonly QuartzWeaselDialect Dialect = new()
    {
        DatabaseName = "SQLite",
        RegistrationMethod = "UseWeaselForSqlite",
        StoreMethod = "UseSqlite",
        DefaultSchema = "main",
        AcceptsConnection = static connection => connection is SqliteConnection,
    };

    /// <summary>How many applies are made, each losing a race, before the failure stands.</summary>
    private const int ApplyAttempts = 3;

    /// <summary>SQLite's <c>SQLITE_ERROR</c>, which a syntax error is too: the message tells them apart.</summary>
    private const int SqliteError = 1;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IFeatureSchema feature;
    private readonly TimeProvider timeProvider;

    public SqliteQuartzDatabase(QuartzWeaselDatabaseContext context, TimeProvider? timeProvider = null)
        : base(
            context.MigrationLogger,
            AutoCreate.CreateOrUpdate,
            CreateMigrator(),
            context.SchedulerName,
            () => (SqliteConnection) context.DbProvider.CreateConnection())
    {
        Context = context;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        feature = new QuartzSqliteFeatureSchema([.. QuartzTables.Build(new QuartzTableNaming(context.Schema, context.TablePrefix))]);

        DatabaseDescriptor descriptor = Describe();
        Id = new DatabaseId(descriptor.ServerName, descriptor.DatabaseName);
    }

    public QuartzWeaselDatabaseContext Context { get; }

    /// <summary>
    /// Refuses a change it could only make by dropping and recreating a table, under every
    /// <see cref="AutoCreate" /> — including <see cref="AutoCreate.All" />, which would otherwise allow it.
    /// </summary>
    private static SqliteMigrator CreateMigrator() => new() { RefuseDestructiveChanges = true };

    public override IFeatureSchema[] BuildFeatureSchemas() => [feature];

    public override DatabaseDescriptor Describe()
    {
        SqliteConnectionStringBuilder builder = new(Context.ConnectionString);

        return new DatabaseDescriptor
        {
            Engine = SqliteProvider.EngineName,
            ServerName = "localhost",
            DatabaseName = string.IsNullOrEmpty(builder.DataSource) ? ":memory:" : builder.DataSource,
            SchemaOrNamespace = Context.Schema,
            Subject = typeof(SqliteQuartzDatabase).FullName!,
            Identifier = Context.SchedulerName,
            SubjectUri = Context.SubjectUri,
        };
    }

    /// <summary>
    /// Nothing: the connections are the scheduler's, and Microsoft.Data.Sqlite's pool clearing reaches
    /// further than one database.
    /// </summary>
    public override ValueTask ReleaseConnectionPoolAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    Task<SchemaPatchDifference> IDatabase.ApplyAllConfiguredChangesToDatabaseAsync(
        AutoCreate? @override,
        ReconnectionOptions? reconnectionOptions,
        CancellationToken ct) =>
        LockFreeApply.ApplyAsync(
            this,
            cancellationToken => ApplyAllConfiguredChangesToDatabaseAsync(@override, reconnectionOptions, cancellationToken),
            IsLostRace,
            ApplyAttempts,
            RetryDelay,
            timeProvider,
            ct);

    /// <summary>
    /// Whether SQLite's error is one a statement raises when another applier made the same change first: an
    /// <c>ADD COLUMN</c> it added (<c>duplicate column name</c>), or an object it created
    /// (<c>already exists</c>).
    /// </summary>
    internal static bool IsLostRace(DbException exception) =>
        exception is SqliteException { SqliteErrorCode: SqliteError } sqlite
        && (sqlite.Message.Contains("duplicate column name", StringComparison.Ordinal)
            || sqlite.Message.Contains("already exists", StringComparison.Ordinal));

    /// <summary>The schema's objects as the one feature the database has.</summary>
    private sealed class QuartzSqliteFeatureSchema : FeatureSchemaBase
    {
        private readonly ISchemaObject[] objects;

        public QuartzSqliteFeatureSchema(ISchemaObject[] objects) : base("quartz", CreateMigrator())
        {
            this.objects = objects;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects() => objects;
    }
}
