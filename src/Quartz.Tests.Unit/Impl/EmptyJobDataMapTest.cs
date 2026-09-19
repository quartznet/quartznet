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

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What a job data map with nothing in it costs. A firing merges the job's map over the trigger's and
/// both are usually empty, so the three maps that produces used to be three dictionaries, three
/// wrappers and three maps that nothing ever read (#3802).
/// </summary>
[TestFixture]
public class EmptyJobDataMapTest
{
    [Test]
    public void AnEmptyMapCarriesNoDictionary()
    {
        // Warm: the first of each pays for JIT and for whatever the runtime caches, which is not what
        // this counts.
        _ = new JobDataMap();
        _ = new Dictionary<string, object>();

        long emptyMap = Allocated(static () => _ = new JobDataMap());
        long bareDictionary = Allocated(static () => _ = new Dictionary<string, object>());

        emptyMap.Should().BeLessThan(bareDictionary,
            "a map with nothing in it must not carry storage for entries it does not have - a firing builds three of these and the dictionary is the largest part of each");
    }

    [Test]
    public void AnEmptyMapStillAcceptsEntries()
    {
        JobDataMap map = new();

        map.IsEmpty.Should().BeTrue();
        map.Count.Should().Be(0);
        map.ContainsKey("absent").Should().BeFalse();
        map.TryGetValue("absent", out _).Should().BeFalse();

        map["present"] = 42;

        map.IsEmpty.Should().BeFalse("storage is created on the first write, and the write must land in it");
        map["present"].Should().Be(42);
        map.Dirty.Should().BeTrue("a write is what the dirty flag records, whenever the storage was created");
    }

    [Test]
    public void BuildingTheJobDataMapCreatesNoMapOnTheJobOrTheTrigger()
    {
        (JobDetailImpl job, SimpleTriggerImpl trigger) = Firing();

        JobDataMap built = new ExposedJobFactory().Build(Bundle(job, trigger), A.Fake<IScheduler>());

        built.Count.Should().Be(0, "neither side carried any data, so the merge of them is empty");
        job.JobDataMapOrNull.Should().BeNull(
            "reading IJobDetail.JobDataMap creates and keeps a map, and the job detail a firing is handed is a copy made for that firing alone");
        trigger.JobDataMapOrNull.Should().BeNull("the same, for the trigger copy");
    }

    [Test]
    public void BuildingTheJobDataMapStillMergesTheTriggerOverTheJob()
    {
        (JobDetailImpl job, SimpleTriggerImpl trigger) = Firing();
        job.JobDataMap["shared"] = "job";
        job.JobDataMap["jobOnly"] = "job";
        trigger.JobDataMap["shared"] = "trigger";

        JobDataMap built = new ExposedJobFactory().Build(Bundle(job, trigger), A.Fake<IScheduler>());

        built.Count.Should().Be(2);
        built["shared"].Should().Be("trigger", "the trigger's entry wins, which is the documented merge order");
        built["jobOnly"].Should().Be("job");
    }

    [Test]
    public void TheMergedMapCreatesNoMapOnTheJobOrTheTrigger()
    {
        (JobDetailImpl job, SimpleTriggerImpl trigger) = Firing();

        JobExecutionContextImpl context = new(A.Fake<IScheduler>(), Bundle(job, trigger), A.Fake<IJob>());

        context.MergedJobDataMap.Count.Should().Be(0);
        job.JobDataMapOrNull.Should().BeNull("the merged map is built from what the two sides hold, not from maps it makes them create");
        trigger.JobDataMapOrNull.Should().BeNull("the same, for the trigger copy");
    }

    private static (JobDetailImpl Job, SimpleTriggerImpl Trigger) Firing()
    {
        JobDetailImpl job = (JobDetailImpl) JobBuilder.Create<NoOpJob>().WithIdentity("job", "group").Build();
        SimpleTriggerImpl trigger = (SimpleTriggerImpl) TriggerBuilder.Create()
            .WithIdentity("trigger", "group")
            .StartNow()
            .Build();
        trigger.FireInstanceId = "fire-1";
        return (job, trigger);
    }

    private static TriggerFiredBundle Bundle(IJobDetail job, IOperableTrigger trigger)
    {
        return new TriggerFiredBundle
        {
            JobDetail = job,
            Trigger = trigger,
            Recovering = false,
            FireTimeUtc = DateTimeOffset.UtcNow,
            ScheduledFireTimeUtc = null,
            PreviousFireTimeUtc = null,
            NextFireTimeUtc = null,
        };
    }

    private static long Allocated(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// Reaches the protected hook the firing calls, which is where the merge happens.
    /// </summary>
    private sealed class ExposedJobFactory : PropertySettingJobFactory
    {
        public JobDataMap Build(TriggerFiredBundle bundle, IScheduler scheduler) => BuildJobDataMap(bundle, scheduler);
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
