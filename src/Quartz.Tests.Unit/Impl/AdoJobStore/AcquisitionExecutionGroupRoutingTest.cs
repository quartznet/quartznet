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
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The store contract for execution-group routing (#4069): what the request and the criteria accept,
/// how the criteria are built from the request, and the backstop the store keeps for a delegate that
/// does not filter the groups itself.
/// </summary>
/// <remarks>
/// The backstop exists for the reason the job-type one does: a delegate that ignores the routing would
/// have the node <em>run</em> a group the deployment gave to other nodes. That the shipped dialects filter
/// in SQL, and say so, is <c>AcquisitionSqlTest</c>'s business.
/// </remarks>
public class AcquisitionExecutionGroupRoutingTest
{
    private static readonly DateTimeOffset FireTime = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private AdoJobStoreBaseTest.TestAdoJobStoreBase store = null!;
    private IDriverDelegate driverDelegate = null!;

    [SetUp]
    public void SetUp()
    {
        driverDelegate = A.Fake<IDriverDelegate>();
        store = new AdoJobStoreBaseTest.TestAdoJobStoreBase();
        store.DirectDelegate = driverDelegate;
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();

        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerKey>._,
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<DateTimeOffset>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
    }

    [Test]
    public void TheRequestRefusesTheCatchAll()
    {
        Action act = () => _ = new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime,
            ServedExecutionGroups = [ExecutionGroupScope.OtherGroups],
        };

        act.Should().Throw<ArgumentException>("'*' is a limits rule, and spelling it in SQL would need a predicate nothing routes by")
            .WithMessage("*named groups and the ungrouped bucket only*");
    }

    [Test]
    public void TheRequestRefusesAPrefix()
    {
        Action act = () => _ = new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime,
            RefusedExecutionGroups = [ExecutionGroupScope.GroupsWithPrefix("tenant:")],
        };

        act.Should().Throw<ArgumentException>().WithMessage("*cannot be routed*");
    }

    [Test]
    public void TheRequestRefusesMoreThanAThousandEntries()
    {
        ExecutionGroupScope[] scopes = Enumerable.Range(0, ExecutionGroupRouting.MaxGroups + 1)
            .Select(index => ExecutionGroupScope.Named("group-" + index))
            .ToArray();

        Action act = () => _ = new TriggerAcquisitionRequest { NoLaterThan = FireTime, ServedExecutionGroups = scopes };

        act.Should().Throw<ArgumentException>().WithMessage($"*must not exceed {ExecutionGroupRouting.MaxGroups} entries*");
    }

    [Test]
    public void TheRequestRefusesADuplicate()
    {
        Action act = () => _ = new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime,
            ServedExecutionGroups = [ExecutionGroupScope.Named("gpu"), ExecutionGroupScope.Named("gpu")],
        };

        act.Should().Throw<ArgumentException>().WithMessage("*'gpu' more than once*");
    }

    [Test]
    public void TheRequestRefusesBothListsAtOnce()
    {
        Action act = () => _ = new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime,
            ServedExecutionGroups = [ExecutionGroupScope.Named("reports")],
            RefusedExecutionGroups = [ExecutionGroupScope.Named("gpu")],
        };

        act.Should().Throw<ArgumentException>().WithMessage("*cannot both be set*");
    }

    [Test]
    public void TheCriteriaHoldTheSameRules()
    {
        Action catchAll = () => _ = Criteria() with { ServedExecutionGroups = [ExecutionGroupScope.OtherGroups] };
        Action both = () => _ = Criteria() with
        {
            ServedExecutionGroups = [ExecutionGroupScope.Named("reports")],
            RefusedExecutionGroups = [ExecutionGroupScope.Default],
        };

        catchAll.Should().Throw<ArgumentException>(
            "a derived store sets its routing on the criteria and never touches the request, so the check has to hold here too");
        both.Should().Throw<ArgumentException>().WithMessage("*cannot both be set*");
    }

    [Test]
    public async Task TheCriteriaCarryTheRequestsListsByReference()
    {
        List<TriggerAcquisitionCriteria> received = GivenNoTriggersToAcquire();
        ExecutionGroupScope[] served = [ExecutionGroupScope.Named("reports"), ExecutionGroupScope.Default];

        await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime,
            MaxCount = 5,
            ServedExecutionGroups = served,
        });

        TriggerAcquisitionCriteria criteria = received.Should().ContainSingle().Subject;
        criteria.ServedExecutionGroups.Should().BeSameAs(served,
            "the delegate keys its statement cache on the shape and binds the names from the very collection the request carried");
        criteria.RefusedExecutionGroups.Should().BeNull();
    }

    /// <summary>
    /// The backstop: a delegate answering <see langword="false" /> that returns a candidate the node does
    /// not serve has it dropped on the group the read already returned, never read back, and counted as
    /// a skipped row — so the round reads past it rather than taking its short read for the end of what
    /// is due.
    /// </summary>
    [Test]
    public async Task ACandidateTheNodeDoesNotServeIsDroppedCountedSkippedAndReadPast()
    {
        List<TriggerAcquisitionCriteria> received = [];
        int call = 0;
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerAcquisitionCriteria>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, TriggerAcquisitionCriteria criteria, CancellationToken _) =>
            {
                received.Add(criteria);

                // The first read comes back full of one unserved row; the wider read behind it finds the
                // served row due behind it.
                List<TriggerAcquireResult> rows = ++call == 1
                    ? [Candidate("gpu-1", "gpu")]
                    : [Candidate("gpu-1", "gpu"), Candidate("reports-1", "reports")];
                return new ValueTask<List<TriggerAcquireResult>>(rows);
            });
        GivenTriggerRows("gpu-1", "reports-1");

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime + TimeSpan.FromHours(1),
            MaxCount = 1,
            ServedExecutionGroups = [ExecutionGroupScope.Named("reports")],
        });

        acquired.Select(trigger => trigger.Key.Name).Should().Equal(["reports-1"],
            "a delegate that does not filter the groups itself must not have the node run one it refuses");
        received.Select(criteria => criteria.MaxCount).Should().Equal([1, 3],
            "the unserved row is a skipped row: it stays WAITING first in the order, so the next read reaches twice as far past the count");

        // The dropped candidate is never read back.
        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>.That.Matches(keys => keys.Any(key => key.Name == "gpu-1")),
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task ARefuseListDropsTheRefusedGroupAndKeepsUngroupedRows()
    {
        GivenCandidates(Candidate("gpu-1", "gpu"), Candidate("plain-1", executionGroup: null), Candidate("reports-1", "reports"));
        GivenTriggerRows("gpu-1", "plain-1", "reports-1");

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime + TimeSpan.FromHours(1),
            MaxCount = 5,
            RefusedExecutionGroups = [ExecutionGroupScope.Named("gpu")],
        });

        acquired.Select(trigger => trigger.Key.Name).Should().Equal(["plain-1", "reports-1"],
            "a refuse-list that does not name the ungrouped bucket serves it");
    }

    [Test]
    public async Task ADelegateThatFiltersTheGroupsItselfIsNotSecondGuessed()
    {
        A.CallTo(() => driverDelegate.FiltersAcquisitionExecutionGroups).Returns(true);
        GivenCandidates(Candidate("gpu-1", "gpu"), Candidate("gpu-2", "gpu"));
        GivenTriggerRows("gpu-1", "gpu-2");

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime + TimeSpan.FromHours(1),
            MaxCount = 5,
            ServedExecutionGroups = [ExecutionGroupScope.Named("reports")],
        });

        acquired.Should().HaveCount(2,
            "the backstop exists for a delegate that ignores the routing, and this one says its statement already carries the clause");
    }

    private static TriggerAcquisitionCriteria Criteria()
    {
        return new TriggerAcquisitionCriteria
        {
            NoLaterThan = FireTime,
            NoEarlierThan = FireTime,
            MaxCount = 1,
            LiveNodeCutoff = FireTime,
        };
    }

    private List<TriggerAcquisitionCriteria> GivenNoTriggersToAcquire()
    {
        List<TriggerAcquisitionCriteria> received = [];
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerAcquisitionCriteria>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, TriggerAcquisitionCriteria criteria, CancellationToken _) =>
            {
                received.Add(criteria);
                return new ValueTask<List<TriggerAcquireResult>>(new List<TriggerAcquireResult>());
            });
        return received;
    }

    private void GivenCandidates(params TriggerAcquireResult[] candidates)
    {
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerAcquisitionCriteria>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerAcquireResult>>(candidates.ToList()));
    }

    private void GivenTriggerRows(params string[] names)
    {
        List<IOperableTrigger> rows = [.. names.Select(CreateTrigger)];

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<IOperableTrigger>>(rows.Where(row => keys.Contains(row.Key)).ToList()));
    }

    private static TriggerAcquireResult Candidate(string name, string? executionGroup)
    {
        return new TriggerAcquireResult(new TriggerKey(name, "g1"), typeof(NoOpRoutingJob).AssemblyQualifiedName!, executionGroup);
    }

    private static IOperableTrigger CreateTrigger(string name)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g1")
            .ForJob("j1", "jg1")
            .StartAt(FireTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        trigger.NextFireTimeUtc = FireTime;
        return trigger;
    }

    private sealed class NoOpRoutingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
