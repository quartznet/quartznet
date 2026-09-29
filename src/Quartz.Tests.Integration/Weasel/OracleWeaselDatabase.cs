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

using System.Data.Common;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Oracle.ManagedDataAccess.Client;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core.Migrations;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// An empty Oracle schema of a test's own — a user, on the shared container — dropped when the test is done.
/// </summary>
/// <remarks>
/// <para>
/// A user rather than a table prefix, because the fresh-install script drops and recreates the
/// <c>QRTZ_</c> tables of whichever schema it runs in, and the tests run side by side.
/// </para>
/// <para>
/// The user has <c>CREATE SESSION</c> and <c>CREATE TABLE</c> and nothing else: no <c>DBA</c>, no
/// <c>EXECUTE</c> on <c>DBMS_LOCK</c>, no <c>CREATE USER</c>. That is what a Quartz store needs, and all
/// Weasel gets here.
/// </para>
/// </remarks>
internal sealed class OracleWeaselDatabase : IAsyncDisposable
{
    private const string Password = "weasel";

    private readonly string adminConnectionString;
    private readonly List<string> users = [];

    private OracleWeaselDatabase(string adminConnectionString, string name, string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        Name = name;
        ConnectionString = connectionString;
    }

    /// <summary>The user, which is also the schema, as Oracle stores it: upper case.</summary>
    public string Name { get; }

    public string ConnectionString { get; }

