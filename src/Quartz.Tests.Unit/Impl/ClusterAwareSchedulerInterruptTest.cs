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

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Whom a dashboard cluster row asks to interrupt (#4141): one member that records requests for its peers,
/// the preferred one first and the next on failure, plus every member that does not.
/// </summary>
/// <remarks>
/// A 4.6 member interrupts its own firings and records a request for every other node's (#4071), so asking
/// each member wrote the same request once per member. A member older than 4.6 records nothing and reads
/// nothing, so only a direct question reaches its firings.
/// </remarks>
public sealed class ClusterAwareSchedulerInterruptTest
{
    private const string Current = "4.7.0.0";
    private const string Older = "4.5.0.0";

    private static readonly JobKey job = new("nightly", "reports");

    [Test]
    public async Task WithEveryMemberRecordingRequestsOnlyThePreferredMemberIsAsked()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", Current), ("b", Current), ("c", Current));
        A.CallTo(() => nodes["a"].Interrupt(job, A<CancellationToken>._)).Returns(true);

        (await cluster.Interrupt(job)).Should().BeTrue("the preferred member interrupted or recorded a request");

        A.CallTo(() => nodes["a"].Interrupt(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => nodes["b"].Interrupt(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
        // The preferred member already recorded a request for every other node's firing, which each reads at its check-in.
        A.CallTo(() => nodes["c"].Interrupt(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ThePreferredMemberIsAskedFirstWhereverItSorts()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 2, ("a", Current), ("b", Current), ("c", Current));

        await cluster.Interrupt(job);

        A.CallTo(() => nodes["c"].Interrupt(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => nodes["a"].Interrupt(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task WhenThePreferredMemberFailsTheNextIsAsked()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", Current), ("b", Current), ("c", Current));
        A.CallTo(() => nodes["a"].Interrupt(job, A<CancellationToken>._)).Throws(new SchedulerException("unreachable"));
        A.CallTo(() => nodes["b"].Interrupt(job, A<CancellationToken>._)).Returns(true);

        (await cluster.Interrupt(job)).Should().BeTrue();

        A.CallTo(() => nodes["b"].Interrupt(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // The second member answered, so the third is not asked.
        A.CallTo(() => nodes["c"].Interrupt(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AMemberOlderThan46IsAskedBesideTheOneThatRecords()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", Current), ("b", Older), ("c", Current));
        A.CallTo(() => nodes["b"].Interrupt(job, A<CancellationToken>._)).Returns(true);

        (await cluster.Interrupt(job)).Should().BeTrue("the older member interrupted a firing it runs");

        A.CallTo(() => nodes["a"].Interrupt(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // A 4.5 node reads no request, so a firing it runs is reached only by asking it.
        A.CallTo(() => nodes["b"].Interrupt(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => nodes["c"].Interrupt(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [TestCase(Older)]
    [TestCase(null)]
    [TestCase("not a version")]
    public async Task WithNoMemberKnownToRecordRequestsEveryMemberIsAsked(string? version)
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", version), ("b", version));

        await cluster.Interrupt(job);

        A.CallTo(() => nodes["a"].Interrupt(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // Nothing says either member reaches the other, so each is asked, as before 4.7.
        A.CallTo(() => nodes["b"].Interrupt(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task WhenEveryRecorderFailsAnOlderMembersAnswerStands()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", Current), ("b", Older));
        A.CallTo(() => nodes["a"].Interrupt(job, A<CancellationToken>._)).Throws(new SchedulerException("unreachable"));
        A.CallTo(() => nodes["b"].Interrupt(job, A<CancellationToken>._)).Returns(false);

        (await cluster.Interrupt(job)).Should().BeFalse("the member that answered had nothing to interrupt, and that is an answer");
    }

    [Test]
    public async Task WhenNoMemberAnswersTheFirstFailureIsThrown()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", Current), ("b", Current));
        A.CallTo(() => nodes["a"].Interrupt(job, A<CancellationToken>._)).Throws(new SchedulerException("a is down"));
        A.CallTo(() => nodes["b"].Interrupt(job, A<CancellationToken>._)).Throws(new SchedulerException("b is down"));

        Func<Task> act = async () => await cluster.Interrupt(job);

        (await act.Should().ThrowAsync<SchedulerException>()).WithMessage("No member of cluster 'a+b' answered: a is down");
    }

    [Test]
    public async Task AFiringIsAskedOfThePreferredMemberWithoutLookingUpItsOwnerWhenEveryMemberRecords()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", Current), ("b", Current));
        A.CallTo(() => nodes["a"].InterruptFireInstance("f", A<CancellationToken>._)).Returns(true);

        (await cluster.InterruptFireInstance("f")).Should().BeTrue();

        // The preferred member records a request for whichever node runs the firing, so the store need not be paged through first.
        A.CallTo(() => nodes["a"].QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => nodes["b"].InterruptFireInstance(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AFiringStillGoesToItsOwnerWhenAMemberIsOlder()
    {
        (ClusterAwareScheduler cluster, Dictionary<string, IScheduler> nodes) = Cluster(preferred: 0, ("a", Current), ("b", Older));
        A.CallTo(() => nodes["a"].QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstance>([Firing("f", "node-b")], HasMore: false));
        A.CallTo(() => nodes["b"].InterruptFireInstance("f", A<CancellationToken>._)).Returns(true);

        (await cluster.InterruptFireInstance("f")).Should().BeTrue();

        // The firing runs on the 4.5 node, which reads no request, so it is asked itself.
        A.CallTo(() => nodes["b"].InterruptFireInstance("f", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => nodes["a"].InterruptFireInstance(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private static (ClusterAwareScheduler Cluster, Dictionary<string, IScheduler> Nodes) Cluster(int preferred, params (string Target, string? Version)[] targets)
    {
        Dictionary<string, IScheduler> nodes = new(StringComparer.Ordinal);
        List<ClusterMember> members = [];
        foreach ((string target, string? version) in targets)
        {
            IScheduler scheduler = A.Fake<IScheduler>();
            nodes[target] = scheduler;
            members.Add(new ClusterMember(target, scheduler, "node-" + target, version));
        }

        return (new ClusterAwareScheduler(string.Join('+', targets.Select(x => x.Target)), members, preferred), nodes);
    }

    private static FireInstance Firing(string fireInstanceId, string instanceId)
    {
        return new FireInstance(
            fireInstanceId,
            new TriggerKey("at-midnight", "nightly"),
            job,
            instanceId,
            FireInstanceState.Executing,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            ExecutionGroup: null);
    }
}
