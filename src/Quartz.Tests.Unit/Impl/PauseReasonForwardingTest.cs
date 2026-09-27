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

using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The pause members that carry a reason, where they are not the store itself: the default interface
/// members an implementation of an earlier 4.x runs, and the types that hand them on.
/// </summary>
public sealed class PauseReasonForwardingTest
{
    private static readonly TriggerKey triggerKey = new("nightly", "reports");
    private static readonly JobKey jobKey = new("export", "reports");
    private static readonly GroupMatcher<TriggerKey> triggerGroups = GroupMatcher<TriggerKey>.GroupEquals("reports");
    private static readonly GroupMatcher<JobKey> jobGroups = GroupMatcher<JobKey>.GroupEquals("reports");
    private static readonly PauseDetails details = new() { Reason = "quarter close", RequestedBy = "finance" };
    private static readonly PauseInfo record = new("quarter close", "finance", new DateTimeOffset(2031, 3, 31, 18, 0, 0, TimeSpan.Zero));

    /// <summary>The delegate's default interface members this exercises, and the one they reach.</summary>
    private static readonly HashSet<string> delegateDefaults =
    [
        "PauseTriggerStates", "PauseTriggerGroupStates", "ClearTriggerPauses", "InsertTriggerGroupPause",
        "InsertJobGroupPauses", "SelectTriggerPause", "SelectTriggerGroupPause", "SelectJobGroupPause",
        "InsertPausedJobGroups"
    ];

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The defaults
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ASchedulerOfAnEarlier4xPausesWithoutTheDetailsAndRecordsNothing()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(scheduler).Where(call => call.Method.Name.EndsWith("With", StringComparison.Ordinal) || call.Method.Name.EndsWith("Pause", StringComparison.Ordinal))
            .CallsBaseMethod();
        A.CallTo(() => scheduler.PauseTrigger(triggerKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => scheduler.PauseJob(jobKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => scheduler.PauseTriggerGroups(triggerGroups, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => scheduler.PauseJobGroups(jobGroups, A<CancellationToken>._)).Returns(new List<string> { "reports" });

        (await scheduler.PauseTriggerWith(triggerKey, details)).Should().BeTrue(
            "the default is the reasonless pause, which is what such a scheduler already does");
        (await scheduler.PauseJobWith(jobKey, details)).Should().BeTrue();
        (await scheduler.PauseTriggerGroupsWith(triggerGroups, details)).Should().Equal(["reports"]);
        (await scheduler.PauseJobGroupsWith(jobGroups, details)).Should().Equal(["reports"]);
        await scheduler.PauseAllWith(details);

        A.CallTo(() => scheduler.PauseAll(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        (await scheduler.GetTriggerPause(triggerKey)).Should().BeNull("a scheduler that records nothing has nothing to read back");
        (await scheduler.GetTriggerGroupPause("reports")).Should().BeNull();
        (await scheduler.GetJobGroupPause("reports")).Should().BeNull();
    }

    [Test]
    public async Task AStoreOfAnEarlier4xPausesWithoutTheDetailsAndRecordsNothing()
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(store).Where(call => call.Method.Name.EndsWith("With", StringComparison.Ordinal) || call.Method.Name.EndsWith("Pause", StringComparison.Ordinal))
            .CallsBaseMethod();
        A.CallTo(() => store.PauseTrigger(triggerKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => store.PauseJob(jobKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => store.PauseTriggerGroups(triggerGroups, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => store.PauseJobGroups(jobGroups, A<CancellationToken>._)).Returns(new List<string> { "reports" });

        (await store.PauseTriggerWith(triggerKey, details)).Should().BeTrue();
        (await store.PauseJobWith(jobKey, details)).Should().BeTrue();
        (await store.PauseTriggerGroupsWith(triggerGroups, details)).Should().Equal(["reports"]);
        (await store.PauseJobGroupsWith(jobGroups, details)).Should().Equal(["reports"]);
        await store.PauseAllWith(details);

        A.CallTo(() => store.PauseAll(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        (await store.GetTriggerPause(triggerKey)).Should().BeNull();
        (await store.GetTriggerGroupPause("reports")).Should().BeNull();
        (await store.GetJobGroupPause("reports")).Should().BeNull();
    }

    [Test]
    public async Task ADelegateOfAnEarlier4xMovesTheRowsAndRecordsNothing()
    {
        IDriverDelegate driver = A.Fake<IDriverDelegate>();
        A.CallTo(driver).Where(call => delegateDefaults.Contains(call.Method.Name)).CallsBaseMethod();

        ConnectionAndTransactionHolder conn = new(A.Fake<System.Data.Common.DbConnection>(), null);
        List<TriggerKey> keys = [triggerKey];

        await driver.PauseTriggerStates(conn, keys, StoredTriggerState.Paused, [StoredTriggerState.Waiting], record);
        await driver.PauseTriggerGroupStates(conn, triggerGroups, StoredTriggerState.Paused, [StoredTriggerState.Waiting], record);
        await driver.ClearTriggerPauses(conn, keys);
        await driver.InsertTriggerGroupPause(conn, "reports", record);
        await driver.InsertJobGroupPauses(conn, ["reports"], record);

        A.CallTo(() => driver.UpdateTriggerStatesFromOtherStates(conn, keys, StoredTriggerState.Paused, A<IReadOnlyCollection<StoredTriggerState>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driver.UpdateTriggerGroupStateFromOtherStates(conn, triggerGroups, StoredTriggerState.Paused, A<IReadOnlyCollection<StoredTriggerState>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driver.InsertPausedTriggerGroup(conn, "reports", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => driver.InsertPausedJobGroup(conn, "reports", A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        (await driver.SelectTriggerPause(conn, triggerKey)).Should().BeNull();
        (await driver.SelectTriggerGroupPause(conn, "reports")).Should().BeNull();
        (await driver.SelectJobGroupPause(conn, "reports")).Should().BeNull();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The types that hand them on
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ADelegatingSchedulerHandsEachOneOnWithItsDetails()
    {
        IScheduler inner = Arranged(A.Fake<IScheduler>());
        await ExerciseAndVerify(new DelegatingScheduler(inner), inner);
    }

    [Test]
    public async Task ADeferredSchedulerHandsEachOneToTheSchedulerItResolves()
    {
        IScheduler inner = Arranged(A.Fake<IScheduler>());
        ISchedulerFactory factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).Returns(new ValueTask<IScheduler>(inner));
        IOptionsMonitor<QuartzSchedulerOptions> options = A.Fake<IOptionsMonitor<QuartzSchedulerOptions>>();
        A.CallTo(() => options.Get(A<string>._)).Returns(new QuartzSchedulerOptions());

        await ExerciseAndVerify(new DeferredScheduler(factory, options, new SchedulerKey("reporting")), inner);
    }

    [Test]
    public async Task ADelegatingJobStoreHandsEachOneOnWithItsDetails()
    {
        IJobStore inner = A.Fake<IJobStore>();
        A.CallTo(() => inner.PauseTriggerWith(triggerKey, details, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => inner.PauseJobWith(jobKey, details, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => inner.PauseTriggerGroupsWith(triggerGroups, details, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => inner.PauseJobGroupsWith(jobGroups, details, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => inner.GetTriggerPause(triggerKey, A<CancellationToken>._)).Returns(record);
        A.CallTo(() => inner.GetTriggerGroupPause("reports", A<CancellationToken>._)).Returns(record);
        A.CallTo(() => inner.GetJobGroupPause("reports", A<CancellationToken>._)).Returns(record);

        DelegatingJobStore store = new(inner);

        (await store.PauseTriggerWith(triggerKey, details)).Should().BeTrue();
        (await store.PauseJobWith(jobKey, details)).Should().BeTrue();
        (await store.PauseTriggerGroupsWith(triggerGroups, details)).Should().Equal(["reports"]);
        (await store.PauseJobGroupsWith(jobGroups, details)).Should().Equal(["reports"]);
        await store.PauseAllWith(details);
        (await store.GetTriggerPause(triggerKey)).Should().Be(record);
        (await store.GetTriggerGroupPause("reports")).Should().Be(record);
        (await store.GetJobGroupPause("reports")).Should().Be(record);

        A.CallTo(() => inner.PauseAllWith(details, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => inner.PauseTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private static IScheduler Arranged(IScheduler inner)
    {
        A.CallTo(() => inner.PauseTriggerWith(triggerKey, details, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => inner.PauseJobWith(jobKey, details, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => inner.PauseTriggerGroupsWith(triggerGroups, details, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => inner.PauseJobGroupsWith(jobGroups, details, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => inner.GetTriggerPause(triggerKey, A<CancellationToken>._)).Returns(record);
        A.CallTo(() => inner.GetTriggerGroupPause("reports", A<CancellationToken>._)).Returns(record);
        A.CallTo(() => inner.GetJobGroupPause("reports", A<CancellationToken>._)).Returns(record);
        return inner;
    }

    private static async Task ExerciseAndVerify(IScheduler forwarder, IScheduler inner)
    {
        (await forwarder.PauseTriggerWith(triggerKey, details)).Should().BeTrue(
            "the forwarder answers what the scheduler behind it answered");
        (await forwarder.PauseJobWith(jobKey, details)).Should().BeTrue();
        (await forwarder.PauseTriggerGroupsWith(triggerGroups, details)).Should().Equal(["reports"]);
        (await forwarder.PauseJobGroupsWith(jobGroups, details)).Should().Equal(["reports"]);
        await forwarder.PauseAllWith(details);
        (await forwarder.GetTriggerPause(triggerKey)).Should().Be(record);
        (await forwarder.GetTriggerGroupPause("reports")).Should().Be(record);
        (await forwarder.GetJobGroupPause("reports")).Should().Be(record);

        A.CallTo(() => inner.PauseAllWith(details, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // A forwarder that answered by the default would drop the details on the way.
        A.CallTo(() => inner.PauseTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => inner.PauseAll(A<CancellationToken>._)).MustNotHaveHappened();
    }
}
