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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

            string postgresScript = ReadScript("database", "tables", "tables_postgres.sql");
            string sqlServerScript = ReadScript("docker", "sqlserver", "tables_sqlServer.sql");
            string sqlServerMotScript = ReadScript("docker", "sqlserver-mot", "tables_sqlServerMOT.sql");
            string mySqlScript = ReadScript("database", "tables", "tables_mysql_innodb.sql");
            string firebirdCreateDatabaseScript = ReadScript("docker", "firebird", "create_database.sql");
            string firebirdScript = ReadScript("docker", "firebird", "tables_firebird.sql");
            string oracleScript = ReadScript("docker", "oracle", "tables_oracle.sql");

            try
            {
                await Task.WhenAll(
                    StartPostgreSqlContainerAsync(postgresScript),
                    StartSqlServerContainerAsync(sqlServerScript),
                    StartSqlServerMotContainerAsync(sqlServerMotScript),
                    StartMySqlContainerAsync(mySqlScript),
                    StartFirebirdSqlContainerAsync(firebirdCreateDatabaseScript, firebirdScript),
                    StartOracleContainerAsync(oracleScript)
                );
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

    private static async Task StartSqlServerContainerAsync(string script)
    {
        sqlServerContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("Quartz!DockerP4ss")
            .Build();

        await sqlServerContainer.StartAsync();

        ExecResult result = await sqlServerContainer.ExecScriptAsync(script);
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

        ExecResult result = await sqlServerMotContainer.ExecScriptAsync(script);
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

    private static async Task StartFirebirdSqlContainerAsync(string createDatabaseScript, string script)
    {
        firebirdSqlContainer = new FirebirdSqlBuilder("jacobalberty/firebird:v4.0")
            .WithDatabase("/firebird/data/quartz.fdb")
            .WithUsername("SYSDBA")
            .WithPassword("masterkey")
            .WithEnvironment("FIREBIRD_DATABASE", string.Empty)
            .Build();

        await firebirdSqlContainer.StartAsync();

        ExecResult createDatabaseResult = await ExecFirebirdAdminScriptAsync(createDatabaseScript);
        EnsureScriptSucceeded("Firebird database creation", createDatabaseResult);

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

    private static string ReadScript(params string[] pathSegments)
    {
        string scriptPath = ResolveRepositoryFile(pathSegments);
        return File.ReadAllText(scriptPath, Encoding.UTF8);
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
