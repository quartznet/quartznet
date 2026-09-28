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

using Npgsql;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core.Migrations;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// An empty PostgreSQL database of a test's own, on the shared container, dropped when the test is done.
/// </summary>
/// <remarks>
/// A database rather than a table prefix, because what is under test is everything Weasel and its
/// neighbours do to one database — Marten's and Wolverine's tables, an application's own, and a second
/// Weasel model — and a prefix would share the <c>public</c> schema with every other fixture.
/// </remarks>
internal sealed class PostgresWeaselDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;

    private PostgresWeaselDatabase(string adminConnectionString, string name, string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<PostgresWeaselDatabase> CreateAsync()
    {
        string admin = TestConstants.PostgresConnectionString;
        string name = $"weasel_{Guid.NewGuid():N}";

        await using (NpgsqlConnection connection = new(admin))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = new($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        string connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ToString();
        return new PostgresWeaselDatabase(admin, name, connectionString);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// Tables, columns and indexes the way <see cref="SchemaSnapshot" /> reads them, plus every primary
    /// and foreign key by name and definition — the names are what Weasel compares a constraint by.
    /// </summary>
    public async Task<(SchemaSnapshot Schema, List<string> Constraints)> ReadAsync(string schema = "public", string prefix = "QRTZ_")
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        SchemaSnapshot snapshot = await SchemaSnapshot.ReadAsync(connection, "postgres", prefix);

        List<string> constraints = [];
        await using NpgsqlCommand command = new(
            "SELECT conrelid::regclass::text, conname, pg_get_constraintdef(oid) FROM pg_constraint"
            + " WHERE connamespace = @schema::regnamespace AND contype IN ('p', 'f') ORDER BY 1, 2",
            connection);
        command.Parameters.AddWithValue("schema", schema);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            constraints.Add(string.Join("|", reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return (snapshot, constraints);
    }

    /// <summary>
    /// Runs <c>create_postgres.sql</c> the way the store does for <c>ProvisionSchema()</c>, with the
    /// prefix substituted: <c>{0}</c> in full, <c>{1}</c> without its schema.
    /// </summary>
    public async Task ProvisionAsync(string tablePrefix = "QRTZ_")
    {
        int lastDot = tablePrefix.LastIndexOf('.');
        string unqualified = lastDot < 0 ? tablePrefix : tablePrefix[(lastDot + 1)..];

        using Stream stream = typeof(IScheduler).Assembly.GetManifestResourceStream("Quartz.Impl.AdoJobStore.Schema.create_postgres.sql")!;
        using StreamReader reader = new(stream);
        string script = await reader.ReadToEndAsync();

        foreach (string statement in script.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n--;;\n").Skip(1))
        {
            await ExecuteAsync(string.Format(CultureInfo.InvariantCulture, statement, tablePrefix, unqualified));
        }
    }

    /// <summary>A script under the repository root: a fresh install, or a released version's.</summary>
    public async Task RunRepositoryScriptAsync(params string[] path) =>
        await ExecuteAsync(await File.ReadAllTextAsync(ResolveRepositoryFile(path)));

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));

        await using NpgsqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new($"DROP DATABASE IF EXISTS {Name} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A container with one scheduler whose PostgreSQL store hands its schema to Weasel, and the Weasel
    /// database it produces — reached the way <c>db-apply</c> reaches it.
    /// </summary>
    public async Task<(ServiceProvider Services, IDatabase Database)> WeaselAsync(
        string schedulerName,
        string tablePrefix = "QRTZ_",
        Action<PostgresWeaselOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UsePostgres(NpgsqlFactory.Instance, ConnectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForPostgres(configure);
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        return (provider, databases.Single());
    }

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
