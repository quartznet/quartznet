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

using Quartz.Listeners;

namespace Quartz.Tests.Unit.Listeners;

/// <summary>
/// A job can be chained to more than one follow-up (#2215); the dictionary behind the links used to
/// throw on the second one, which left sequential chaining as the only shape a chain could have.
/// Each follow-up is a firing of its own, so the scheduler decides how many run at once — what the
/// listener owes them is that every one is asked for, in the order it was registered, whatever the
/// follow-ups before it did.
/// </summary>
public sealed class JobChainingJobListenerTest
{
    private static readonly JobKey parent = new JobKey("parent", "chain");
    private static readonly JobKey childOne = new JobKey("child-one", "chain");
    private static readonly JobKey childTwo = new JobKey("child-two", "chain");
    private static readonly JobKey childThree = new JobKey("child-three", "chain");

    [Test]
    public async Task AJobChainedToSeveralJobsTriggersEveryOneOfThemInOrder()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");
        listener.AddJobChainLinks(parent, [childOne, childTwo, childThree]);

        List<JobKey> triggered = new List<JobKey>();
        IJobExecutionContext context = CreateContext(parent, triggered);

        await listener.JobWasExecuted(context, jobException: null);

        triggered.Should().Equal([childOne, childTwo, childThree],
            "every follow-up of the completed job is triggered, in the order its link was added");
    }

    [Test]
    public async Task AddingALinkTwiceFromTheSameJobChainsBoth()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");
        listener.AddJobChainLink(parent, childOne);
        listener.AddJobChainLink(parent, childTwo);

        List<JobKey> triggered = new List<JobKey>();
        IJobExecutionContext context = CreateContext(parent, triggered);

        await listener.JobWasExecuted(context, jobException: null);

        triggered.Should().Equal([childOne, childTwo],
            "a second link from the same job adds a follow-up rather than replacing the first one, "
            + "which is what #2215 could not express");
    }

    [Test]
    public async Task AFollowUpThatCannotBeTriggeredDoesNotCostItsSiblingsTheirFiring()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");
        listener.AddJobChainLinks(parent, [childOne, childTwo, childThree]);

        List<JobKey> triggered = new List<JobKey>();
        IJobExecutionContext context = CreateContext(parent, triggered, failingFollowUp: childTwo);

        await listener.JobWasExecuted(context, jobException: null);

        triggered.Should().Equal([childOne, childTwo, childThree],
            "a follow-up that does not exist any more is logged and stepped over; the ones after it "
            + "are still triggered");
    }

    [Test]
    public async Task AJobChainedToOneJobTriggersIt()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");
        listener.AddJobChainLink(parent, childOne);

        List<JobKey> triggered = new List<JobKey>();
        IJobExecutionContext context = CreateContext(parent, triggered);

        await listener.JobWasExecuted(context, jobException: null);

        triggered.Should().Equal([childOne],
            "the single-follow-up case is what most chains are, and it is unchanged");
    }

    [Test]
    public async Task AJobWithNoLinksTriggersNothing()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");
        listener.AddJobChainLink(childOne, childTwo);

        List<JobKey> triggered = new List<JobKey>();
        IJobExecutionContext context = CreateContext(parent, triggered);

        await listener.JobWasExecuted(context, jobException: null);

        triggered.Should().BeEmpty("the completed job has no chain link of its own");
    }

    [Test]
    public void ChainingTheSameFollowUpTwiceToOneJobIsRejected()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");
        listener.AddJobChainLink(parent, childOne);

        Action act = () => listener.AddJobChainLink(parent, childOne);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*already chained*",
                "firing one job twice for a single completion is a configuration mistake, not a fan-out");
    }

    [Test]
    public void NamingTheSameFollowUpTwiceInOneCallIsRejected()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");

        Action act = () => listener.AddJobChainLinks(parent, [childOne, childTwo, childOne]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*already chained*",
                "a duplicate inside the collection is the same mistake as a duplicate across two calls");
    }

    [Test]
    public async Task ARejectedCollectionLeavesTheLinksAsTheyWere()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");
        listener.AddJobChainLink(parent, childOne);

        Action act = () => listener.AddJobChainLinks(parent, [childTwo, childOne]);
        act.Should().Throw<ArgumentException>();

        List<JobKey> triggered = new List<JobKey>();
        IJobExecutionContext context = CreateContext(parent, triggered);

        await listener.JobWasExecuted(context, jobException: null);

        triggered.Should().Equal([childOne],
            "the whole collection is validated before any of it is added, so a rejected call does not "
            + "leave the listener half-configured");
    }

    [Test]
    public void AnEmptyCollectionOfFollowUpsIsRejected()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");

        Action act = () => listener.AddJobChainLinks(parent, []);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one follow-up job*",
                "a link to nothing is a typo, and silently accepting it hides the job that was meant to run");
    }

    [Test]
    public void ANullCollectionOfFollowUpsIsRejected()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");

        Action act = () => listener.AddJobChainLinks(parent, null);

        act.Should().Throw<ArgumentException>().WithMessage("*cannot be null*");
    }

    [Test]
    public void ANullKeyIsRejected()
    {
        JobChainingJobListener listener = new JobChainingJobListener("chain");

        Action addLink = () => listener.AddJobChainLink(parent, null);
        addLink.Should().Throw<ArgumentException>().WithMessage("*cannot be null*");

        Action addLinks = () => listener.AddJobChainLinks(parent, [childOne, null]);
        addLinks.Should().Throw<ArgumentException>().WithMessage("*cannot be null*");
    }

    /// <summary>
    /// A context whose completed job is <paramref name="completedJob" />, over a scheduler that records
    /// the key of every <see cref="IScheduler.TriggerJob" /> it is asked for and fails the one named by
    /// <paramref name="failingFollowUp" /> the way a deleted job would.
    /// </summary>
    private static IJobExecutionContext CreateContext(JobKey completedJob, List<JobKey> triggered, JobKey failingFollowUp = null)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.TriggerJob(A<JobKey>._, A<JobDataMap>._, A<CancellationToken>._))
            .Invokes((JobKey jobKey, JobDataMap _, CancellationToken _) => triggered.Add(jobKey));

        if (failingFollowUp is not null)
        {
            A.CallTo(() => scheduler.TriggerJob(failingFollowUp, A<JobDataMap>._, A<CancellationToken>._))
                .Invokes((JobKey jobKey, JobDataMap _, CancellationToken _) => triggered.Add(jobKey))
                .Throws(new SchedulerException($"The job '{failingFollowUp}' does not exist"));
        }

        IJobDetail jobDetail = A.Fake<IJobDetail>();
        A.CallTo(() => jobDetail.Key).Returns(completedJob);

        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.JobDetail).Returns(jobDetail);
        A.CallTo(() => context.Scheduler).Returns(scheduler);

        return context;
    }
}
