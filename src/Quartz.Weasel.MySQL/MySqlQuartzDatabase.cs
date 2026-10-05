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

using MySqlConnector;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.MySql;

namespace Quartz.Weasel.MySQL;

/// <summary>
/// One scheduler's MySQL schema as a Weasel database, reached through the store's own connections.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="DatabaseBase{TConnection}" /> rather than on Weasel's <c>DatabaseWithTables</c>,
/// which wants a connection string of its own: connections come from the store's <c>IDbProvider</c>, so
/// a named connection string and a custom provider reach the database the scheduler does.
/// </para>
/// <para>
/// The tables are in the table prefix's database, or else in the connection's. Weasel's own MySQL default
/// is the literal <c>public</c>, which is no database the store's unqualified SQL ever reaches, so a
/// connection that names no database and a prefix that names none either are refused.
/// </para>
/// <para>
/// Every apply goes through <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" />,
/// re-implemented here to take a named user lock first. Weasel's own takes none, so two nodes starting at
/// once, or a node starting while <c>db-apply</c> runs, would otherwise both read the schema as missing
/// and race to create it.
/// </para>
/// </remarks>
internal sealed class MySqlQuartzDatabase : DatabaseBase<MySqlConnection>, IQuartzWeaselDatabase
{
    internal static readonly QuartzWeaselDialect Dialect = new()
    {
        DatabaseName = "MySQL",
        RegistrationMethod = "UseWeaselForMySql",
        StoreMethod = "UseMySqlConnector",
        // None of its own: without one in the table prefix, the tables are in the connection's database.
        DefaultSchema = "",
        AcceptsConnection = static connection => connection is MySqlConnection,
    };

    private readonly QuartzMySqlFeatureSchema feature;
    private readonly string lockName;
    private readonly TimeSpan lockTimeout;
    private readonly TimeProvider timeProvider;

    public MySqlQuartzDatabase(QuartzWeaselDatabaseContext context, string lockName, TimeSpan lockTimeout, TimeProvider? timeProvider = null)
        : base(
            context.MigrationLogger,
            AutoCreate.CreateOrUpdate,
            CreateMigrator(),
            context.SchedulerName,
            () => (MySqlConnection) context.DbProvider.CreateConnection())
    {
        Context = context;
        Schema = context.Schema.Length > 0 ? SchemaUtils.Unquote(context.Schema) : ConnectionDatabase(context);
        this.lockName = lockName;
        this.lockTimeout = lockTimeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        feature = new QuartzMySqlFeatureSchema([.. QuartzTables.Build(new QuartzTableNaming(Schema, context.TablePrefix))]);

        DatabaseDescriptor descriptor = Describe();
        Id = new DatabaseId(descriptor.ServerName, descriptor.DatabaseName);
    }

    public QuartzWeaselDatabaseContext Context { get; }

    /// <summary>The database the tables are in: the table prefix's, or the connection's.</summary>
    public string Schema { get; }

    /// <summary>
    /// Refuses a change it could only make by dropping and recreating a table, under every
    /// <see cref="AutoCreate" /> — including <see cref="AutoCreate.All" />, which would otherwise allow it.
    /// </summary>
    internal static MySqlMigrator CreateMigrator() => new() { RefuseDestructiveChanges = true };

    public override IFeatureSchema[] BuildFeatureSchemas() => [feature];

    public override DatabaseDescriptor Describe()
    {
        MySqlConnectionStringBuilder builder = new(Context.ConnectionString);

        return new DatabaseDescriptor
        {
            Engine = MySqlProvider.EngineName,
            ServerName = FirstHost(builder.Server),
            Port = (int) builder.Port,
            DatabaseName = builder.Database ?? "",
            SchemaOrNamespace = Schema,
            Subject = typeof(MySqlQuartzDatabase).FullName!,
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
        // 9.37.0, JasperFx/weasel#659), but gives up quietly if that release fails, and a user lock
        // left on a pooled connection would outlive the failure.
        MySqlUserLock globalLock = new(CreateConnection, lockName, lockTimeout, timeProvider);
        await using (globalLock.ConfigureAwait(false))
        {
            return await ApplyAllConfiguredChangesToDatabaseAsync(globalLock, @override, reconnectionOptions, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>The database the connection opens in, which is where the store's unqualified SQL goes.</summary>
    private static string ConnectionDatabase(QuartzWeaselDatabaseContext context)
    {
        string? database = new MySqlConnectionStringBuilder(context.ConnectionString).Database;

        if (string.IsNullOrEmpty(database))
        {
            throw new SchedulerConfigException(
                $"UseWeaselForMySql() cannot tell which database scheduler '{context.SchedulerName}' keeps its tables in:"
                + " its connection string names none, and neither does its table prefix. Add Database=<name> to the"
                + " connection string, or name the database in the prefix, as in '<database>.QRTZ_'.");
        }

        return database;
    }

    /// <summary>The first host of a connection string that lists several, the way MySqlConnector fails over.</summary>
    private static string FirstHost(string? server)
    {
        if (string.IsNullOrEmpty(server))
        {
            return "";
        }

        return server.Split(',')[0].Trim();
    }

    /// <summary>The schema's objects as the one feature the database has.</summary>
    private sealed class QuartzMySqlFeatureSchema : FeatureSchemaBase
    {
        private readonly ISchemaObject[] objects;

        public QuartzMySqlFeatureSchema(ISchemaObject[] objects) : base("quartz", CreateMigrator())
        {
            this.objects = objects;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects() => objects;
    }
}
