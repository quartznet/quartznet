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
/// The job-type exclusion backstop: when the store applies it, and what it costs when it does.
/// </summary>
/// <remarks>
/// <para>
/// <c>ExcludedJobTypeNames</c> is the one acquisition criterion a delegate cannot safely ignore, because
/// ignoring it means the node <em>runs</em> job types the deployment excluded. Every other criterion
/// degrades to a wider result set. So <see cref="AdoJobStoreBase" /> keeps a backstop — and the two
/// facts about it that matter are asserted here: it drops the candidate on the job type name the
/// acquisition read already returned, before that candidate costs a read or a type resolution, and it
/// is skipped entirely for a delegate that says its own statement already filters (#3443).
/// </para>
/// <para>
/// That the shipped dialects really do say so, and really do carry the clause, is
/// <c>AcquisitionSqlTest</c>'s business.
/// </para>
/// </remarks>
public class AcquisitionJobTypeExclusionTest
{
    /// <summary>
    /// A job type name that names no type this process can load, so that resolving it would be visible
    /// as an error-state write.
    /// </summary>
    private const string ExcludedJobTypeName = "Contoso.Jobs.ReportJob, Contoso.Jobs";

    private static readonly DateTimeOffset FireTime = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private AdoJobStoreBaseTest.TestAdoJobStoreBase store;
    private IDriverDelegate driverDelegate;

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
    public async Task AnExcludedJobTypeIsDroppedBeforeItCostsAReadOrATypeResolution()
    {
        GivenCandidates(Candidate("t1"), Candidate("excluded", ExcludedJobTypeName), Candidate("t2"));

        List<IOperableTrigger> acquired = await Acquire([ExcludedJobTypeName]);

        acquired.Select(trigger => trigger.Key.Name).Should().Equal(["t1", "t2"],
            "a delegate that does not filter the exclusions itself must not have them run anyway");

        // The excluded candidate is dropped on the job type name the acquisition read already returned,
        // so it is never read back.
        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>.That.Matches(keys => !keys.Contains(new TriggerKey("excluded", "g1"))),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // ExcludedJobTypeName names no type that will load. Had the check still run after
        // JobType.Resolve, the candidate would have been driven into the error state instead.
        A.CallTo(() => driverDelegate.UpdateTriggerState(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerKey>._,
                StoredTriggerState.Error,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// The other half of the contract: a delegate that says it keeps the excluded rows out is taken at
    /// its word, and the store neither builds the exclusion set nor tests a candidate against it. Every
    /// dialect Quartz ships says exactly that, so the shipped path pays nothing per candidate for a
    /// promise its own statement already keeps.
    /// </summary>
    [Test]
    public async Task ADelegateThatFiltersTheExclusionsItselfIsNotSecondGuessed()
    {
        A.CallTo(() => driverDelegate.FiltersAcquisitionJobTypeExclusions).Returns(true);

        GivenCandidates(Candidate("t1"), Candidate("t2"));

        // Both candidates carry a job type the request excludes, which a filtering delegate would never
        // have returned.
        List<IOperableTrigger> acquired = await Acquire([typeof(NoOpAcquisitionJob).AssemblyQualifiedName]);

        acquired.Should().HaveCount(2,
            "the backstop exists for a delegate that ignores the exclusions, and this one says it does not");
    }

    private ValueTask<List<IOperableTrigger>> Acquire(IReadOnlyCollection<string> excludedJobTypeNames)
    {
        return store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime + TimeSpan.FromHours(1),
            MaxCount = 5,
            ExcludedJobTypeNames = excludedJobTypeNames,
        });
    }

    private void GivenCandidates(params TriggerAcquireResult[] candidates)
    {
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerAcquisitionCriteria>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerAcquireResult>>(candidates.ToList()));

        List<IOperableTrigger> rows = [.. candidates.Select(candidate => CreateTrigger(candidate.TriggerKey.Name))];

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<IOperableTrigger>>(rows.Where(row => keys.Contains(row.Key)).ToList()));
    }

    private static TriggerAcquireResult Candidate(string name, string jobTypeName = null)
    {
        return new TriggerAcquireResult(
            new TriggerKey(name, "g1"),
            jobTypeName ?? typeof(NoOpAcquisitionJob).AssemblyQualifiedName,
            null);
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

    private sealed class NoOpAcquisitionJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
