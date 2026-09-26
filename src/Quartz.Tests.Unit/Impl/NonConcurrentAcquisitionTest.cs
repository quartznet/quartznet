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

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Acquisition takes one trigger per non-concurrent job into a batch, however the job says it is
/// non-concurrent.
/// </summary>
/// <remarks>
/// <para>
/// A job disallows concurrent execution through <see cref="DisallowConcurrentExecutionAttribute" /> on its
/// type or through <c>DisallowConcurrentExecution()</c> on its builder. The fire path obeys the stored
/// flag either way. The ADO store's acquisition pre-filter used to ask the job's <em>type</em>, so a job
/// flagged only by its builder was let into one batch twice. The fire path declined the second firing,
/// so the job never ran twice at once, but the batch spent a slot on a trigger that could not fire.
/// </para>
/// <para>
/// Both stores are held to it, against the real store: the in-memory one reads the job detail, and the
/// ADO one is exercised on a SQLite file.
/// </para>
/// </remarks>
[TestFixture("ram")]
[TestFixture("sqlite")]
public sealed class NonConcurrentAcquisitionTest
{
    private readonly string storeKind;
    private SqliteTestDatabase? database;
    private ServiceProvider container = null!;
    private IScheduler scheduler = null!;
    private IJobStore store = null!;

    public NonConcurrentAcquisitionTest(string storeKind)
    {
        this.storeKind = storeKind;
    }

    [SetUp]
    public async Task SetUp()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "non-concurrent-acquisition";
                options.InstanceId = "node";
            });

            if (storeKind == "sqlite")
            {
                database = new SqliteTestDatabase("non-concurrent-acquisition");
                q.UsePersistentStore(persistent =>
                {
                    persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                    persistent.ProvisionSchema();
                });
            }
        });

        container = services.BuildServiceProvider();

        // Never started: the batch is asked for by hand, so nothing else can take the triggers first.
        scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        store = container.GetRequiredService<IJobStore>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await scheduler.Shutdown();
        await container.DisposeAsync();
        database?.Dispose();
    }

    [Test]
    public async Task AJobFlaggedByItsBuilderIsTakenOncePerBatch()
    {
        IJobDetail job = JobBuilder.Create<UnattributedJob>()
            .WithIdentity("flagged-by-builder")
            .DisallowConcurrentExecution()
            .StoreDurably()
            .Build();

        List<IOperableTrigger> acquired = await AcquireTwoDueTriggersOf(job);

        acquired.Should().ContainSingle(
            "the job says it must not run concurrently, and the store records that whether the type or the "
            + "builder said it — a second trigger of the job in one batch is one the fire path has to decline");
    }

    [Test]
    public async Task AJobFlaggedByItsTypeIsTakenOncePerBatch()
    {
        IJobDetail job = JobBuilder.Create<AttributedJob>()
            .WithIdentity("flagged-by-type")
            .StoreDurably()
            .Build();

        List<IOperableTrigger> acquired = await AcquireTwoDueTriggersOf(job);

        acquired.Should().ContainSingle("the attribute is the other way of saying the same thing");
    }

    [Test]
    public async Task AJobThatAllowsConcurrencyIsTakenAsOftenAsItIsDue()
    {
        IJobDetail job = JobBuilder.Create<UnattributedJob>()
            .WithIdentity("concurrent")
            .StoreDurably()
            .Build();

        List<IOperableTrigger> acquired = await AcquireTwoDueTriggersOf(job);

        acquired.Should().HaveCount(2, "nothing about this job holds its triggers back, which is the control");
    }

    private async Task<List<IOperableTrigger>> AcquireTwoDueTriggersOf(IJobDetail job)
    {
        DateTimeOffset due = DateTimeOffset.UtcNow.AddSeconds(1);

        await scheduler.AddJob(job);
        foreach (string name in (string[]) ["first", "second"])
        {
            await scheduler.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(name, job.Key.Name)
                .ForJob(job)
                .StartAt(due)
                .Build());
        }

        return await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = due.AddMinutes(1),
            MaxCount = 5,
            // Wide enough that the batch does not close on the first trigger's fire time, so the only thing
            // that can keep the second trigger out is the job it belongs to.
            TimeWindow = TimeSpan.FromMinutes(1),
        });
    }

    public sealed class UnattributedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    public sealed class AttributedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
