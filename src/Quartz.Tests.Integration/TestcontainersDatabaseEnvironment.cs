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

using System.Text;

using DotNet.Testcontainers.Containers;

using Microsoft.Data.SqlClient;

using Testcontainers.FirebirdSql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Oracle;
using Testcontainers.PostgreSql;

namespace Quartz.Tests.Integration;

internal static class TestcontainersDatabaseEnvironment
{
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private static bool initialized;

    private static PostgreSqlContainer postgreSqlContainer;
    private static MsSqlContainer sqlServerContainer;
    private static MsSqlContainer sqlServerMotContainer;
    private static MySqlContainer mySqlContainer;
    private static FirebirdSqlContainer firebirdSqlContainer;
    private static OracleContainer oracleContainer;

    public static async Task InitializeAsync()
    {
        await InitializationLock.WaitAsync();

        try
        {
            if (initialized)
            {
                return;
            }

            string targetDatabase = Environment.GetEnvironmentVariable("QUARTZ_TEST_DATABASE")?.ToLowerInvariant();
            bool startAll = string.IsNullOrEmpty(targetDatabase) || targetDatabase == "all";

            // No containers needed for basic or sqlite tests
            if (targetDatabase is "basic" or "sqlite")
            {
                initialized = true;
                return;
            }

            try
            {
                if (startAll || targetDatabase == "postgres")
                {
                    await StartPostgreSqlContainerAsync(await ReadScriptAsync("database", "tables", "tables_postgres.sql"));
                }

                if (startAll || targetDatabase == "sqlserver")
                {
                    await StartSqlServerContainerAsync(await ReadScriptAsync("database", "tables", "tables_sqlServer.sql"));
                    await StartSqlServerMotContainerAsync(await ReadScriptAsync("database", "tables", "tables_sqlServerMOT.sql"));
                }

                if (startAll || targetDatabase == "mysql")
                {
                    await StartMySqlContainerAsync(await ReadScriptAsync("database", "tables", "tables_mysql_innodb.sql"));
                }

                if (startAll || targetDatabase == "firebird")
                {
                    await StartFirebirdSqlContainerAsync(
                        await ReadScriptAsync("database", "tables", "tables_firebird.sql"));
                }

                if (startAll || targetDatabase == "oracle")
                {
                    await StartOracleContainerAsync(await ReadScriptAsync("database", "tables", "tables_oracle.sql"));
                }
            }
            catch
            {
                await DisposeContainersAsync(throwOnError: false);
                throw;
            }

            initialized = true;
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    public static async Task DisposeAsync()
    {
        await InitializationLock.WaitAsync();

        try
        {
            if (!initialized)
            {
                return;
            }

            try
            {
                await DisposeContainersAsync(throwOnError: true);
            }
            finally
            {
                initialized = false;
            }
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    private static async Task StartPostgreSqlContainerAsync(string script)
    {
        postgreSqlContainer = new PostgreSqlBuilder("postgres:15.1")
            .WithDatabase("quartznet")
            .WithUsername("quartznet")
            .WithPassword("quartznet")
            .Build();

        await postgreSqlContainer.StartAsync();

        ExecResult result = await postgreSqlContainer.ExecScriptAsync(script);
        EnsureScriptSucceeded("PostgreSQL", result);

        string connectionString = postgreSqlContainer.GetConnectionString();
        Environment.SetEnvironmentVariable("PG_CONNECTION_STRING", connectionString);
        Environment.SetEnvironmentVariable("PG_USER", "quartznet");
        Environment.SetEnvironmentVariable("PG_PASSWORD", "quartznet");
    }

    /// <summary>
    /// Prepares a SQL Server table script for use with a fresh Testcontainer by replacing
    /// the placeholder database name with 'quartznet' and prepending CREATE DATABASE.
    /// </summary>
    private static string PrepareSqlServerScript(string script)
    {
        // The database/tables/ scripts use placeholder values that need to be replaced
        // for Testcontainers. The MOT script also needs a file path for memory-optimized data.
        script = script
            .Replace("[enter_db_name_here]", "[quartznet]")
            .Replace("[enter_path_here]", "/tmp");

        // Strip USE [master] — it causes sqlcmd to write "Changed database context" to stderr
        // which fails the exit code check. ALTER DATABASE works from any context.
        script = StripUseMasterStatements(script);

        // Prepend CREATE DATABASE before the rest of the script
        script = "CREATE DATABASE quartznet;\nGO\n" + script;

        return script;
    }

    private static string StripUseMasterStatements(string script)
    {
        var lines = script.Split('\n');
        var filtered = new List<string>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim().TrimEnd('\r');
            if (trimmed.Equals("USE [master];", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("USE [master]", StringComparison.OrdinalIgnoreCase))
            {
                // Also skip the following GO statement if present
                if (i + 1 < lines.Length && lines[i + 1].Trim().TrimEnd('\r').Equals("GO", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }
                continue;
            }
            filtered.Add(lines[i]);
        }
        return string.Join('\n', filtered);
    }

    private static async Task StartSqlServerContainerAsync(string script)
    {
        sqlServerContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("Quartz!DockerP4ss")
            .Build();

        await sqlServerContainer.StartAsync();

        ExecResult result = await sqlServerContainer.ExecScriptAsync(PrepareSqlServerScript(script));
        EnsureScriptSucceeded("SQL Server", result);

        string connectionString = sqlServerContainer.GetConnectionString();
        SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "quartznet",
            TrustServerCertificate = true
        };

        Environment.SetEnvironmentVariable("MSSQL_CONNECTION_STRING", builder.ConnectionString);
        Environment.SetEnvironmentVariable("MSSQL_USER", builder.UserID);
        Environment.SetEnvironmentVariable("MSSQL_PASSWORD", builder.Password);
    }

    private static async Task StartSqlServerMotContainerAsync(string script)
    {
        sqlServerMotContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2017-latest")
            .WithPassword("Quartz!DockerP4ss")
            .Build();

        await sqlServerMotContainer.StartAsync();

        ExecResult result = await sqlServerMotContainer.ExecScriptAsync(PrepareSqlServerScript(script));
        EnsureScriptSucceeded("SQL Server (MOT)", result);

        string connectionString = sqlServerMotContainer.GetConnectionString();
        SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "quartznet",
            TrustServerCertificate = true
        };

        Environment.SetEnvironmentVariable("MSSQL_CONNECTION_STRING_MOT", builder.ConnectionString);
    }

    private static async Task StartMySqlContainerAsync(string script)
    {
        mySqlContainer = new MySqlBuilder("mysql:8.0")
            .WithDatabase("quartznet")
            .WithUsername("quartznet")
            .WithPassword("quartznet")
            .Build();

        await mySqlContainer.StartAsync();

        ExecResult result = await mySqlContainer.ExecScriptAsync(script);
        EnsureScriptSucceeded("MySQL", result);

        Environment.SetEnvironmentVariable("MYSQL_CONNECTION_STRING", mySqlContainer.GetConnectionString());
    }

    private const string FirebirdCreateDatabaseScript = "CREATE DATABASE '/firebird/data/quartz.fdb' USER 'SYSDBA' PASSWORD 'masterkey';";

    private static async Task StartFirebirdSqlContainerAsync(string script)
    {
        firebirdSqlContainer = new FirebirdSqlBuilder("jacobalberty/firebird:v4.0")
            .WithDatabase("/firebird/data/quartz.fdb")
            .WithUsername("SYSDBA")
            .WithPassword("masterkey")
            .WithEnvironment("FIREBIRD_DATABASE", string.Empty)
            .Build();

        await firebirdSqlContainer.StartAsync();

        ExecResult createDatabaseResult = await ExecFirebirdAdminScriptAsync(FirebirdCreateDatabaseScript);
        EnsureScriptSucceeded("Firebird database creation", createDatabaseResult);

        // Strip unconditional DROP TABLE statements that fail on a fresh database
        script = StripDropStatements(script);
        ExecResult result = await firebirdSqlContainer.ExecScriptAsync(script);
        EnsureScriptSucceeded("Firebird", result);

        Environment.SetEnvironmentVariable("FIREBIRD_CONNECTION_STRING", firebirdSqlContainer.GetConnectionString());
    }

    private static async Task StartOracleContainerAsync(string script)
    {
        oracleContainer = new OracleBuilder("gvenzl/oracle-xe:21.3.0-slim-faststart")
            .WithUsername("oracle")
            .WithPassword("oracle")
            .Build();

        await oracleContainer.StartAsync();

        ExecResult result = await oracleContainer.ExecScriptAsync(script);
        EnsureScriptSucceeded("Oracle", result);

        Environment.SetEnvironmentVariable("ORACLE_CONNECTION_STRING", oracleContainer.GetConnectionString());
    }

    private static async Task<string> ReadScriptAsync(params string[] pathSegments)
    {
        string scriptPath = ResolveRepositoryFile(pathSegments);
        return await File.ReadAllTextAsync(scriptPath, Encoding.UTF8);
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate required script file.", relativePath);
    }

    /// <summary>
    /// Removes unconditional DROP TABLE/INDEX statements from scripts that lack IF EXISTS
    /// guards (e.g. Firebird), since Testcontainers start with a fresh empty database.
    /// </summary>
    private static string StripDropStatements(string script)
    {
        var lines = script.Split('\n');
        var filtered = lines.Where(line => !line.TrimStart().StartsWith("DROP ", StringComparison.OrdinalIgnoreCase));
        return string.Join('\n', filtered);
    }

    private static void EnsureScriptSucceeded(string provider, ExecResult execResult)
    {
        if (execResult.ExitCode is 0)
        {
            return;
        }

        throw new InvalidOperationException($"Failed to initialize {provider} schema. Exit code: {execResult.ExitCode}. Error: {execResult.Stderr}");
    }

    private static async Task<ExecResult> ExecFirebirdAdminScriptAsync(string scriptContent)
    {
        string scriptFilePath = string.Join("/", string.Empty, "tmp", Guid.NewGuid().ToString("D"), Path.GetRandomFileName());

        await firebirdSqlContainer.CopyAsync(Encoding.Default.GetBytes(scriptContent), scriptFilePath);

        return await firebirdSqlContainer.ExecAsync(
            [
                "/usr/local/firebird/bin/isql",
                "-q",
                "-u",
                "SYSDBA",
                "-p",
                "masterkey",
                "-i",
                scriptFilePath
            ]);
    }

    private static async Task DisposeContainersAsync(bool throwOnError)
    {
        List<Exception> exceptions = throwOnError
            ? []
            : null;

        await DisposeContainerAsync(oracleContainer, exceptions);
        await DisposeContainerAsync(firebirdSqlContainer, exceptions);
        await DisposeContainerAsync(mySqlContainer, exceptions);
        await DisposeContainerAsync(sqlServerMotContainer, exceptions);
        await DisposeContainerAsync(sqlServerContainer, exceptions);
        await DisposeContainerAsync(postgreSqlContainer, exceptions);

        oracleContainer = null;
        firebirdSqlContainer = null;
        mySqlContainer = null;
        sqlServerMotContainer = null;
        sqlServerContainer = null;
        postgreSqlContainer = null;

        if (exceptions is not null && exceptions.Count > 0)
        {
            throw new AggregateException("One or more container disposal operations failed.", exceptions);
        }
    }

    private static async Task DisposeContainerAsync(IAsyncDisposable container, List<Exception> exceptions)
    {
        if (container is null)
        {
            return;
        }

        try
        {
            await container.DisposeAsync();
        }
        catch (Exception ex)
        {
            if (exceptions is not null)
            {
                exceptions.Add(ex);
            }
        }
    }
}
