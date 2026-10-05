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
using System.Globalization;
using System.Text.RegularExpressions;

using JasperFx;
using JasperFx.Descriptors;

using Oracle.ManagedDataAccess.Client;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Oracle;

namespace Quartz.Weasel.Oracle;

/// <summary>
/// One scheduler's Oracle schema as a Weasel database, reached through the store's own connections.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="DatabaseBase{TConnection}" /> rather than on Weasel's <c>DatabaseWithTables</c>,
/// which wants a connection string of its own: connections come from the store's <c>IDbProvider</c>, so
/// a named connection string and a custom provider reach the database the scheduler does.
/// </para>
/// <para>
/// The tables are in the table prefix's schema, or else in the session's current schema,
/// <c>SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')</c> — which is where the store's unqualified SQL goes, and
/// which is not always the user the connection logs in as. Weasel's own default is the literal
/// <c>WEASEL</c>. The current schema is read once, the first time the model is needed, so describing the
/// database asks the server nothing.
/// </para>
/// <para>
/// There is no global lock. Oracle's only lock a session can name, <c>DBMS_LOCK</c>, needs an
/// <c>EXECUTE</c> grant from a DBA that the store itself never needs, and without it the package is not
/// even visible. Appliers therefore race, the way <c>ProvisionSchema()</c> does on Oracle: every
/// <c>CREATE TABLE</c> Weasel issues is guarded, the loser of any other statement fails, and a failure a
/// lost race raises (<see cref="LostRaceErrors" />) is answered by reading the schema again — done when
/// another process finished it, retried when it has not. Any other failure is not retried.
/// <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" /> is re-implemented here for that.
/// </para>
/// </remarks>
internal sealed class OracleQuartzDatabase : DatabaseBase<OracleConnection>, IQuartzWeaselDatabase
{
    internal static readonly QuartzWeaselDialect Dialect = new()
    {
        DatabaseName = "Oracle",
        RegistrationMethod = "UseWeaselForOracle",
        StoreMethod = "UseOracle",
        // None of its own: without one in the table prefix, the tables are in the session's current schema.
        DefaultSchema = "",
        AcceptsConnection = static connection => connection is OracleConnection,
    };

    /// <summary>
    /// How many applies are made, each losing a race, before the failure stands: as many as
    /// <c>ProvisionSchema()</c> gives a create that lost a race, for the same reason — two appliers fill in
    /// each other's gaps rather than wait for each other, and converge in a round or two.
    /// </summary>
    internal const int ApplyAttempts = 10;

    /// <summary>
    /// The errors a statement raises when another applier made the same change first, or when another
    /// session is busy with the table: each one raised by a real server in <c>OracleWeaselSchemaTest</c>.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <item><term>ORA-00054</term><description>an index on a table another session is writing to</description></item>
    /// <item><term>ORA-00955</term><description>the table or index name is taken, guarded <c>CREATE TABLE</c> included</description></item>
    /// <item><term>ORA-01418</term><description>the index to drop is gone</description></item>
    /// <item><term>ORA-01430</term><description>the column to add is there</description></item>
    /// <item><term>ORA-02260</term><description>the table has its primary key</description></item>
    /// <item><term>ORA-02275</term><description>the foreign key is there</description></item>
    /// </list>
    /// </remarks>
    internal static readonly int[] LostRaceErrors = [54, 955, 1418, 1430, 2260, 2275];

    private const string CurrentSchemaSql = "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly Lock gate = new();
    private readonly TimeProvider timeProvider;
    private string? schema;
    private IFeatureSchema? feature;

    public OracleQuartzDatabase(QuartzWeaselDatabaseContext context, TimeProvider? timeProvider = null)
        : base(
            context.MigrationLogger,
            AutoCreate.CreateOrUpdate,
            CreateMigrator(),
            context.SchedulerName,
            () => (OracleConnection) context.DbProvider.CreateConnection())
    {
        Context = context;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        schema = context.Schema.Length > 0 ? Fold(context.Schema) : null;

        DatabaseDescriptor descriptor = Describe();
        Id = new DatabaseId(descriptor.ServerName, descriptor.DatabaseName);
    }

    public QuartzWeaselDatabaseContext Context { get; }

    /// <summary>
    /// The schema the tables are in: the table prefix's, or the session's current schema, read from the
    /// server the first time it is asked for.
    /// </summary>
    public string Schema
    {
        get
        {
            lock (gate)
            {
                return schema ??= ReadCurrentSchema();
            }
        }
    }

    /// <summary>
    /// Refuses a change it could only make by dropping and recreating a table, under every
    /// <see cref="AutoCreate" /> — including <see cref="AutoCreate.All" />, which would otherwise allow it.
    /// </summary>
    internal static OracleMigrator CreateMigrator() => new QuartzOracleMigrator();

