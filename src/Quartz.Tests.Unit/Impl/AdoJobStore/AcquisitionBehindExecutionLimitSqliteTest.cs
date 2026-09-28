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

using CountingSqliteDelegate = Quartz.Tests.Unit.Impl.AdoJobStore.AcquisitionBehindExecutingJobSqliteTest.CountingSqliteDelegate;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A node reading one trigger at a time reaches the due rows behind an execution group at its limit
/// (#3928).
/// </summary>
/// <remarks>
/// <para>
/// The acquisition statement limits its rows to the count asked for, and the delegate then refused any
/// row whose execution group had no slot left. A refused row was dropped after the limit, so the store
/// saw a read that came back short and took it for the end of what is due; with one row a round, the
/// earliest due trigger of a full group hid everything behind it, and the read-past of #3927 never
/// applied because the store had not counted the row as skipped. The delegate now returns the row
/// flagged, the store skips it, and reads past it.
/// </para>
/// <para>
/// One store on a SQLite file, driven by hand, with the shipped SQLite delegate recording how long each
/// acquisition read was asked to be. The limit is cluster-scoped so that the store, not a scheduler
/// thread, lowers it by what is in flight: one fired row of the group is what makes the group full.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class AcquisitionBehindExecutionLimitSqliteTest
{
    private const string Group = "at-limit";
    private const string Tenant = "tenant-acme";
    private static readonly JobKey tenantJobKey = new("tenant-job", Group);
    private static readonly JobKey ordinaryJobKey = new("ordinary", Group);

    /// <summary>One slot for the tenant, across the cluster.</summary>
    private static readonly ExecutionLimits oneTenantSlot = ExecutionLimitsBuilder.Create()
        .ForGroup(Tenant, 1, ExecutionLimitScope.Cluster)
        .Build();

    private SqliteTestDatabase database = null!;
    private ServiceProvider node = null!;
    private IScheduler scheduler = null!;
    private IJobStore store = null!;
    private DateTimeOffset due;

    [SetUp]
    public async Task CreateNode()
    {
        database = new SqliteTestDatabase("acquisition-behind-execution-limit");
        CountingSqliteDelegate.Reset();

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "acquisition-behind-execution-limit";
                options.InstanceId = "node";
            });

            q.UsePersistentStore(persistent =>
            {
                // Before UseSqlite, which registers the delegate it names: the registrations are
                // try-add, so the first one in wins and this subclass would otherwise never be built.
                persistent.UseDriverDelegate<CountingSqliteDelegate>();
                persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                persistent.ProvisionSchema();
            });
        });

        node = services.BuildServiceProvider();
        scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        store = node.GetRequiredService<IJobStore>();

        // Ahead of now by a known margin, so the misfire cutoff stays out of the acquisition read and
        // the fire-time order is the one each test schedules: the tenant's triggers first.
        due = TimeProvider.System.GetUtcNow().AddSeconds(30);
    }

    [TearDown]
    public async Task DisposeNode()
    {
        await node.DisposeAsync();
        database.Dispose();
    }

    /// <summary>
    /// The issue's scenario: the earliest due row belongs to a group that is full, and an ordinary
    /// trigger is due behind it.
    /// </summary>
    [Test]
    public async Task ADueTriggerBehindAGroupAtItsLimitIsAcquiredByANodeReadingOneAtATime()
    {
        await AddJobs();
        await Schedule("tenant-1", tenantJobKey, due, Tenant);
        await Schedule("tenant-2", tenantJobKey, due.AddMilliseconds(1), Tenant);
        await Schedule("ordinary-1", ordinaryJobKey, due.AddMilliseconds(2));
        await FillTheTenantsSlot();
        CountingSqliteDelegate.Reset();

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));

        acquired.Should().ContainSingle().Which.Key.Name.Should().Be("ordinary-1",
            "the first trigger due whose group has a slot; the tenant's other trigger sits first in the order with none");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal([1, 3],
            "the first read is the count asked for and came back with one refused row, so the next reaches past it; "
            + "it came back short of its limit, so the round ends there");
        (await StateOf("tenant-2")).Should().Be("WAITING", "refused for its group's limit, and left for when a slot frees");
        (await StateOf("ordinary-1")).Should().Be("ACQUIRED");
    }

    /// <summary>
    /// The control: an acquisition that refuses nothing costs what it always did, one read of the count
    /// asked for — limits configured or not.
    /// </summary>
    [Test]
    public async Task AnAcquisitionThatRefusesNothingReadsOnce()
    {
        await AddJobs();
        await Schedule("tenant-1", tenantJobKey, due, Tenant);
        await Schedule("ordinary-1", ordinaryJobKey, due.AddMilliseconds(1));
        CountingSqliteDelegate.Reset();

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));

        acquired.Should().ContainSingle().Which.Key.Name.Should().Be("tenant-1", "the tenant has its slot");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal([1], "nothing was refused, so there was nothing to read past");
    }

    /// <summary>
    /// A full group with nothing due behind it is answered after one wider read, not three identical ones.
    /// </summary>
    [Test]
    public async Task AGroupAtItsLimitWithNothingDueBehindItStopsAfterOneWiderRead()
    {
        await AddJobs();
        await Schedule("tenant-1", tenantJobKey, due, Tenant);
        await Schedule("tenant-2", tenantJobKey, due.AddMilliseconds(1), Tenant);
        await FillTheTenantsSlot();
        CountingSqliteDelegate.Reset();

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));

        acquired.Should().BeEmpty("the only row due belongs to the full group");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal([1, 3],
            "the wider read came back with the one refused row and nothing behind it, which is the answer");
    }

    /// <summary>
    /// A batch takes as many as it may from behind the refused rows, and no more.
    /// </summary>
    [Test]
    public async Task ABatchReadingPastRefusedRowsStillTakesNoMoreThanItsCount()
    {
        await AddJobs();
        await Schedule("tenant-1", tenantJobKey, due, Tenant);
        await Schedule("tenant-2", tenantJobKey, due.AddMilliseconds(1), Tenant);
        await Schedule("tenant-3", tenantJobKey, due.AddMilliseconds(2), Tenant);
        await Schedule("ordinary-1", ordinaryJobKey, due.AddMilliseconds(3));
        await Schedule("ordinary-2", ordinaryJobKey, due.AddMilliseconds(4));
        await Schedule("ordinary-3", ordinaryJobKey, due.AddMilliseconds(5));
        await FillTheTenantsSlot();
        CountingSqliteDelegate.Reset();

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 2));

        acquired.Select(x => x.Key.Name).Should().Equal(["ordinary-1", "ordinary-2"],
            "two were asked for, and the two refused rows ahead of them do not count against that");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal([2, 6],
            "two refused rows filled the first read; the next reaches twice as far past the count");
        (await StateOf("ordinary-3")).Should().Be("WAITING", "the cap: two triggers were asked for");
    }

    /// <summary>
    /// Reserves and fires the tenant's first trigger, so that its fired row is the one execution the
    /// cluster-scoped count sees and the group is at its limit of one.
    /// </summary>
    private async Task FillTheTenantsSlot()
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));
        acquired.Should().ContainSingle("the premise: the tenant's slot is free until this fills it")
            .Which.Key.Name.Should().Be("tenant-1");

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
    }

    private static TriggerAcquisitionRequest RequestFor(int maxCount)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(5),
            MaxCount = maxCount,
            // Wide enough that triggers due milliseconds apart make one batch.
            TimeWindow = TimeSpan.FromSeconds(5),
            ExecutionLimits = oneTenantSlot,
        };
    }

    private async Task AddJobs()
    {
        await scheduler.AddJob(JobBuilder.Create<TenantJob>().WithIdentity(tenantJobKey).StoreDurably().Build());
        await scheduler.AddJob(JobBuilder.Create<OrdinaryJob>().WithIdentity(ordinaryJobKey).StoreDurably().Build());
    }

    private async Task Schedule(string name, JobKey job, DateTimeOffset at, string? executionGroup = null)
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at);
        if (executionGroup is not null)
        {
            builder = builder.WithExecutionGroup(executionGroup);
        }

        await scheduler.ScheduleJob(builder.Build());
    }

    private async Task<string?> StateOf(string triggerName)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", triggerName);
        return (string?) await command.ExecuteScalarAsync();
    }

    public sealed class TenantJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class OrdinaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
