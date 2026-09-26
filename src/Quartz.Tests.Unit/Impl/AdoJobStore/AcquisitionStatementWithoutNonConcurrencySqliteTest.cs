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
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A driver delegate whose acquisition statement was written for 4.2 keeps acquiring on 4.3.
/// </summary>
/// <remarks>
/// <para>
/// <c>GetSelectNextTriggerToAcquireSql</c> is a protected extension point, and 4.3 added
/// <c>IS_NONCONCURRENT</c> to the statement it builds. A subclass that overrides it with the 4.2
/// projection — trigger name, trigger group, job class, execution group — has no such column in its
/// result set, and demanding it would fail every acquisition after the upgrade.
/// </para>
/// <para>
/// So the column is looked for, and a result without it leaves
/// <see cref="TriggerAcquireResult.ConcurrentExecutionDisallowed" /> <see langword="null" />, which the
/// store answers from the job type's attribute: what acquisition asked before 4.3.
/// </para>
/// </remarks>
public sealed class AcquisitionStatementWithoutNonConcurrencySqliteTest
{
    private SqliteTestDatabase database = null!;
    private ServiceProvider container = null!;
    private IScheduler scheduler = null!;
    private IJobStore store = null!;
    private DelegateWithTheFourTwoProjection driverDelegate = null!;

    [SetUp]
    public async Task SetUp()
    {
        database = new SqliteTestDatabase("acquisition-4-2-projection");
        driverDelegate = new DelegateWithTheFourTwoProjection();

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "acquisition-4-2-projection";
                options.InstanceId = "node";
            });

            q.UsePersistentStore(persistent =>
            {
                // Before UseSqlite, whose own delegate would otherwise be the first registration and win.
                persistent.UseDriverDelegate(_ => driverDelegate);
                persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                persistent.ProvisionSchema();
            });
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
        database.Dispose();
    }

    [Test]
    public async Task AJobTypeCarryingTheAttributeIsStillTakenOncePerBatch()
    {
        IJobDetail job = JobBuilder.Create<AttributedJob>()
            .WithIdentity("flagged-by-type")
            .StoreDurably()
            .Build();

        List<IOperableTrigger> acquired = await AcquireTwoDueTriggersOf(job);

        driverDelegate.Statement.Should().NotBeNull("the subclass's own statement has to be the one that ran")
            .And.NotContain(AdoConstants.ColumnIsNonConcurrent, "the arrangement is a result set without the column");

        acquired.Should().ContainSingle(
            "without the stored flag the store answers from the job type, as it did before 4.3, and the type "
            + "says the job must not run twice at once");
    }

    /// <summary>
    /// The fallback is the type's answer and no more: a job flagged only by its builder is what the
    /// stored flag exists for, so without it the job is taken as 4.2 took it.
    /// </summary>
    [Test]
    public async Task AJobFlaggedOnlyByItsBuilderIsTakenAsFourTwoTookIt()
    {
        IJobDetail job = JobBuilder.Create<UnattributedJob>()
            .WithIdentity("flagged-by-builder")
            .DisallowConcurrentExecution()
            .StoreDurably()
            .Build();

        List<IOperableTrigger> acquired = await AcquireTwoDueTriggersOf(job);

        acquired.Should().HaveCount(2,
            "the statement does not read IS_NONCONCURRENT, so the result says nothing and the type has no "
            + "attribute; the fire path still declines the second firing");
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
            TimeWindow = TimeSpan.FromMinutes(1),
        });
    }

    /// <summary>
    /// A dialect delegate of somebody's own, written against 4.2: it builds the acquisition statement
    /// itself, projecting the four columns that version read.
    /// </summary>
    private sealed class DelegateWithTheFourTwoProjection : SQLiteDelegate
    {
        private static readonly string NonConcurrencyProjection = "jd." + AdoConstants.ColumnIsNonConcurrent + ", ";

        public string? Statement { get; private set; }

        protected override string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
        {
            Statement = base.GetSelectNextTriggerToAcquireSql(shape).Replace(NonConcurrencyProjection, "", StringComparison.Ordinal);
            return Statement;
        }
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