    public override IFeatureSchema[] BuildFeatureSchemas()
    {
        string tables = Schema;

        lock (gate)
        {
            return [feature ??= new QuartzOracleFeatureSchema([.. QuartzTables.Build(new QuartzTableNaming(tables, Context.TablePrefix))])];
        }
    }

    public override DatabaseDescriptor Describe()
    {
        OracleConnectionStringBuilder builder = new(Context.ConnectionString);
        (string server, int? port, string database) = SplitDataSource(builder.DataSource ?? "");

        string? known;
        lock (gate)
        {
            known = schema;
        }

        return new DatabaseDescriptor
        {
            Engine = OracleProvider.EngineName,
            ServerName = server,
            Port = port,
            DatabaseName = database,
            // The current schema once it has been read; until then the user, which it is unless the
            // session moves it.
            SchemaOrNamespace = known ?? Fold(builder.UserID ?? ""),
            Subject = typeof(OracleQuartzDatabase).FullName!,
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
        CancellationToken ct) =>
        LockFreeApply.ApplyAsync(
            this,
            cancellationToken => ApplyAllConfiguredChangesToDatabaseAsync(@override, reconnectionOptions, cancellationToken),
            IsLostRace,
            ApplyAttempts,
            RetryDelay,
            timeProvider,
            ct);

    /// <summary>Whether Oracle's error is one of <see cref="LostRaceErrors" />.</summary>
    internal static bool IsLostRace(DbException exception) =>
        exception is OracleException oracle && LostRaceErrors.Contains(oracle.Number);

    /// <summary>
    /// The session's current schema, which is where the store's unqualified SQL resolves a table: the
    /// user's own, or wherever a logon trigger or the application moved it.
    /// </summary>
    /// <remarks>
    /// Synchronous, because Weasel asks for the model synchronously, from every path — the startup apply,
    /// <c>db-assert</c> and <c>db-patch</c> alike. It is asked once per database.
    /// </remarks>
    private string ReadCurrentSchema()
    {
        using OracleConnection connection = CreateConnection();
        connection.Open();

        using OracleCommand command = connection.CreateCommand();
        command.CommandText = CurrentSchemaSql;
        return command.ExecuteScalar() as string
               ?? throw new SchedulerException($"Oracle reported no current schema for the session of scheduler '{Context.SchedulerName}'.");
    }

    /// <summary>
    /// The host, the port and the service of a data source: an Easy Connect one, <c>host:1521/XEPDB1</c>, or
    /// a connect descriptor's first address and its <c>SERVICE_NAME</c> or <c>SID</c>. A TNS alias is only a
    /// name, and stands for both the server and the database.
    /// </summary>
    internal static (string Server, int? Port, string Database) SplitDataSource(string dataSource)
    {
        if (dataSource.Contains('(', StringComparison.Ordinal))
        {
            string host = DescriptorValue(dataSource, "HOST");
            string service = DescriptorValue(dataSource, "SERVICE_NAME") is { Length: > 0 } name ? name : DescriptorValue(dataSource, "SID");
            return (host, ParsePort(DescriptorValue(dataSource, "PORT")), service);
        }

        string text = dataSource.StartsWith("//", StringComparison.Ordinal) ? dataSource[2..] : dataSource;
        int slash = text.IndexOf('/', StringComparison.Ordinal);

        if (slash <= 0)
        {
            return (dataSource, null, dataSource);
        }

        string address = text[..slash];
        string database = text[(slash + 1)..];
        int colon = address.LastIndexOf(':');

        return colon > 0 && ParsePort(address[(colon + 1)..]) is { } port
            ? (address[..colon], port, database)
            : (address, null, database);
    }

    /// <summary>The first <c>(KEY=value)</c> of a connect descriptor, or empty.</summary>
    private static string DescriptorValue(string descriptor, string key) =>
        Regex.Match(descriptor, $@"\(\s*{key}\s*=\s*([^)]*?)\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Groups[1].Value;

    private static int? ParsePort(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : null;

    /// <summary>An unquoted name as Oracle stores it; a quoted one as written.</summary>
    private static string Fold(string name) =>
        name.StartsWith('"') ? SchemaUtils.Unquote(name) : name.ToUpperInvariant();

    /// <summary>The schema's objects as the one feature the database has.</summary>
    private sealed class QuartzOracleFeatureSchema : FeatureSchemaBase
    {
        private readonly ISchemaObject[] objects;

        public QuartzOracleFeatureSchema(ISchemaObject[] objects) : base("quartz", CreateMigrator())
        {
            this.objects = objects;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects() => objects;
    }
}
