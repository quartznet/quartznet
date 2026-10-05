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

using Npgsql;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Quartz.Weasel.PostgreSQL;

/// <summary>
/// One scheduler's PostgreSQL schema as a Weasel database, reached through the store's own connections.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="DatabaseBase{TConnection}" /> rather than on Weasel's <c>PostgresqlDatabase</c>,
/// which wants an <see cref="NpgsqlDataSource" /> of its own: connections come from the store's
/// <c>IDbProvider</c>, so a registered data source, a named connection string and a custom provider all
/// reach the database the scheduler does. <see cref="possiblyCheckForSchemas" /> restores the one thing
/// <c>PostgresqlDatabase</c> adds, the schema existence check.
/// </para>
/// <para>
/// Every apply goes through <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" />,
/// re-implemented here to take an advisory lock first. Weasel's own takes none on the command line, so
/// two nodes starting at once, or a node starting while <c>db-apply</c> runs, would otherwise both read
/// the schema as missing and race to create it.
/// </para>
/// </remarks>
internal sealed class PostgresQuartzDatabase : DatabaseBase<NpgsqlConnection>, IQuartzWeaselDatabase
{
    internal static readonly QuartzWeaselDialect Dialect = new()
    {
        DatabaseName = "PostgreSQL",
        RegistrationMethod = "UseWeaselForPostgres",
        StoreMethod = "UsePostgres",
        DefaultSchema = "public",
        AcceptsConnection = static connection => connection is NpgsqlConnection,
    };

    private readonly QuartzPostgresFeatureSchema feature;
    private readonly int lockId;
    private readonly TimeSpan lockTimeout;
    private readonly TimeProvider timeProvider;

    public PostgresQuartzDatabase(QuartzWeaselDatabaseContext context, int lockId, TimeSpan lockTimeout, TimeProvider? timeProvider = null)
        : base(
            context.MigrationLogger,
            AutoCreate.CreateOrUpdate,
            CreateMigrator(),
            context.SchedulerName,
            () => (NpgsqlConnection) context.DbProvider.CreateConnection())
    {
        Context = context;
        this.lockId = lockId;
        this.lockTimeout = lockTimeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        feature = new QuartzPostgresFeatureSchema(new QuartzTableNaming(context.Schema, context.TablePrefix));

        DatabaseDescriptor descriptor = Describe();
        Id = new DatabaseId(descriptor.ServerName, descriptor.DatabaseName);
    }

    public QuartzWeaselDatabaseContext Context { get; }

    /// <summary>
    /// Refuses a change it could only make by dropping and recreating a table, under every
    /// <see cref="AutoCreate" /> — including <see cref="AutoCreate.All" />, which would otherwise allow it.
    /// </summary>
    internal static PostgresqlMigrator CreateMigrator() => new() { RefuseDestructiveChanges = true };

    public override IFeatureSchema[] BuildFeatureSchemas() => [feature];

    public override DatabaseDescriptor Describe()
    {
        NpgsqlConnectionStringBuilder builder = new(Context.ConnectionString);

        return new DatabaseDescriptor
        {
            Engine = PostgresqlProvider.EngineName,
            ServerName = FirstHost(builder.Host),
            Port = builder.Port,
            DatabaseName = builder.Database ?? "",
            SchemaOrNamespace = Context.Schema,
            Subject = typeof(PostgresQuartzDatabase).FullName!,
            Identifier = Context.SchedulerName,
            SubjectUri = Context.SubjectUri,
        };
    }

    protected override ISchemaObject[] possiblyCheckForSchemas(ISchemaObject[] objects) =>
        SchemaExistenceCheck.WithSchemaCheck(objects);

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
        // 9.37.0, JasperFx/weasel#659), but gives up quietly if that release fails, and a session-level advisory lock
        // left on a pooled connection would outlive the failure.
        PostgresAdvisoryLock globalLock = new(CreateConnection, lockId, lockTimeout, timeProvider);
        await using (globalLock.ConfigureAwait(false))
        {
            return await ApplyAllConfiguredChangesToDatabaseAsync(globalLock, @override, reconnectionOptions, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>The first host of a multi-host connection string, without its port.</summary>
    private static string FirstHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return "";
        }

        string first = host.Split(',')[0];
        int colon = first.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? first : first[..colon];
    }
}
