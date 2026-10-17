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

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The 4.6 half of the history contract: an execution looked up by its exact trigger key and by its
/// fire instance id, a misfire by its exact trigger key, and runs counted by either.
/// </summary>
/// <remarks>
/// Run against the in-memory store and the database-backed one, as the rest of the contract is.
/// </remarks>
public abstract partial class ExecutionHistoryStoreContractTest
{
    [Test]
    public async Task ExecutionsCanBeReadForOneTriggerExactly()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired(Start, "g", "t", "fire-1"));
        await store.AddExecution(Fired(Start.AddSeconds(-1), "g", "t2", "fire-2"));
        await store.AddExecution(Fired(Start.AddSeconds(-2), "g", "T", "fire-3"));
        await store.AddExecution(Fired(Start.AddSeconds(-3), "other", "t", "fire-4"));

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            Trigger = new TriggerKey("t", "g"),
            IncludeTotalCount = true
        });

        page.Items.Should().ContainSingle("a trigger key is matched whole and as written, where TriggerContains matches a fragment")
            .Which.FireInstanceId.Should().Be("fire-1");
        page.TotalCount.Should().Be(1, "the count reads the same predicate as the page");
    }

    [Test]
    public async Task ExecutionsCanBeReadForOneFiringExactly()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired(Start, "g", "t", "node-a-1"));
        await store.AddExecution(Fired(Start.AddSeconds(-1), "g", "t", "node-a-17"));
        await store.AddExecution(Fired(Start.AddSeconds(-2), "g", "t", "NODE-A-1"));
        await store.AddExecution(Fired(Start.AddSeconds(-3), "g", "t", fireInstanceId: null));

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            FireInstanceId = "node-a-1"
        });

        page.Items.Should().ContainSingle("a fire instance id names one firing, so node-a-17 is not node-a-1")
            .Which.FiredAtUtc.Should().Be(Start);
    }

    [Test]
    public async Task TheExactFiltersCombine()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired(Start, "g", "t", "fire-1"));
        await store.AddExecution(Fired(Start.AddSeconds(-1), "g", "t", "fire-2") with { JobName = "other-job" });
        await store.AddExecution(Fired(Start.AddSeconds(-2), "g", "t2", "fire-1"));

        ExecutionHistoryQuery all = new()
        {
            SchedulerName = SchedulerName,
            Job = new JobKey("nightly-report", JobGroup),
            Trigger = new TriggerKey("t", "g"),
            FireInstanceId = "fire-1",
            IncludeTotalCount = true
        };

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(all);
        page.Items.Should().ContainSingle("job, trigger and firing are ANDed, as every other filter is")
            .Which.Should().Match<ExecutionHistoryEntry>(row => row.TriggerName == "t" && row.FireInstanceId == "fire-1");
        page.TotalCount.Should().Be(1);

        (await store.QueryExecutions(all with { FireInstanceId = "fire-2" })).Items.Should().BeEmpty(
            "fire-2 was the other job's firing, so no row is that job, that trigger and that firing at once");
        (await store.QueryExecutions(all with { Trigger = new TriggerKey("t2", "g"), Job = null })).Items.Should().ContainSingle()
            .Which.TriggerName.Should().Be("t2");
    }

    [Test]
    public async Task ALookupThatMatchesNothingIsAnEmptyPage()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired(Start, "g", "t", "fire-1"));

        PagedResult<ExecutionHistoryEntry> byFiring = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = SchedulerName,
            FireInstanceId = "fire-never",
            IncludeTotalCount = true
        });

        byFiring.Items.Should().BeEmpty("a firing that was never recorded, or has been trimmed, has no outcome to read");
        byFiring.TotalCount.Should().Be(0);
        byFiring.HasMore.Should().BeFalse();

        (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName, Trigger = new TriggerKey("t", "absent") }))
            .Items.Should().BeEmpty();
        (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName, FireInstanceId = " " }))
            .Items.Should().ContainSingle("a blank id is no filter, as a blank node is");
    }

    [Test]
    public async Task MisfiresCanBeReadForOneTriggerExactly()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddMisfire(Misfire(Start, "t"));
        await store.AddMisfire(Misfire(Start, "t2"));
        await store.AddMisfire(Misfire(Start, "T"));
        await store.AddMisfire(Misfire(Start, "t") with { TriggerGroup = "other" });

        PagedResult<MisfireHistoryEntry> page = await store.QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = SchedulerName,
            Trigger = new TriggerKey("t", TriggerGroup),
            IncludeTotalCount = true
        });

        page.Items.Should().ContainSingle("a misfire's trigger is matched as an execution's is: whole and as written")
            .Which.Should().Match<MisfireHistoryEntry>(row => row.TriggerName == "t" && row.TriggerGroup == TriggerGroup);
        page.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task RunsCanBeCountedForOneTriggerAndOneFiring()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Fired(Start, "g", "t", "fire-1"));
        await store.AddExecution(Fired(Start.AddSeconds(-1), "g", "t", "fire-2"));
        await store.AddExecution(Fired(Start.AddSeconds(-2), "g", "t2", "fire-3"));

        (await RunsOf(store, new TriggerKey("t", "g"), fireInstanceId: null)).Should().Be(2,
            "the chart beside a trigger's history counts that trigger's runs, not t2's");
        (await RunsOf(store, new TriggerKey("t", "g"), "fire-2")).Should().Be(1);
        (await RunsOf(store, trigger: null, "fire-3")).Should().Be(1);
        (await RunsOf(store, new TriggerKey("t2", "g"), "fire-1")).Should().Be(0, "the two filters are ANDed");
    }

    private static async Task<long> RunsOf(IExecutionHistoryStore store, TriggerKey? trigger, string? fireInstanceId)
    {
        ExecutionStatistics statistics = await store.QueryExecutionStatistics(new ExecutionStatisticsQuery
        {
            SchedulerName = SchedulerName,
            Trigger = trigger,
            FireInstanceId = fireInstanceId,
            BucketSize = TimeSpan.FromDays(1)
        });

        return statistics.Buckets.Sum(bucket => bucket.RunCount);
    }

    /// <summary>One execution of <c>nightly-report</c>, fired by the trigger and as the firing given.</summary>
    private static ExecutionHistoryEntry Fired(DateTimeOffset firedAt, string triggerGroup, string triggerName, string? fireInstanceId) =>
        Execution(firedAt, "nightly-report") with
        {
            TriggerGroup = triggerGroup,
            TriggerName = triggerName,
            FireInstanceId = fireInstanceId
        };
}
