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
using System.Text.RegularExpressions;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core.Migrations;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// An empty Firebird database file of a test's own, on the shared container's server, dropped when the
/// test is done.
/// </summary>
/// <remarks>
/// <para>
/// A database rather than a table prefix, for the reason <see cref="PostgresWeaselDatabase" /> gives,
/// and because Firebird has no schemas to give a test a namespace of its own inside one.
/// </para>
/// <para>
/// Created with the character set the test asks for — UTF8 or NONE — at a page size of 16384, the
/// smallest that takes the keys over Quartz's long <c>VARCHAR</c> columns in a UTF8 database. The
/// server is whichever the environment started: <c>jacobalberty/firebird:v4.0</c>, or the official
/// image <c>QUARTZ_FIREBIRD_IMAGE</c> names, which is how these tests run on Firebird 3, 4 and 5.
/// </para>
/// </remarks>
internal sealed class FirebirdWeaselDatabase : IAsyncDisposable
{
    private const int PageSize = 16384;

    private FirebirdWeaselDatabase(string connectionString, string path)
    {
        ConnectionString = connectionString;
        DatabasePath = path;
    }

    public string ConnectionString { get; }

    /// <summary>The database file, as the server names it.</summary>
    public string DatabasePath { get; }

    public static async Task<FirebirdWeaselDatabase> CreateAsync(string charset)
    {
        string? server = Environment.GetEnvironmentVariable("FIREBIRD_CONNECTION_STRING");
        if (string.IsNullOrEmpty(server))
        {
            Assert.Ignore("FIREBIRD_CONNECTION_STRING is not set; the Firebird container is not running.");
        }

        FbConnectionStringBuilder builder = new(server);
        string directory = builder.Database[..(builder.Database.LastIndexOf('/') + 1)];
        builder.Database = $"{directory}weasel_{Guid.NewGuid():N}.fdb";
        builder.Charset = charset;

        await FbConnection.CreateDatabaseAsync(builder.ConnectionString, PageSize, forcedWrites: false, overwrite: true);
        return new FirebirdWeaselDatabase(builder.ConnectionString, builder.Database);
    }

    /// <summary>The major version of the server, read from the engine itself.</summary>
    public async Task<int> ServerMajorVersionAsync()
    {
        string version = (string) (await ScalarAsync("SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database"))!;
        return int.Parse(version.Split('.')[0], CultureInfo.InvariantCulture);
    }

    /// <summary>Runs each statement in a transaction of its own.</summary>
    public async Task ExecuteAsync(params string[] statements)
    {
        await using FbConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        foreach (string statement in statements)
        {
            await using FbTransaction transaction = await connection.BeginTransactionAsync();
            await using FbCommand command = new(statement, connection, transaction);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using FbConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using FbCommand command = new(sql, connection);
        object? value = await command.ExecuteScalarAsync();
        return value is string text ? text.TrimEnd() : value;
    }

    public async Task<long> CountAsync(string sql) => Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    /// <summary>
    /// A script under the repository root — a fresh install, or a released version's — run through
    /// the server's own isql, the way a person installs it. The bare <c>DROP TABLE</c> lines of the 3.x
    /// scripts are left out, as <see cref="TestcontainersDatabaseEnvironment" /> leaves them out: they
    /// fail on an empty database.
    /// </summary>
    public async Task RunRepositoryScriptAsync(params string[] path)
    {
        string script = await File.ReadAllTextAsync(ResolveRepositoryFile(path));
        string withoutDrops = string.Join('\n', script.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("DROP ", StringComparison.OrdinalIgnoreCase)));

        await RunIsqlAsync(withoutDrops);
    }

    /// <summary>Runs a script through the server's isql against this database.</summary>
    public Task RunIsqlAsync(string script) => TestcontainersDatabaseEnvironment.ExecuteFirebirdScriptAsync(DatabasePath, script);

