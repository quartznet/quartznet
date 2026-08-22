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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// How <see cref="RAMJobStore" /> honours a <see cref="ExecutionLimitScope.Cluster" /> execution
/// limit: by counting what it is itself holding in flight.
/// </summary>
/// <remarks>
/// <para>
/// The store is never clustered, so its cluster is this one process and a cluster-scoped limit comes
/// out as the number a node-scoped one would. That is the whole point of asserting it here — a store
/// contract feature that only the ADO store implements is a divergence, and this is the half of the
/// pair that needs no database.
/// </para>
/// <para>
/// The same assertions run against both stores in <c>JobStoreContractTest</c>; these exist because
/// that fixture lives in the integration project, and the in-memory reservation counting is worth
/// pinning where it can be run without one.
/// </para>
/// </remarks>
public class RAMJobStoreClusterLimitTest
{
    private const string Tenant = "tenant-acme";
    private const string TriggerGroup = "nightly";

    private RAMJobStore store;

    [SetUp]
    public async Task SetUp()
    {
        store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity());
    }

    [TearDown]
    public async Task TearDown()
    {
        await store.Shutdown();
    }

    [Test]
    public async Task AReservationTheStoreIsHoldingCountsAgainstAClusterScopedLimit()
    {
        await GivenDueTrigger("held", Tenant);
        await AcquireWith(limits: null, maxCount: 1);

        await GivenDueTrigger("candidate", Tenant);

        List<IOperableTrigger> acquired = await AcquireWith(ClusterLimit(1));

        acquired.Should().BeEmpty(
            "the trigger already acquired holds the group's one cluster-wide slot, and a reservation is spoken for whether or not it has started");
    }

    [Test]
    public async Task AFiringThatHasStartedGoesOnHoldingItsSlot()
    {
        await GivenDueTrigger("running", Tenant);
        List<IOperableTrigger> first = await AcquireWith(limits: null, maxCount: 1);
        await store.TriggersFired(first);

        await GivenDueTrigger("candidate", Tenant);

        List<IOperableTrigger> acquired = await AcquireWith(ClusterLimit(1));

        acquired.Should().BeEmpty(
            "the reservation became a running execution, which is the same slot under a different name");
    }

    [Test]
    public async Task ACompletedFiringGivesItsSlotBack()
    {
        await GivenDueTrigger("running", Tenant);
        List<IOperableTrigger> first = await AcquireWith(limits: null, maxCount: 1);
        List<TriggerFiredResult> fired = await store.TriggersFired(first);
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Which.TriggerFiredBundle;
        await store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        await GivenDueTrigger("candidate", Tenant);

        List<IOperableTrigger> acquired = await AcquireWith(ClusterLimit(1));

        acquired.Should().ContainSingle("nothing is in flight any more, so the whole quota is on offer again");
    }

    [Test]
    public async Task ANodeScopedLimitIsNotLoweredByWhatTheStoreHolds()
    {
        await GivenDueTrigger("held", Tenant);
        await AcquireWith(limits: null, maxCount: 1);

        await GivenDueTrigger("candidate", Tenant);

        ExecutionLimits nodeLimit = ExecutionLimitsBuilder.Create().ForGroup(Tenant, 1).Build();

        (await AcquireWith(nodeLimit)).Should().ContainSingle(
            "a node-scoped limit arrives already lowered by what runs here; the store lowering it again would charge this node twice for its own work");
    }

    [Test]
    public async Task WorkInFlightForOneGroupDoesNotChargeAnother()
    {
        await GivenDueTrigger("held", Tenant);
        await AcquireWith(limits: null, maxCount: 1);

        await GivenDueTrigger("candidate", "tenant-initech");

        (await AcquireWith(ClusterLimit(1))).Should().ContainSingle(
            "the ceiling is per execution group, so a different tenant's work is not counted against this one");
    }

    [Test]
    public async Task TheDerivedGroupIsCountedTheWayTheFilterResolvesIt()
    {
        // Neither trigger carries an execution group; the limits stand the trigger group in for it.
        await GivenDueTrigger("held", executionGroup: null);
        await AcquireWith(limits: null, maxCount: 1);

        await GivenDueTrigger("candidate", executionGroup: null);

        ExecutionLimits derived = ExecutionLimitsBuilder.Create()
            .ForGroup(TriggerGroup, 1, ExecutionLimitScope.Cluster)
            .UseTriggerGroupWhenUnset()
            .Build();

        (await AcquireWith(derived)).Should().BeEmpty(
            "the in-flight count and the acquisition filter have to key work the same way, or the derived group would be counted in one bucket and spent from another");
    }

    private static ExecutionLimits ClusterLimit(int maxConcurrent)
    {
        return ExecutionLimitsBuilder.Create()
            .ForGroup(Tenant, maxConcurrent, ExecutionLimitScope.Cluster)
            .Build();
    }

    private async Task GivenDueTrigger(string name, string executionGroup)
    {
        IJobDetail job = JobBuilder.Create<ClusterLimitTestJob>()
            .WithIdentity(name, "jobs")
            .Build();

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, TriggerGroup)
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartNow()
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
    }

    private ValueTask<List<IOperableTrigger>> AcquireWith(ExecutionLimits limits, int maxCount = 5)
    {
        return store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = maxCount,
            // Wide enough that the batch does not close on the first trigger's fire time.
            TimeWindow = TimeSpan.FromMinutes(1),
            ExecutionLimits = limits,
        });
    }

    private sealed class ClusterLimitTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
