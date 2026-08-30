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
/// <see cref="DelegatingScheduler" /> hands a call on unchanged, which is the whole of its job.
/// </summary>
/// <remarks>
/// It is the base class an application writes a decorator over — counting, tracing, guarding — and a
/// decorator is only safe if the members it does not override behave as if it were not there. A
/// forwarding member that dropped an argument, or answered instead of asking, would be invisible to
/// every test of the scheduler itself.
/// </remarks>
public sealed class DelegatingSchedulerForwardingTest
{
    [Test]
    public async Task DeletingAGroupOfJobsIsHandedOnWithItsMatcherAndItsAnswer()
    {
        JobKey deleted = new JobKey("job", "saga-17");

        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.DeleteJobs(A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { deleted });

        DelegatingScheduler scheduler = new DelegatingScheduler(inner);
        GroupMatcher<JobKey> matcher = GroupMatcher<JobKey>.GroupEquals("saga-17");

        (await scheduler.DeleteJobs(matcher)).Should().Equal([deleted],
            "the wrapper answers with what it was told, rather than with a list of its own");

        A.CallTo(() => inner.DeleteJobs(matcher, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task UnschedulingAGroupOfTriggersIsHandedOnWithItsMatcherAndItsAnswer()
    {
        TriggerKey removed = new TriggerKey("trigger", "saga-17");

        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.UnscheduleJobs(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { removed });

        DelegatingScheduler scheduler = new DelegatingScheduler(inner);
        GroupMatcher<TriggerKey> matcher = GroupMatcher<TriggerKey>.GroupEquals("saga-17");

        (await scheduler.UnscheduleJobs(matcher)).Should().Equal([removed]);

        A.CallTo(() => inner.UnscheduleJobs(matcher, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The token travels too, which a wrapper that wrote <c>default</c> would quietly stop doing.
    /// </summary>
    [Test]
    public async Task TheCancellationTokenTravelsWithTheCall()
    {
        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.DeleteJobs(A<GroupMatcher<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey>());

        using CancellationTokenSource cancellation = new();
        DelegatingScheduler scheduler = new DelegatingScheduler(inner);

        await scheduler.DeleteJobs(GroupMatcher<JobKey>.AnyGroup(), cancellation.Token);

        A.CallTo(() => inner.DeleteJobs(A<GroupMatcher<JobKey>>._, cancellation.Token)).MustHaveHappenedOnceExactly();
    }
}