    /// <summary>
    /// Creates the schema the way <c>ProvisionSchema()</c> does — by starting a scheduler whose store
    /// provisions it — under <paramref name="tablePrefix" />.
    /// </summary>
    public async Task ProvisionAsync(string tablePrefix = "QRTZ_")
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(o => o.InstanceName = $"provisioning_{Guid.NewGuid():N}")
                .UsePersistentStore(store =>
                {
                    store.UseFirebird(FirebirdClientFactory.Instance, ConnectionString);
                    store.ConfigureStore(o => o.TablePrefix = tablePrefix);
                    store.ProvisionSchema();
                    store.UseSystemTextJsonSerializer();
                }))
            .BuildScheduler();

        await scheduler.Start();
        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    /// <summary>
    /// The tables, columns and indexes under <paramref name="prefix" /> the way
    /// <see cref="SchemaSnapshot" /> reads them, and then what it does not: every column's stored type,
    /// sub-type, precision, scale, length in characters, character set, collation, nullability and
    /// default; and every index — the ones behind a primary or foreign key included — by name, with its
    /// uniqueness, direction, activity, key columns in order, the constraint it backs, that constraint's
    /// rules and the key it references.
    /// </summary>
    /// <remarks>
    /// Column positions are left out, because an apply adds a column at the end of its table where a
    /// fresh install declares it in the middle. A default is compared as its text with the keyword, case
    /// and spacing folded, which is all that differs between <c>default NULL</c> in the script and
    /// Weasel's <c>DEFAULT NULL</c>.
    /// </remarks>
    public async Task<(SchemaSnapshot Schema, List<string> Details)> ReadAsync(string prefix = "QRTZ_")
    {
        await using FbConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        SchemaSnapshot snapshot = await SchemaSnapshot.ReadAsync(connection, "firebird", prefix);

        List<string> details = [];

        await using (FbCommand columns = new(ColumnsSql, connection))
        {
            columns.Parameters.Add("@prefix", prefix);
            await using FbDataReader reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                object[] cells = new object[reader.FieldCount];
                reader.GetValues(cells);
                string defaultSource = cells[^1] is string text ? NormalizeDefault(text) : "";
                details.Add("COL " + string.Join("|", cells[..^1].Select(Text)) + "|" + defaultSource);
            }
        }

        Dictionary<string, List<(int Position, string Column)>> segments = new(StringComparer.Ordinal);
        await using (FbCommand indexes = new(IndexesSql, connection))
        {
            indexes.Parameters.Add("@prefix", prefix);
            await using FbDataReader reader = await indexes.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string key = string.Join("|", Enumerable.Range(0, 10).Select(i => Text(reader.GetValue(i))));
                if (!segments.TryGetValue(key, out List<(int, string)>? list))
                {
                    segments[key] = list = [];
                }

                list.Add((Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture), Text(reader.GetValue(11))));
            }
        }

        details.AddRange(segments.Select(x => "IX " + x.Key + "|" + string.Join(",", x.Value.OrderBy(s => s.Position).Select(s => s.Column))));
        details.Sort(StringComparer.Ordinal);

        return (snapshot, details);
    }

    public async ValueTask DisposeAsync()
    {
        FbConnection.ClearPool(new FbConnection(ConnectionString));

        try
        {
            await FbConnection.DropDatabaseAsync(ConnectionString);
        }
        catch (FbException)
        {
            // Still attached somewhere — a host that has not let go of its pool yet. The file is on a
            // throwaway container, so it goes when the container does.
        }
    }

    /// <summary>
    /// A container with one scheduler whose Firebird store hands its schema to Weasel, and the Weasel
    /// database it produces — reached the way <c>db-apply</c> reaches it.
    /// </summary>
    public async Task<(ServiceProvider Services, IDatabase Database)> WeaselAsync(
        string schedulerName,
        string tablePrefix = "QRTZ_",
        Action<FirebirdWeaselOptions>? configure = null,
        ILoggerFactory? loggerFactory = null)
    {
        ServiceCollection services = new();
        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
        }

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseFirebird(FirebirdClientFactory.Instance, ConnectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForFirebird(configure);
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        return (provider, databases.Single());
    }

    private static string Text(object value) => value is DBNull ? "" : Convert.ToString(value, CultureInfo.InvariantCulture)!.Trim();

    /// <summary><c>default NULL</c>, <c>DEFAULT  NULL</c> and <c>DEFAULT NULL</c> as one text.</summary>
    private static string NormalizeDefault(string source) =>
        Regex.Replace(Regex.Replace(source.Trim(), "^DEFAULT\\s+", "", RegexOptions.IgnoreCase), "\\s+", " ").ToUpperInvariant();

    private const string ColumnsSql = """
        SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME),
               f.RDB$FIELD_TYPE, COALESCE(f.RDB$FIELD_SUB_TYPE, 0), COALESCE(f.RDB$FIELD_PRECISION, 0),
               COALESCE(f.RDB$FIELD_SCALE, 0), COALESCE(f.RDB$CHARACTER_LENGTH, 0), COALESCE(f.RDB$CHARACTER_SET_ID, -1),
               COALESCE(rf.RDB$COLLATION_ID, f.RDB$COLLATION_ID, 0), COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0),
               CAST(COALESCE(rf.RDB$DEFAULT_SOURCE, f.RDB$DEFAULT_SOURCE) AS VARCHAR(8191))
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
        JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
        WHERE rf.RDB$RELATION_NAME STARTING WITH @prefix AND COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
        """;

    private const string IndexesSql = """
        SELECT TRIM(i.RDB$RELATION_NAME), TRIM(i.RDB$INDEX_NAME), COALESCE(i.RDB$UNIQUE_FLAG, 0),
               COALESCE(i.RDB$INDEX_TYPE, 0), COALESCE(i.RDB$INDEX_INACTIVE, 0),
               TRIM(COALESCE(rc.RDB$CONSTRAINT_NAME, '')), TRIM(COALESCE(rc.RDB$CONSTRAINT_TYPE, '')),
               TRIM(COALESCE(ref.RDB$UPDATE_RULE, '')), TRIM(COALESCE(ref.RDB$DELETE_RULE, '')),
               TRIM(COALESCE(i.RDB$FOREIGN_KEY, '')),
               s.RDB$FIELD_POSITION, TRIM(s.RDB$FIELD_NAME)
        FROM RDB$INDICES i
        JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = i.RDB$INDEX_NAME
        LEFT JOIN RDB$RELATION_CONSTRAINTS rc ON rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME
        LEFT JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
        WHERE i.RDB$RELATION_NAME STARTING WITH @prefix AND COALESCE(i.RDB$SYSTEM_FLAG, 0) = 0
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
