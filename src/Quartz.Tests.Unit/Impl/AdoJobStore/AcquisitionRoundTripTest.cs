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
/// What one acquisition round costs, and in which order it spends it.
/// </summary>
/// <remarks>
/// <para>
/// The trigger-access lock is held across the whole round, so every statement in it is a window the
/// other nodes of a cluster wait on. A round used to be three statements per candidate: the acquisition
/// read named them, then each was read back on its own, marked acquired, and written to the
/// fired-triggers table. What only this level can show is that the reads and the fired-trigger writes
/// are now one statement each for the whole round (#3424).
/// </para>
/// <para>
/// The delegate is faked, so a "statement" here is a call to a delegate member. That is the right
/// granularity: each member below is one statement against a real database, and the batched ones say
/// so in their own tests.
/// </para>
/// </remarks>
public class AcquisitionRoundTripTest
{
    private static readonly DateTimeOffset FireTime = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobKey OrdinaryJob = new("j1", "jg1");
    private static readonly JobKey SerialJob = new("serial", "jg1");
    private const string UnloadableJobType = "No.Such.Job, Nowhere";

    private AdoJobStoreBaseTest.TestAdoJobStoreBase store;
    private IDriverDelegate driverDelegate;

    // The keys of every read of the candidates' rows, comma-joined, in the order the reads were made.
    private List<string> rowsRead;

    [SetUp]
    public void SetUp()
    {
        driverDelegate = A.Fake<IDriverDelegate>();
        store = new AdoJobStoreBaseTest.TestAdoJobStoreBase();
        store.DirectDelegate = driverDelegate;
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();
        rowsRead = [];

        // Every candidate wins its compare-and-swap unless a test says otherwise.
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerKey>._,
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<DateTimeOffset>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
    }

    /// <summary>
    /// The acquisition read has just named the candidates, so reading them back is one statement for
    /// the round rather than one per candidate.
    /// </summary>
    [Test]
    public async Task TheRoundReadsItsCandidatesInOneStatement()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"), Candidate("t3"));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 3);

        acquired.Should().HaveCount(3);

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.SelectTrigger(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerKey>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// The rows name no job until the trigger fires, so nothing in the round can see them and they can
    /// all go out together.
    /// </summary>
    [Test]
    public async Task TheRoundWritesItsFiredTriggerRowsTogether()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"), Candidate("t3"));

        await Acquire(maxCount: 3);

        A.CallTo(() => driverDelegate.InsertFiredTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<IOperableTrigger>>.That.Matches(triggers => triggers.Count == 3),
                StoredTriggerState.Acquired,
                null,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.InsertFiredTrigger(
                A<ConnectionAndTransactionHolder>._,
                A<IOperableTrigger>._,
                A<StoredTriggerState>._,
                A<IJobDetail>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// Deferring the fired-trigger row to the end of the round must not decouple it from the
    /// compare-and-swap that earned it — that swap is the whole of what makes lock-free acquisition
    /// safe.
    /// </summary>
    [Test]
    public async Task ACandidateThatLosesItsStateUpdateWritesNoFiredTriggerRow()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"));

        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                new TriggerKey("t1", "g1"),
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<DateTimeOffset>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 2);

        acquired.Should().ContainSingle().Which.Key.Should().Be(new TriggerKey("t2", "g1"),
            "the trigger whose state update affected no row was taken by somebody else");

        A.CallTo(() => driverDelegate.InsertFiredTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<IOperableTrigger>>.That.Matches(triggers => triggers.Count == 1 && triggers[0].Key.Name == "t2"),
                A<StoredTriggerState>._,
                A<IJobDetail>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A candidate named by the acquisition read but absent from the batch read is one whose row has
    /// gone, which is what the per-candidate read used to say by answering null.
    /// </summary>
    [Test]
    public async Task ACandidateWhoseRowHasGoneIsSkipped()
    {
        GivenCandidates([Candidate("t1"), Candidate("t2")], readBack: ["t1"]);

        List<IOperableTrigger> acquired = await Acquire(maxCount: 2);

        acquired.Should().ContainSingle().Which.Key.Should().Be(new TriggerKey("t1", "g1"));
    }

    /// <summary>
    /// A job that disallows concurrent execution takes one trigger a round, so its later candidates are
    /// refused whatever their rows hold: only its first candidate's row is read (#4173).
    /// </summary>
    [Test]
    public async Task TheRoundReadsOnlyTheFirstCandidateOfASerialJob()
    {
        GivenCandidates(SerialCandidate("t1"), SerialCandidate("t2"), SerialCandidate("t3"), Candidate("t4"));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 4);

        acquired.Select(x => x.Key.Name).Should().Equal(["t1", "t4"], "the serial job takes one trigger a round");
        rowsRead.Should().Equal(["t1,t4"], "the acquisition read named the job of every candidate, which is all a refusal needs");
    }

    /// <summary>
    /// A first candidate whose row has gone is not taken up, so its job may still take the next one: that
    /// one, left out of the round's read, is read on its own and taken.
    /// </summary>
    [Test]
    public async Task ASerialJobsNextCandidateIsReadOnItsOwnWhenTheFirstOnesRowHasGone()
    {
        GivenCandidates([SerialCandidate("t1"), SerialCandidate("t2"), SerialCandidate("t3")], readBack: ["t2", "t3"]);

        List<IOperableTrigger> acquired = await Acquire(maxCount: 3);

        acquired.Select(x => x.Key.Name).Should().Equal(["t2"], "the job took none of its triggers before this one");
        rowsRead.Should().Equal(["t1", "t2"], "the third candidate is refused once the job has taken the second");
    }

    /// <summary>
    /// A job type that will not load is settled the round it is met, for every candidate of the job, as it
    /// was when the round read every candidate's row.
    /// </summary>
    [Test]
    public async Task EveryCandidateOfASerialJobWhoseTypeWillNotLoadIsStoredError()
    {
        GivenCandidates(SerialCandidate("t1", jobTypeName: UnloadableJobType), SerialCandidate("t2", jobTypeName: UnloadableJobType));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 2);

        acquired.Should().BeEmpty();
        foreach (string name in (string[]) ["t1", "t2"])
        {
            A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(
                    A<ConnectionAndTransactionHolder>._,
                    new TriggerKey(name, "g1"),
                    StoredTriggerState.Error,
                    StoredTriggerState.Waiting,
                    A<CancellationToken>._))
                .MustHaveHappened();
        }
    }

