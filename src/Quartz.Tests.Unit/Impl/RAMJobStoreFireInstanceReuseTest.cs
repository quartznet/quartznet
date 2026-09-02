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
/// What a firing sees of the executions that came before it, now that the map a trigger's running
/// executions live in is handed on from one firing to the next instead of being allocated for each.
/// </summary>
/// <remarks>
/// The reuse is invisible by design, which is exactly why it is worth pinning: a map that came back
/// out of the spare list still holding an entry, or one handed on while an execution was still using
/// it, would show up as a firing of one trigger being reported under another — and nothing else in
/// the suite asks <c>QueryFireInstances</c> what it holds across a completion.
/// </remarks>
[TestFixture]
public sealed class RAMJobStoreFireInstanceReuseTest
{
    private static readonly DateTimeOffset now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private RAMJobStore store = null!;

    [SetUp]
    public async Task SetUp()
    {
        store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity());
        await store.SchedulerStarted();
    }

    [Test]
    public async Task AFiringSeesNothingOfTheTriggerThatFinishedBeforeIt()
    {
        IJobDetail job = CreateJob();
        IOperableTrigger first = CreateTrigger("first", job, now);
        IOperableTrigger second = CreateTrigger("second", job, now.AddMinutes(1));

        await store.ScheduleJob(job, first);
        await store.AddTrigger(second);

        IOperableTrigger firstFiring = await Fire(first.Key);
        await store.TriggeredJobComplete(firstFiring, job, SchedulerInstruction.NoInstruction);

        IOperableTrigger secondFiring = await Fire(second.Key);

        PagedResult<FireInstance> executing = await store.QueryFireInstances(new FireInstanceQuery());

        executing.Items.Should().ContainSingle(
            "the first trigger's only execution has completed, so the second trigger's is the one that is running")
            .Which.Should().Match<FireInstance>(
                instance => instance.FireInstanceId == secondFiring.FireInstanceId && instance.TriggerKey.Equals(second.Key),
                "a map that outlives the firing it was created for must carry none of that firing into the next one");
    }

    [Test]
    public async Task ATriggerThatFiresAgainReportsOnlyItsNewExecution()
    {
        IJobDetail job = CreateJob();
        IOperableTrigger trigger = CreateTrigger("only", job, now);

        await store.ScheduleJob(job, trigger);

        IOperableTrigger firstFiring = await Fire(trigger.Key);
        await store.TriggeredJobComplete(firstFiring, job, SchedulerInstruction.NoInstruction);

        IOperableTrigger secondFiring = await Fire(trigger.Key);

        PagedResult<FireInstance> executing = await store.QueryFireInstances(new FireInstanceQuery());

        executing.Items.Select(x => x.FireInstanceId).Should().Equal([secondFiring.FireInstanceId],
            "one firing of the trigger is running, and it is the second one — the first was completed");
        secondFiring.FireInstanceId.Should().NotBe(firstFiring.FireInstanceId,
            "each firing is its own occurrence, so the two are told apart by their fire instance ids");
    }

    [Test]
    public async Task ATriggerKeepsTheExecutionStillRunningWhenAnEarlierOneCompletes()
    {
        IJobDetail job = CreateJob();
        IOperableTrigger trigger = CreateTrigger("only", job, now);

        await store.ScheduleJob(job, trigger);

        IOperableTrigger earlier = await Fire(trigger.Key);
        IOperableTrigger later = await Fire(trigger.Key);

        await store.TriggeredJobComplete(earlier, job, SchedulerInstruction.NoInstruction);

        PagedResult<FireInstance> executing = await store.QueryFireInstances(new FireInstanceQuery());

        executing.Items.Select(x => x.FireInstanceId).Should().Equal([later.FireInstanceId],
            "a trigger whose second execution is still running keeps it when the first one completes");
    }

    /// <summary>
    /// Acquires and fires the trigger that is due next, which the callers arrange to be the one they
    /// name.
    /// </summary>
    private async Task<IOperableTrigger> Fire(TriggerKey expected)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = now.AddDays(1),
            MaxCount = 1,
        });

        acquired.Select(x => x.Key).Should().Equal([expected],
            "the trigger due next is the one this step is about");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the firing has to be committed before the store records an execution for it");

        return acquired[0];
    }

    private static IJobDetail CreateJob()
    {
        return JobBuilder.Create<ReuseTestJob>()
            .WithIdentity("job", "reuse")
            .Build();
    }

    private static IOperableTrigger CreateTrigger(string name, IJobDetail job, DateTimeOffset startAt)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "reuse")
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        // Job stores keep what they are given; working out when a trigger first fires is the
        // scheduler's job, and nothing is acquirable until it has been done.
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private sealed class ReuseTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
