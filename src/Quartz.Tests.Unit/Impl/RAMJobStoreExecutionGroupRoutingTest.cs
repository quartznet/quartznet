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

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// How <see cref="RAMJobStore" /> honours the execution groups a request serves or refuses (#4069).
/// </summary>
/// <remarks>
/// The store is never clustered, so a group it refuses is a group nothing runs — the filter is honoured
/// so the contract is store-independent, and because the one thing that must not happen is the misfire
/// policy being applied to a trigger this node declined: that is the other half of the contract, and it
/// is what lets the overdue trigger be the sign that nobody is serving its group.
/// </remarks>
public class RAMJobStoreExecutionGroupRoutingTest
{
    private RAMJobStore store = null!;
    private ISchedulerSignaler signaler = null!;

    [SetUp]
    public async Task SetUp()
    {
        signaler = A.Fake<ISchedulerSignaler>();
        store = TestJobStores.Ram(signaler);
        store.MisfireThreshold = TimeSpan.FromSeconds(1);
        await store.Initialize(TestJobStores.Identity());
    }

    [TearDown]
    public async Task TearDown()
    {
        await store.Shutdown();
    }

    [Test]
    public async Task AServeListAcquiresOnlyWhatItNames()
    {
        await GivenDueTrigger("gpu-1", "gpu");
        await GivenDueTrigger("reports-1", "reports");
        await GivenDueTrigger("plain-1", executionGroup: null);

        List<IOperableTrigger> acquired = await Acquire(served: [ExecutionGroupScope.Named("reports")]);

        acquired.Select(x => x.Key.Name).Should().Equal(["reports-1"],
            "a serve-list that names neither gpu nor the ungrouped bucket serves neither");
    }

    [Test]
    public async Task ARefuseListNamingTheBucketDeclinesUngroupedTriggersToo()
    {
        await GivenDueTrigger("gpu-1", "gpu");
        await GivenDueTrigger("reports-1", "reports");
        await GivenDueTrigger("plain-1", executionGroup: null);

        List<IOperableTrigger> acquired = await Acquire(refused: [ExecutionGroupScope.Named("gpu"), ExecutionGroupScope.Default]);

        acquired.Select(x => x.Key.Name).Should().Equal(["reports-1"]);
    }

    [Test]
    public async Task ARefuseListThatOmitsTheBucketServesUngroupedTriggers()
    {
        await GivenDueTrigger("gpu-1", "gpu");
        await GivenDueTrigger("plain-1", executionGroup: null);

        List<IOperableTrigger> acquired = await Acquire(refused: [ExecutionGroupScope.Named("gpu")]);

        acquired.Select(x => x.Key.Name).Should().Equal(["plain-1"]);
    }

    /// <summary>
    /// The half of the contract that makes the trap detectable: a declined trigger gets no misfire
    /// policy applied and no misfire announced, however late it is. Its fire time stays where it was,
    /// which is what a node that does serve the group — or a health check — can read off it.
    /// </summary>
    [Test]
    public async Task ADeclinedTriggerPastTheMisfireThresholdIsNeitherReArmedNorAnnounced()
    {
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-10);
        await GivenDueTrigger("gpu-1", "gpu", due);
        await GivenDueTrigger("reports-1", "reports", due);

        List<IOperableTrigger> acquired = await Acquire(served: [ExecutionGroupScope.Named("reports")]);

        acquired.Select(x => x.Key.Name).Should().Equal(["reports-1"],
            "the served trigger misfired, its FireNow policy re-armed it to now and the pass took it; the refused one was left alone");

        // The control: the served trigger's misfire is handled here.
        A.CallTo(() => signaler.NotifyTriggerListenersMisfired(
                A<ITrigger>.That.Matches(trigger => trigger.Key.Name == "reports-1"),
                A<CancellationToken>._))
            .MustHaveHappened();

        // A node that does not serve the group does not apply its misfire policy either.
        A.CallTo(() => signaler.NotifyTriggerListenersMisfired(
                A<ITrigger>.That.Matches(trigger => trigger.Key.Name == "gpu-1"),
                A<CancellationToken>._))
            .MustNotHaveHappened();

        ITrigger gpu = (await store.GetTrigger(new TriggerKey("gpu-1", "nightly")))!;
        gpu.NextFireTimeUtc.Should().Be(due, "declined, not misfire-handled: the fire time is exactly what it was");
        (await store.GetTriggerState(gpu.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task ADeclinedTriggerIsAcquiredByALaterRequestThatServesIt()
    {
        await GivenDueTrigger("gpu-1", "gpu");

        (await Acquire(served: [ExecutionGroupScope.Named("reports")])).Should().BeEmpty("declined for this request");

        List<IOperableTrigger> acquired = await Acquire(served: null, refused: null);

        acquired.Select(x => x.Key.Name).Should().Equal(["gpu-1"],
            "routing declines a trigger for one attempt, it does not retire it - the next request may serve it");
    }

    private async Task GivenDueTrigger(string name, string? executionGroup, DateTimeOffset? at = null)
    {
        IJobDetail job = JobBuilder.Create<RoutingTestJob>().WithIdentity(name, "jobs").Build();

        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity(name, "nightly")
            .ForJob(job)
            .StartAt(at ?? DateTimeOffset.UtcNow)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow));

        if (executionGroup is not null)
        {
            builder = builder.WithExecutionGroup(executionGroup);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
    }

    private ValueTask<List<IOperableTrigger>> Acquire(
        IReadOnlyCollection<ExecutionGroupScope>? served = null,
        IReadOnlyCollection<ExecutionGroupScope>? refused = null)
    {
        return store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 5,
            TimeWindow = TimeSpan.FromMinutes(1),
            ServedExecutionGroups = served,
            RefusedExecutionGroups = refused,
        });
    }

    private sealed class RoutingTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
