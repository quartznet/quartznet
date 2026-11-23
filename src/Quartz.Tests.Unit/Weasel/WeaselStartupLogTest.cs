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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// What a host whose SQLite store hands its schema to Weasel says about the schema at startup: the
/// statements it ran and that it changed the database, and then, on the next start, that nothing needed to.
/// </summary>
/// <remarks>
/// Each of those lines is guarded by <c>ILogger.IsEnabled</c>, so each is asserted with its level on.
/// </remarks>
public sealed class WeaselStartupLogTest
{
    private const int SchemaApplied = 10000;
    private const int SchemaUnchanged = 10001;
    private const int SchemaChange = 10004;

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("weasel-startup-log");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task AFirstStartSaysItChangedTheSchemaAndASecondSaysItMatches()
    {
        using FakeLoggerProvider first = new();
        await StartAndStop("weasel-log-first", first);

        List<FakeLogRecord> firstRecords = first.Collector.GetSnapshot().Where(x => x.Category == "Quartz.Weasel").ToList();
        firstRecords.Should().Contain(x => x.Id.Id == SchemaChange && x.Level == LogLevel.Information
                                           && x.Message.Contains("weasel-log-first") && x.Message.Contains("CREATE TABLE"),
            "every statement Weasel runs against the database is written down");
        firstRecords.Should().ContainSingle(x => x.Id.Id == SchemaApplied).Which.Level.Should().Be(LogLevel.Information);
        firstRecords.Should().NotContain(x => x.Id.Id == SchemaUnchanged);

        using FakeLoggerProvider second = new();
        await StartAndStop("weasel-log-second", second);

        List<FakeLogRecord> secondRecords = second.Collector.GetSnapshot().Where(x => x.Category == "Quartz.Weasel").ToList();
        secondRecords.Should().ContainSingle(x => x.Id.Id == SchemaUnchanged).Which.Level.Should().Be(LogLevel.Debug);
        secondRecords.Should().NotContain(x => x.Id.Id == SchemaApplied || x.Id.Id == SchemaChange);
    }

    private async Task StartAndStop(string schedulerName, FakeLoggerProvider logs)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production,
        });

        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(logs);
        builder.Services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.UseSystemTextJsonSerializer();
                store.UseWeaselForSqlite();
            });
        });
        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        using IHost host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }
}
