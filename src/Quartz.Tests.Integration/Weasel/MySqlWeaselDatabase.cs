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

using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using MySqlConnector;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core.Migrations;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// An empty MySQL database of a test's own, on the shared container, dropped when the test is done.
/// </summary>
/// <remarks>
/// <para>
/// A database rather than a table prefix, because the fresh-install script drops and recreates the
/// <c>QRTZ_</c> tables of whatever database it runs in, and the tests run side by side.
/// </para>
/// <para>
/// The store connects as the container's application user, which is granted everything on this
/// database and nothing on the server: no <c>CREATE</c> of databases in general, which is the privilege
/// Weasel used to ask for on every apply (JasperFx/weasel#647).
/// </para>
/// </remarks>
internal sealed class MySqlWeaselDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string applicationUser;
    private readonly List<string> created = [];

    private MySqlWeaselDatabase(string adminConnectionString, string applicationUser, string name, string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        this.applicationUser = applicationUser;
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }

    /// <summary>The application user's connection, opening in this database.</summary>
    public string ConnectionString { get; }

    public static async Task<MySqlWeaselDatabase> CreateAsync()
    {
        MySqlConnectionStringBuilder container = new(ContainerConnectionString());

        // The Testcontainers module gives root the application user's password.
        string admin = new MySqlConnectionStringBuilder(container.ConnectionString) { UserID = "root", Database = "" }.ConnectionString;
        string name = $"weasel_{Guid.NewGuid():N}";

        MySqlWeaselDatabase database = new(
            admin,
            container.UserID,
            name,
            new MySqlConnectionStringBuilder(container.ConnectionString) { Database = name }.ConnectionString);

        await database.CreateDatabaseAsync(name);
        return database;
    }

    /// <summary>Another empty database the application user may use, dropped with this one.</summary>
    public async Task CreateDatabaseAsync(string name)
    {
        await ExecuteAsync(adminConnectionString, $"CREATE DATABASE `{name}`; GRANT ALL PRIVILEGES ON `{name}`.* TO '{applicationUser}'@'%';");
        created.Add(name);
    }

    public Task ExecuteAsync(string sql) => ExecuteAsync(ConnectionString, sql);

    public async Task<object?> ScalarAsync(string sql)
    {
        await using MySqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using MySqlCommand command = new(sql, connection);
        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    /// <summary>Asked as root, for what the application user cannot see.</summary>
    public async Task<object?> AdminScalarAsync(string sql)
    {
        await using MySqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();
        await using MySqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// Tables, columns and indexes the way <see cref="SchemaSnapshot" /> reads them, and then what it does
    /// not: every column's full type and default, every primary and foreign key — the names are what Weasel
    /// compares a foreign key by — and each index column's direction. The tables under the default
    /// <c>QRTZ_</c> prefix, in <paramref name="database" /> or this one.
    /// </summary>
    public async Task<(SchemaSnapshot Schema, List<string> Details)> ReadAsync(string? database = null)
    {
        await using MySqlConnection connection = new(new MySqlConnectionStringBuilder(ConnectionString) { Database = database ?? Name }.ConnectionString);
        await connection.OpenAsync();

        SchemaSnapshot snapshot = await SchemaSnapshot.ReadAsync(connection, "mysql_innodb", "QRTZ_");

        List<string> details = [];
        foreach (string sql in (string[]) [ColumnsSql, PrimaryKeysSql, ForeignKeysSql, IndexDirectionsSql])
        {
            await using MySqlCommand command = new(sql, connection);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                object[] cells = new object[reader.FieldCount];
                reader.GetValues(cells);
                details.Add(string.Join("|", cells.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))));
            }
        }

        details.Sort(StringComparer.Ordinal);
        return (snapshot, details);
    }

    /// <summary>
    /// Runs <c>create_mysql_innodb.sql</c> the way the store does for <c>ProvisionSchema()</c>, with the
    /// prefix substituted: <c>{0}</c> in full, <c>{1}</c> without its database.
    /// </summary>
    public async Task ProvisionAsync(string tablePrefix = "QRTZ_")
    {
        int lastDot = tablePrefix.LastIndexOf('.');
        string unqualified = lastDot < 0 ? tablePrefix : tablePrefix[(lastDot + 1)..];

        using Stream stream = typeof(IScheduler).Assembly.GetManifestResourceStream("Quartz.Impl.AdoJobStore.Schema.create_mysql_innodb.sql")!;
        using StreamReader reader = new(stream);
        string script = await reader.ReadToEndAsync();

        await using MySqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        foreach (string statement in script.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n--;;\n").Skip(1))
        {
            await using MySqlCommand command = new(string.Format(CultureInfo.InvariantCulture, statement, tablePrefix, unqualified), connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A script under the repository root — a fresh install, or a released version's — run in this
    /// database by the container's own <c>mysql</c> client, which is what understands the script's
    /// comments and prepared statements.
    /// </summary>
    public async Task RunRepositoryScriptAsync(params string[] path)
    {
        string script = await File.ReadAllTextAsync(ResolveRepositoryFile(path));
        await TestcontainersDatabaseEnvironment.ExecuteScriptAsync("mysql_innodb", $"USE `{Name}`;\n{script}");
    }

    public async ValueTask DisposeAsync()
    {
        MySqlConnection.ClearPool(new MySqlConnection(ConnectionString));

        await ExecuteAsync(adminConnectionString, string.Concat(created.Select(x => $"DROP DATABASE IF EXISTS `{x}`;")));
    }

    /// <summary>
    /// A container with one scheduler whose MySQL store hands its schema to Weasel, and the Weasel database
    /// it produces — reached the way <c>db-apply</c> reaches it.
    /// </summary>
    /// <param name="schedulerName">The scheduler, which is also the Weasel database's identifier.</param>
    /// <param name="tablePrefix">The store's table prefix.</param>
    /// <param name="configure">Adjusts the Weasel options.</param>
    /// <param name="adjustConnection">Adjusts this database's connection string for the store.</param>
    public async Task<(ServiceProvider Services, IDatabase Database)> WeaselAsync(
        string schedulerName,
        string tablePrefix = "QRTZ_",
        Action<MySqlWeaselOptions>? configure = null,
        Action<MySqlConnectionStringBuilder>? adjustConnection = null)
    {
        MySqlConnectionStringBuilder connection = new(ConnectionString);
        adjustConnection?.Invoke(connection);

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseMySqlConnector(MySqlConnectorFactory.Instance, connection.ConnectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForMySql(configure);
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        return (provider, databases.Single());
    }

    private static string ContainerConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING");
        connectionString.Should().NotBeNullOrWhiteSpace("the db-mysql leg's container publishes it");
        return connectionString!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using MySqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private const string ColumnsSql = """
        SELECT 'COL', TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, IFNULL(COLUMN_DEFAULT, '(none)')
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE 'QRTZ!_%' ESCAPE '!'
        """;

    private const string PrimaryKeysSql = """
        SELECT 'PK', TABLE_NAME, CAST(GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS CHAR)
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME = 'PRIMARY' AND TABLE_NAME LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY TABLE_NAME
        """;

    private const string ForeignKeysSql = """
        SELECT 'FK', k.TABLE_NAME, k.CONSTRAINT_NAME, k.REFERENCED_TABLE_NAME,
               CAST(GROUP_CONCAT(k.COLUMN_NAME ORDER BY k.ORDINAL_POSITION) AS CHAR), r.DELETE_RULE
        FROM information_schema.KEY_COLUMN_USAGE k
        JOIN information_schema.REFERENTIAL_CONSTRAINTS r
          ON r.CONSTRAINT_SCHEMA = k.TABLE_SCHEMA AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME
        WHERE k.TABLE_SCHEMA = DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL AND k.TABLE_NAME LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY k.TABLE_NAME, k.CONSTRAINT_NAME, k.REFERENCED_TABLE_NAME, r.DELETE_RULE
        """;

    private const string IndexDirectionsSql = """
        SELECT 'IX', TABLE_NAME, INDEX_NAME,
               CAST(GROUP_CONCAT(CONCAT(COLUMN_NAME, IF(COLLATION = 'D', ' DESC', '')) ORDER BY SEQ_IN_INDEX) AS CHAR), NON_UNIQUE
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME <> 'PRIMARY' AND TABLE_NAME LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE
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
