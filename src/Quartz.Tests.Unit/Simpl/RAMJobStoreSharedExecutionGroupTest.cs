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
using System.Collections.Generic;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// <see cref="RAMJobStore" /> says which execution group a manual firing counts against (#4092) off the
/// job's stored triggers, without the clone of each one <see cref="RAMJobStore.GetTriggersForJob" /> hands
/// out (#4167).
/// </summary>
public sealed class RAMJobStoreSharedExecutionGroupTest
{
    private const string Reports = "reports";
    private static readonly JobKey JobKey = new JobKey("render", "shared-group");

    private RAMJobStore store;

    [SetUp]
    public async Task SetUp()
    {
        store = new RAMJobStore();
        await store.Initialize(null, A.Fake<ISchedulerSignaler>());
        await store.StoreJob(JobBuilder.Create<NoOpJob>().WithIdentity(JobKey).StoreDurably().Build(), replaceExisting: false);
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(Array.Empty<string>(), null, "no trigger says anything")
            .SetName("NoTriggersShareNoGroup");
        yield return new TestCaseData(new[] { Reports, Reports }, Reports, "every trigger of the job carries reports")
            .SetName("TriggersAllInOneGroupShareIt");
        yield return new TestCaseData(new[] { Reports, null }, null, "a grouped and an ungrouped trigger is no one group")
            .SetName("AnUngroupedTriggerSharesNoGroup");
        yield return new TestCaseData(new[] { Reports, "batch" }, null, "two groups is no one group")
            .SetName("TriggersInTwoGroupsShareNoGroup");
        yield return new TestCaseData(new[] { Reports, "Reports" }, null, "groups compare ordinally, as execution limits count them")
            .SetName("GroupsThatDifferOnlyInCaseAreTwoGroups");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task TheSharedGroupIsTheOneEveryTriggerOfTheJobCarries(string[] executionGroups, string expected, string because)
    {
        for (int i = 0; i < executionGroups.Length; i++)
        {
            await store.StoreTrigger(Trigger("t" + i, executionGroups[i]), replaceExisting: false);
        }

        (await ((INextVersionJobStore) store).GetSharedExecutionGroup(JobKey)).Should().Be(expected, because);
    }

    [Test]
    public async Task TheSharedGroupIsReadWithoutCloningTheTriggers()
    {
        CloneCounter clones = new CloneCounter();
        for (int i = 0; i < 3; i++)
        {
            await store.StoreTrigger(new CloneCountingTrigger(clones, "t" + i, JobKey), replaceExisting: false);
        }

        clones.Count = 0;
        await store.GetTriggersForJob(JobKey);
        clones.Count.Should().Be(3, "the premise: GetTriggersForJob, which the group used to be read through, hands out a clone of each trigger");

        clones.Count = 0;
        (await ((INextVersionJobStore) store).GetSharedExecutionGroup(JobKey)).Should().Be(Reports);
        clones.Count.Should().Be(0, "the group is read off the stored triggers, and nothing is handed out to clone for");
    }

    private static IOperableTrigger Trigger(string name, string executionGroup)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "shared-group")
            .ForJob(JobKey)
            .WithExecutionGroup(executionGroup)
            .StartAt(DateTimeOffset.UtcNow.AddDays(1))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private sealed class CloneCounter
    {
        public int Count { get; set; }
    }

    /// <summary>
    /// Counts its clones; a clone shares the original's counter, so the clones of clones count too.
    /// </summary>
    private sealed class CloneCountingTrigger : SimpleTriggerImpl
    {
        private readonly CloneCounter clones;

        public CloneCountingTrigger(CloneCounter clones, string name, JobKey job)
            : base(name, "shared-group", job.Name, job.Group, DateTimeOffset.UtcNow.AddDays(1), null, 0, TimeSpan.Zero)
        {
            this.clones = clones;
            ExecutionGroup = Reports;
            ComputeFirstFireTimeUtc(null);
        }

        public override ITrigger Clone()
        {
            clones.Count++;
            return base.Clone();
        }
    }

    private sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
