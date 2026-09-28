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

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core.Migrations;
using Weasel.SqlServer;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// An empty SQL Server database of a test's own, on the shared container, dropped when the test is done.
/// </summary>
/// <remarks>
/// A database rather than a table prefix, for the reason <see cref="PostgresWeaselDatabase" /> gives, and
/// because an application lock is scoped to one database: two tests on one database would wait for each
/// other's applies.
/// </remarks>
internal sealed class SqlServerWeaselDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;

    private SqlServerWeaselDatabase(string adminConnectionString, string name, string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<SqlServerWeaselDatabase> CreateAsync()
    {
        string admin = new SqlConnectionStringBuilder(TestConstants.SqlServerConnectionString) { InitialCatalog = "master" }.ConnectionString;
        string name = $"weasel_{Guid.NewGuid():N}";

        await using (SqlConnection connection = new(admin))
        {
            await connection.OpenAsync();
            await using SqlCommand command = new($"CREATE DATABASE [{name}]", connection);
            await command.ExecuteNonQueryAsync();
        }

        string connectionString = new SqlConnectionStringBuilder(admin) { InitialCatalog = name }.ConnectionString;
        return new SqlServerWeaselDatabase(admin, name, connectionString);
    }

    /// <summary>Runs SQL that may carry <c>GO</c> separators, one batch at a time.</summary>
    public Task ExecuteAsync(string sql) => ExecuteAsync(ConnectionString, sql);

    public async Task<object?> ScalarAsync(string sql)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// Tables, columns and indexes the way <see cref="SchemaSnapshot" /> reads them, and then what it does
    /// not: every primary and foreign key by name and definition — the names are what Weasel compares a
    /// foreign key by — each index column's direction, and each column's default. The tables under the
    /// default <c>QRTZ_</c> prefix.
    /// </summary>
    public async Task<(SchemaSnapshot Schema, List<string> Details)> ReadAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        SchemaSnapshot snapshot = await SchemaSnapshot.ReadAsync(connection, "sqlServer", "QRTZ_");

        List<string> details = [];
        foreach (string sql in (string[]) [PrimaryKeysSql, ForeignKeysSql, IndexDirectionsSql, DefaultsSql])
        {
            await using SqlCommand command = new(sql, connection);
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
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
    /// Runs <c>create_sqlServer.sql</c> the way the store does for <c>ProvisionSchema()</c>, with the
    /// prefix substituted: <c>{0}</c> in full, <c>{1}</c> without its schema.
    /// </summary>
    public async Task ProvisionAsync(string tablePrefix = "QRTZ_")
    {
        int lastDot = tablePrefix.LastIndexOf('.');
        string unqualified = lastDot < 0 ? tablePrefix : tablePrefix[(lastDot + 1)..];

        using Stream stream = typeof(IScheduler).Assembly.GetManifestResourceStream("Quartz.Impl.AdoJobStore.Schema.create_sqlServer.sql")!;
        using StreamReader reader = new(stream);
        string script = await reader.ReadToEndAsync();

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        foreach (string statement in script.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n--;;\n").Skip(1))
        {
            await using SqlCommand command = new(string.Format(CultureInfo.InvariantCulture, statement, tablePrefix, unqualified), connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A script under the repository root — a fresh install, or a released version's — with its
    /// placeholders filled in for this database, the way <c>TestcontainersDatabaseEnvironment</c> fills
    /// them.
    /// </summary>
    /// <param name="asAdministrator">
    /// Runs from <c>master</c>, for a script that alters the database it then switches to.
    /// </param>
    /// <param name="path">The script, relative to the repository root.</param>
    public async Task RunRepositoryScriptAsync(bool asAdministrator, params string[] path)
    {
        string script = (await File.ReadAllTextAsync(ResolveRepositoryFile(path)))
            .Replace("[enter_db_name_here]", $"[{Name}]", StringComparison.Ordinal)
            .Replace(@"[enter_path_here]\MemoryOptimizedData", $"/var/opt/mssql/data/{Name}_mod", StringComparison.Ordinal);

        await ExecuteAsync(asAdministrator ? adminConnectionString : ConnectionString, script);
    }

    public Task RunRepositoryScriptAsync(params string[] path) => RunRepositoryScriptAsync(false, path);

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearPool(new SqlConnection(ConnectionString));

        await using SqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(
            $"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A container with one scheduler whose SQL Server store hands its schema to Weasel, and the Weasel
    /// database it produces — reached the way <c>db-apply</c> reaches it.
    /// </summary>
    /// <param name="schedulerName">The scheduler, which is also the Weasel database's identifier.</param>
    /// <param name="tablePrefix">The store's table prefix.</param>
    /// <param name="configure">Adjusts the Weasel options.</param>
    /// <param name="adjustConnection">Adjusts this database's connection string for the store.</param>
    public async Task<(ServiceProvider Services, IDatabase Database)> WeaselAsync(
        string schedulerName,
        string tablePrefix = "QRTZ_",
        Action<SqlServerWeaselOptions>? configure = null,
        Action<SqlConnectionStringBuilder>? adjustConnection = null)
    {
        SqlConnectionStringBuilder connection = new(ConnectionString);
        adjustConnection?.Invoke(connection);

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(SqlClientFactory.Instance, connection.ConnectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForSqlServer(configure);
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        return (provider, databases.Single());
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        foreach (string batch in SqlServerBatchSplitter.Split(sql))
        {
            await using SqlCommand command = new(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private const string PrimaryKeysSql = """
        SELECT 'PK', t.name, kc.name,
               STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal), i.type_desc
        FROM sys.key_constraints kc
        JOIN sys.tables t ON t.object_id = kc.parent_object_id
        JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE kc.type = 'PK' AND t.name LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY t.name, kc.name, i.type_desc
        """;

    private const string ForeignKeysSql = """
        SELECT 'FK', OBJECT_NAME(fk.parent_object_id), fk.name, OBJECT_NAME(fk.referenced_object_id),
               STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY fkc.constraint_column_id), fk.delete_referential_action_desc
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        WHERE OBJECT_NAME(fk.parent_object_id) LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY fk.parent_object_id, fk.name, fk.referenced_object_id, fk.delete_referential_action_desc
        """;

    private const string IndexDirectionsSql = """
        SELECT 'IX', t.name, i.name,
               STRING_AGG(c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END, ',') WITHIN GROUP (ORDER BY ic.key_ordinal),
               i.type_desc, i.is_unique
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.is_primary_key = 0 AND t.name LIKE 'QRTZ!_%' ESCAPE '!'
        GROUP BY t.name, i.name, i.type_desc, i.is_unique
        """;

    private const string DefaultsSql = """
        SELECT 'DF', t.name, c.name, dc.definition
        FROM sys.default_constraints dc
        JOIN sys.tables t ON t.object_id = dc.parent_object_id
        JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE t.name LIKE 'QRTZ!_%' ESCAPE '!'
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
