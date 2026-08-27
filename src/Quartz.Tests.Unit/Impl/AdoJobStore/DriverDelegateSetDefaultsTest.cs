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
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a driver delegate written before the set-shaped members does when the store calls them.
/// </summary>
/// <remarks>
/// Every set-shaped member on <see cref="IDriverDelegate" /> is a default interface member whose
/// default is the per-key loop the store used to make itself, which is the whole of the promise that a
/// delegate of somebody's own keeps compiling and keeps behaving as it did. Nothing else exercises
/// those defaults — <see cref="StdAdoDelegate" /> overrides all of them — so the promise is asserted
/// here against a fake that overrides none.
/// </remarks>
public class DriverDelegateSetDefaultsTest
{
    private static readonly TriggerKey First = new("first", "g");
    private static readonly TriggerKey Second = new("second", "g");

    private IDriverDelegate driverDelegate;
    private ConnectionAndTransactionHolder conn;

    [SetUp]
    public void SetUp()
    {
        driverDelegate = A.Fake<IDriverDelegate>(options => options.CallsBaseMethods());
        conn = new ConnectionAndTransactionHolder(new StubBatchingConnection(), transaction: null);
    }

    [TearDown]
    public void TearDown()
    {
        conn?.Dispose();
    }

    [Test]
    public void TheDefaultFiltersNoJobTypeExclusionsItself()
    {
        driverDelegate.FiltersAcquisitionJobTypeExclusions.Should().BeFalse(
            "a delegate written before the exclusions existed cannot have honoured them, so the store keeps its backstop");
    }

    [Test]
    public async Task TheDefaultWritesOneFiredTriggerRowPerTrigger()
    {
        IOperableTrigger[] triggers = [Trigger("a"), Trigger("b")];

        await driverDelegate.InsertFiredTriggers(conn, triggers, StoredTriggerState.Acquired, null);

        foreach (IOperableTrigger trigger in triggers)
        {
            A.CallTo(() => driverDelegate.InsertFiredTrigger(conn, trigger, StoredTriggerState.Acquired, null, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
    }

    [Test]
    public async Task TheDefaultReadsOneHeaderPerKeyAndDropsTheMissingOnes()
    {
        StoredTriggerHeader header = new(First, new JobKey("j", "jg"), StoredTriggerState.Waiting, DateTimeOffset.UtcNow, AdoConstants.TriggerTypeSimple);
        A.CallTo(() => driverDelegate.SelectTriggerHeader(conn, First, A<CancellationToken>._))
            .Returns(new ValueTask<StoredTriggerHeader>(header));
        A.CallTo(() => driverDelegate.SelectTriggerHeader(conn, Second, A<CancellationToken>._))
            .Returns(new ValueTask<StoredTriggerHeader>((StoredTriggerHeader) null));

        List<StoredTriggerHeader> headers = await driverDelegate.SelectStoredTriggerHeaders(conn, [First, Second]);

        headers.Should().Equal([header], "a key with no row is absent from the result, as it is from the set read");
    }

    [Test]
    public async Task TheDefaultUpdatesOneKeyAtATimeAndSumsTheRows()
    {
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStates(
                conn, A<TriggerKey>._, A<StoredTriggerState>._, A<IReadOnlyCollection<StoredTriggerState>>._, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));

        int updated = await driverDelegate.UpdateTriggerStatesFromOtherStates(
            conn, [First, Second], StoredTriggerState.Paused, [StoredTriggerState.Waiting]);

        updated.Should().Be(2, "the loop reports what the whole set moved, as the one statement does");
    }

    [Test]
    public async Task TheDefaultReadsOneJobsTriggerKeysAtATime()
    {
        JobKey job = new("j", "jg");
        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(conn, job, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerKey>>([First, Second]));

        List<TriggerKey> keys = await driverDelegate.SelectTriggerKeysForJobs(conn, [job]);

        keys.Should().Equal([First, Second]);
    }

    [Test]
    public async Task TheDefaultAsksAboutOnePausedJobGroupAtATime()
    {
        A.CallTo(() => driverDelegate.IsJobGroupPaused(conn, "reports", A<CancellationToken>._))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.IsJobGroupPaused(conn, "billing", A<CancellationToken>._))
            .Returns(new ValueTask<bool>(false));

        List<string> paused = await driverDelegate.SelectPausedJobGroups(conn, ["reports", "billing"]);

        paused.Should().Equal(["reports"]);
    }

    [Test]
    public async Task TheDefaultInsertsOnePausedJobGroupAtATime()
    {
        await driverDelegate.InsertPausedJobGroups(conn, ["reports", "billing"]);

        A.CallTo(() => driverDelegate.InsertPausedJobGroup(conn, "reports", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.InsertPausedJobGroup(conn, "billing", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private static IOperableTrigger Trigger(string name)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        trigger.FireInstanceId = name + "-fire";
        return trigger;
    }
}
