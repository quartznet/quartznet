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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What each <see cref="TriggerConflict" /> does to a trigger already stored under the key, through the
/// scheduler and down to the store, asserted the same way against the in-memory store and the ADO store on
/// a SQLite file.
/// </summary>
/// <remarks>
/// The decision is the store's, under its lock, which is what the concurrent cases are about: two callers
/// keeping one key must end with one trigger and one of them told it was kept, not with a primary-key
/// violation or two triggers.
/// </remarks>
public abstract class TriggerConflictStoreTest
{
    private static readonly JobKey jobKey = new("conflict-job", "conflicts");
    private static readonly TriggerKey triggerKey = new("order-42", "conflicts");
    private static readonly DateTimeOffset baseTime = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(2);

    private ServiceProvider container = null!;

    protected IScheduler Scheduler { get; private set; } = null!;

    protected IJobStore Store { get; private set; } = null!;

    protected abstract void UseStore(IQuartzBuilder quartz);

    [SetUp]
    public async Task BuildScheduler()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = $"conflicts-{Guid.NewGuid():N}";
                options.InstanceId = "one";
            });

            UseStore(q);
        });

        container = services.BuildServiceProvider();

        // Never started: the scheduler thread would fire what the cases store.
        Scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        Store = container.GetRequiredService<IJobStore>();

        await Scheduler.AddJob(JobBuilder.Create<ConflictJob>().WithIdentity(jobKey).StoreDurably().Build());
    }

    [TearDown]
    public virtual async Task ShutDown()
    {
        await Scheduler.Shutdown(waitForJobsToComplete: false);
        await container.DisposeAsync();
    }

    [Test]
    public async Task ThrowRefusesASecondTriggerUnderTheKey()
    {
        ScheduleTriggerResult first = await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.Throw);
        first.Outcome.Should().Be(ScheduleOutcome.Created);

        Func<Task> second = async () => await Scheduler.ScheduleTrigger(TriggerAt(Hours(2)), TriggerConflict.Throw);

        await second.Should().ThrowAsync<ObjectAlreadyExistsException>("Throw is what scheduling has always done");
        (await StoredFireTime()).Should().Be(Hours(1));
    }

    [Test]
    public async Task ReplaceStoresTheNewTriggerAndSaysSo()
    {
        (await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.Replace)).Outcome.Should().Be(ScheduleOutcome.Created,
            "nothing was there to replace");

        ScheduleTriggerResult second = await Scheduler.ScheduleTrigger(TriggerAt(Hours(2)), TriggerConflict.Replace);

        second.Should().Be(new ScheduleTriggerResult(Hours(2), ScheduleOutcome.Replaced));
        (await StoredFireTime()).Should().Be(Hours(2), "a debounce: the last call is the one that fires");
    }

    [Test]
    public async Task KeepLeavesAPendingTriggerAloneAndAnswersWithItsTime()
    {
        (await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.Keep)).Outcome.Should().Be(ScheduleOutcome.Created);

        ScheduleTriggerResult second = await Scheduler.ScheduleTrigger(TriggerAt(Hours(2)), TriggerConflict.Keep);

        second.Should().Be(new ScheduleTriggerResult(Hours(1), ScheduleOutcome.Kept),
            "an idempotent enqueue answers with the firing that is scheduled, not the one it was handed");
        (await StoredFireTime()).Should().Be(Hours(1));
    }

    [Test]
    public async Task KeepEarlierReplacesAPendingTriggerWithAnEarlierOne()
    {
        await Scheduler.ScheduleTrigger(TriggerAt(Hours(2)), TriggerConflict.KeepEarlier);

        ScheduleTriggerResult earlier = await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.KeepEarlier);

        earlier.Should().Be(new ScheduleTriggerResult(Hours(1), ScheduleOutcome.Replaced));
        (await StoredFireTime()).Should().Be(Hours(1), "the deadline moves closer");
    }

    [Test]
    public async Task KeepEarlierKeepsAPendingTriggerThatFiresFirst()
    {
        await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.KeepEarlier);

        ScheduleTriggerResult later = await Scheduler.ScheduleTrigger(TriggerAt(Hours(2)), TriggerConflict.KeepEarlier);
        ScheduleTriggerResult same = await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.KeepEarlier);

        later.Should().Be(new ScheduleTriggerResult(Hours(1), ScheduleOutcome.Kept), "the deadline never moves away");
        same.Outcome.Should().Be(ScheduleOutcome.Kept, "a tie is not earlier, and replacing would only rewrite the row");
        (await StoredFireTime()).Should().Be(Hours(1));
    }

    [Test]
    public async Task KeepStoresANewTriggerOverOneWhoseFiringHasBegun()
    {
        IOperableTrigger due = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .Build();
        await Scheduler.ScheduleTrigger(due, TriggerConflict.Keep);

        List<IOperableTrigger> acquired = await Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
        acquired.Should().ContainSingle("the premise: the one-shot trigger is due");
        await Store.TriggersFired(acquired);

        ScheduleTriggerResult again = await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.Keep);

        again.Should().Be(new ScheduleTriggerResult(Hours(1), ScheduleOutcome.Replaced),
            "a one-shot trigger that has fired has nothing pending, and keeping it would drop the work asked for now");
        (await StoredFireTime()).Should().Be(Hours(1));
    }

    [Test]
    public async Task TwoCallersKeepingOneKeyAtOnceStoreExactlyOneTrigger()
    {
        const int callers = 8;
        Task<ScheduleTriggerResult>[] calls = new Task<ScheduleTriggerResult>[callers];
        for (int i = 0; i < callers; i++)
        {
            ITrigger trigger = TriggerAt(Hours(1 + i));
            calls[i] = Task.Run(async () => await Scheduler.ScheduleTrigger(trigger, TriggerConflict.Keep));
        }

        ScheduleTriggerResult[] results = await Task.WhenAll(calls);

        results.Should().ContainSingle(x => x.Outcome == ScheduleOutcome.Created,
            "the store decides under its lock, so only the first caller finds the key free");
        results.Where(x => x.Outcome == ScheduleOutcome.Kept).Should().HaveCount(callers - 1,
            "every other caller is told the first one's trigger was kept, rather than failing on the key");

        DateTimeOffset created = results.Single(x => x.Outcome == ScheduleOutcome.Created).NextFireTimeUtc;
        results.Should().OnlyContain(x => x.NextFireTimeUtc == created, "every caller is answered with the one trigger that is stored");
        (await Scheduler.GetTriggersOfJob(jobKey)).Should().ContainSingle();
    }

    [Test]
    public async Task TwoOneLinerEnqueuesWithOneNameStoreOneFiring()
    {
        OneOffJobOptions options = new() { Name = "invoice-7", OnConflict = TriggerConflict.Keep };

        Task<ScheduledOneOffJob> first = Task.Run(async () =>
            await Scheduler.ScheduleJob<ConflictInputJob, ConflictInput>(new ConflictInput("a"), Hours(1), options));
        Task<ScheduledOneOffJob> second = Task.Run(async () =>
            await Scheduler.ScheduleJob<ConflictInputJob, ConflictInput>(new ConflictInput("b"), Hours(2), options));

        ScheduledOneOffJob[] scheduled = await Task.WhenAll(first, second);

        scheduled.Select(x => x.Outcome).Should().BeEquivalentTo([ScheduleOutcome.Created, ScheduleOutcome.Kept]);
        scheduled.Select(x => x.TriggerKey).Distinct().Should().ContainSingle("both calls name the same firing");
        scheduled[0].FirstFireTimeUtc.Should().Be(scheduled[1].FirstFireTimeUtc,
            "the kept call answers with the time of the firing that is actually scheduled");
        (await Scheduler.GetTriggersOfJob(SchedulerConstants.ScheduledJobKey<ConflictInputJob>())).Should().ContainSingle();
    }

    [Test]
    public async Task AKeptTriggerIsNotAnnouncedAsScheduled()
    {
        CountingListener listener = new();
        Scheduler.ListenerManager.AddSchedulerListener(listener);

        await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.Keep);
        await Scheduler.ScheduleTrigger(TriggerAt(Hours(2)), TriggerConflict.Keep);
        await Scheduler.ScheduleTrigger(TriggerAt(Hours(3)), TriggerConflict.Replace);

        listener.Scheduled.Should().Be(2, "the kept call stored nothing, and a listener told otherwise would count a firing twice");
    }

    [Test]
    public async Task AModeThatIsNoneOfTheFourIsRefusedByTheStore()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerAt(Hours(1));
        trigger.ComputeFirstFireTimeUtc(calendar: null);

        Func<Task> store = async () => await Store.StoreTrigger(trigger, (TriggerConflict) 42);

        await store.Should().ThrowAsync<ArgumentOutOfRangeException>("an unknown mode read as Throw would be a silent guess");
        (await Scheduler.Exists(triggerKey)).Should().BeFalse();
    }

    [Test]
    public async Task AStoreThrowsWhenAskedToThrowOnATakenKey()
    {
        await Scheduler.ScheduleTrigger(TriggerAt(Hours(1)), TriggerConflict.Throw);

        IOperableTrigger trigger = (IOperableTrigger) TriggerAt(Hours(2));
        trigger.ComputeFirstFireTimeUtc(calendar: null);

        Func<Task> store = async () => await Store.StoreTrigger(trigger, TriggerConflict.Throw);

        await store.Should().ThrowAsync<ObjectAlreadyExistsException>();
    }

    private static DateTimeOffset Hours(int hours)
    {
        // A day ahead on a whole hour, fixed for the fixture, so a store that keeps a coarser precision
        // reads back exactly what was written and no case straddles an hour boundary.
        return baseTime.AddHours(hours);
    }


    private static ITrigger TriggerAt(DateTimeOffset at)
    {
        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(at)
            .Build();
    }

    private async Task<DateTimeOffset?> StoredFireTime()
    {
        return (await Scheduler.GetTrigger(triggerKey))?.NextFireTimeUtc;
    }

    public sealed class ConflictJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed record ConflictInput(string Reference);

    public sealed class ConflictInputJob : IJob<ConflictInput>
    {
        public ValueTask Execute(IJobExecutionContext context, ConflictInput input, CancellationToken cancellationToken = default) => default;
    }

    private sealed class CountingListener : ISchedulerListener
    {
        private int scheduled;

        public int Scheduled => Volatile.Read(ref scheduled);

        public ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref scheduled);
            return default;
        }
    }
}

public sealed class RamTriggerConflictStoreTest : TriggerConflictStoreTest
{
    protected override void UseStore(IQuartzBuilder quartz)
    {
        quartz.UseInMemoryStore();
    }
}

public sealed class SqliteTriggerConflictStoreTest : TriggerConflictStoreTest
{
    private SqliteTestDatabase database = null!;

    protected override void UseStore(IQuartzBuilder quartz)
    {
        database = new SqliteTestDatabase("trigger-conflict");
        quartz.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
            store.ProvisionSchema();
        });
    }

    public override async Task ShutDown()
    {
        await base.ShutDown();
        database.Dispose();
    }
}
