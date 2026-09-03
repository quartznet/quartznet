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
using System.IO;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The job a trigger belongs to, read by a process that does not have the job's class.
/// </summary>
/// <remarks>
/// <para>
/// <c>SelectJobForTrigger</c> is the read behind <c>ReplaceTrigger</c> and <c>UpdateTriggerDetails</c>,
/// and what those two do with the answer is decide whether the trigger they write is <c>WAITING</c> or
/// <c>BLOCKED</c>. That decision is <see cref="IJobDetail.ConcurrentExecutionDisallowed" />, so the
/// answer has to be truthful without the class — otherwise a schedule-editing process either throws
/// (#3705, which is what it did) or, worse, quietly stores a non-concurrent job's trigger as ready to
/// run while the job is running.
/// </para>
/// <para>
/// The row is written by an ordinary scheduler and read back through the dialect delegate, against a
/// real SQLite file, so what is asserted is what the store actually stores.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class SelectJobForTriggerFlagsSqliteTest
{
    private const string SchedulerName = "select-job-for-trigger";

    private static readonly JobKey JobKey = new JobKey("cleanup", "acme");
    private static readonly TriggerKey TriggerKey = new TriggerKey("cleanup", "acme");

    private string databaseFile;
    private string connectionString;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-select-job-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";

        InstallSchemaFromFreshInstallScript();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public async Task TheAttributeFlagsComeFromTheRowWhenTheClassIsNotLoaded()
    {
        await GivenAStoredNonConcurrentJob();

        IJobDetail detail = await SelectJobForTrigger(new NullTypeLoader(), loadJobType: false);

        detail.Should().NotBeNull();

        detail.JobType.Should().BeNull("the class was not asked for, and this process is one that could not answer");

        detail.ConcurrentExecutionDisallowed.Should().BeTrue(
            "IS_NONCONCURRENT is the record of what the attribute said when the job was stored, and it "
            + "is readable without the assembly the attribute is written in - a deduced flag would answer "
            + "false here, and false is what would store a replacement trigger WAITING while the job is running");
        detail.PersistJobDataAfterExecution.Should().BeTrue(
            "IS_UPDATE_DATA says the same about the other attribute");
    }

    /// <summary>
    /// Asking for the class is still asking for the class.
    /// </summary>
    /// <remarks>
    /// <c>loadJobType: true</c> is the fire path's read, and it must keep failing loudly rather than
    /// handing back a detail whose type is <see langword="null" />. What #3705 changed is which callers
    /// ask for it, not what asking means.
    /// </remarks>
    [Test]
    public async Task AskingForTheClassStillFailsWhenItIsNotThere()
    {
        await GivenAStoredNonConcurrentJob();

        Func<Task> act = async () => await SelectJobForTrigger(new AssemblylessTypeLoader(), loadJobType: true);

        await act.Should().ThrowAsync<TypeLoadException>()
            .WithMessage($"*{typeof(MessageCleanupJob).FullName}*");
    }

    /// <summary>
    /// Writes the job and its trigger through an ordinary scheduler that has the class.
    /// </summary>
    private async Task GivenAStoredNonConcurrentJob()
    {
        SchedulerBuilder config = SchedulerBuilder.Create("writer", SchedulerName);
        config.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });

        // Never started: nothing needs to fire for the row to exist.
        IScheduler scheduler = await config.BuildScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<MessageCleanupJob>()
                .WithIdentity(JobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity(TriggerKey)
                .StartAt(DateTimeOffset.UtcNow.AddYears(1))
                .Build());

        await scheduler.Shutdown();
    }

    private async Task<IJobDetail> SelectJobForTrigger(ITypeLoadHelper typeLoader, bool loadJobType)
    {
        SQLiteDelegate driverDelegate = new SQLiteDelegate();
        driverDelegate.Initialize(new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = "reader",
            DbProvider = new DbProvider("SQLite-Microsoft", connectionString),
            TypeLoadHelper = typeLoader,
            ObjectSerializer = new SystemTextJsonObjectSerializer(),
        });

        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            using (ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction: null))
            {
                return await driverDelegate.SelectJobForTrigger(holder, TriggerKey, typeLoader, loadJobType);
            }
        }
    }

    private void InstallSchemaFromFreshInstallScript()
    {
        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
                command.ExecuteNonQuery();
            }
        }
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// The loader an administration node has: it resolves the types that process knows about, and
    /// answers nothing for the ones compiled into the worker.
    /// </summary>
    private sealed class NullTypeLoader : ITypeLoadHelper
    {
        public void Initialize()
        {
        }

        public Type LoadType(string name) => null;
    }

    /// <summary>
    /// What <see cref="SimpleTypeLoadHelper" /> does in a process that does not have the assembly: the
    /// stored name is in this test's own assembly, so the refusal has to be written out.
    /// </summary>
    private sealed class AssemblylessTypeLoader : ITypeLoadHelper
    {
        public void Initialize()
        {
        }

        public Type LoadType(string name) => throw new TypeLoadException($"Could not load type '{name}'");
    }

    /// <summary>
    /// The job as the worker compiles it, attributes and all.
    /// </summary>
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public sealed class MessageCleanupJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