    private static readonly Lazy<Task<(SchemaSnapshot Schema, List<string> Details)>> freshInstall =
        new(ReadFreshInstallAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// What <c>tables_oracle.sql</c> creates, read once per run: the same script on the same server builds
    /// the same schema every time, and building it costs an Oracle test more than anything else it does.
    /// </summary>
    public static Task<(SchemaSnapshot Schema, List<string> Details)> FreshInstallAsync() => freshInstall.Value;

    private static async Task<(SchemaSnapshot Schema, List<string> Details)> ReadFreshInstallAsync()
    {
        await using OracleWeaselDatabase fresh = await CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_oracle.sql");
        return await fresh.ReadAsync();
    }

    public static async Task<OracleWeaselDatabase> CreateAsync()
    {
        OracleConnectionStringBuilder container = new(ContainerConnectionString());

        // The Testcontainers module gives SYSTEM the application user's password.
        string admin = new OracleConnectionStringBuilder(container.ConnectionString) { UserID = "system" }.ConnectionString;
        string name = NewUserName();

        OracleWeaselDatabase database = new(admin, name, ConnectionStringFor(name));
        await database.CreateUserAsync(name);
        return database;
    }

    /// <summary>A user name no other test has, within Oracle's 128 characters.</summary>
    public static string NewUserName() => $"WEASEL_{Guid.NewGuid():N}".ToUpperInvariant();

    /// <summary>A connection that logs in as <paramref name="user" />.</summary>
    public static string ConnectionStringFor(string user) =>
        new OracleConnectionStringBuilder(ContainerConnectionString()) { UserID = user, Password = Password }.ConnectionString;

    /// <summary>Another user, with the same two privileges, dropped with this one.</summary>
    public async Task CreateUserAsync(string user)
    {
        await AdminExecuteAsync(
            $"CREATE USER {user} IDENTIFIED BY \"{Password}\" QUOTA UNLIMITED ON USERS",
            $"GRANT CREATE SESSION, CREATE TABLE TO {user}");
        users.Add(user);
    }

    /// <summary>Statements run as SYSTEM, for the grants and triggers a test sets up around a user.</summary>
    public async Task AdminExecuteAsync(params string[] statements)
    {
        await using OracleConnection connection = new(adminConnectionString);
        await connection.OpenAsync();

        foreach (string statement in statements)
        {
            await using OracleCommand command = new(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Statements run as this user, one per command, the way ODP.NET takes them.</summary>
    public async Task ExecuteAsync(params string[] statements)
    {
        await using OracleConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        foreach (string statement in statements)
        {
            await using OracleCommand command = new(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using OracleConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using OracleCommand command = new(sql, connection);
        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    /// <summary>A count, which ODP.NET hands back as a <see cref="decimal" />.</summary>
    public async Task<long> CountAsync(string sql) => Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    /// <summary>A count asked as SYSTEM, for another user's objects.</summary>
    public async Task<long> AdminCountAsync(string sql)
    {
        await using OracleConnection connection = new(adminConnectionString);
        await connection.OpenAsync();
        await using OracleCommand command = new(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Tables, columns and indexes the way <see cref="SchemaSnapshot" /> reads them — index directions and
    /// the columns behind a descending key included — and then what it does not: every column's full type
    /// and default, and every primary and foreign key by name and definition, which is what Weasel compares
    /// a foreign key by. The tables under the default <c>QRTZ_</c> prefix.
    /// </summary>
    public async Task<(SchemaSnapshot Schema, List<string> Details)> ReadAsync()
    {
        await using OracleConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        SchemaSnapshot snapshot = await SchemaSnapshot.ReadAsync(connection, "oracle", "QRTZ_");

        List<string> details = [];
        foreach (string sql in (string[]) [ColumnsSql, PrimaryKeysSql, ForeignKeysSql])
        {
            await using OracleCommand command = new(sql, connection);

            // DATA_DEFAULT is a LONG, which ODP.NET reads back empty unless told how much of it to fetch.
            command.InitialLONGFetchSize = -1;

            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                object[] cells = new object[reader.FieldCount];
                reader.GetValues(cells);
                details.Add(string.Join("|", cells.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)?.Trim())));
            }
        }

        details.Sort(StringComparer.Ordinal);
        return (snapshot, details);
    }

    /// <summary>
    /// Runs <c>create_oracle.sql</c> the way the store does for <c>ProvisionSchema()</c>, with the prefix
    /// substituted: <c>{0}</c> in full, <c>{1}</c> without its schema.
    /// </summary>
    public async Task ProvisionAsync(string tablePrefix = "QRTZ_")
    {
        int lastDot = tablePrefix.LastIndexOf('.');
        string unqualified = lastDot < 0 ? tablePrefix : tablePrefix[(lastDot + 1)..];

        using Stream stream = typeof(IScheduler).Assembly.GetManifestResourceStream("Quartz.Impl.AdoJobStore.Schema.create_oracle.sql")!;
        using StreamReader reader = new(stream);
        string script = await reader.ReadToEndAsync();

        await ExecuteAsync([.. script.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n--;;\n").Skip(1)
            .Select(x => string.Format(CultureInfo.InvariantCulture, x, tablePrefix, unqualified))]);
    }

    /// <summary>
    /// A script under the repository root — a fresh install, or a released version's — run as this user by
    /// the container's own <c>sqlplus</c>, which is what understands the script's PL/SQL blocks.
    /// </summary>
    public async Task RunRepositoryScriptAsync(params string[] path)
    {
        string script = await File.ReadAllTextAsync(ResolveRepositoryFile(path));
        await TestcontainersDatabaseEnvironment.ExecuteOracleScriptAsync(Name, Password, script);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string user in users)
        {
            OracleConnection.ClearPool(new OracleConnection(ConnectionStringFor(user)));
        }

        foreach (string user in Enumerable.Reverse(users))
        {
            await DropUserAsync(user);
        }
    }

    /// <summary>
    /// A container with one scheduler whose Oracle store hands its schema to Weasel, and the Weasel database
    /// it produces — reached the way <c>db-apply</c> reaches it.
    /// </summary>
    /// <param name="schedulerName">The scheduler, which is also the Weasel database's identifier.</param>
    /// <param name="tablePrefix">The store's table prefix.</param>
    /// <param name="connectionString">Another login than this user's.</param>
    public async Task<(ServiceProvider Services, IDatabase Database)> WeaselAsync(
        string schedulerName,
        string tablePrefix = "QRTZ_",
        string? connectionString = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseOracle(OracleClientFactory.Instance, connectionString ?? ConnectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForOracle();
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        return (provider, databases.Single());
    }

    /// <summary>
    /// Drops a user and what it owns, ending any session it still has first: a session left behind by a
    /// pool the test could not reach would otherwise refuse the drop (ORA-01940).
    /// </summary>
    private async Task DropUserAsync(string user)
    {
        await using OracleConnection connection = new(adminConnectionString);
        await connection.OpenAsync();

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using OracleCommand drop = new($"DROP USER {user} CASCADE", connection);
                await drop.ExecuteNonQueryAsync();
                return;
            }
            catch (OracleException e) when (e.Number == 1940 && attempt < 5)
            {
                await using OracleCommand sessions = new($"SELECT sid, serial# FROM v$session WHERE username = '{user}'", connection);
                List<string> kill = [];
                await using (DbDataReader reader = await sessions.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        kill.Add($"ALTER SYSTEM KILL SESSION '{reader.GetValue(0)},{reader.GetValue(1)}' IMMEDIATE");
                    }
                }

                foreach (string statement in kill)
                {
                    try
                    {
                        await using OracleCommand command = new(statement, connection);
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (OracleException)
                    {
                        // Gone between the query and the kill.
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }

    private static string ContainerConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING");
        connectionString.Should().NotBeNullOrWhiteSpace("the db-oracle leg's container publishes it");
        return connectionString!;
    }

    private const string ColumnsSql = """
        SELECT 'COL', table_name, column_name, data_type, data_length, NVL(data_precision, -1), NVL(data_scale, -1), char_used, nullable, data_default
        FROM user_tab_columns
        WHERE table_name LIKE 'QRTZ!_%' ESCAPE '!'
        """;

    private const string PrimaryKeysSql = """
        SELECT 'PK', c.table_name, c.constraint_name, LISTAGG(cc.column_name, ',') WITHIN GROUP (ORDER BY cc.position)
        FROM user_constraints c
        JOIN user_cons_columns cc ON cc.constraint_name = c.constraint_name
        WHERE c.constraint_type = 'P' AND c.table_name LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY c.table_name, c.constraint_name
        """;

    private const string ForeignKeysSql = """
        SELECT 'FK', c.table_name, c.constraint_name, r.table_name, LISTAGG(cc.column_name, ',') WITHIN GROUP (ORDER BY cc.position), c.delete_rule
        FROM user_constraints c
        JOIN user_constraints r ON r.owner = c.r_owner AND r.constraint_name = c.r_constraint_name
        JOIN user_cons_columns cc ON cc.constraint_name = c.constraint_name
        WHERE c.constraint_type = 'R' AND c.table_name LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY c.table_name, c.constraint_name, r.table_name, c.delete_rule
        """;

    private static string ResolveRepositoryFile(params string[] path)
    {
        string relative = Path.Combine(path);

        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{relative} is not under any directory above {AppContext.BaseDirectory}");
    }
}
