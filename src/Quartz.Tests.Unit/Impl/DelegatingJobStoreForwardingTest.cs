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
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The three storing members of <see cref="DelegatingJobStore" /> hand their options on unchanged.
/// </summary>
/// <remarks>
/// These used to take a <see cref="bool" />, and a decorator forwarding one flag is hard to get wrong.
/// An options record is not: a wrapper that read <c>options.Replace</c> and rebuilt the record, or that
/// passed <see langword="default" /> because the parameter has a default, would turn a caller's
/// "replace what is there" into "throw if it is there" — and it would do so only for the applications
/// that wrap their store, which is exactly the population no other test covers.
/// </remarks>
public sealed class DelegatingJobStoreForwardingTest
{
    [Test]
    public async Task AddJobHandsOnTheOptionsItWasGiven()
    {
        IJobStore inner = A.Fake<IJobStore>();
        DelegatingJobStore store = new DelegatingJobStore(inner);
        IJobDetail job = Job();

        await store.AddJob(job, AddJobOptions.Replacing);

        A.CallTo(() => inner.AddJob(job, AddJobOptions.Replacing, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AddJobHandsOnTheDefaultWhenItIsOmitted()
    {
        IJobStore inner = A.Fake<IJobStore>();
        DelegatingJobStore store = new DelegatingJobStore(inner);
        IJobDetail job = Job();

        await store.AddJob(job);

        A.CallTo(() => inner.AddJob(job, default, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => inner.AddJob(job, AddJobOptions.Replacing, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AddTriggerHandsOnTheOptionsItWasGiven()
    {
        IJobStore inner = A.Fake<IJobStore>();
        DelegatingJobStore store = new DelegatingJobStore(inner);
        IOperableTrigger trigger = Trigger();

        await store.AddTrigger(trigger, AddTriggerOptions.Replacing);

        A.CallTo(() => inner.AddTrigger(trigger, AddTriggerOptions.Replacing, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ScheduleJobsHandsOnTheOptionsItWasGiven()
    {
        IJobStore inner = A.Fake<IJobStore>();
        DelegatingJobStore store = new DelegatingJobStore(inner);
        Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> batch = new() { [Job()] = [Trigger()] };

        await store.ScheduleJobs(batch, ScheduleJobOptions.Replacing);

        A.CallTo(() => inner.ScheduleJobs(batch, ScheduleJobOptions.Replacing, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The token travels too, which a wrapper that wrote <c>default</c> would quietly stop doing.
    /// </summary>
    [Test]
    public async Task TheCancellationTokenTravelsWithTheCall()
    {
        using CancellationTokenSource source = new CancellationTokenSource();

        IJobStore inner = A.Fake<IJobStore>();
        DelegatingJobStore store = new DelegatingJobStore(inner);
        IOperableTrigger trigger = Trigger();

        await store.AddTrigger(trigger, cancellationToken: source.Token);

        A.CallTo(() => inner.AddTrigger(trigger, default, source.Token)).MustHaveHappenedOnceExactly();
    }

    private static IJobDetail Job()
    {
        return JobBuilder.Create<NoOpJob>().WithIdentity("compaction").StoreDurably().Build();
    }

    private static IOperableTrigger Trigger()
    {
        return (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("nightly")
            .ForJob("compaction")
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
    }
}