    /// <summary>
    /// A serial job executing elsewhere refuses all of its candidates, and the first is the only one whose
    /// row is read to find that out.
    /// </summary>
    [Test]
    public async Task ASerialJobExecutingElsewhereHasOnlyItsFirstCandidateRead()
    {
        GivenCandidates(SerialCandidate("t1"), SerialCandidate("t2"), Candidate("t3"));
        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(A<ConnectionAndTransactionHolder>._, SerialJob, A<CancellationToken>._))
            .Returns(new ValueTask<bool>(true));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 3);

        acquired.Select(x => x.Key.Name).Should().Equal(["t3"]);
        rowsRead.Should().Equal(["t1,t3"]);
        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(A<ConnectionAndTransactionHolder>._, SerialJob, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A delegate that does not name the job has every candidate's row read, and the job's second
    /// candidate is refused on the job its row names, as before.
    /// </summary>
    [Test]
    public async Task ASerialCandidateThatNamesNoJobHasItsRowRead()
    {
        GivenCandidates(SerialCandidate("t1", namesJob: false), SerialCandidate("t2", namesJob: false), Candidate("t3"));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 3);

        acquired.Select(x => x.Key.Name).Should().Equal(["t1", "t3"]);
        rowsRead.Should().Equal(["t1,t2,t3"]);
    }

    private ValueTask<List<IOperableTrigger>> Acquire(int maxCount)
    {
        return store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = FireTime + TimeSpan.FromHours(1),
            MaxCount = maxCount,
        });
    }

    private void GivenCandidates(params TriggerAcquireResult[] candidates)
    {
        GivenCandidates(candidates, [.. candidates.Select(candidate => candidate.TriggerKey.Name)]);
    }

    /// <summary>
    /// Arranges an acquisition read that returns <paramref name="candidates" /> and a batch read that
    /// answers with the subset named by <paramref name="readBack" />.
    /// </summary>
    private void GivenCandidates(IReadOnlyCollection<TriggerAcquireResult> candidates, IReadOnlyCollection<string> readBack)
    {
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerAcquisitionCriteria>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerAcquireResult>>(candidates.ToList()));

        // Each row fires the job its candidate is of: the serial job's own, or the ordinary one.
        Dictionary<string, JobKey> jobs = candidates.ToDictionary(
            candidate => candidate.TriggerKey.Name,
            candidate => candidate.ConcurrentExecutionDisallowed == true ? SerialJob : OrdinaryJob);
        List<IOperableTrigger> rows = [.. readBack.Select(name => CreateTrigger(name, jobs[name]))];

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
            {
                rowsRead.Add(string.Join(",", keys.Select(key => key.Name)));
                return new ValueTask<List<IOperableTrigger>>(rows.Where(row => keys.Contains(row.Key)).ToList());
            });
    }

    private static TriggerAcquireResult Candidate(string name)
    {
        return new TriggerAcquireResult(new TriggerKey(name, "g1"), typeof(NoOpAcquisitionJob).AssemblyQualifiedName, null);
    }

    /// <summary>
    /// A candidate of <see cref="SerialJob" />, as a shipped delegate reads it: flagged, and naming the job
    /// unless <paramref name="namesJob" /> says the delegate did not read it.
    /// </summary>
    private static TriggerAcquireResult SerialCandidate(string name, bool namesJob = true, string jobTypeName = null)
    {
        return new TriggerAcquireResult(new TriggerKey(name, "g1"), jobTypeName ?? typeof(NoOpAcquisitionJob).AssemblyQualifiedName, null)
        {
            ConcurrentExecutionDisallowed = true,
            JobKey = namesJob ? SerialJob : null,
        };
    }

    private static IOperableTrigger CreateTrigger(string name, JobKey jobKey)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g1")
            .ForJob(jobKey)
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
