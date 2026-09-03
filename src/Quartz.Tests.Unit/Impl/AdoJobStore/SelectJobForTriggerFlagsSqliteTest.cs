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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

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
public sealed class SelectJobForTriggerFlagsSqliteTest
{
    private const string SchedulerName = "select-job-for-trigger";

    /// <summary>
    /// The stored <c>JOB_CLASS_NAME</c>: a name no assembly in this process carries, which is what an
    /// administration node sees when the job classes live in a worker it does not reference.
    /// </summary>
    private const string StoredJobClassName = "Acme.Jobs.MessageCleanupJob, Acme.Jobs";

    private static readonly JobKey JobKey = new("cleanup", "acme");
    private static readonly TriggerKey TriggerKey = new("cleanup", "acme");

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-select-job-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
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

        IJobDetail? detail = await SelectJobForTrigger(new NullTypeLoader(), loadJobType: false);

        detail.Should().NotBeNull();

        detail!.JobType.TryResolve(out _).Should().BeFalse(
            "the stored class is in an assembly this process does not have, which is the whole arrangement");

        detail.ConcurrentExecutionDisallowed.Should().BeTrue(
            "IS_NONCONCURRENT is the record of what the attribute said when the job was stored, and it "
            + "is readable without the assembly the attribute is written in");
        detail.PersistJobDataAfterExecution.Should().BeTrue(
            "IS_UPDATE_DATA says the same about the other attribute");

        JobDetailFlags.ConcurrentExecutionDisallowed(detail).Should().BeTrue(
            "the flag is stated rather than deduced — a deduced one would answer false here, and false "
            + "is what would store a replacement trigger WAITING while the job is running");
        JobDetailFlags.PersistJobDataAfterExecution(detail).Should().BeTrue();
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

        Func<Task> act = async () => await SelectJobForTrigger(new SimpleTypeLoader(), loadJobType: true);

        await act.Should().ThrowAsync<TypeLoadException>()
            .WithMessage($"*{StoredJobClassName}*");
    }

    /// <summary>
    /// Writes the job and its trigger through an ordinary scheduler.
    /// </summary>
    /// <remarks>
    /// The detail is built over a <see cref="Quartz.JobType" /> that carries the stored name and resolves to
    /// the real class, which is what a worker holding the assembly has: the two flags reach the row
    /// from the attributes, and the row afterwards names a class nothing here can load.
    /// </remarks>
    private async Task GivenAStoredNonConcurrentJob()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "writer";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });
        });

        await using ServiceProvider container = services.BuildServiceProvider();

        // Never started: nothing needs to fire for the row to exist.
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create()
                .WithIdentity(JobKey)
                .OfType(new Quartz.JobType(StoredJobClassName, _ => typeof(MessageCleanupJob)))
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity(TriggerKey)
                .StartAt(DateTimeOffset.UtcNow.AddYears(1))
                .Build());

        await scheduler.Shutdown();
    }

    private async Task<IJobDetail?> SelectJobForTrigger(ITypeLoader typeLoader, bool loadJobType)
    {
        SQLiteDelegate driverDelegate = new();
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = AdoConstants.DefaultTablePrefix,
            SchedulerName = SchedulerName,
            InstanceId = "reader",
            DbProvider = Provider(),
            TypeLoader = typeLoader,
            ObjectSerializer = new SystemTextJsonObjectSerializer(),
        });

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        using ConnectionAndTransactionHolder holder = new(connection, transaction: null);

        return await driverDelegate.SelectJobForTrigger(holder, TriggerKey, typeLoader, loadJobType);
    }

    private IDbProvider Provider()
    {
        DbMetadata metadata = DbMetadataResolver.BuiltIn().ResolveWithoutTypes("SQLite-Microsoft");
        return new ProviderFactoryDbProvider(metadata, SqliteFactory.Instance, connectionString);
    }

    /// <summary>
    /// The loader an administration node has: it resolves the types that process knows about, and
    /// answers nothing for the ones compiled into the worker.
    /// </summary>
    private sealed class NullTypeLoader : ITypeLoader
    {
        public Type? LoadType(string name) => null;
    }

    /// <summary>
    /// The job as the worker compiles it, attributes and all.
    /// </summary>
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public sealed class MessageCleanupJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
