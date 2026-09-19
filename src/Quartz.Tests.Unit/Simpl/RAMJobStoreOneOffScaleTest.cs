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

using System.Globalization;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// One durable job behind many triggers, which is what <c>ScheduleJob&lt;TJob, TInput&gt;</c> produces:
/// one durable job per job type and a trigger per call. The store used to keep a job's triggers in a
/// list, so every completion that deleted one walked the list to find it and then built an array of the
/// remaining keys to ask whether the job had any left — quadratic in the number of triggers behind the
/// job, and 83 KB a firing at twenty thousand of them (#3823).
/// </summary>
[NonParallelizable]
public class RAMJobStoreOneOffScaleTest
{
    private const int TriggersBehindTheJob = 5_000;

    private IJobStore store;
    private IJobDetail durableJob;

    [SetUp]
    public async Task SetUp()
    {
        store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity());
        await store.SchedulerStarted();

        durableJob = JobBuilder.Create<NoOpJob>()
            .WithIdentity("oneOff", "scale")
            .StoreDurably()
            .Build();

        await store.AddJob(durableJob);
    }

    [Test]
    public async Task RemovingOneTriggerDoesNotCostTheJobsOtherTriggers()
    {
        await GivenTriggers(TriggersBehindTheJob);

        // Warm: the first removal pays for whatever the path JITs, and that is not what this counts.
        (await store.DeleteTrigger(TriggerKeyFor(0))).Should().BeTrue();

        long before = GC.GetAllocatedBytesForCurrentThread();
        (await store.DeleteTrigger(TriggerKeyFor(1))).Should().BeTrue();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(1_000,
            "removing one of a job's triggers must not depend on how many the job has - it used to build "
            + $"an array of the other {TriggersBehindTheJob:N0} keys to ask whether the job was orphaned, "
            + "which is forty kilobytes here and eighty-three per firing at the twenty thousand the "
            + "one-off API reaches");

        (await store.GetTriggersForJob(durableJob.Key)).Should().HaveCount(TriggersBehindTheJob - 2,
            "the two removed are gone and nothing else is");
    }

    [Test]
    public async Task ARemovedTriggerIsGoneFromEveryListingOfTheJobs()
    {
        await GivenTriggers(4);

        (await store.DeleteTrigger(TriggerKeyFor(2))).Should().BeTrue();

        List<IOperableTrigger> remaining = await store.GetTriggersForJob(durableJob.Key);

        remaining.Select(t => t.Key).Should().BeEquivalentTo(
            new[] { TriggerKeyFor(0), TriggerKeyFor(1), TriggerKeyFor(3) },
            "the job's triggers are what is left of them, in whatever order the store holds them");
    }

    [Test]
    public async Task StoringATriggerOverOneOfTheSameKeyReplacesItRatherThanAddingIt()
    {
        await GivenTriggers(2);

        IOperableTrigger replacement = NewTrigger(0);
        replacement.Description = "the replacement";
        await store.AddTrigger(replacement, AddTriggerOptions.Replacing);

        List<IOperableTrigger> triggers = await store.GetTriggersForJob(durableJob.Key);

        triggers.Should().HaveCount(2, "a trigger stored over one of the same key is the same trigger");
        triggers.Single(t => t.Key.Equals(TriggerKeyFor(0))).Description.Should().Be("the replacement");
    }

    [Test]
    public async Task ANonDurableJobIsRemovedWithItsLastTriggerAndNotBefore()
    {
        IJobDetail perishable = JobBuilder.Create<NoOpJob>()
            .WithIdentity("perishable", "scale")
            .Build();

        IOperableTrigger first = NewTrigger(0, perishable);
        IOperableTrigger second = NewTrigger(1, perishable);
        await store.ScheduleJob(perishable, first);
        await store.AddTrigger(second);

        (await store.DeleteTrigger(first.Key)).Should().BeTrue();
        (await store.Exists(perishable.Key)).Should().BeTrue(
            "the job still has a trigger, so it is not orphaned");

        (await store.DeleteTrigger(second.Key)).Should().BeTrue();
        (await store.Exists(perishable.Key)).Should().BeFalse(
            "its last trigger is gone and it is not durable, so the store removes it - which is the answer "
            + "the removed array of remaining keys used to give");
    }

    [Test]
    public async Task ADurableJobSurvivesItsLastTrigger()
    {
        await GivenTriggers(1);

        (await store.DeleteTrigger(TriggerKeyFor(0))).Should().BeTrue();

        (await store.Exists(durableJob.Key)).Should().BeTrue("a durable job outlives its triggers");
        (await store.GetTriggersForJob(durableJob.Key)).Should().BeEmpty();
    }

    private async Task GivenTriggers(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await store.AddTrigger(NewTrigger(i));
        }
    }

    private IOperableTrigger NewTrigger(int index, IJobDetail job = null)
    {
        job ??= durableJob;
        SimpleTriggerImpl trigger = new()
        {
            Key = TriggerKeyFor(index),
            JobKey = job.Key,
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(1),
        };
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private static TriggerKey TriggerKeyFor(int index)
    {
        return new TriggerKey("trigger-" + index.ToString(CultureInfo.InvariantCulture), "scale");
    }
}
