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

using Microsoft.Data.SqlClient;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.SqlServer;
using Weasel.SqlServer.Tables;

namespace Quartz.Weasel.SqlServer;

/// <summary>
/// One scheduler's SQL Server schema as a Weasel database, reached through the store's own connections.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="DatabaseBase{TConnection}" /> rather than on Weasel's <c>DatabaseWithTables</c>,
/// which wants a connection string of its own: connections come from the store's <c>IDbProvider</c>, so
/// a named connection string and a custom provider reach the database the scheduler does.
/// </para>
/// <para>
/// Every apply goes through <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" />,
/// re-implemented here to take an application lock first. Weasel's own takes none on the command line, so
/// two nodes starting at once, or a node starting while <c>db-apply</c> runs, would otherwise both read
/// the schema as missing and race to create it.
/// </para>
/// <para>
/// The first object every comparison reads is <see cref="MemoryOptimizedTableGuard" />, so a
/// memory-optimized schema is refused before Weasel looks at a table.
/// </para>
/// </remarks>
internal sealed class SqlServerQuartzDatabase : DatabaseBase<SqlConnection>, IQuartzWeaselDatabase
{
    internal static readonly QuartzWeaselDialect Dialect = new()
    {
        DatabaseName = "SQL Server",
        RegistrationMethod = "UseWeaselForSqlServer",
        StoreMethod = "UseSqlServer",
        DefaultSchema = "dbo",
        AcceptsConnection = static connection => connection is SqlConnection,
    };

    private readonly QuartzSqlServerFeatureSchema feature;
    private readonly string lockResource;
    private readonly TimeSpan lockTimeout;
    private readonly TimeProvider timeProvider;

    public SqlServerQuartzDatabase(QuartzWeaselDatabaseContext context, string lockResource, TimeSpan lockTimeout, TimeProvider? timeProvider = null)
        : base(
            context.MigrationLogger,
            AutoCreate.CreateOrUpdate,
            CreateMigrator(),
            context.SchedulerName,
            () => (SqlConnection) context.DbProvider.CreateConnection())
    {
        Context = context;
        this.lockResource = lockResource;
        this.lockTimeout = lockTimeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        QuartzTableNaming naming = new(context.Schema, context.TablePrefix);
        List<ISchemaObject> objects = QuartzTables.Build(naming);
        objects.Insert(0, new MemoryOptimizedTableGuard(
            naming.Name("MEMORY_OPTIMIZED_CHECK"),
            context.SchedulerName,
            objects.OfType<Table>().Select(x => x.Identifier.Name)));

        feature = new QuartzSqlServerFeatureSchema([.. objects]);

        DatabaseDescriptor descriptor = Describe();
        Id = new DatabaseId(descriptor.ServerName, descriptor.DatabaseName);
    }

    public QuartzWeaselDatabaseContext Context { get; }

    /// <summary>
    /// Refuses a change it could only make by dropping and recreating a table, under every
    /// <see cref="AutoCreate" /> — including <see cref="AutoCreate.All" />, which would otherwise allow it.
    /// </summary>
    internal static SqlServerMigrator CreateMigrator() => new() { RefuseDestructiveChanges = true };

    public override IFeatureSchema[] BuildFeatureSchemas() => [feature];

    public override DatabaseDescriptor Describe()
    {
        SqlConnectionStringBuilder builder = new(Context.ConnectionString);

        return new DatabaseDescriptor
        {
            Engine = SqlServerProvider.EngineName,
            ServerName = builder.DataSource ?? "",
            DatabaseName = builder.InitialCatalog ?? "",
            SchemaOrNamespace = SchemaUtils.Unbracket(Context.Schema),
            Subject = typeof(SqlServerQuartzDatabase).FullName!,
            Identifier = Context.SchedulerName,
            SubjectUri = Context.SubjectUri,
        };
    }

    /// <summary>
    /// Nothing: the pool is the scheduler's, and <c>db-apply</c> releasing it would close the
    /// connections of a scheduler running in the same process.
    /// </summary>
    public override ValueTask ReleaseConnectionPoolAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    Task<SchemaPatchDifference> IDatabase.ApplyAllConfiguredChangesToDatabaseAsync(
        AutoCreate? @override,
        ReconnectionOptions? reconnectionOptions,
        CancellationToken ct) => ApplyUnderLockAsync(@override, reconnectionOptions, ct);

    private async Task<SchemaPatchDifference> ApplyUnderLockAsync(
        AutoCreate? @override,
        ReconnectionOptions? reconnectionOptions,
        CancellationToken ct)
    {
        // Disposed whichever way the apply ends: Weasel releases the lock after a failed apply too (from
        // 9.37.0, JasperFx/weasel#659), but gives up quietly if that release fails, and a session-owned application lock
        // left on a pooled connection would outlive the failure.
        SqlServerApplicationLock globalLock = new(CreateConnection, lockResource, lockTimeout, timeProvider);
        await using (globalLock.ConfigureAwait(false))
        {
            return await ApplyAllConfiguredChangesToDatabaseAsync(globalLock, @override, reconnectionOptions, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>The schema's objects as the one feature the database has.</summary>
    private sealed class QuartzSqlServerFeatureSchema : FeatureSchemaBase
    {
        private readonly ISchemaObject[] objects;

        public QuartzSqlServerFeatureSchema(ISchemaObject[] objects) : base("quartz", CreateMigrator())
        {
            this.objects = objects;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects() => objects;
    }
}
