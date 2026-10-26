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

using System.Globalization;

using FakeItEasy;

using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Who is asked for the execution group every stored trigger of a job carries, which a manual firing of a
/// job whose type declares none counts against (#4174).
/// </summary>
/// <remarks>
/// <see cref="RAMJobStore" /> answers from the job's own stored triggers rather than a listing of every
/// trigger it holds. A store, a decorator or a scheduler of another make is asked for the job's trigger
/// headers, as every one of them was before. <c>ManualFiringExecutionGroupSqliteTest</c> covers the ADO store.
/// </remarks>
public sealed class SharedExecutionGroupSourceTest
{
    private const string JobGroup = "shared-group";
    private const string Reports = "reports";

    private static readonly JobKey asked = new("asked", JobGroup);

    [Test]
    public async Task TheInMemoryStoreGivesTheGroupOnlyWhenEveryTriggerOfTheJobCarriesIt()
    {
        RAMJobStore store = NewStore();
        JobKey agreeing = await Store(store, "agreeing", Reports, Reports);
        JobKey mixed = await Store(store, "mixed", Reports, "batch");
        JobKey partly = await Store(store, "partly", Reports, null);
        JobKey cased = await Store(store, "cased", Reports, "Reports");
        JobKey untriggered = await Store(store, "untriggered");

        (await store.GetSharedExecutionGroup(agreeing)).Should().Be(Reports, "every trigger of the job carries reports");
        (await store.GetSharedExecutionGroup(mixed)).Should().BeNull("two groups is no one group");
        (await store.GetSharedExecutionGroup(partly)).Should().BeNull("a grouped and an ungrouped trigger is no one group");
        (await store.GetSharedExecutionGroup(cased)).Should().BeNull("groups compare ordinally, as execution limits count them");
        (await store.GetSharedExecutionGroup(untriggered)).Should().BeNull("no trigger says anything");
        (await store.GetSharedExecutionGroup(new JobKey("absent", JobGroup))).Should().BeNull("nor does a job that is not stored");

        foreach (JobKey job in new[] { agreeing, mixed, partly, cased, untriggered })
        {
            (await ManualFiringExecutionGroup.SharedGroupOf(store, job)).Should().Be(await Listed(store, job),
                "the store's answer for {0} is the one the job's trigger headers give", job);
        }
    }

    /// <summary>
    /// Another job's triggers say nothing about this one's group, wherever the store keeps them.
    /// </summary>
    [Test]
    public async Task TheInMemoryStoreReadsOnlyTheTriggersOfTheJobAskedAbout()
    {
        RAMJobStore store = NewStore();
        await Store(store, asked.Name, Reports, Reports);
        await Store(store, "neighbour", "batch", null);

        (await store.GetSharedExecutionGroup(asked)).Should().Be(Reports,
            "the neighbour's triggers are in the same trigger group, and belong to another job");
    }

    [Test]
    public async Task AStoreOfAnotherMakeIsAskedForTheJobsTriggerHeaders()
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(Listing(Reports, Reports));

        (await ManualFiringExecutionGroup.SharedGroupOf(store, asked)).Should().Be(Reports);

        A.CallTo(() => store.QueryTriggers(A<TriggerQuery>.That.Matches(x => asked.Equals(x.Job) && x.Take == PagedQuery.All), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A decorator over a store that could answer is asked for its listing all the same, so that what it
    /// overrides there still decides the group.
    /// </summary>
    [Test]
    public async Task ADecoratorIsAskedForItsListingRatherThanPassedBy()
    {
        RAMJobStore inner = NewStore();
        await Store(inner, asked.Name, Reports, "batch");
        ListingOnlyReports decorator = new(inner);

        (await ManualFiringExecutionGroup.SharedGroupOf(decorator, asked)).Should().Be(Reports,
            "the decorator lists only the reports trigger, and its listing is what the group was always read from");
        decorator.Listings.Should().Be(1);
    }

    [Test]
    public async Task ASchedulerOfAnotherMakeIsAskedForTheJobsTriggerHeaders()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(Listing(Reports, "batch"));

        (await ManualFiringExecutionGroup.SharedGroupOf(scheduler, asked)).Should().BeNull("two groups is no one group");

        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>.That.Matches(x => asked.Equals(x.Job) && x.Take == PagedQuery.All), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// Read through the scheduler, the group is refused once it has shut down, as the listing it stands
    /// in for was.
    /// </summary>
    [Test]
    public async Task ASchedulerThatHasShutDownRefusesTheRead()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "shared-group-" + Guid.NewGuid().ToString("N"));
                q.UseInMemoryStore();
            })
            .BuildScheduler();
        await scheduler.Shutdown();

        Func<Task> read = async () => await ManualFiringExecutionGroup.SharedGroupOf(scheduler, asked);

        await read.Should().ThrowAsync<SchedulerException>().WithMessage("*Shutdown*");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    private static RAMJobStore NewStore() => new(NullLoggerFactory.Instance, A.Fake<ISchedulerSignaler>(), TimeProvider.System);

    /// <summary>A durable job with a trigger a group, each due tomorrow.</summary>
    private static async Task<JobKey> Store(RAMJobStore store, string name, params string?[] executionGroups)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, JobGroup).StoreDurably().Build();
        await store.AddJob(job);

        for (int i = 0; i < executionGroups.Length; i++)
        {
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity(name + "-" + i.ToString(CultureInfo.InvariantCulture), JobGroup)
                .ForJob(job)
                .WithExecutionGroup(executionGroups[i])
                .StartAt(TimeProvider.System.GetUtcNow().AddDays(1))
                .Build();
            trigger.ComputeFirstFireTimeUtc(calendar: null);
            await store.AddTrigger(trigger);
        }

        return job.Key;
    }

    private static async Task<string?> Listed(IJobStore store, JobKey job)
    {
        PagedResult<TriggerHeader> listing = await store.QueryTriggers(new TriggerQuery { Job = job, Take = PagedQuery.All });
        return ManualFiringExecutionGroup.SharedBy(listing.Items);
    }

    private static PagedResult<TriggerHeader> Listing(params string?[] executionGroups)
    {
        List<TriggerHeader> headers = [.. executionGroups.Select((group, i) => Header(i, group))];
        return new PagedResult<TriggerHeader>(headers, false, null);
    }

    private static TriggerHeader Header(int index, string? executionGroup)
    {
        return new TriggerHeader(
            new TriggerKey("t-" + index.ToString(CultureInfo.InvariantCulture), JobGroup),
            asked,
            Description: null,
            TriggerType: "SIMPLE",
            TriggerState.Normal,
            StartTimeUtc: DateTimeOffset.UnixEpoch,
            EndTimeUtc: null,
            NextFireTimeUtc: null,
            PreviousFireTimeUtc: null,
            CalendarName: null,
            Priority: 5,
            executionGroup,
            RetryPolicy: null,
            RetryAttempt: 0);
    }

    /// <summary>
    /// A decorator whose listing leaves out every trigger outside <see cref="Reports" />, as a tenant filter
    /// might.
    /// </summary>
    private sealed class ListingOnlyReports(IJobStore inner) : DelegatingJobStore(inner)
    {
        public int Listings { get; private set; }

        public override async ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
        {
            Listings++;
            PagedResult<TriggerHeader> listing = await base.QueryTriggers(query, cancellationToken);
            List<TriggerHeader> reports = [.. listing.Items.Where(x => x.ExecutionGroup == Reports)];
            return new PagedResult<TriggerHeader>(reports, false, null);
        }
    }

    /// <summary>Never executed: nothing here starts a scheduler.</summary>
    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
