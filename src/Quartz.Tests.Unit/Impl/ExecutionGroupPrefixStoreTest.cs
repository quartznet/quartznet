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
/// A prefix limit through both stores' acquisition: each tenant under <c>tenant:</c> gets its own
/// allowance, counted per node or across what the store holds in flight.
/// </summary>
/// <remarks>
/// The same assertions against the in-memory store and the ADO store on a SQLite file. The ADO store
/// counts the cluster from <c>QRTZ_FIRED_TRIGGERS</c> grouped by execution group, so a prefix is the
/// place where that per-group count has to come out per tenant rather than per prefix.
/// </remarks>
public abstract class ExecutionGroupPrefixStoreTest
{
    private const string TriggerGroup = "nightly";

    protected IJobStore Store { get; set; } = null!;

    [TearDown]
    public virtual async Task TearDown()
    {
        await Store.Shutdown();
    }

    [Test]
    public async Task AClusterScopedPrefixCountsWhatTheStoreHoldsForEachTenant()
    {
        await GivenDueTrigger("held", "tenant:acme");
        (await Acquire(limits: null, maxCount: 1)).Should().ContainSingle("the premise: acme holds one reservation");

        await GivenDueTrigger("acme-candidate", "tenant:acme");
        await GivenDueTrigger("initech-candidate", "tenant:initech");

        List<IOperableTrigger> acquired = await Acquire(ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 1, ExecutionLimitScope.Cluster)
            .Build());

        acquired.Select(x => x.Key.Name).Should().Equal(["initech-candidate"],
            "acme's one slot is the reservation the store holds; initech's is its own and still free");
    }

    [Test]
    public async Task ANodeScopedPrefixHandsEachTenantItsOwnSlots()
    {
        await GivenDueTrigger("acme-1", "tenant:acme");
        await GivenDueTrigger("acme-2", "tenant:acme");
        await GivenDueTrigger("initech-1", "tenant:initech");
        await GivenDueTrigger("initech-2", "tenant:initech");
        await GivenDueTrigger("reports", "reports");

        List<IOperableTrigger> acquired = await Acquire(ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 1)
            .ForOtherGroups(0)
            .Build());

        acquired.Select(x => x.ExecutionGroup).Should().BeEquivalentTo(["tenant:acme", "tenant:initech"],
            "one slot each for the two tenants, and the catch-all forbids what the prefix does not cover");
    }

    private async Task GivenDueTrigger(string name, string executionGroup)
    {
        IJobDetail job = JobBuilder.Create<PrefixStoreJob>()
            .WithIdentity(name, "jobs")
            .Build();

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, TriggerGroup)
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartNow()
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await Store.ScheduleJob(job, trigger);
    }

    private ValueTask<List<IOperableTrigger>> Acquire(ExecutionLimits? limits, int maxCount = 10)
    {
        return Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = maxCount,
            // Wide enough that the batch does not close on the first trigger's fire time.
            TimeWindow = TimeSpan.FromMinutes(1),
            ExecutionLimits = limits,
        });
    }

    public sealed class PrefixStoreJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}

public sealed class RamExecutionGroupPrefixStoreTest : ExecutionGroupPrefixStoreTest
{
    [SetUp]
    public async Task SetUp()
    {
        Store = TestJobStores.Ram();
        await Store.Initialize(TestJobStores.Identity());
    }
}

public sealed class SqliteExecutionGroupPrefixStoreTest : ExecutionGroupPrefixStoreTest
{
    private SqliteTestDatabase database = null!;
    private ServiceProvider container = null!;
    private IScheduler scheduler = null!;

    [SetUp]
    public async Task SetUp()
    {
        database = new SqliteTestDatabase("prefix-limits");

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "prefix-limits";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        container = services.BuildServiceProvider();

        // Never started: the scheduler thread would acquire what the test acquires by hand.
        scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        Store = container.GetRequiredService<IJobStore>();
    }

    public override async Task TearDown()
    {
        // The scheduler owns the store it was built with, and shuts it down.
        await scheduler.Shutdown(waitForJobsToComplete: false);
        await container.DisposeAsync();
        database.Dispose();
    }
}
