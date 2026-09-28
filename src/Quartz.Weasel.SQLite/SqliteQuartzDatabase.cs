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

using JasperFx;
using JasperFx.Descriptors;

using Microsoft.Data.Sqlite;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Sqlite;

using TableDelta = Weasel.Sqlite.Tables.TableDelta;

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
/// nothing is left to do.
/// </para>
/// <para>
/// Every apply goes through <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" />, which is
/// re-implemented here for that retry and for the rebuild guard: see
/// <see cref="ThrowIfARebuildWouldLoseUndeclaredObjects" />.
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

    /// <summary>How many times an apply that failed is read again and retried before the failure stands.</summary>
    private const int ApplyAttempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IFeatureSchema feature;
    private readonly HashSet<string> retiredIndexes;
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

        ISchemaObject[] objects = [.. QuartzTables.Build(new QuartzTableNaming(context.Schema, context.TablePrefix))];
        feature = new QuartzSqliteFeatureSchema(objects);
        retiredIndexes = objects.OfType<RetiredSqliteIndex>()
            .Select(x => x.Identifier.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        CancellationToken ct) => ApplyGuardedAsync(@override, reconnectionOptions, ct);

    private async Task<SchemaPatchDifference> ApplyGuardedAsync(
        AutoCreate? @override,
        ReconnectionOptions? reconnectionOptions,
        CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            SchemaMigration migration = await CreateMigrationAsync(ct).ConfigureAwait(false);
            ThrowIfARebuildWouldLoseUndeclaredObjects(migration);

            try
            {
                return await ApplyAllConfiguredChangesToDatabaseAsync(@override, reconnectionOptions, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (attempt < ApplyAttempts && !ct.IsCancellationRequested)
            {
                Context.Logger.SchemaApplyRetrying(Context.SchedulerName, Describe().DatabaseUri(), attempt, ApplyAttempts, e);
            }

            SchemaMigration after = await CreateMigrationAsync(ct).ConfigureAwait(false);
            if (after.Difference == SchemaPatchDifference.None)
            {
                Context.Logger.SchemaAppliedByAnotherProcess(Context.SchedulerName, Describe().DatabaseUri());
                return SchemaPatchDifference.None;
            }

            await Task.Delay(RetryDelay, timeProvider, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Refuses a migration that would rebuild a Quartz table carrying columns, indexes or foreign keys the
    /// model does not declare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQLite cannot alter most of a table in place, so Weasel rebuilds it: a new table from the model,
    /// the rows copied across, the old one dropped. The new table has only what the model declares, so an
    /// application's own column on <c>QRTZ_TRIGGERS</c> — which the add-only model otherwise keeps — goes
    /// with the rebuild, and with it the data. Until Weasel's rebuild carries undeclared objects across,
    /// such a migration is refused before anything runs, naming what would be lost.
    /// </para>
    /// <para>
    /// The index names Quartz itself retired do not count: the migration drops them anyway.
    /// </para>
    /// </remarks>
    internal void ThrowIfARebuildWouldLoseUndeclaredObjects(SchemaMigration migration)
    {
        foreach (TableDelta delta in migration.Deltas.OfType<TableDelta>())
        {
            bool rebuilds = delta.CanRebuildInPlace
                            && (delta.RequiresTableRecreation || delta.Difference == SchemaPatchDifference.Invalid);

            if (!rebuilds)
            {
                continue;
            }

            List<string> lost = delta.WithheldDrops.Where(x => !IsRetiredIndex(x)).ToList();
            if (lost.Count == 0)
            {
                continue;
            }

            throw new SchedulerException(
                $"Weasel would rebuild table {delta.SchemaObject.Identifier.Name} to bring the schema of scheduler"
                + $" '{Context.SchedulerName}' up to date ({delta.InvalidReason ?? "SQLite cannot make the change in place"}),"
                + $" and a rebuild keeps only what the model declares, so it would drop {string.Join(", ", lost)}."
                + " Nothing was changed. Drop those objects, apply the schema, and add them back; or make the change by hand.");
        }
    }

    private bool IsRetiredIndex(string withheld) =>
        withheld.StartsWith("index ", StringComparison.Ordinal) && retiredIndexes.Contains(withheld["index ".Length..]);

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
