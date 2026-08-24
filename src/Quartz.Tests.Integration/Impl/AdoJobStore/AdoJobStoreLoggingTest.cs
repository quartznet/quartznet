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

using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Jobs;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A persistent store logs into the container's logging, with nothing said to
/// <see cref="Quartz.Diagnostics.LogProvider" />.
/// </summary>
/// <remarks>
/// This is the half of the store nobody could see before: the store itself, the lock handler it chose
/// and the command preparation underneath the driver delegate all created their loggers through the
/// ambient factory, which is <c>NullLogger</c> in an application that never set it — so a hosted
/// application saw acquisition, locking and schema failures from none of them. SQLite because it needs
/// no container, and because it exercises the lock handler the store picks for itself.
/// </remarks>
[TestFixture]
[Category("db-sqlite")]
[NonParallelizable]
public sealed class AdoJobStoreLoggingTest
{
    private string dbFileName = null!;

    [SetUp]
    public async Task SetUp()
    {
        dbFileName = $"test-store-logging-{Guid.NewGuid():N}.db";

        await using SqliteConnection connection = new($"Data Source={dbFileName};");
        await connection.OpenAsync();
        await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
        await command.ExecuteNonQueryAsync();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(dbFileName))
        {
            try
            {
                File.Delete(dbFileName);
            }
            catch (IOException)
            {
                // scratch space; leaving it behind is not worth failing a test over
            }
        }
    }

    [Test]
    public async Task ThePersistentStoreAndItsPartsLogThroughTheContainer()
    {
        CategoryCapturingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));

        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = "logging-store");
            quartz.UsePersistentStore(store =>
            {
                store.UseSqlite($"Data Source={dbFileName};");
                store.UseSystemTextJsonSerializer();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            // A scheduling call, so the lock handler is taken and a statement is prepared. Initializing
            // the store alone would only prove the store's own lines arrive.
            await scheduler.ScheduleJob(
                JobBuilder.Create<NoOpJob>().WithIdentity("job").StoreDurably().Build(),
                TriggerBuilder.Create().WithIdentity("trigger").StartAt(DateTimeOffset.UtcNow.AddDays(1)).Build());
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        recorder.Categories.Should().Contain("Quartz.Impl.AdoJobStore.LocalTransactionJobStore",
            "the store's own lines - the lock handler it chose, the isolation level it forced, a failed "
            + "schema validation - reach an application that configured logging and nothing else");
        recorder.Categories.Should().Contain("Quartz.Impl.AdoJobStore.SQLiteSemaphore",
            "the lock handler learns where to log from the context the store initializes it with");
        recorder.Categories.Should().Contain("Quartz.Impl.AdoJobStore.AdoUtil",
            "so does the command preparation underneath the driver delegate");
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Records which categories wrote anything, which is what "this type logs through the container"
    /// means; the messages themselves belong to the types that write them.
    /// </summary>
    private sealed class CategoryCapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, byte> categories = new(StringComparer.Ordinal);

        public ICollection<string> Categories => categories.Keys;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CategoryCapturingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                provider.categories.TryAdd(category, 0);
            }
        }
    }
}
